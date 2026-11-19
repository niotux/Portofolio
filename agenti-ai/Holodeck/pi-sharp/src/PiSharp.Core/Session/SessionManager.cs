// Ported from pi: packages/coding-agent/src/core/session-manager.ts (linear subset) @ 71dca871b
// Linear JSONL: header + message / compaction / model_change / session_info. No branching.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Json;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Session;

/// <summary>Append-only JSONL session store. Context is rebuilt from the latest compaction.</summary>
public sealed class SessionManager
{
    private readonly List<JsonObject> _fileEntries = [];
    private readonly List<SessionEntry> _entries = [];
    private readonly Dictionary<string, SessionEntry> _byId = new(StringComparer.Ordinal);
    private readonly bool _persist;
    private bool _flushed;
    private string? _sessionFile;
    private string _sessionId = "";
    private string? _leafId;

    private SessionManager(string cwd, string sessionDir, bool persist)
    {
        Cwd = cwd;
        SessionDir = sessionDir;
        _persist = persist;
    }

    public string Cwd { get; }
    public string SessionDir { get; }
    public string SessionId => _sessionId;
    public string? SessionFile => _sessionFile;
    public string? LeafId => _leafId;
    public bool Persist => _persist;

    public SessionHeader? Header { get; private set; }

    public IReadOnlyList<SessionEntry> Entries => _entries;

    public static SessionManager InMemory(string? cwd = null) =>
        CreateNew(cwd ?? Directory.GetCurrentDirectory(), sessionDir: "", persist: false);

    public static SessionManager Create(string cwd, string? sessionDir = null)
    {
        var dir = sessionDir ?? DefaultSessionDir(cwd);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return CreateNew(cwd, dir, persist: true);
    }

    public static SessionManager Open(string path, string? cwdOverride = null)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory();
        var loaded = LoadEntriesFromFile(full);
        if (loaded.Count == 0)
        {
            if (File.Exists(full) && new FileInfo(full).Length > 0)
                throw new InvalidOperationException($"Session file is not a valid pi-sharp session: {full}");
            var cwd = cwdOverride ?? Directory.GetCurrentDirectory();
            var created = CreateNew(cwd, dir, persist: true);
            created._sessionFile = full;
            created.RewriteFile();
            created._flushed = true;
            return created;
        }

        var header = ParseHeader(loaded[0]) ?? throw new InvalidOperationException($"Session file is not a valid pi-sharp session: {full}");
        var manager = new SessionManager(cwdOverride ?? header.Cwd, dir, persist: true)
        {
            Header = header,
            _sessionId = header.Id,
            _sessionFile = full,
            _flushed = true,
        };
        manager._fileEntries.AddRange(loaded);
        manager.RebuildIndex();
        return manager;
    }

    /// <summary>A copy of a session under a new id whose header names the source (<see cref="SessionHeader.ParentSession"/>):
    /// a second attempt from the same point leaves the first untouched (Codex's fork; plan-pi-sharp-codex item 21).</summary>
    public static SessionManager Fork(string sourcePath, string cwd, string? sessionDir = null)
    {
        var source = Open(sourcePath, cwd);
        var fork = CreateNew(cwd, sessionDir ?? Path.GetDirectoryName(Path.GetFullPath(sourcePath))!, persist: true);
        var header = fork.Header! with { ParentSession = source.SessionId };
        fork.Header = header;
        fork._fileEntries.Clear();
        fork._fileEntries.Add(HeaderToJson(header));
        fork._fileEntries.AddRange(source._fileEntries.Skip(1));
        fork.RebuildIndex();
        fork.RewriteFile();
        fork._flushed = true;
        return fork;
    }

    public static SessionManager ContinueRecent(string cwd, string? sessionDir = null)
    {
        var dir = sessionDir ?? DefaultSessionDir(cwd);
        var recent = FindMostRecentSession(dir, cwd);
        return recent is null ? Create(cwd, dir) : Open(recent, cwd);
    }

    public static string DefaultSessionDir(string cwd) => Config.ConfigPaths.SessionDir(cwd);

    public static void AssertValidSessionId(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$"))
        {
            throw new ArgumentException(
                "Session id must be non-empty, contain only alphanumeric characters, '-', '_' and '.', and start and end with an alphanumeric character",
                nameof(id));
        }
    }

    public string AppendMessage(Message message) =>
        Append(new SessionMessageEntry { Id = "", Message = Normalize(message) });

    public string AppendCompaction(string summary, string firstKeptEntryId, int tokensBefore, CompactionDetails? details = null, Usage? usage = null) =>
        Append(new CompactionEntry
        {
            Id = "",
            Summary = summary,
            FirstKeptEntryId = firstKeptEntryId,
            TokensBefore = tokensBefore,
            Details = details,
            Usage = usage,
        });

    public string AppendCompactionLog(CompactionLogEntry entry) => Append(entry);

    public string AppendModelChange(string provider, string modelId) =>
        Append(new ModelChangeEntry { Id = "", Provider = provider, ModelId = modelId });

    public string AppendSessionInfo(string name) =>
        Append(new SessionInfoEntry { Id = "", Name = name.Replace('\r', ' ').Replace('\n', ' ').Trim() });

    /// <summary>pi-sharp-checkpoints-1: append one shadow-store snapshot record.</summary>
    public string AppendCheckpoint(CheckpointEntry entry) => Append(entry);

    /// <summary>
    /// pi-sharp-checkpoints-3: flip the checkpoint entry with this id to green. Entries are records
    /// and the session file is append-only, so the update rewrites the one entry in memory and, when
    /// persisting, rewrites the file (session files are small; <see cref="RewriteFile"/> is the same
    /// path the first entry takes). Returns the entry when found, null otherwise (the caller then
    /// keeps the green marker only in the shadow store).
    /// </summary>
    public CheckpointEntry? MarkCheckpointGreen(string entryId)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i] is CheckpointEntry { Id: var id } cp && string.Equals(id, entryId, StringComparison.Ordinal))
            {
                var updated = cp with { Green = true };
                _entries[i] = updated;
                _byId[entryId] = updated;
                var json = EntryToJson(updated);
                _fileEntries[i] = json;
                if (_persist) RewriteFile();
                return updated;
            }
        }
        return null;
    }

    public IReadOnlyList<SessionEntry> BuildContextEntries()
    {
        var path = _entries;
        CompactionEntry? compaction = null;
        foreach (var entry in path)
        {
            if (entry is CompactionEntry c) compaction = c;
        }

        if (compaction is null) return path;

        var compactionIdx = -1;
        for (var i = 0; i < path.Count; i++)
        {
            if (path[i].Id == compaction.Id)
            {
                compactionIdx = i;
                break;
            }
        }

        if (compactionIdx < 0) return path;

        var context = new List<SessionEntry> { compaction };
        var foundFirstKept = false;
        for (var i = 0; i < compactionIdx; i++)
        {
            var entry = path[i];
            if (entry.Id == compaction.FirstKeptEntryId) foundFirstKept = true;
            if (foundFirstKept) context.Add(entry);
        }
        for (var i = compactionIdx + 1; i < path.Count; i++) context.Add(path[i]);
        return context;
    }

    public IReadOnlyList<Message> BuildContextMessages()
    {
        var messages = new List<Message>();
        foreach (var entry in BuildContextEntries())
        {
            foreach (var message in EntryToMessages(entry)) messages.Add(message);
        }
        return messages;
    }

    public SessionContext BuildSessionContext()
    {
        string? provider = null;
        string? modelId = null;
        foreach (var entry in _entries)
        {
            if (entry is ModelChangeEntry change)
            {
                provider = change.Provider;
                modelId = change.ModelId;
            }
            else if (entry is SessionMessageEntry { Message: AssistantMessage assistant })
            {
                modelId = assistant.Model;
            }
        }
        return new SessionContext(BuildContextMessages(), provider, modelId);
    }

    public string? GetSessionName()
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i] is SessionInfoEntry info) return string.IsNullOrWhiteSpace(info.Name) ? null : info.Name;
        }
        return null;
    }

    public CompactionEntry? GetLatestCompaction()
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i] is CompactionEntry c) return c;
        }
        return null;
    }

    public static IReadOnlyList<Message> EntryToMessages(SessionEntry entry) => entry switch
    {
        SessionMessageEntry { Message: { } message } => [Normalize(message)],
        CompactionEntry compaction =>
        [
            new CompactionSummaryMessage(compaction.Summary, compaction.TokensBefore)
            {
                Timestamp = compaction.Timestamp,
            },
        ],
        _ => [],
    };

    public static List<JsonObject> ParseSessionLines(string content)
    {
        var entries = new List<JsonObject>();
        foreach (var line in content.Split('\n'))
        {
            var parsed = ParseLine(line);
            if (parsed is not null) entries.Add(parsed);
        }
        return entries;
    }

    private static SessionManager CreateNew(string cwd, string sessionDir, bool persist, string? id = null)
    {
        if (id is not null) AssertValidSessionId(id);
        var timestamp = DateTimeOffset.UtcNow;
        var sessionId = id ?? Guid.CreateVersion7().ToString("N");
        var header = new SessionHeader(sessionId, timestamp, cwd);
        var manager = new SessionManager(cwd, sessionDir, persist)
        {
            Header = header,
            _sessionId = sessionId,
        };
        manager._fileEntries.Add(HeaderToJson(header));
        if (persist)
        {
            var fileStamp = timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH-mm-ss-fffffff", CultureInfo.InvariantCulture) + "Z";
            manager._sessionFile = Path.Combine(sessionDir, $"{fileStamp}_{sessionId}.jsonl");
        }
        return manager;
    }

    private string Append(SessionEntry entry)
    {
        var id = GenerateId();
        var stamped = entry with { Id = id, ParentId = _leafId, Timestamp = DateTimeOffset.UtcNow };
        var json = EntryToJson(stamped);
        _fileEntries.Add(json);
        _entries.Add(stamped);
        _byId[id] = stamped;
        _leafId = id;
        PersistEntry(stamped, json);
        return id;
    }

    private void PersistEntry(SessionEntry entry, JsonObject json)
    {
        if (!_persist || _sessionFile is null) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_sessionFile)!);
        var line = json.ToJsonString(CoreJsonContext.Default.Options) + "\n";
        if (!_flushed)
        {
            RewriteFile();
            _flushed = true;
        }
        else
        {
            using var stream = new FileStream(_sessionFile, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(Encoding.UTF8.GetBytes(line));
            stream.Flush(true);
        }
    }

    private void RewriteFile()
    {
        if (!_persist || _sessionFile is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_sessionFile)!);
        using var stream = new FileStream(_sessionFile, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (var entry in _fileEntries)
            writer.Write(entry.ToJsonString(CoreJsonContext.Default.Options) + "\n");
        writer.Flush();
        stream.Flush(true);
    }

    private void RebuildIndex()
    {
        _entries.Clear();
        _byId.Clear();
        _leafId = null;
        foreach (var json in _fileEntries)
        {
            if (json["type"]?.GetValue<string>() == "session") continue;
            var entry = ParseEntry(json);
            if (entry is null) continue;
            _entries.Add(entry);
            _byId[entry.Id] = entry;
            _leafId = entry.Id;
        }
    }

    private string GenerateId()
    {
        for (var i = 0; i < 100; i++)
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            if (!_byId.ContainsKey(id)) return id;
        }
        return Guid.NewGuid().ToString("N");
    }

    private static Message Normalize(Message message)
    {
        if (message is UserMessage user && user.Content is null)
            return user with { Content = Array.Empty<ContentPart>() };
        if (message is AssistantMessage assistant && assistant.Content is null)
            return assistant with { Content = Array.Empty<ContentPart>() };
        if (message is ToolResultMessage tool && tool.Content is null)
            return tool with { Content = Array.Empty<ContentPart>() };
        return message;
    }

    private static JsonObject HeaderToJson(SessionHeader header)
    {
        var obj = new JsonObject
        {
            ["type"] = "session",
            ["version"] = header.Version,
            ["id"] = header.Id,
            ["timestamp"] = header.Timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            ["cwd"] = header.Cwd,
        };
        if (header.ParentSession is not null) obj["parentSession"] = header.ParentSession;
        return obj;
    }

    private static JsonObject EntryToJson(SessionEntry entry)
    {
        var obj = new JsonObject
        {
            ["id"] = entry.Id,
            ["parentId"] = entry.ParentId is null ? null : JsonValue.Create(entry.ParentId),
            ["timestamp"] = entry.Timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
        };
        switch (entry)
        {
            case SessionMessageEntry message:
                obj["type"] = "message";
                obj["message"] = JsonSerializer.SerializeToNode(message.Message, CoreJsonContext.Default.Message);
                break;
            case CompactionEntry compaction:
                obj["type"] = "compaction";
                obj["summary"] = compaction.Summary;
                obj["firstKeptEntryId"] = compaction.FirstKeptEntryId;
                obj["tokensBefore"] = compaction.TokensBefore;
                if (compaction.Details is not null)
                    obj["details"] = JsonSerializer.SerializeToNode(compaction.Details, CoreJsonContext.Default.CompactionDetails);
                if (compaction.Usage is not null)
                    obj["usage"] = JsonSerializer.SerializeToNode(compaction.Usage, CoreJsonContext.Default.Usage);
                break;
            case CompactionLogEntry log:
                obj["type"] = "compaction_log";
                if (log.Actor is not null) obj["actor"] = log.Actor;
                obj["trigger"] = log.Trigger;
                obj["tokensBefore"] = log.TokensBefore;
                obj["tokensAfter"] = log.TokensAfter;
                if (log.OpenTasks is int open) obj["openTasks"] = open;
                obj["saveBeforeCompact"] = log.SaveBeforeCompact;
                break;
            case ModelChangeEntry change:
                obj["type"] = "model_change";
                obj["provider"] = change.Provider;
                obj["modelId"] = change.ModelId;
                break;
            case CheckpointEntry checkpoint:
                obj["type"] = "checkpoint";
                obj["commit"] = checkpoint.Commit;
                obj["head"] = checkpoint.Head;
                if (checkpoint.ToolCallIds.Length > 0)
                {
                    var arr = new JsonArray();
                    foreach (var id in checkpoint.ToolCallIds) arr.Add((JsonNode?)JsonValue.Create(id));
                    obj["toolCallIds"] = arr;
                }
                obj["green"] = checkpoint.Green;
                obj["partial"] = checkpoint.Partial;
                if (checkpoint.RewoundTo is not null) obj["rewoundTo"] = checkpoint.RewoundTo;
                break;
            case SessionInfoEntry info:
                obj["type"] = "session_info";
                if (info.Name is not null) obj["name"] = info.Name;
                break;
            default:
                obj["type"] = "unknown";
                break;
        }
        return obj;
    }

    private static SessionHeader? ParseHeader(JsonObject json)
    {
        if (json["type"]?.GetValue<string>() != "session") return null;
        var id = json["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return null;
        return new SessionHeader(
            id,
            ReadTimestamp(json["timestamp"]) ?? DateTimeOffset.UtcNow,
            json["cwd"]?.GetValue<string>() ?? "",
            json["version"]?.GetValue<int>() ?? 1,
            json["parentSession"]?.GetValue<string>());
    }

    private static SessionEntry? ParseEntry(JsonObject json)
    {
        var type = json["type"]?.GetValue<string>();
        var id = json["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(id)) return null;
        string? parentId = null;
        if (json["parentId"] is JsonValue parent && parent.TryGetValue<string>(out var parentText))
            parentId = parentText;
        var timestamp = ReadTimestamp(json["timestamp"]) ?? DateTimeOffset.UtcNow;

        switch (type)
        {
            case "message":
            {
                Message? message = null;
                if (json["message"] is JsonNode node)
                    message = JsonSerializer.Deserialize(node, CoreJsonContext.Default.Message);
                if (message is null) return null;
                return new SessionMessageEntry { Id = id, ParentId = parentId, Timestamp = timestamp, Message = Normalize(message) };
            }
            case "compaction":
            {
                var summary = json["summary"]?.GetValue<string>() ?? "";
                var firstKept = json["firstKeptEntryId"]?.GetValue<string>() ?? "";
                var tokensBefore = json["tokensBefore"]?.GetValue<int>() ?? 0;
                CompactionDetails? details = null;
                if (json["details"] is JsonNode detailsNode)
                    details = JsonSerializer.Deserialize(detailsNode, CoreJsonContext.Default.CompactionDetails);
                Usage? usage = null;
                if (json["usage"] is JsonNode usageNode)
                    usage = JsonSerializer.Deserialize(usageNode, CoreJsonContext.Default.Usage);
                return new CompactionEntry
                {
                    Id = id,
                    ParentId = parentId,
                    Timestamp = timestamp,
                    Summary = summary,
                    FirstKeptEntryId = firstKept,
                    TokensBefore = tokensBefore,
                    Details = details,
                    Usage = usage,
                };
            }
            case "compaction_log":
                return new CompactionLogEntry
                {
                    Id = id,
                    ParentId = parentId,
                    Timestamp = timestamp,
                    Actor = json["actor"]?.GetValue<string>(),
                    Trigger = json["trigger"]?.GetValue<string>() ?? "",
                    TokensBefore = json["tokensBefore"]?.GetValue<int>() ?? 0,
                    TokensAfter = json["tokensAfter"]?.GetValue<int>() ?? 0,
                    OpenTasks = json["openTasks"]?.GetValue<int>(),
                    SaveBeforeCompact = json["saveBeforeCompact"]?.GetValue<string>() ?? "",
                };
            case "model_change":
                return new ModelChangeEntry
                {
                    Id = id,
                    ParentId = parentId,
                    Timestamp = timestamp,
                    Provider = json["provider"]?.GetValue<string>() ?? "",
                    ModelId = json["modelId"]?.GetValue<string>() ?? "",
                };
            case "checkpoint":
            {
                var commit = json["commit"]?.GetValue<string>();
                if (string.IsNullOrEmpty(commit)) return null;
                string[] toolCallIds = [];
                if (json["toolCallIds"] is JsonArray ids && ids.All(p => p is JsonValue jv && jv.TryGetValue<string>(out _)))
                    toolCallIds = [.. ids.OfType<JsonValue>().Where(p => p.TryGetValue<string>(out _)).Select(p => p.GetValue<string>()!)] ;
                return new CheckpointEntry
                {
                    Id = id,
                    ParentId = parentId,
                    Timestamp = timestamp,
                    Commit = commit,
                    Head = json["head"]?.GetValue<string>() ?? "",
                    ToolCallIds = toolCallIds,
                    Green = json["green"]?.GetValue<bool>() ?? false,
                    Partial = json["partial"]?.GetValue<bool>() ?? false,
                    RewoundTo = json["rewoundTo"]?.GetValue<string>(),
                };
            }
            case "session_info":
                return new SessionInfoEntry
                {
                    Id = id,
                    ParentId = parentId,
                    Timestamp = timestamp,
                    Name = json["name"]?.GetValue<string>(),
                };
            default:
                return null;
        }
    }

    private static DateTimeOffset? ReadTimestamp(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<string>(out var iso) && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed.ToUniversalTime();
        if (value.TryGetValue<long>(out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        if (value.TryGetValue<double>(out var msDouble))
            return DateTimeOffset.FromUnixTimeMilliseconds((long)msDouble);
        return null;
    }

    private static JsonObject? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<JsonObject> LoadEntriesFromFile(string path)
    {
        if (!File.Exists(path)) return [];
        var entries = new List<JsonObject>();
        foreach (var line in File.ReadLines(path))
        {
            var parsed = ParseLine(line);
            if (parsed is not null) entries.Add(parsed);
        }
        if (entries.Count == 0) return entries;
        if (entries[0]["type"]?.GetValue<string>() != "session" || entries[0]["id"] is null) return [];
        return entries;
    }

    public static string? FindMostRecentSession(string sessionDir, string? cwd = null)
    {
        if (!Directory.Exists(sessionDir)) return null;
        string? best = null;
        var bestTime = DateTime.MinValue;
        foreach (var file in Directory.EnumerateFiles(sessionDir, "*.jsonl"))
        {
            try
            {
                using var reader = new StreamReader(file, Encoding.UTF8);
                JsonObject? header = null;
                while (reader.ReadLine() is { } line)
                {
                    header = ParseLine(line);
                    if (header is not null) break;
                }
                if (header is null || header["type"]?.GetValue<string>() != "session") continue;
                if (cwd is not null)
                {
                    var headerCwd = header["cwd"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(headerCwd)) continue;
                    if (!PathsEqual(headerCwd, cwd)) continue;
                }
                var mtime = File.GetLastWriteTimeUtc(file);
                if (mtime >= bestTime)
                {
                    bestTime = mtime;
                    best = file;
                }
            }
            catch (IOException)
            {
                // Discovery is best-effort.
            }
        }
        return best;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
