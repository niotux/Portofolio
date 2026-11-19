using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PiSharp.Cli;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm;
using PiSharp.Tests.Agent;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Llm;

public class SubscriptionBrokerCaTests
{
    [Fact]
    public async Task PinnedSelfSignedCertificateIsAccepted()
    {
        using var serverCert = Certs.SelfSigned("broker");
        await using var server = await Broker.Start(serverCert);
        using var pin = Certs.FromPem(serverCert);
        using var http = Broker.Client(pin);
        Assert.Equal("ok", await http.GetStringAsync(server.Address + "health"));
    }

    [Fact]
    public async Task DifferentSelfSignedCertificateIsRejected()
    {
        using var serverCert = Certs.SelfSigned("broker");
        using var other = Certs.SelfSigned("other");
        await using var server = await Broker.Start(serverCert);
        using var http = Broker.Client(other);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetStringAsync(server.Address + "health"));
    }

    [Fact]
    public async Task UnsetPinStillRejectsSelfSignedCertificate()
    {
        using var serverCert = Certs.SelfSigned("broker");
        await using var server = await Broker.Start(serverCert);
        using var http = Broker.Client(pin: null);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetStringAsync(server.Address + "health"));
    }

    [Fact]
    public async Task CertificateChainedToPinnedCaIsAccepted()
    {
        using var ca = Certs.Authority("broker-ca");
        using var serverCert = Certs.Issued(ca, "broker");
        await using var server = await Broker.Start(serverCert);
        using var pin = Certs.FromPem(ca);
        using var http = Broker.Client(pin);
        Assert.Equal("ok", await http.GetStringAsync(server.Address + "health"));
    }

    [Fact]
    public async Task RouterTrustsOnlyThePinnedBroker()
    {
        using var serverCert = Certs.SelfSigned("broker");
        await using var server = await Broker.Start(serverCert);
        using var pin = Certs.FromPem(serverCert);
        var model = new ModelConfig { Id = "grok-model", BaseUrl = "https://api.x.ai/v1", Provider = "xai", Connection = "personal" };

        using (var accepted = new SubscriptionRouter(null, server.Uri, "seat-token", pin))
        {
            var events = await Collect(accepted.Stream(model, new("", [], []), new(), default));
            Assert.Equal(StopReason.Stop, Assert.IsType<AssistantDone>(events[^1]).Reason);
        }

        using var rejected = new SubscriptionRouter(null, server.Uri, "seat-token");
        var failed = await Collect(rejected.Stream(model, new("", [], []), new(), default));
        var error = Assert.IsType<AssistantError>(failed[^1]).Error;
        Assert.Equal("subscription_protocol_error", error.ErrorCode);
        // The log names the cause, not only "check connection configuration".
        Assert.Contains("HttpRequestException", error.ErrorMessage);
    }

    [Fact]
    public async Task StartupProbeTrustsOnlyThePinnedCertificate()
    {
        using var tmp = new TempWorkspace();
        tmp.File("settings.json", """{"factory":{"token":"seat-token"}}""");
        using var serverCert = Certs.SelfSigned("broker");
        var pem = tmp.File("broker.pem", serverCert.ExportCertificatePem());
        await using var server = await Broker.Start(serverCert, instance: true);

        var pinned = await Run(tmp, server.Uri, pem);
        Assert.Equal(0, pinned.Code);
        Assert.Contains("pong", pinned.Stdout);

        var unset = await Run(tmp, server.Uri, caPath: null);
        Assert.Equal(2, unset.Code);
        Assert.Contains("Subscription setup failed:", unset.Stderr);
        Assert.DoesNotContain("pong", unset.Stdout);
    }

    [Fact]
    public async Task InvalidPemFailsAtStartupWithoutAcceptingAnyCertificate()
    {
        using var tmp = new TempWorkspace();
        var path = tmp.File("bad.pem", "-----BEGIN CERTIFICATE-----\nnot-a-cert\n-----END CERTIFICATE-----\n");
        var (code, _, stderr) = await Run(tmp, broker: null, path);
        Assert.Equal(2, code);
        Assert.Contains("PI_SHARP_SUBSCRIPTION_BROKER_CA", stderr);
        Assert.Contains("is not a PEM certificate", stderr);
        Assert.DoesNotContain("Subscription setup failed", stderr);
    }

    [Fact]
    public async Task UnreadablePemFailsAtStartup()
    {
        using var tmp = new TempWorkspace();
        var missing = System.IO.Path.Combine(tmp.Path, "missing.pem");
        var (code, _, stderr) = await Run(tmp, broker: null, missing);
        Assert.Equal(2, code);
        Assert.Contains("PI_SHARP_SUBSCRIPTION_BROKER_CA", stderr);
        Assert.Contains("cannot read", stderr);
    }

    private static async Task<(int Code, string Stdout, string Stderr)> Run(TempWorkspace tmp, Uri? broker, string? caPath)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var host = new CliHost
        {
            Out = stdout, Err = stderr, In = new StringReader(""), Color = false,
            ConfigDir = tmp.Path, HookCancelKey = false,
            SubscriptionBrokerUrl = broker?.AbsoluteUri,
            SubscriptionBrokerCaPath = caPath,
            StreamFactory = _ => new ScriptedStream().Then(Fakes.Text("pong")).Stream,
            ToolsFactory = (_, _) => [],
        };
        var code = await host.RunAsync(["--cwd", tmp.Path, "--no-context-files", "--no-skills", "--no-prompt-templates", "--no-session", "--print", "hello"]);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static async Task<List<AssistantMessageEvent>> Collect(IAsyncEnumerable<AssistantMessageEvent> events)
    {
        var list = new List<AssistantMessageEvent>();
        await foreach (var evt in events) list.Add(evt);
        return list;
    }

    private sealed class Broker : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required Uri Uri { get; init; }
        public string Address => Uri.AbsoluteUri;

        public static async Task<Broker> Start(X509Certificate2 certificate, bool instance = false)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
            var app = builder.Build();
            app.MapGet("/health", () => Results.Text("ok"));
            if (instance)
            {
                const string model = """{"id":"grok-model","baseUrl":"https://api.x.ai/v1","provider":"xai","connection":"personal","input":["text"],"contextWindow":32768,"maxOutputTokens":4096,"compat":{"overflowPatterns":[]}}""";
                app.MapGet("/instance", () => Results.Text(model, "application/json"));
            }
            app.MapPost("/responses", () => Results.Text(
                "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}\n\n",
                "text/event-stream"));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new Broker { App = app, Uri = new Uri(address.TrimEnd('/') + "/") };
        }

        public static HttpClient Client(X509Certificate2? pin)
        {
            var handler = new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5) };
            if (pin is not null) SubscriptionBrokerTrust.Pin(handler, pin);
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private static class Certs
    {
        public static X509Certificate2 SelfSigned(string cn) => Issue(cn, ca: false, issuer: null);
        public static X509Certificate2 Authority(string cn) => Issue(cn, ca: true, issuer: null);
        public static X509Certificate2 Issued(X509Certificate2 issuer, string cn) => Issue(cn, ca: false, issuer);

        public static X509Certificate2 FromPem(X509Certificate2 certificate)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pi-sharp-ca-" + Guid.NewGuid().ToString("N") + ".pem");
            File.WriteAllText(path, certificate.ExportCertificatePem());
            try { return SubscriptionBrokerTrust.Load(path); }
            finally { File.Delete(path); }
        }

        private static X509Certificate2 Issue(string cn, bool ca, X509Certificate2? issuer)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(new X500DistinguishedName($"CN={cn}"), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                ca ? X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign : X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            if (!ca)
            {
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                var san = new SubjectAlternativeNameBuilder();
                san.AddIpAddress(IPAddress.Loopback);
                request.CertificateExtensions.Add(san.Build());
            }
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            X509Certificate2 issued;
            if (issuer is null)
                issued = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            else
            {
                var serial = new byte[16];
                RandomNumberGenerator.Fill(serial);
                serial[0] &= 0x7F;
                // Clamp to the issuer: a second UtcNow.AddDays(2) can land after the CA's own notAfter (seconds
                // truncate) and Create then throws "notAfter is later than issuerCertificate.NotAfter".
                var notAfter = new DateTimeOffset(issuer.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                using var signed = request.Create(issuer, DateTimeOffset.UtcNow.AddDays(-1), notAfter, serial);
                issued = signed.CopyWithPrivateKey(rsa);
            }
            try
            {
                // SChannel cannot serve TLS with an ephemeral key (the handshake ends in EOF), so Windows keeps it in a key container.
                return X509CertificateLoader.LoadPkcs12(issued.Export(X509ContentType.Pfx), null,
                    OperatingSystem.IsWindows() ? X509KeyStorageFlags.Exportable : X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
            }
            finally { issued.Dispose(); }
        }
    }
}
