// SeatMigrator against an in-memory KB (FakeHandler) and real git repositories in a temp directory: no server, no token.
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Factory;
using PiSharp.Core.Messages;
using PiSharp.Core.Session;

namespace PiSharp.Tests.Factory;

public class SeatMigratorTests : IDisposable
{
    private static readonly SeatRelease Release = new("pi-sharp", "0.5.70", SessionFormat.CurrentVersion, SeatRelease.NotebookJournalVersion, "test-host");
    private readonly string _root = Directory.CreateTempSubdirectory("seatmig-").FullName;

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    /// <summary>A KB with one pending migration for the caller; records what the seat sent.</summary>
    private sealed class Kb
    {
        public JsonObject? Pending { get; set; }
        public JsonObject? Packet { get; set; }
        public HttpStatusCode HelloStatus { get; set; } = HttpStatusCode.OK;

        public FakeHandler Handler() => new(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            string body = path switch
            {
                "/seat/hello" when HelloStatus != HttpStatusCode.OK => "{\"error\":\"down\"}",
                "/seat/hello" => "{\"ok\":true}",
                "/seat/migrations" => new JsonObject { ["migrations"] = Pending is null ? new JsonArray() : new JsonArray(Pending.DeepClone()) }.ToJsonString(),
                _ when path.EndsWith("/packet") && request.Method == HttpMethod.Get => Packet!.ToJsonString(),
                _ when path.EndsWith("/packet") => "{\"status\":\"placed\"}",
                _ => "{\"status\":\"ok\"}",
            };
            var status = path == "/seat/hello" ? HelloStatus : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        });
    }

    private SeatMigrator Migrator(FakeHandler handler, string actor, string work, string sessions, List<string>? log = null) =>
        new("http://kb.test:8081/mcp", "test-token", Release, handler)
        {
            Actor = actor, WorkDir = work, SessionDir = sessions, Log = line => log?.Add(line),
        };

    private static async Task<string> Git(string dir, params string[] args)
    {
        var (exit, output, err) = await SeatMigrator.RunGitAsync(dir, args, default);
        Assert.True(exit == 0, $"git {string.Join(' ', args)}: {err}");
        return output.Trim();
    }

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal)) gz.Write(Encoding.UTF8.GetBytes(text));
        return output.ToArray();
    }

    private static string Gunzip(byte[] data)
    {
        using var reader = new StreamReader(new GZipStream(new MemoryStream(data), CompressionMode.Decompress), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task Pack_PushesWorkOffTheDefaultBranch_AndUploadsSessionWithItsRelease()
    {
        var origin = Path.Combine(_root, "origin.git");
        Directory.CreateDirectory(origin);
        await Git(origin, "init", "--bare", "-b", "main");
        var work = Path.Combine(_root, "work");
        Directory.CreateDirectory(work);
        await Git(work, "clone", origin, "app");
        var app = Path.Combine(work, "app");
        await Git(app, "config", "user.email", "seat@test");
        await Git(app, "config", "user.name", "seat");
        await Git(app, "switch", "-c", "main");
        File.WriteAllText(Path.Combine(app, "a.txt"), "one");
        await Git(app, "add", "-A");
        await Git(app, "commit", "-m", "init");
        await Git(app, "push", "-u", "origin", "main");
        File.WriteAllText(Path.Combine(app, "a.txt"), "half done");

        var sessions = Path.Combine(_root, "sessions");
        var session = SessionManager.Create(work, sessions);
        session.AppendMessage(new UserMessage("work on wi-1"));

        var kb = new Kb { Pending = new JsonObject { ["id"] = 7, ["action"] = "pack", ["source"] = "seat-a", ["items"] = new JsonArray("wi-1") } };
        using var handler = kb.Handler();
        using var migrator = Migrator(handler, "seat-a", work, sessions);
        var start = await migrator.StartAsync(default);

        Assert.Same(MigrationStart.None, start);
        var hello = handler.Requests.First(r => r.Uri.AbsolutePath == "/seat/hello").Envelope;
        Assert.Equal("0.5.70", hello["version"]!.GetValue<string>());
        Assert.Equal("Bearer test-token", handler.Requests[0].Authorization);
        var upload = handler.Requests.Single(r => r.Uri.AbsolutePath == "/seat/migrations/7/packet").Envelope;
        Assert.Equal("0.5.70", upload["header"]!["version"]!.GetValue<string>());
        Assert.Equal(SessionFormat.CurrentVersion, upload["header"]!["sessionFormat"]!.GetValue<int>());
        var packet = JsonNode.Parse(Gunzip(Convert.FromBase64String(upload["data"]!.GetValue<string>())))!;
        Assert.Contains("work on wi-1", packet["session"]!.GetValue<string>());
        var repo = packet["repos"]![0]!;
        Assert.Equal("seat-a/migrate-7", repo["branch"]!.GetValue<string>());
        // The WIP is on the remote, on the migration branch; the default branch is untouched.
        Assert.Equal(repo["sha"]!.GetValue<string>(), await Git(origin, "rev-parse", "seat-a/migrate-7"));
        Assert.NotEqual(repo["sha"]!.GetValue<string>(), await Git(origin, "rev-parse", "main"));
        Assert.DoesNotContain(handler.Requests, r => r.Uri.AbsolutePath.EndsWith("/refuse"));
    }

    [Fact]
    public async Task Pack_WithNoSession_RefusesSoTheWorkStays()
    {
        var kb = new Kb { Pending = new JsonObject { ["id"] = 8, ["action"] = "pack", ["items"] = new JsonArray("wi-2") } };
        using var handler = kb.Handler();
        using var migrator = Migrator(handler, "seat-a", Path.Combine(_root, "none"), Path.Combine(_root, "no-sessions"));
        await migrator.StartAsync(default);
        var refusal = handler.Requests.Single(r => r.Uri.AbsolutePath == "/seat/migrations/8/refuse").Envelope;
        Assert.Contains("no session", refusal["reason"]!.GetValue<string>());
    }

    private JsonObject PacketFor(long id, string version, string session)
    {
        var inner = new JsonObject
        {
            ["format"] = 1, ["id"] = id, ["source"] = "seat-a", ["items"] = new JsonArray("wi-3"), ["cwd"] = "/home/seat-a/work",
            ["sessionName"] = "2026-10-04_abc.jsonl", ["session"] = session, ["notebook"] = "remember the flaky test",
            ["repos"] = new JsonArray(new JsonObject { ["path"] = "app", ["remote"] = "https://git.test/app.git", ["branch"] = "seat-a/migrate-9", ["sha"] = "abc123" }),
        };
        var data = Gzip(inner.ToJsonString());
        return new JsonObject
        {
            ["id"] = id, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(data)), ["data"] = Convert.ToBase64String(data),
            ["header"] = new JsonObject { ["harness"] = "pi-sharp", ["version"] = version, ["sessionFormat"] = SessionFormat.CurrentVersion, ["journalVersion"] = 1 },
        };
    }

    [Fact]
    public async Task Resume_OpensThePacketSession_WithANoteSayingWhoseItWasAndWhereTheWorkIs()
    {
        var source = SessionManager.Create("/home/seat-a/work", Path.Combine(_root, "source-sessions"));
        source.AppendMessage(new UserMessage("halfway through wi-3"));
        var kb = new Kb
        {
            Pending = new JsonObject { ["id"] = 9, ["action"] = "resume", ["source"] = "seat-a", ["target"] = "seat-b", ["items"] = new JsonArray("wi-3") },
            Packet = PacketFor(9, "0.5.70", File.ReadAllText(source.SessionFile!)),
        };
        using var handler = kb.Handler();
        var sessions = Path.Combine(_root, "target-sessions");
        using var migrator = Migrator(handler, "seat-b", Path.Combine(_root, "work-b"), sessions);
        var start = await migrator.StartAsync(default);

        Assert.Equal(9, start.ResumedId);
        var opened = SessionManager.Open(start.SessionFile!, Path.Combine(_root, "work-b"));
        Assert.Contains(opened.BuildContextMessages(), m => m is UserMessage u && u.Text.Contains("halfway through wi-3"));
        Assert.Contains("@seat-a", start.Note);
        Assert.Contains("https://git.test/app.git seat-a/migrate-9 @ abc123", start.Note);
        Assert.Contains("remember the flaky test", start.Note);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.AbsolutePath.EndsWith("/refuse"));
    }

    [Fact]
    public async Task Resume_OfAPacketFromAnotherRelease_IsRefused()
    {
        var kb = new Kb
        {
            Pending = new JsonObject { ["id"] = 10, ["action"] = "resume", ["items"] = new JsonArray("wi-3") },
            Packet = PacketFor(10, "0.5.69", "{}"),
        };
        using var handler = kb.Handler();
        var sessions = Path.Combine(_root, "target-sessions");
        using var migrator = Migrator(handler, "seat-b", _root, sessions);
        var start = await migrator.StartAsync(default);

        Assert.Same(MigrationStart.None, start);
        Assert.False(Directory.Exists(sessions) && Directory.EnumerateFiles(sessions).Any());
        var refusal = handler.Requests.Single(r => r.Uri.AbsolutePath == "/seat/migrations/10/refuse").Envelope;
        Assert.Contains("0.5.69", refusal["reason"]!.GetValue<string>());
        Assert.Contains("0.5.70", refusal["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task AKbThatCannotBeReached_MeansANormalStart()
    {
        var kb = new Kb { HelloStatus = HttpStatusCode.InternalServerError };
        using var handler = kb.Handler();
        var log = new List<string>();
        using var migrator = Migrator(handler, "seat-a", _root, _root, log);
        Assert.Same(MigrationStart.None, await migrator.StartAsync(default));
        Assert.Contains(log, l => l.Contains("hello failed"));
    }

    [Theory]
    [InlineData("https://seat:glpat-secret@gitlab.test/devs/app.git", "https://gitlab.test/devs/app.git")]
    [InlineData("https://gitlab.test/devs/app.git", "https://gitlab.test/devs/app.git")]
    [InlineData("git@gitlab.test:devs/app.git", "git@gitlab.test:devs/app.git")]
    public void RemotesTravelWithoutCredentials(string remote, string expected) =>
        Assert.Equal(expected, SeatMigrator.StripUserInfo(remote));
}
