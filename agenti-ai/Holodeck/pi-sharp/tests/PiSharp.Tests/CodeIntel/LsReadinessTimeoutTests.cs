using System.Diagnostics;
using System.Text;
using PiSharp.CodeIntel.Lsp;

namespace PiSharp.Tests.CodeIntel;

public sealed class LsReadinessTimeoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci2-no-ready-{Guid.NewGuid():N}");
    private readonly string _serverDll;

    public LsReadinessTimeoutTests()
    {
        Directory.CreateDirectory(_root);
        _serverDll = BuildNoReadyServer();
    }

    [Fact]
    public async Task WaitReadyAsync_BoundsAttachWhenServerNeverSignalsProgressEnd()
    {
        var timeout = TimeSpan.FromMilliseconds(250);
        var options = new LsOptions
        {
            Kind = "no-ready",
            WorkspaceUri = "file:///tmp/ci2-no-ready",
            ReadyTimeout = timeout,
            StartProcess = _ => StartNoReadyServer(),
        };

        await using var ls = await LsProcess.StartAsync(options, CancellationToken.None);
        using var testWatchdog = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stopwatch = Stopwatch.StartNew();

        // McpEndpoint.CodeAttachAsync gates its response on this wait. A server that
        // initializes but never reports $/progress kind=end must not hold that request forever.
        var ready = await ls.WaitReadyAsync(testWatchdog.Token);

        Assert.False(ready);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private Process StartNoReadyServer()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(_serverDll);
        return Process.Start(start) ?? throw new InvalidOperationException("no-ready LSP process did not start");
    }

    private string BuildNoReadyServer()
    {
        var project = Path.Combine(_root, "no-ready-ls");
        var output = Path.Combine(project, "out");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Program.cs"), NoReadyServerSource);
        File.WriteAllText(Path.Combine(project, "no-ready-ls.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <OutputType>Exe</OutputType>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "    <ImplicitUsings>enable</ImplicitUsings>\n" +
            "    <Nullable>enable</Nullable>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n");

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = project,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "build", "no-ready-ls.csproj", "-c", "Release", "-o", output, "-v", "q" })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("no-ready LSP build did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("no-ready LSP build exceeded 120 seconds");
        }
        var outputText = stdout.GetAwaiter().GetResult();
        var errorText = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"no-ready LSP build failed: {outputText}\n{errorText}");

        var dll = Path.Combine(output, "no-ready-ls.dll");
        if (!File.Exists(dll)) throw new InvalidOperationException("no-ready LSP DLL was not produced");
        return dll;
    }

    private const string NoReadyServerSource = """
        using System.Text;
        using System.Text.Json.Nodes;

        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();
        while (true)
        {
            var length = ReadContentLength(input);
            if (length < 0) break;
            var body = new byte[length];
            var read = 0;
            while (read < body.Length)
            {
                var count = input.Read(body, read, body.Length - read);
                if (count == 0) return;
                read += count;
            }

            var message = JsonNode.Parse(Encoding.UTF8.GetString(body))!.AsObject();
            if (message["method"]?.GetValue<string>() == "initialize")
            {
                var response = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = message["id"]?.DeepClone(),
                    ["result"] = new JsonObject { ["capabilities"] = new JsonObject() },
                };
                Send(output, response);
            }
            // Deliberately never send $/progress kind=end.
        }

        static int ReadContentLength(Stream input)
        {
            var contentLength = -1;
            var line = new StringBuilder();
            while (true)
            {
                var value = input.ReadByte();
                if (value < 0) return line.Length == 0 ? -1 : throw new EndOfStreamException();
                if (value != '\r') { line.Append((char)value); continue; }
                if (input.ReadByte() != '\n') throw new InvalidDataException("malformed LSP header");
                if (line.Length == 0) return contentLength;
                const string prefix = "Content-Length:";
                var text = line.ToString();
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(text[prefix.Length..].Trim());
                line.Clear();
            }
        }

        static void Send(Stream output, JsonObject message)
        {
            var body = Encoding.UTF8.GetBytes(message.ToJsonString());
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            output.Write(header);
            output.Write(body);
            output.Flush();
        }
        """;
}
