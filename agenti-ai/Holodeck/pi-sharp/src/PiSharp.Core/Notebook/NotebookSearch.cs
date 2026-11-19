using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Notebook;

/// <summary>One ranked search hit: the record (a copy), its score and a short snippet of the text around the match.</summary>
public sealed record NotebookHit(JsonObject Record, double Score, string Snippet);

/// <summary>Ranked full-text search over a notebook's title, tags, id and text.
/// <para>The seat's authoritative notebook is its local journal (the KB's pi_notebooks schemas are a replica written
/// only through the KB's operation endpoint), so search runs in-process over the records the journal already holds
/// in memory. Semantics follow PostgreSQL's <c>'simple'</c> configuration and <c>websearch_to_tsquery</c>: words are
/// lower-cased runs of letters and digits with no stemming or stop words; the query takes plain words (all required),
/// <c>"quoted phrases"</c>, <c>or</c> between two words, and <c>-word</c> to exclude. Ranking is BM25 with
/// title and tags weighted above the id, and the id above the text, like <c>setweight</c> A/B/C.</para>
/// <para>When no record matches every word, the search falls back to any word (ranked), and says so: seats phrase
/// queries as sentences, and a strict AND over a sentence finds nothing.</para></summary>
public sealed class NotebookSearchIndex
{
    private const double K1 = 1.2, B = 0.75;
    private const double TitleWeight = 3, TagWeight = 3, IdWeight = 2, TextWeight = 1;
    private readonly Dictionary<string, Document> _documents = new(StringComparer.Ordinal);

    private sealed class Document
    {
        public required long Revision { get; init; }
        public required string UpdatedAt { get; init; }
        public required string[] Title { get; init; }
        public required string[] Tags { get; init; }
        public required string[] Id { get; init; }
        public required string[] Text { get; init; }
        public required Dictionary<string, double> Weighted { get; init; }
        public double Length => Title.Length + Tags.Length + Id.Length + Text.Length;
    }

    /// <summary>A query term or phrase (a phrase is several words that must be adjacent).</summary>
    private sealed record Item(string[] Words);
    /// <summary>A required clause: any one of its alternatives must match (<c>a or b</c>).</summary>
    private sealed record Clause(List<Item> Alternatives);
    private sealed record Query(List<Clause> Required, List<Item> Excluded)
    {
        public IEnumerable<string> PositiveWords => Required.SelectMany(c => c.Alternatives).SelectMany(i => i.Words).Distinct(StringComparer.Ordinal);
    }

    /// <summary>Lower-cased runs of letters and digits, like the 'simple' configuration's word tokens.</summary>
    public static string[] Tokenize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) { current.Append(char.ToLowerInvariant(c)); continue; }
            if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) words.Add(current.ToString());
        return [.. words];
    }

    private static Query Parse(string query)
    {
        var tokens = new List<(string Text, bool Quoted, bool Negated)>();
        var i = 0;
        while (i < query.Length)
        {
            if (char.IsWhiteSpace(query[i])) { i++; continue; }
            var negated = false;
            if (query[i] == '-' && i + 1 < query.Length && !char.IsWhiteSpace(query[i + 1])) { negated = true; i++; }
            if (query[i] == '"')
            {
                var end = query.IndexOf('"', i + 1);
                if (end < 0) end = query.Length;
                tokens.Add((query[(i + 1)..end], true, negated));
                i = Math.Min(query.Length, end + 1);
                continue;
            }
            var start = i;
            while (i < query.Length && !char.IsWhiteSpace(query[i]) && query[i] != '"') i++;
            tokens.Add((query[start..i], false, negated));
        }
        var required = new List<Clause>();
        var excluded = new List<Item>();
        var joinNext = false;
        foreach (var (text, quoted, negated) in tokens)
        {
            if (!quoted && !negated && text.Equals("or", StringComparison.OrdinalIgnoreCase))
            {
                joinNext = required.Count > 0;
                continue;
            }
            var words = Tokenize(text);
            if (words.Length == 0) { joinNext = false; continue; }
            var item = new Item(words);
            if (negated) { excluded.Add(item); joinNext = false; continue; }
            if (joinNext) required[^1].Alternatives.Add(item);
            else required.Add(new Clause([item]));
            joinNext = false;
        }
        return new Query(required, excluded);
    }

    private Document Index(JsonObject record)
    {
        var id = record["id"]!.GetValue<string>();
        var revision = record["revision"]?.GetValue<long>() ?? 0;
        var updatedAt = record["updatedAt"]?.GetValue<string>() ?? "";
        if (_documents.TryGetValue(id, out var cached) && cached.Revision == revision && cached.UpdatedAt == updatedAt) return cached;
        var title = Tokenize(record["title"]?.GetValue<string>());
        var tags = (record["tags"] as JsonArray ?? []).SelectMany(t => Tokenize(t?.GetValue<string>())).ToArray();
        var idWords = Tokenize(id);
        var text = Tokenize(record["text"]?.GetValue<string>());
        var weighted = new Dictionary<string, double>(StringComparer.Ordinal);
        void Add(string[] words, double weight) { foreach (var w in words) weighted[w] = weighted.GetValueOrDefault(w) + weight; }
        Add(title, TitleWeight); Add(tags, TagWeight); Add(idWords, IdWeight); Add(text, TextWeight);
        var document = new Document { Revision = revision, UpdatedAt = updatedAt, Title = title, Tags = tags, Id = idWords, Text = text, Weighted = weighted };
        _documents[id] = document;
        return document;
    }

    private static bool Matches(Document d, Item item) => item.Words.Length == 1
        ? d.Weighted.ContainsKey(item.Words[0])
        : ContainsPhrase(d.Title, item.Words) || ContainsPhrase(d.Tags, item.Words) || ContainsPhrase(d.Id, item.Words) || ContainsPhrase(d.Text, item.Words);

    private static bool ContainsPhrase(string[] field, string[] phrase)
    {
        for (var i = 0; i + phrase.Length <= field.Length; i++)
        {
            var j = 0;
            while (j < phrase.Length && field[i + j] == phrase[j]) j++;
            if (j == phrase.Length) return true;
        }
        return false;
    }

    /// <summary>Ranks <paramref name="records"/> against <paramref name="query"/>. <paramref name="mode"/> is "all" when
    /// every word matched, "any" when the search fell back to any word, "none" for an empty query.</summary>
    public IReadOnlyList<NotebookHit> Search(IEnumerable<JsonObject> records, string query, int limit, out string mode)
    {
        var parsed = Parse(query ?? "");
        var candidates = records.Select(r => (Record: r, Doc: Index(r))).ToArray();
        if (parsed.Required.Count == 0 || candidates.Length == 0) { mode = parsed.Required.Count == 0 ? "none" : "all"; return []; }
        var allowed = candidates.Where(c => !parsed.Excluded.Any(x => Matches(c.Doc, x))).ToArray();
        var strict = allowed.Where(c => parsed.Required.All(clause => clause.Alternatives.Any(a => Matches(c.Doc, a)))).ToArray();
        mode = "all";
        var pool = strict;
        if (pool.Length == 0)
        {
            mode = "any";
            pool = allowed.Where(c => parsed.Required.Any(clause => clause.Alternatives.Any(a => Matches(c.Doc, a)))).ToArray();
        }
        if (pool.Length == 0) return [];
        var words = parsed.PositiveWords.ToArray();
        var average = candidates.Average(c => Math.Max(1, c.Doc.Length));
        var n = candidates.Length;
        var idf = words.ToDictionary(w => w, w =>
        {
            var df = candidates.Count(c => c.Doc.Weighted.ContainsKey(w));
            return Math.Log(1 + (n - df + 0.5) / (df + 0.5));
        }, StringComparer.Ordinal);
        return pool.Select(c =>
            {
                var score = 0.0;
                foreach (var w in words)
                {
                    if (!c.Doc.Weighted.TryGetValue(w, out var tf)) continue;
                    score += idf[w] * tf * (K1 + 1) / (tf + K1 * (1 - B + B * Math.Max(1, c.Doc.Length) / average));
                }
                return (c.Record, Score: score);
            })
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.Record["updatedAt"]?.GetValue<string>() ?? "", StringComparer.Ordinal)
            .ThenBy(h => h.Record["id"]!.GetValue<string>(), StringComparer.Ordinal)
            .Take(Math.Max(1, limit))
            .Select(h => new NotebookHit((JsonObject)h.Record.DeepClone(), Math.Round(h.Score, 3), Snippet(h.Record["text"]?.GetValue<string>() ?? "", words)))
            .ToArray();
    }

    /// <summary>About <paramref name="width"/> characters of text around the first query word, whitespace collapsed.</summary>
    public static string Snippet(string text, IReadOnlyCollection<string> words, int width = 160)
    {
        var at = -1;
        foreach (var w in words)
        {
            var i = FindWord(text, w);
            if (i >= 0 && (at < 0 || i < at)) at = i;
        }
        var start = at < 0 ? 0 : Math.Max(0, at - width / 3);
        var length = Math.Min(width, text.Length - start);
        var body = string.Join(' ', text.Substring(start, length).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return (start > 0 ? "..." : "") + body + (start + length < text.Length ? "..." : "");
    }

    private static int FindWord(string text, string word)
    {
        var from = 0;
        while (from < text.Length)
        {
            var i = text.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return -1;
            var before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            var after = i + word.Length >= text.Length || !char.IsLetterOrDigit(text[i + word.Length]);
            if (before && after) return i;
            from = i + 1;
        }
        return -1;
    }

    public void Forget(string id) => _documents.Remove(id);

    internal static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
