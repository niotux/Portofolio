using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PiSharp.Llm;

/// <summary>
/// Seat-side trust for the subscription broker. When <c>PI_SHARP_SUBSCRIPTION_BROKER_CA</c> is set,
/// the broker clients accept a server certificate only if it equals that PEM or chains to it.
/// The system trust store is not consulted for those clients, and a bad file fails closed.
/// </summary>
public static class SubscriptionBrokerTrust
{
    public const string Variable = "PI_SHARP_SUBSCRIPTION_BROKER_CA";

    public static X509Certificate2 Load(string path)
    {
        string pem;
        try
        {
            pem = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            throw new InvalidOperationException($"{Variable}: cannot read '{path}': {ex.Message}", ex);
        }

        try
        {
            return X509Certificate2.CreateFromPem(pem);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            throw new InvalidOperationException($"{Variable}: '{path}' is not a PEM certificate.", ex);
        }
    }

    /// <summary>Replace the handler's chain check. Other handlers are left alone.</summary>
    public static void Pin(HttpMessageHandler handler, X509Certificate2 pin)
    {
        switch (handler)
        {
            case SocketsHttpHandler sockets:
                sockets.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, _) => Matches(cert, pin);
                return;
            case HttpClientHandler http:
                http.ServerCertificateCustomValidationCallback = (_, cert, _, _) => Matches(cert, pin);
                return;
            default:
                throw new InvalidOperationException("Subscription broker CA pin cannot be applied to this HTTP handler.");
        }
    }

    public static bool Matches(X509Certificate? server, X509Certificate2 pin)
    {
        if (server is null) return false;
        try
        {
            var raw = server.GetRawCertData();
            // A self-signed server certificate pinned as itself often will not chain as a CA.
            if (raw.AsSpan().SequenceEqual(pin.RawData)) return true;
            using var leaf = X509CertificateLoader.LoadCertificate(raw);
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(pin);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            return chain.Build(leaf);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
