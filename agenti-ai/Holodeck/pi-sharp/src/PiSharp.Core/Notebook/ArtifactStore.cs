using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Notebook;

/// <summary>Content-addressed originals, retained outside context. At the quota the oldest half is pruned rather than
/// the new artifact refused (digital-scribe, 2026-10-20: "clear bottom 50% of captures when reaching the limit");
/// every prune is logged to <see cref="PruneLogName"/> in the same directory.</summary>
public sealed class ArtifactStore
{
    public const long DefaultQuotaBytes = 256L * 1024 * 1024;
    public const string PruneLogName = "prunes.jsonl";
    private readonly string _directory;
    private readonly long _quotaBytes;
    private readonly object _gate = new();
    public NotebookStore? Notebook { get; set; }
    public IEnumerable<(string Id, string Path)> PendingUploads() => Artifacts()
        .Where(f => f.Length <= 16 * 1024 * 1024 && !File.Exists(f.FullName + ".committed"))
        .Select(f => (Path.GetFileNameWithoutExtension(f.Name), f.FullName));
    public void MarkUploaded(string id) => File.WriteAllText(Path.Combine(_directory, id + ".json.committed"), "committed");

    /// <summary>Whether an artifact's bytes still hash to its name, as the KB checks before it stores one.</summary>
    public bool Intact(string id)
    {
        try
        {
            using var file = File.OpenRead(Path.Combine(_directory, id + ".json"));
            return string.Equals(Convert.ToHexString(SHA256.HashData(file)), id, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Sets aside an artifact whose bytes no longer hash to its name (<c>&lt;id&gt;.json.corrupt</c>), so it is not offered for
    /// upload again, and marks its notebook record superseded. The KB refuses such a file for good ("Artifact size or hash
    /// mismatch"), and until 2026-10-30 the seat offered it again on every turn: pi13's uploads stopped behind one file cut
    /// short by a restart (00:02Z), with "400 (Bad Request)" in its log every turn for hours.
    /// </summary>
    public void SetAside(string id)
    {
        lock (_gate)
        {
            var path = Path.Combine(_directory, id + ".json");
            if (File.Exists(path)) File.Move(path, path + ".corrupt", overwrite: true);
            MarkPruned(id, uploaded: false, why: "set aside: its bytes no longer match its hash (cut short when written); never uploaded");
        }
    }
    public ArtifactStore(string directory, long quotaBytes = DefaultQuotaBytes)
    {
        _directory = Path.GetFullPath(directory); Directory.CreateDirectory(_directory);
        _quotaBytes = quotaBytes > 0 ? quotaBytes : throw new ArgumentOutOfRangeException(nameof(quotaBytes));
    }
    // Artifact files only: "<64 hex>.json". The upload markers and the prune log are not artifacts.
    private IEnumerable<FileInfo> Artifacts() => new DirectoryInfo(_directory).EnumerateFiles("*.json")
        .Where(f => f.Name.Length == 69 && f.Name.EndsWith(".json", StringComparison.Ordinal) && f.Name[..64].All(char.IsAsciiHexDigit));
    private long UsedBytes() => new DirectoryInfo(_directory).EnumerateFiles().Sum(f => f.Length);
    public string Save(IReadOnlyList<ContentPart> parts)
    {
        var json = new JsonArray(parts.Select(p => (JsonNode)(p switch
        {
            ImageContent image => new JsonObject { ["type"] = "image", ["mimeType"] = image.MimeType, ["data"] = image.Data },
            TextContent text => new JsonObject { ["type"] = "text", ["text"] = text.Text },
            _ => new JsonObject { ["type"] = "text", ["text"] = p.ToString() }
        })).ToArray()).ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        var id = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        lock (_gate)
        {
            var path = Path.Combine(_directory, id + ".json");
            if (File.Exists(path)) { Record(id, bytes.Length); return id; }
            if (UsedBytes() + bytes.Length > _quotaBytes)
            {
                PruneOldestHalf();
                if (UsedBytes() + bytes.Length > _quotaBytes)
                    throw new IOException($"Artifact quota reached ({_quotaBytes / (1024 * 1024)} MiB) even after pruning the oldest half; preserve/export evidence before continuing.");
            }
            // Written aside and moved into place, so a seat stopped mid-write never leaves a short file under the full name.
            var partial = path + ".partial-" + Guid.NewGuid().ToString("N");
            using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes); file.Flush(true);
            }
            try { File.Move(partial, path); }
            catch (IOException) when (File.Exists(path)) { File.Delete(partial); } // the same bytes arrived first
            Record(id, bytes.Length);
        }
        return id;
    }

    /// <summary>Deletes the oldest half of the stored artifacts by write time (at least one when any exist), with their
    /// upload markers, marks their notebook records superseded and appends one line to <see cref="PruneLogName"/>.
    /// Returns how many were pruned.</summary>
    public int PruneOldestHalf()
    {
        lock (_gate)
        {
            var artifacts = Artifacts().OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
            if (artifacts.Count == 0) return 0;
            var victims = artifacts.Take(Math.Max(1, artifacts.Count / 2)).ToList();
            var ids = new JsonArray();
            long freed = 0;
            foreach (var file in victims)
            {
                var id = Path.GetFileNameWithoutExtension(file.Name);
                var uploaded = File.Exists(file.FullName + ".committed");
                freed += file.Length;
                file.Delete();
                File.Delete(file.FullName + ".committed");
                MarkPruned(id, uploaded);
                ids.Add((JsonNode)JsonValue.Create(id));
            }
            var oldestKept = artifacts.Count > victims.Count ? artifacts[victims.Count].LastWriteTimeUtc.ToString("O") : null;
            var line = new JsonObject { ["at"] = DateTime.UtcNow.ToString("O"), ["pruned"] = victims.Count, ["bytesFreed"] = freed,
                ["kept"] = artifacts.Count - victims.Count, ["oldestKept"] = oldestKept, ["ids"] = ids }.ToJsonString();
            File.AppendAllText(Path.Combine(_directory, PruneLogName), line + "\n");
            return victims.Count;
        }
    }

    // A pruned original can no longer be read here; its record says so instead of promising a retrieval. The KB copy, if
    // the artifact was uploaded, is untouched. A notebook failure must not stop the prune that frees the space.
    private void MarkPruned(string id, bool uploaded, string? why = null)
    {
        if (Notebook is not { } memory || memory.Get("artifact-" + id) is not { } record) return;
        try
        {
            var text = JsonNode.Parse(record["text"]!.GetValue<string>())!.AsObject();
            text["remoteAvailability"] = why ?? (uploaded ? "pruned locally at the artifact quota; the KB copy remains" : "pruned locally at the artifact quota; never uploaded");
            memory.Put("artifact-" + id, "artifact", text.ToJsonString(), record["task"]?.GetValue<string>() ?? "", "superseded",
                record["revision"]!.GetValue<long>());
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException) { }
    }
    private void Record(string id, int bytes)
    {
        if (Notebook is { } memory && memory.Get("artifact-" + id) is null)
            memory.Put("artifact-" + id, "artifact", new JsonObject { ["artifactId"] = id, ["bytes"] = bytes,
                ["format"] = "content-parts-json", ["remoteAvailability"] = "retrieve to verify; upload may be pending" }.ToJsonString(), "", "open", 0);
    }
    public JsonArray Read(string id)
    {
        if (id.Length != 64 || id.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Invalid artifact ID.");
        var bytes = File.ReadAllBytes(Path.Combine(_directory, id + ".json"));
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), id, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Artifact hash mismatch.");
        return JsonNode.Parse(bytes)!.AsArray();
    }
    public AgentToolResult Bound(AgentToolResult result)
    {
        var imageBytes = result.Content.OfType<ImageContent>().Sum(i => (long)i.Data.Length);
        if (result.Text.Length <= 8000 && imageBytes == 0) return result;
        var id = Save(result.Content);
        var text = result.Text;
        var excerpt = text.Length <= 7000 ? text : text[..3500] + "\n…\n" + text[^3500..];
        var parts = new List<ContentPart> { new TextContent(excerpt + $"\n[Full result artifact {id}; retrieve using artifact.]") };
        var image = result.Content.OfType<ImageContent>().FirstOrDefault(i => i.Data.Length <= 2_000_000);
        if (image is not null) parts.Add(image);
        return result with { Content = parts };
    }
    /// <summary>Assistant turns an image stays in the working context after it arrived.</summary>
    public const int ImageTurns = 2;

    public IReadOnlyList<Message> PrepareMessages(IReadOnlyList<Message> messages)
    {
        var result = messages.ToArray();
        var imagesLeft = 2;
        var batchCharacters = 16000;
        var turnsAfter = 0;
        for (var i = result.Length - 1; i >= 0; i--)
        {
            // Images leave by age, not by count: an image older than ImageTurns assistant turns is dropped. Keeping
            // "the newest two" changed a message wherever the third-newest image sat, however deep, and every
            // cached token after it was paid again; by age, the one message that changes is always near the end.
            if (result[i] is AssistantMessage && ++turnsAfter > ImageTurns) imagesLeft = 0;
            var parts = result[i] switch { ToolResultMessage t => t.Content, UserMessage u => u.Content, _ => null };
            if (result[i] is not ToolResultMessage) batchCharacters = 16000;
            if (parts is null) continue;
            var text = string.Concat(parts.OfType<TextContent>().Select(t => t.Text));
            var images = parts.OfType<ImageContent>().ToArray();
            var keep = images.Take(imagesLeft).Where(image => image.Data.Length <= 2_000_000).ToArray();
            imagesLeft -= keep.Length;
            var allowance = result[i] is ToolResultMessage ? Math.Min(8000, Math.Max(512, batchCharacters)) : int.MaxValue;
            if (result[i] is ToolResultMessage) batchCharacters -= Math.Min(text.Length, allowance);
            if (images.Length == keep.Length && text.Length <= allowance) continue;
            ContentPart[] bounded;
            if (result[i] is ToolResultMessage { ToolName: ArtifactTool.ToolName })
            {
                // An artifact's own result is already the original: storing it again chained new artifacts forever.
                bounded = [new TextContent(ArtifactTool.Cut(text, allowance)), .. keep];
                result[i] = ((ToolResultMessage)result[i]) with { Content = bounded };
                continue;
            }
            var id = Save(parts);
            var excerpt = text.Length > allowance ? text[..Math.Max(0, allowance - 160)] : text;
            bounded = [new TextContent(excerpt + $"\n[Original content: artifact {id}. Images omitted by working-context budget can be retrieved explicitly.]"), .. keep];
            result[i] = result[i] switch { ToolResultMessage t => t with { Content = bounded }, UserMessage u => u with { Content = bounded }, _ => result[i] };
        }
        return result;
    }
}

public sealed partial class ArtifactTool(ArtifactStore store) : IAgentTool
{
    public const string ToolName = "artifact";

    /// <summary>Characters of text one call returns, its header included: under the 8000 a tool result keeps in the
    /// working context, so a page is never stored again as an artifact of its own. Until 0.5.148 a page was 8000
    /// characters plus a trailing "[offset=...]" line: always over the limit, so the working context stored it as a new
    /// artifact and cut it before that line; the model saw only "[Original content: artifact N]", read N, and got the
    /// same page with a new pointer. Two demo seats read pages that way for an hour, 530 calls each (2026-10-29).</summary>
    public const int PageCharacters = 7600;

    public string Name => ToolName;
    public string Label => Name;
    public string Description => $"Read an exact original tool result by artifact ID. Text is paged by character offset ({PageCharacters} characters per call); the first line of each page gives the next offset and the total. Set image=true to retrieve its first image if within the 2 MB input budget. Artifacts are private to this agent.";
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Parallel;
    public JsonObject Parameters => JsonNode.Parse("""{"type":"object","properties":{"id":{"type":"string"},"offset":{"type":"integer","minimum":0},"image":{"type":"boolean"}},"required":["id"]}""")!.AsObject();
    public Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var id = arguments["id"]!.GetValue<string>();
        var records = store.Read(id);
        if (arguments["image"]?.GetValue<bool>() == true)
        {
            var image = records.FirstOrDefault(r => r?["type"]?.GetValue<string>() == "image") ?? throw new ArgumentException("Artifact has no image.");
            var data = image["data"]!.GetValue<string>();
            if (data.Length > 2_000_000) return Task.FromResult(AgentToolResult.Error("Image exceeds input budget. Capture a viewport or element instead."));
            return Task.FromResult(new AgentToolResult([new TextContent($"[artifact {id}; image]"), new ImageContent(data, image["mimeType"]!.GetValue<string>())]));
        }
        var text = string.Join("\n", records.Where(r => r?["type"]?.GetValue<string>() == "text").Select(r => r!["text"]!.GetValue<string>()));
        var offset = Math.Clamp(arguments["offset"]?.GetValue<int>() ?? 0, 0, text.Length);
        // The header leads, so a page cut by the working-context budget still says where to read on.
        var length = Math.Min(PageCharacters - Header(id, offset, text.Length, text.Length).Length, text.Length - offset);
        return Task.FromResult(AgentToolResult.FromText(Header(id, offset, offset + length, text.Length) + text.Substring(offset, length)));
    }

    private static string Header(string id, int offset, int next, int total) => $"[artifact {id}; offset={offset}; next={next}; total={total}]\n";

    /// <summary>An artifact page cut to <paramref name="allowance"/> characters for the working context. It is never
    /// stored again (that made the loop above): the cut keeps the header and says which offset of the same artifact to
    /// read next.</summary>
    public static string Cut(string text, int allowance)
    {
        if (text.Length <= allowance) return text;
        var header = PageHeader().Match(text);
        if (!header.Success)
            return text[..Math.Max(0, allowance - 60)] + "\n[Cut by the working-context budget.]";
        var keep = Math.Max(header.Length, allowance - 200);
        var resume = int.Parse(header.Groups["offset"].Value, System.Globalization.CultureInfo.InvariantCulture) + (keep - header.Length);
        return text[..keep] + $"\n[Cut by the working-context budget: read on with artifact {header.Groups["id"].Value} offset={resume}.]";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\A\[artifact (?<id>[0-9a-f]{64}); offset=(?<offset>\d+); next=\d+; total=\d+\]\n")]
    private static partial System.Text.RegularExpressions.Regex PageHeader();
}

public sealed class BoundedTool(IAgentTool inner, ArtifactStore artifacts) : IAgentTool
{
    public IAgentTool Inner => inner;
    public string Name => inner.Name;
    public string Label => inner.Label;
    public string Description => inner.Description;
    public string ShortDescription => inner.ShortDescription;
    public JsonObject Parameters => inner.Parameters;
    public ToolExecutionMode? ExecutionMode => ParallelSafeTools.IsParallelSafe(Name) ? ToolExecutionMode.Parallel : ToolExecutionMode.Sequential;
    public TimeSpan? MaxDuration => inner.MaxDuration;
    public ToolAccess? Access(JsonObject arguments) => inner.Access(arguments);
    public ValueTask<string?> ReadCacheKeyAsync(JsonObject arguments, CancellationToken cancellationToken) => inner.ReadCacheKeyAsync(arguments, cancellationToken);
    public ValueTask<bool> AcceptCachedReadAsync(JsonObject arguments, string key, CancellationToken cancellationToken) => inner.AcceptCachedReadAsync(arguments, key, cancellationToken);
    public JsonNode? PrepareArguments(JsonNode? arguments) => inner.PrepareArguments(arguments);
    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken) =>
        artifacts.Bound(await inner.ExecuteAsync(toolCallId, arguments, onUpdate is null ? null : r => onUpdate(r with
            { Content = [new TextContent(r.Text.Length > 8000 ? r.Text[^8000..] : r.Text)] }), cancellationToken).ConfigureAwait(false));
}
