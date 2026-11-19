using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Notebook;

public interface INotebookEmbeddings
{
    string Identity { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
    Task<IReadOnlyList<float[]>> EmbedRetrievalAsync(IReadOnlyList<string> documents, string? query, CancellationToken cancellationToken)
        => EmbedAsync(query is null ? documents : [.. documents, query], cancellationToken);
}

/// <summary>Optional owner/model-scoped vector sidecar. Exact records remain authoritative; this cache is rebuildable.</summary>
public sealed class NotebookSemanticIndex
{
    public const int BatchSize = 32, MaxEntries = 2048;
    private const long MaxBytes = 32 * 1024 * 1024;
    private readonly INotebookEmbeddings _provider;
    private readonly string _path, _owner;
    private readonly Dictionary<string, float[]> _vectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float[]> _queries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1);
    private DateTimeOffset _retryAfter;
    public string? LastError { get; private set; }

    public NotebookSemanticIndex(string directory, string owner, INotebookEmbeddings provider)
    {
        _provider = provider; _owner = owner;
        _path = Path.Combine(directory, "embeddings-" + Hash(owner + "\n" + provider.Identity)[..24] + ".json");
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > MaxBytes) return;
            var root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? throw new ArgumentException("Invalid embedding cache.");
            if (root["owner"]?.GetValue<string>() != owner || root["provider"]?.GetValue<string>() != provider.Identity) return;
            var rows = root["vectors"] as JsonObject ?? throw new ArgumentException("Invalid embedding cache vectors.");
            foreach (var pair in rows.Take(MaxEntries))
                _vectors[pair.Key] = Normalize((pair.Value as JsonArray ?? throw new ArgumentException("Invalid embedding cache vector.")).Select(n => n?.GetValue<float>() ?? float.NaN).ToArray());
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        { _vectors.Clear(); LastError = "Embedding cache will be rebuilt."; }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Text(JsonObject row) => NotebookStore.Lead((row["title"]?.GetValue<string>() ?? "") + "\n" + row["tags"]?.ToJsonString() + "\n" + row["text"]!.GetValue<string>(), 4000);
    private static string Key(JsonObject row) => Hash(row["id"]!.GetValue<string>() + "\n" + row["revision"] + "\n" + Text(row));
    private static float[] Normalize(float[] vector)
    {
        if (vector.Length is < 1 or > 8192 || vector.Any(v => !float.IsFinite(v))) throw new ArgumentException("Invalid embedding vector.");
        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        if (norm <= 0) throw new ArgumentException("Empty embedding vector.");
        return vector.Select(v => (float)(v / norm)).ToArray();
    }

    public async Task PrepareAsync(IReadOnlyList<JsonObject> records, string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || DateTimeOffset.UtcNow < _retryAfter) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dimension = _vectors.Values.FirstOrDefault()?.Length ?? 1;
            records = records.Take(Math.Min(MaxEntries, 2_000_000 / dimension)).ToArray();
            var wanted = records.Select(Key).ToHashSet(StringComparer.Ordinal);
            foreach (var key in _vectors.Keys.Where(k => !wanted.Contains(k)).ToArray()) _vectors.Remove(key);
            var pending = records.Where(r => !_vectors.ContainsKey(Key(r))).Take(BatchSize).ToArray();
            var queryKey = Hash(query);
            var needsQuery = !_queries.ContainsKey(queryKey);
            var documents = pending.Select(Text).ToArray();
            var queryText = needsQuery ? query.Length <= 4000 ? query : query[..4000] : null;
            var count = documents.Length + (queryText is null ? 0 : 1);
            if (count == 0) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var vectors = await _provider.EmbedRetrievalAsync(documents, queryText, timeout.Token).ConfigureAwait(false);
            if (vectors.Count != count) throw new ArgumentException("Embedding response count mismatch.");
            var normalized = vectors.Select(Normalize).ToArray();
            if (normalized.Select(v => v.Length).Distinct().Count() != 1
                || _vectors.Values.Any(v => v.Length != normalized[0].Length)) throw new ArgumentException("Embedding dimensions changed; use a new provider identity.");
            for (var i = 0; i < pending.Length; i++)
            {
                var capacity = Math.Min(MaxEntries, 2_000_000 / normalized[i].Length);
                if (_vectors.Count >= capacity) _vectors.Remove(_vectors.Keys.First());
                _vectors[Key(pending[i])] = normalized[i];
            }
            if (needsQuery)
            {
                if (_queries.Count >= 32) _queries.Remove(_queries.Keys.First());
                _queries[queryKey] = normalized[^1];
            }
            if (pending.Length > 0) Save();
            LastError = null;
        }
        catch (Exception ex) when ((ex is HttpRequestException or IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or OperationCanceledException) && !ct.IsCancellationRequested)
        { LastError = "Semantic recall unavailable; using lexical recall."; _retryAfter = DateTimeOffset.UtcNow.AddMinutes(1); }
        finally { _gate.Release(); }
    }

    public IReadOnlyList<NotebookHit> Search(IReadOnlyList<JsonObject> records, string query, int limit)
    {
        _gate.Wait();
        try
        {
            if (!_queries.TryGetValue(Hash(query), out var queryVector)) return [];
            return records.Select(r =>
            {
                var score = _vectors.TryGetValue(Key(r), out var v) && v.Length == queryVector.Length
                    ? v.Zip(queryVector, (a, b) => (double)a * b).Sum() : 0;
                return new NotebookHit((JsonObject)r.DeepClone(), score, NotebookStore.Lead(r["text"]!.GetValue<string>(), 240));
            }).Where(h => h.Score >= 0.55).OrderByDescending(h => h.Score)
              .ThenBy(h => h.Record["id"]!.GetValue<string>(), StringComparer.Ordinal).Take(limit).ToArray();
        }
        finally { _gate.Release(); }
    }

    private void Save()
    {
        var vectors = new JsonObject();
        foreach (var pair in _vectors) vectors[pair.Key] = new JsonArray(pair.Value.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
        var bytes = Encoding.UTF8.GetBytes(new JsonObject { ["owner"] = _owner, ["provider"] = _provider.Identity, ["vectors"] = vectors }.ToJsonString());
        if (bytes.Length > MaxBytes) return;
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, _path, true);
    }
}
