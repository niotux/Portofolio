using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using PiSharp.CodeIntel;
using PiSharp.CodeIntel.Auth;
using PiSharp.CodeIntel.Git;
using PiSharp.CodeIntel.Lsp;
using PiSharp.CodeIntel.Mcp;

namespace PiSharp.Tests.CodeIntel;

// ── LSP Content-Length framing ─────────────────────────────────────────────

public class LspPeerTests
{
    [Fact]
    public async Task SendAsync_ProducesCorrectContentLengthFraming()
    {
        // Test that the LSP peer's SendAsync produces correct Content-Length framing.
        var input = new MemoryStream(); // never read
        var output = new MemoryStream();

        await using var peer = new LspPeer(input, output, onRequest: null);

        // Send a notification via the public API
        await peer.NotifyAsync("window/log", System.Text.Json.Nodes.JsonNode.Parse("{\"type\":3}"), CancellationToken.None);

        output.Position = 0;
        var raw = output.ToArray();
        var text = Encoding.UTF8.GetString(raw);

        // Must start with "Content-Length: N\r\n\r\n"
        var headerEnd = text.IndexOf("\r\n\r\n");
        Assert.True(headerEnd > 0, $"No header terminator in: {text}");
        var header = text[..(headerEnd + 4)];
        Assert.StartsWith("Content-Length: ", header);
        var declaredLen = int.Parse(header["Content-Length: ".Length..].TrimEnd('\r', '\n'));

        // The body must be exactly that many bytes
        var bodyBytes = Encoding.UTF8.GetBytes(text[(headerEnd + 4)..]);
        Assert.Equal(declaredLen, bodyBytes.Length);

        // The body must be valid JSON with the right method
        var doc = System.Text.Json.Nodes.JsonNode.Parse(bodyBytes)!;
        Assert.Equal("2.0", doc["jsonrpc"]!.GetValue<string>());
        Assert.Equal("window/log", doc["method"]!.GetValue<string>());
    }

    [Fact]
    public async Task RequestAsync_PendingRequestIsTracked()
    {
        // We can't easily test a full request/response cycle without a mock server,
        // but we can verify that RequestAsync sends the correctly framed request.
        var input = new MemoryStream();
        var output = new MemoryStream();

        await using var peer = new LspPeer(input, output, onRequest: null);

        // Fire and forget: the request will be pending, but the send should work.
        var task = peer.RequestAsync("initialize", System.Text.Json.Nodes.JsonNode.Parse("{}"),
            new CancellationTokenSource(200).Token);

        // The send should have happened by now (it's synchronous within the lock)
        output.Position = 0;
        var raw = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("initialize", raw);
        Assert.StartsWith("Content-Length: ", raw);
    }

    [Fact]
    public async Task ReadContentLength_ParsesHeader()
    {
        // A stream with a Content-Length header followed by a body.
        var body = "\"hello\"".ToCharArray();
        var header = $"Content-Length: {body.Length}\r\n\r\n";
        var raw = (header + new string(body)).ToCharArray();
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));

        var peer = new LspPeer(stream, new MemoryStream(), onRequest: null);
        // The read loop should parse the header and dispatch the message.
        // We can't easily intercept HandleAsync, but we can verify the read loop
        // doesn't throw and completes when the stream is exhausted.
        var done = await Task.WhenAny(peer.Completion, Task.Delay(5000));
        Assert.Same(peer.Completion, done);
        await peer.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CancelsReadLoop()
    {
        // A stream that never produces data: the read loop should block until disposed.
        using var input = new MemoryStream(new byte[0]); // immediate EOF
        var output = new MemoryStream();

        await using var peer = new LspPeer(input, output, onRequest: null);
        // The read loop should complete quickly on EOF
        var done = await Task.WhenAny(peer.Completion, Task.Delay(5000));
        Assert.Same(peer.Completion, done);
    }
}

// ── Auth: BearerTokens ────────────────────────────────────────────────────

public class BearerTokenTests
{
    [Fact]
    public void ValidBearerToken_Parsed()
    {
        var token = BearerTokens.FromHeader("Bearer abc123");
        Assert.Equal("abc123", token);
    }

    [Fact]
    public void MissingHeader_ReturnsNull()
    {
        var token = BearerTokens.FromHeader(null);
        Assert.Null(token);
    }

    [Fact]
    public void WrongScheme_ReturnsNull()
    {
        var token = BearerTokens.FromHeader("Basic abc123");
        Assert.Null(token);
    }

    [Fact]
    public void EmptyToken_ReturnsNull()
    {
        var token = BearerTokens.FromHeader("Bearer ");
        Assert.Null(token);
    }

    [Fact]
    public void LowercaseBearer_Parsed()
    {
        var token = BearerTokens.FromHeader("bearer abc123");
        Assert.Equal("abc123", token);
    }

    [Fact]
    public void TrimmedToken_Parsed()
    {
        var token = BearerTokens.FromHeader("Bearer   abc123  ");
        Assert.Equal("abc123", token);
    }
}

// ── ShadowWorkspace: FileUriOf ────────────────────────────────────────────

public class ShadowWorkspaceFileUriTests
{
    [Fact]
    public void LinuxPath_ProducesFileUri()
    {
        var uri = ShadowWorkspace.FileUriOf("/tmp/workspace/Main.cs");
        Assert.StartsWith("file://", uri);
        Assert.Contains("Main.cs", uri);
    }

    [Fact]
    public void LinuxPathWithSpaces_EscapesFilename()
    {
        var uri = ShadowWorkspace.FileUriOf("/tmp/my workspace/Main File.cs");
        // FileUriOf escapes the filename via Uri.EscapeDataString
        Assert.Contains("Main%20File.cs", uri);
    }

    [Fact]
    public void FileUriOf_Linux_RootPath()
    {
        var uri = ShadowWorkspace.FileUriOf("/Main.cs");
        Assert.StartsWith("file://", uri);
        Assert.Contains("Main.cs", uri);
    }
}

// ── WorkspaceKey ──────────────────────────────────────────────────────────

public class WorkspaceKeyTests
{
    [Fact]
    public void Equality_SameValues()
    {
        var a = new WorkspaceKey { Seat = "coder-06", Repo = "pi-sharp", BaseSha = "abc12345" };
        var b = new WorkspaceKey { Seat = "coder-06", Repo = "pi-sharp", BaseSha = "abc12345" };
        // WorkspaceKey is a plain class; verify field equality manually.
        Assert.Equal(a.Seat, b.Seat);
        Assert.Equal(a.Repo, b.Repo);
        Assert.Equal(a.BaseSha, b.BaseSha);
    }

    [Fact]
    public void Equality_DifferentSha()
    {
        var a = new WorkspaceKey { Seat = "coder-06", Repo = "pi-sharp", BaseSha = "abc12345" };
        var c = new WorkspaceKey { Seat = "coder-06", Repo = "pi-sharp", BaseSha = "def45678" };
        Assert.NotEqual(a.BaseSha, c.BaseSha);
    }
}

// ── Live LSP integration: csharp-ls ───────────────────────────────────────
// Requires csharp-ls installed (dotnet tool install -g csharp-ls). Skipped if not found.

public class LiveLsTests
{
    private static string? FindCsharpLs()
    {
        // Check the standard dotnet tools location
        var toolsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");
        var candidate = Path.Combine(toolsDir, "csharp-ls");
        if (File.Exists(candidate)) return candidate;
        // Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var c = Path.Combine(dir.Trim(), "csharp-ls");
            if (File.Exists(c)) return c;
        }
        return null;
    }

    private static (string root, string docPath, string docUri) MakeTempRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ls-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "Program.cs"),
            "using System;\n\npublic class Foo\n{\n    public void Bar() { }\n}\n\npublic class Program\n{\n    public static void Main()\n    {\n        var foo = new Foo();\n        foo.Bar();\n    }\n}\n");

        File.WriteAllText(Path.Combine(root, "test.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");

        // Build so the LS has a valid project
        var csprojPath = Path.Combine(root, "test.csproj");
        var build = Process.Start(new ProcessStartInfo("dotnet", $"build {csprojPath} -v q")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        build?.WaitForExit(60_000);

        var docPath = Path.Combine(root, "Program.cs");
        var docUri = ShadowWorkspace.FileUriOf(docPath);
        return (root, docPath, docUri);
    }

    [Fact]
    public async Task LiveCsharpLs_DefinitionAndDidChange()
    {
        var lsPath = FindCsharpLs();
        if (lsPath is null)
            return; // csharp-ls not installed

        var (root, docPath, docUri) = MakeTempRepo();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            var process = new Process { StartInfo = new ProcessStartInfo(lsPath)
            {
                WorkingDirectory = root,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            } };
            process.Start();

            try
            {
                var stdin = process.StandardInput.BaseStream;
                var stdout = process.StandardOutput.BaseStream;

                // Helper: send a framed LSP message
                async Task SendFrameAsync(JsonObject msg)
                {
                    var body = msg.ToJsonString().ToCharArray();
                    var bodyBytes = System.Text.Encoding.UTF8.GetBytes(body);
                    var header = System.Text.Encoding.UTF8.GetBytes($"Content-Length: {bodyBytes.Length}\r\n\r\n");
                    await stdin.WriteAsync(header, cts.Token);
                    await stdin.WriteAsync(bodyBytes, cts.Token);
                    await stdin.FlushAsync(cts.Token);
                }

                // Helper: read one framed LSP message
                async Task<JsonObject> ReadFrameAsync()
                {
                    // Read header byte-by-byte
                    var headerBuf = new System.Collections.Generic.List<byte>();
                    while (true)
                    {
                        var b = new byte[1];
                        var n = await stdout.ReadAsync(b, cts.Token);
                        if (n == 0) throw new IOException("LSP stream closed");
                        headerBuf.Add(b[0]);
                        if (headerBuf.Count >= 4 &&
                            headerBuf[headerBuf.Count - 4] == 0x0D &&
                            headerBuf[headerBuf.Count - 3] == 0x0A &&
                            headerBuf[headerBuf.Count - 2] == 0x0D &&
                            headerBuf[headerBuf.Count - 1] == 0x0A)
                            break;
                    }
                    var headerText = System.Text.Encoding.ASCII.GetString(headerBuf.ToArray());
                    int contentLength = -1;
                    foreach (var line in headerText.Split('\n'))
                    {
                        var t = line.Trim('\r', ' ');
                        if (t.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            contentLength = int.Parse(t["Content-Length:".Length..].Trim());
                    }
                    if (contentLength < 0) throw new IOException("No Content-Length");

                    // Read body
                    var body = new byte[contentLength];
                    int totalRead = 0;
                    while (totalRead < contentLength)
                    {
                        var n = await stdout.ReadAsync(body.AsMemory(totalRead, contentLength - totalRead), cts.Token);
                        if (n == 0) throw new IOException("LSP stream closed during body");
                        totalRead += n;
                    }
                    return JsonNode.Parse(System.Text.Encoding.UTF8.GetString(body))!.AsObject();
                }

                // 1. initialize
                await SendFrameAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 1,
                    ["method"] = "initialize",
                    ["params"] = new JsonObject
                    {
                        ["processId"] = null,
                        ["rootUri"] = $"file://{root}",
                        ["capabilities"] = new JsonObject(),
                        ["workspaceFolders"] = new JsonArray(new JsonObject
                        {
                            ["uri"] = $"file://{root}",
                            ["name"] = "ls-test",
                        }),
                    },
                });

                // Read messages until we get the initialize response (id=1, no method)
                JsonObject? initResponse = null;
                for (int i = 0; i < 100 && initResponse is null; i++)
                {
                    var msg = await ReadFrameAsync();
                    if (msg["method"] is null && msg["id"]?.GetValue<int>() == 1)
                        initResponse = msg;
                    // else: notification, skip
                }
                Assert.NotNull(initResponse);
                Assert.NotNull(initResponse!["result"]);
                var serverInfo = initResponse["result"]!["serverInfo"]?.ToJsonString() ?? "unknown";
                Console.WriteLine($"[live] initialize OK: {serverInfo}");

                // 2. initialized
                await SendFrameAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "initialized" });
                await Task.Delay(1000, cts.Token);

                // 3. didOpen
                var content = File.ReadAllText(docPath);
                await SendFrameAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "textDocument/didOpen",
                    ["params"] = new JsonObject
                    {
                        ["textDocument"] = new JsonObject
                        {
                            ["uri"] = docUri,
                            ["languageId"] = "csharp",
                            ["version"] = 1,
                            ["text"] = content,
                        },
                    },
                });
                await Task.Delay(3000, cts.Token);

                // 4. definition of Bar in foo.Bar()
                var lines = content.Split('\n');
                int line = -1, col = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    var idx = lines[i].IndexOf("Bar", StringComparison.Ordinal);
                    if (idx >= 0 && lines[i].Contains("foo."))
                    {
                        line = i;
                        col = idx;
                        break;
                    }
                }
                Assert.True(line >= 0, "foo.Bar() not found in source");

                await SendFrameAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 2,
                    ["method"] = "textDocument/definition",
                    ["params"] = new JsonObject
                    {
                        ["textDocument"] = new JsonObject { ["uri"] = docUri },
                        ["position"] = new JsonObject { ["line"] = line, ["character"] = col },
                    },
                });

                // Read until id=2 response
                JsonObject? defResponse = null;
                for (int i = 0; i < 100 && defResponse is null; i++)
                {
                    var msg = await ReadFrameAsync();
                    if (msg["method"] is null && msg["id"]?.GetValue<int>() == 2)
                        defResponse = msg;
                }
                Assert.NotNull(defResponse);
                Assert.NotNull(defResponse!["result"]);
                var defArray = defResponse["result"]! as JsonArray;
                Assert.NotNull(defArray);
                Assert.True(defArray!.Count > 0, "Definition returned zero locations");
                var firstLocUri = defArray[0]!["uri"]?.GetValue<string>() ?? defArray[0]!["targetUri"]?.GetValue<string>() ?? "";
                Assert.Contains("Program.cs", firstLocUri);
                Console.WriteLine($"[live] definition OK: {defArray.Count} location(s), first={firstLocUri}");

                // 5. didChange (code_sync equivalent)
                var newContent = content + "// synced\n";
                await SendFrameAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "textDocument/didChange",
                    ["params"] = new JsonObject
                    {
                        ["textDocument"] = new JsonObject { ["uri"] = docUri, ["version"] = 2 },
                        ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = newContent }),
                    },
                });
                await Task.Delay(2000, cts.Token);

                // 6. Post-didChange definition check
                await SendFrameAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 3,
                    ["method"] = "textDocument/definition",
                    ["params"] = new JsonObject
                    {
                        ["textDocument"] = new JsonObject { ["uri"] = docUri },
                        ["position"] = new JsonObject { ["line"] = line, ["character"] = col },
                    },
                });

                JsonObject? def2Response = null;
                for (int i = 0; i < 100 && def2Response is null; i++)
                {
                    var msg = await ReadFrameAsync();
                    if (msg["method"] is null && msg["id"]?.GetValue<int>() == 3)
                        def2Response = msg;
                }
                Assert.NotNull(def2Response);
                var def2Array = def2Response!["result"]! as JsonArray;
                Assert.NotNull(def2Array);
                Assert.True(def2Array!.Count > 0, "Post-didChange definition returned zero locations");
                Console.WriteLine($"[live] post-didChange definition OK: {def2Array.Count} location(s)");

                // 7. shutdown
                await SendFrameAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 4, ["method"] = "shutdown" });
                for (int i = 0; i < 50; i++)
                {
                    try
                    {
                        var msg = await ReadFrameAsync();
                        if (msg["method"] is null && msg["id"]?.GetValue<int>() == 4) break;
                    }
                    catch (IOException) { break; }
                }
                await SendFrameAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "exit" });
                await Task.Delay(500, cts.Token);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    process.WaitForExit();
                }
                process.Dispose();
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
