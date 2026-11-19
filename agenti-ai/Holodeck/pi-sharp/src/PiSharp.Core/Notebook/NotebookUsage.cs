using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Notebook;

/// <summary>Append-only local usage sidecar. It is deliberately separate from notebook records and sync.</summary>
public sealed class NotebookUsage
{
    private const long MaxBytes = 32L * 1024 * 1024;
    private readonly string _path;
    private readonly string _owner;
    private readonly Dictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _archived = new(StringComparer.Ordinal);
    private readonly HashSet<(string Task, string Id, string Event)> _emitted = [];
    private readonly object _gate = new();

    public sealed record Counter(int RecalledCount, int UsedCount, DateTimeOffset? LastUsedAt, int Helpful = 0, int Harmful = 0);

    public NotebookUsage(string directory, string owner)
    {
        _path = Path.Combine(directory, "usage.jsonl");
        _owner = owner;
        Recover();
    }

    public Counter Get(string id)
    {
        lock (_gate) return _counters.GetValueOrDefault(id) ?? new Counter(0, 0, null);
    }

    public bool IsArchived(string id)
    {
        lock (_gate) return _archived.Contains(id);
    }

    /// <summary>Archive state is local curation metadata, outside the synced notebook payload and hash.</summary>
    public void SetArchived(string id, bool archived)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        lock (_gate)
        {
            if (_archived.Contains(id) == archived) return;
            Append(id, archived ? "archived" : "unarchived", DateTimeOffset.UtcNow);
        }
    }

    public bool Record(string id, string eventName, string taskKey, string? evidenceId = null)
    {
        if (eventName is not ("recalled" or "used" or "helpful" or "harmful") || string.IsNullOrWhiteSpace(taskKey)) return false;
        lock (_gate)
        {
            var marker = (taskKey, id, eventName);
            if (_emitted.Contains(marker)) return true;
            if (!Append(id, eventName, DateTimeOffset.UtcNow, taskKey, evidenceId)) return false;
            _emitted.Add(marker);
            return true;
        }
    }

    private bool Append(string id, string eventName, DateTimeOffset at, string? task = null, string? evidenceId = null)
    {
        long originalLength = -1;
        try
        {
            var line = new JsonObject { ["v"] = 1, ["owner"] = _owner, ["id"] = id, ["event"] = eventName, ["task"] = task, ["evidenceId"] = evidenceId, ["at"] = at.ToString("O", CultureInfo.InvariantCulture) }.ToJsonString() + "\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            using var stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            if (stream.Length + bytes.Length > MaxBytes) return false;
            originalLength = stream.Length;
            stream.Seek(0, SeekOrigin.End);
            stream.Write(bytes);
            stream.Flush(true);
            Fold(id, eventName, at);
            return true;
        }
        catch (IOException) { RollBackPartialAppend(originalLength); }
        catch (UnauthorizedAccessException) { RollBackPartialAppend(originalLength); }
        return false;
    }

    private void RollBackPartialAppend(long originalLength)
    {
        if (originalLength < 0) return;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.Read);
            stream.SetLength(originalLength);
            stream.Flush(true);
        }
        catch { }
    }

    private void Recover()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var bytes = File.ReadAllBytes(_path);
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Usage sidecar exceeds quota.");
            var start = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != (byte)'\n') continue;
                try
                {
                    var row = JsonNode.Parse(Encoding.UTF8.GetString(bytes, start, i - start))!.AsObject();
                    if (row["v"]?.GetValue<int>() != 1 || row["owner"]?.GetValue<string>() != _owner
                        || string.IsNullOrWhiteSpace(row["id"]?.GetValue<string>())
                        || row["event"]?.GetValue<string>() is not ("recalled" or "used" or "helpful" or "harmful" or "archived" or "unarchived")
                        || !DateTimeOffset.TryParse(row["at"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                        throw new InvalidDataException("Invalid usage event.");
                    var id = row["id"]!.GetValue<string>();
                    var eventName = row["event"]!.GetValue<string>();
                    var task = row["task"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(task) || _emitted.Add((task, id, eventName))) Fold(id, eventName, at);
                }
                catch { throw new InvalidDataException("Malformed completed usage event."); }
                start = i + 1;
            }
            if (start != bytes.Length)
            {
                File.WriteAllBytes(_path + ".torn-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), bytes[start..]);
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Write);
                stream.SetLength(start);
                stream.Flush(true);
            }
        }
        catch
        {
            var quarantine = _path + ".quarantine-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try { File.Move(_path, quarantine, true); } catch { }
            _counters.Clear();
            _emitted.Clear();
            _archived.Clear();
        }
    }

    private void Fold(string id, string eventName, DateTimeOffset at)
    {
        var old = _counters.GetValueOrDefault(id) ?? new Counter(0, 0, null);
        _counters[id] = old with
        {
            RecalledCount = old.RecalledCount + (eventName == "recalled" ? 1 : 0),
            UsedCount = old.UsedCount + (eventName == "used" ? 1 : 0),
            Helpful = old.Helpful + (eventName == "helpful" ? 1 : 0),
            Harmful = old.Harmful + (eventName == "harmful" ? 1 : 0),
            LastUsedAt = eventName is "used" or "helpful" or "harmful"
                ? old.LastUsedAt is null || at > old.LastUsedAt ? at : old.LastUsedAt : old.LastUsedAt
        };
        if (eventName == "archived") _archived.Add(id);
        else if (eventName == "unarchived") _archived.Remove(id);
    }
}
