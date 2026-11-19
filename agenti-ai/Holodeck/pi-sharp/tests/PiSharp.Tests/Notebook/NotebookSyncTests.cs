using System.Net;
using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

public class NotebookSyncTests
{
    [Fact]
    public async Task LostReceiptReplaysSameOperationThenFreshDiskRestoresCommittedRecord()
    {
        using var disk = new TempWorkspace();
        var server = new Server();
        using (var store = new NotebookStore(disk.Path, "seat"))
        using (var sync = new NotebookSync("http://kb/mcp", "token", server))
        {
            store.Put("c", "commitment", "Never lose this", "t", "open", 0, operationId: "stable");
            await Assert.ThrowsAsync<HttpRequestException>(() => sync.FlushAsync(store, default));
            Assert.Equal(1, store.PendingCount);
        }
        using (var store = new NotebookStore(disk.Path, "seat"))
        using (var sync = new NotebookSync("http://kb/mcp", "token", server))
        {
            await sync.FlushAsync(store, default);
            Assert.Equal(0, store.PendingCount);
            Assert.Equal(2, server.Writes);
            Assert.Single(server.Operations);
        }
        using var fresh = new TempWorkspace();
        using var restored = new NotebookStore(fresh.Path, "seat");
        using var restore = new NotebookSync("http://kb/mcp", "token", server);
        await restore.FlushAsync(restored, default);
        Assert.Equal("Never lose this", restored.Get("c")!["text"]!.GetValue<string>());
        Assert.Equal(0, restored.PendingCount);
    }

    [Fact]
    public async Task WrongOwnerOrProtocolCannotAcknowledgePendingWrites()
    {
        using var disk = new TempWorkspace();
        using var store = new NotebookStore(disk.Path, "seat");
        store.Put("c", "commitment", "Keep", "t", "open", 0);
        foreach (var identity in new[] { "{\"owner\":\"other\",\"generation\":1,\"protocolVersion\":1}", "{\"owner\":\"seat\",\"generation\":1,\"protocolVersion\":2}" })
        {
            using var sync = new NotebookSync("http://kb", "token", new Server { Identity = identity });
            await Assert.ThrowsAsync<InvalidOperationException>(() => sync.FlushAsync(store, default));
            Assert.Equal(1, store.PendingCount);
        }
    }

    [Fact]
    public void ARestoreSkipsWhatTheSeatHolds_RefusesItsPendingRecords_AndWritesAPageAtOnce()
    {
        // team-lead-2 (2026-10-24/25): every start re-imported its whole notebook and scanned every operation it ever wrote
        // for each record, about an hour of CPU in "startup: still in 'notebook open; syncing it'".
        using var disk = new TempWorkspace();
        using var store = new NotebookStore(disk.Path, "seat");
        for (var i = 0; i < 3; i++) store.Put("n" + i, "note", "local " + i, "t", "open", 0);
        foreach (var op in store.Pending().Where(o => o["record"]!["id"]!.GetValue<string>() != "n2"))
            store.Acknowledge(op["operationId"]!.GetValue<string>());
        JsonObject Remote(string id, long revision, string text) =>
            new() { ["id"] = id, ["kind"] = "note", ["text"] = text, ["task"] = "t", ["status"] = "open", ["revision"] = revision };

        var journal = Directory.GetFiles(disk.Path, "*.jsonl", SearchOption.AllDirectories).Single();
        var before = File.ReadAllLines(journal).Length;
        store.ImportCommitted([Remote("n0", 1, "stale copy"), Remote("n1", 1, "stale copy")]);
        Assert.Equal(before, File.ReadAllLines(journal).Length); // held at that revision: nothing written
        Assert.Equal("local 0", store.Get("n0")!["text"]!.GetValue<string>());

        Assert.Throws<InvalidOperationException>(() => store.ImportCommitted([Remote("n2", 2, "remote")]));

        store.ImportCommitted([Remote("n0", 2, "newer"), Remote("n9", 1, "new"), Remote("n8", 1, "also new")]);
        Assert.Equal(before + 3, File.ReadAllLines(journal).Length);
        Assert.Equal("newer", store.Get("n0")!["text"]!.GetValue<string>());
        Assert.Equal("new", store.Get("n9")!["text"]!.GetValue<string>());

        store.Acknowledge(store.Pending().Single()["operationId"]!.GetValue<string>());
        store.ImportCommitted([Remote("n2", 2, "remote")]); // acknowledged: the remote snapshot may replace it now
        Assert.Equal("remote", store.Get("n2")!["text"]!.GetValue<string>());

        store.Dispose();
        using var reopened = new NotebookStore(disk.Path, "seat"); // the journal replays to the same state
        Assert.Equal("remote", reopened.Get("n2")!["text"]!.GetValue<string>());
        Assert.Equal(0, reopened.PendingCount);
    }

    /// <summary>2026-10-30: pi13's log said "400 (Bad Request)" every turn for hours and nobody could tell which record or why.
    /// A refusal now names the record and the server's reason, and the operation stays pending.</summary>
    [Fact]
    public async Task ARefusalNamesTheRecordAndTheServersReason()
    {
        using var disk = new TempWorkspace();
        using var store = new NotebookStore(disk.Path, "seat");
        store.Put("listing", "note", "SUM.LST", "t", "open", 0);
        using var sync = new NotebookSync("http://kb", "token", new Server { Refusal = "{\"error\":\"Record too large.\"}" });
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => sync.FlushAsync(store, default));
        Assert.Contains("400 (Bad Request) for record listing rev 1 (note, ", refused.Message);
        Assert.EndsWith(": Record too large.", refused.Message);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(1, store.PendingCount);
    }

    /// <summary>2026-10-30: one artifact cut short by a restart (00:02Z) kept pi13's uploads refused on every turn for hours. A
    /// file whose bytes no longer hash to its name is set aside and its record superseded; the next artifact goes up.</summary>
    [Fact]
    public async Task AnArtifactCutShortIsSetAsideAndTheNextOneGoesUp()
    {
        using var disk = new TempWorkspace();
        using var store = new NotebookStore(disk.Path, "seat");
        var dir = Path.Combine(disk.Path, "artifacts");
        var artifacts = new ArtifactStore(dir) { Notebook = store };
        var broken = artifacts.Save([new PiSharp.Core.Messages.TextContent("the listing that was cut short")]);
        var good = artifacts.Save([new PiSharp.Core.Messages.TextContent("a whole capture")]);
        var brokenPath = Path.Combine(dir, broken + ".json");
        File.WriteAllBytes(brokenPath, File.ReadAllBytes(brokenPath)[..10]); // stopped mid-write
        var setAside = new List<string>();
        var server = new Server { Writes = 1 }; // past the lost first receipt the other tests rely on
        using var sync = new NotebookSync("http://kb", "token", server) { Artifacts = artifacts, SetAside = setAside.Add };

        await sync.FlushAsync(store, default);

        Assert.Equal([broken], setAside);
        Assert.False(File.Exists(brokenPath));
        Assert.True(File.Exists(brokenPath + ".corrupt"));
        Assert.Equal([good], server.Artifacts);
        Assert.DoesNotContain(artifacts.PendingUploads(), a => a.Id == broken);
        Assert.Equal("superseded", store.Get("artifact-" + broken)!["status"]!.GetValue<string>());
        Assert.False(artifacts.Intact(broken));
        Assert.True(artifacts.Intact(good));
    }

    [Fact]
    public void AnArtifactIsWrittenAsideAndMovedIntoPlace()
    {
        using var disk = new TempWorkspace();
        var artifacts = new ArtifactStore(disk.Path);
        var id = artifacts.Save([new PiSharp.Core.Messages.TextContent("x")]);
        Assert.Equal(id, artifacts.Save([new PiSharp.Core.Messages.TextContent("x")]));
        Assert.True(artifacts.Intact(id));
        Assert.Empty(Directory.EnumerateFiles(disk.Path, "*.partial-*"));
    }

    private sealed class Server : HttpMessageHandler
    {
        public string Identity = "{\"owner\":\"seat\",\"generation\":1,\"protocolVersion\":1}";
        public Dictionary<string, JsonObject> Operations = [];
        public int Writes;
        /// <summary>The artifact IDs stored (PUT /notebook/artifacts/{id}).</summary>
        public List<string> Artifacts = [];
        /// <summary>A 400 body every write gets instead of the first one's lost receipt.</summary>
        public string? Refusal;
        protected override void Dispose(bool disposing) { } // reused across simulated client restarts
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            if (request.Method == HttpMethod.Post)
            {
                var op = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                var id = op["operationId"]!.GetValue<string>();
                if (Refusal is not null) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(Refusal) };
                Operations.TryAdd(id, op);
                if (++Writes == 1) throw new HttpRequestException("Response lost after durable commit");
                return Json(new JsonObject { ["operationId"] = id, ["committed"] = true, ["revision"] = op["record"]!["revision"]!.DeepClone() }.ToJsonString());
            }
            if (request.Method == HttpMethod.Put)
            {
                var id = request.RequestUri!.AbsolutePath["/notebook/artifacts/".Length..];
                Artifacts.Add(id);
                return Json(new JsonObject { ["committed"] = true, ["sha256"] = id }.ToJsonString());
            }
            if (request.RequestUri!.AbsolutePath == "/notebook") return Json(Identity);
            var rows = request.RequestUri.Query == "?after=" || request.RequestUri.Query.Length == 0
                ? Operations.Values.Select(o => o["record"]!.DeepClone()).ToArray() : [];
            return Json(new JsonArray(rows).ToJsonString());
        }
        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    }
}
