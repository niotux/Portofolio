using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;
using PiSharp.Core.Session;

namespace PiSharp.Core.Factory;

/// <summary>What this harness is, as the KB matches seats on it (kb 1.0.85 <c>POST /seat/hello</c>).</summary>
public sealed record SeatRelease(string Harness, string Version, int SessionFormat, int JournalVersion, string Host)
{
    /// <summary>The notebook journal version NotebookStore writes and reads (its records say <c>"version": 1</c>).</summary>
    public const int NotebookJournalVersion = 1;

    public static SeatRelease PiSharp(string version) =>
        new("pi-sharp", version, Session.SessionFormat.CurrentVersion, NotebookJournalVersion, Environment.MachineName);
}

/// <summary>What startup has to do differently after the migration step: open this session file instead of a new one,
/// and put this note in front of the prompt. <see cref="ResumedId"/> is reported once the session opened.</summary>
public sealed record MigrationStart(string? SessionFile, string? Note, long? ResumedId)
{
    public static readonly MigrationStart None = new(null, null, null);
}

/// <summary>
/// Seat migration, the harness's half (kb 1.0.85). digital-scribe, 2026-10-04: "if a rate was hit the state of that
/// conversation inside that agent should be ported to another free agent ... the system should be self-healing"; and
/// "are we observing version matching so we don't attempt to resume on a mismatched version of pi-sharp?".
/// <para>Runs at startup, before the session opens, so it needs no model: a seat whose provider refuses it can still
/// pack. It says hello (harness, release, session format, journal version), asks the KB what it has to do, and then
/// either packs (the source of an approved migration: commits and pushes its work, uploads its last session and its
/// notebook briefing) or resumes (the target of a placed one, or a source whose target gave it back: downloads the packet,
/// checks it was written by this very release, and opens that session). The KB wakes both with a <c>/migrate-out</c> or
/// <c>/migrate-in</c> DM, which FactorySteering treats as a restart at the next safe point.</para>
/// <para>Nothing here may stop a seat from starting: every failure is logged, refused to the KB where it has a step,
/// and startup goes on as before.</para>
/// </summary>
public sealed class SeatMigrator : IDisposable
{
    /// <summary>The KB's limit on a packet (MigrationStore.MaxPacketBytes).</summary>
    public const long MaxPacketBytes = 64L * 1024 * 1024;

    public delegate Task<(int Exit, string Out, string Err)> GitRunner(string directory, IReadOnlyList<string> arguments, CancellationToken ct);

    private readonly HttpClient _http;
    private readonly SeatRelease _release;

    public SeatMigrator(string kbUrl, string token, SeatRelease release, HttpMessageHandler? handler = null)
    {
        var endpoint = new Uri(kbUrl);
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(endpoint.GetLeftPart(UriPartial.Authority));
        _http.Timeout = TimeSpan.FromMinutes(3);
        _http.MaxResponseContentBufferSize = 2 * MaxPacketBytes;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _release = release;
    }

    public Action<string> Log { get; init; } = _ => { };

    public GitRunner Git { get; init; } = RunGitAsync;

    /// <summary>The seat's actor slug (branch names and the resume note).</summary>
    public required string Actor { get; init; }

    /// <summary>Where the seat's clones live (pi-seat-exec starts in <c>~/work</c>).</summary>
    public required string WorkDir { get; init; }

    public required string SessionDir { get; init; }

    public string? NotebookDir { get; init; }

    public void Dispose() => _http.Dispose();

    /// <summary>Hello, then pack or resume. Never throws: a KB that cannot be reached means a normal start.</summary>
    public async Task<MigrationStart> StartAsync(CancellationToken ct)
    {
        JsonArray pending;
        try
        {
            await SendAsync(HttpMethod.Post, "/seat/hello", new JsonObject
            {
                ["harness"] = _release.Harness, ["version"] = _release.Version, ["sessionFormat"] = _release.SessionFormat,
                ["journalVersion"] = _release.JournalVersion, ["host"] = _release.Host,
            }, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            pending = (await SendAsync(HttpMethod.Get, "/seat/migrations", null, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false))?["migrations"] as JsonArray ?? [];
        }
        catch (Exception e) when (e is HttpRequestException or MigrationException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log($"[migration] hello failed, starting normally: {e.Message}");
            return MigrationStart.None;
        }

        MigrationStart start = MigrationStart.None;
        foreach (var m in pending.OfType<JsonObject>())
        {
            var id = m["id"]!.GetValue<long>();
            var action = m["action"]?.GetValue<string>();
            var items = (m["items"] as JsonArray)?.Select(i => i!.GetValue<string>()).ToArray() ?? [];
            if (action == "pack") await PackAsync(id, items, ct).ConfigureAwait(false);
            else if (action == "resume" && start.SessionFile is null) start = await ResumeAsync(id, ct).ConfigureAwait(false);
        }
        return start;
    }

    /// <summary>Tells the KB the resumed session is open; the KB then drops the packet bytes.</summary>
    public async Task ResumedAsync(long id, CancellationToken ct)
    {
        try
        {
            await SendAsync(HttpMethod.Post, $"/seat/migrations/{id}/resumed", new JsonObject(), TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            Log($"[migration] #{id} resumed");
        }
        catch (Exception e) when (e is HttpRequestException or MigrationException or TaskCanceledException)
        {
            Log($"[migration] #{id} resumed, but the KB was not told: {e.Message}");
        }
    }

    // --- pack -------------------------------------------------------------------------------------------------------

    private async Task PackAsync(long id, string[] items, CancellationToken ct)
    {
        try
        {
            var repos = await PushReposAsync(id, ct).ConfigureAwait(false);
            var sessionFile = SessionManager.FindMostRecentSession(SessionDir, WorkDir) ?? SessionManager.FindMostRecentSession(SessionDir);
            if (sessionFile is null) throw new MigrationException("there is no session to pack");
            var packet = new JsonObject
            {
                ["format"] = 1, ["id"] = id, ["source"] = Actor, ["items"] = new JsonArray([.. items.Select(i => (JsonNode)i)]),
                ["cwd"] = WorkDir, ["sessionName"] = Path.GetFileName(sessionFile), ["session"] = await File.ReadAllTextAsync(sessionFile, ct).ConfigureAwait(false),
                ["notebook"] = Briefing(), ["repos"] = new JsonArray([.. repos.Select(r => (JsonNode)r.ToJson())]),
            };
            var data = Gzip(Encoding.UTF8.GetBytes(packet.ToJsonString()));
            if (data.LongLength > MaxPacketBytes) throw new MigrationException($"the packet is {data.LongLength / 1048576} MB, over the {MaxPacketBytes / 1048576} MB limit");
            var header = HeaderJson();
            header["repos"] = new JsonArray([.. repos.Where(r => r.Remote is not null).Select(r => (JsonNode)new JsonObject { ["remote"] = r.Remote, ["branch"] = r.Branch, ["sha"] = r.Sha })]);
            var result = await SendAsync(HttpMethod.Post, $"/seat/migrations/{id}/packet",
                new JsonObject { ["header"] = header, ["data"] = Convert.ToBase64String(data) }, TimeSpan.FromMinutes(3), ct).ConfigureAwait(false);
            Log($"[migration] #{id} packed ({data.LongLength} bytes, {repos.Count} repositories): {result?["status"]?.GetValue<string>() ?? "?"}");
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log($"[migration] #{id} could not pack: {e.Message}");
            await RefuseAsync(id, "could not pack: " + e.Message, ct).ConfigureAwait(false);
        }
    }

    /// <summary>One clone under the work directory, as it was left for the next seat.</summary>
    public sealed record RepoState(string Path, string? Remote, string Branch, string Sha, string? Note)
    {
        public JsonObject ToJson() => new() { ["path"] = Path, ["remote"] = Remote, ["branch"] = Branch, ["sha"] = Sha, ["note"] = Note };
    }

    /// <summary>Commits and pushes every clone that has work not on its remote. A clone on its default branch (or detached)
    /// moves to <c>&lt;actor&gt;/migrate-&lt;id&gt;</c> first: nobody pushes to the default branch. A push that fails fails
    /// the pack, so the work stays with the seat that has it.</summary>
    public async Task<List<RepoState>> PushReposAsync(long id, CancellationToken ct)
    {
        var list = new List<RepoState>();
        foreach (var dir in FindRepos(WorkDir))
        {
            var rel = Path.GetRelativePath(WorkDir, dir).Replace('\\', '/');
            var remote = (await Git(dir, ["remote", "get-url", "origin"], ct).ConfigureAwait(false)) is (0, var url, _) ? StripUserInfo(url.Trim()) : null;
            var dirty = (await MustAsync(dir, ["status", "--porcelain"], ct).ConfigureAwait(false)).Trim().Length > 0;
            var branch = (await MustAsync(dir, ["rev-parse", "--abbrev-ref", "HEAD"], ct).ConfigureAwait(false)).Trim();
            if (remote is null)
            {
                list.Add(new(rel, null, branch, (await MustAsync(dir, ["rev-parse", "HEAD"], ct).ConfigureAwait(false)).Trim(),
                    dirty ? "no remote: its uncommitted changes did not move" : "no remote"));
                continue;
            }
            var ahead = await Git(dir, ["rev-list", "--count", "@{u}..HEAD"], ct).ConfigureAwait(false) is (0, var n, _) ? int.Parse(n.Trim()) : -1;
            var onDefault = branch == "HEAD" || await IsDefaultBranchAsync(dir, branch, ct).ConfigureAwait(false);
            // Clean and on its remote, or a clean clone of the default branch with no upstream: nothing to move.
            if (!dirty && (ahead == 0 || (ahead < 0 && onDefault)))
            {
                list.Add(new(rel, remote, branch, (await MustAsync(dir, ["rev-parse", "HEAD"], ct).ConfigureAwait(false)).Trim(), null));
                continue;
            }
            if (onDefault)
            {
                branch = $"{Actor}/migrate-{id}";
                await MustAsync(dir, ["switch", "-c", branch], ct).ConfigureAwait(false);
            }
            if (dirty)
            {
                await MustAsync(dir, ["add", "-A"], ct).ConfigureAwait(false);
                await MustAsync(dir, ["commit", "-m", $"WIP seat migration #{id}: packed by {Actor}"], ct).ConfigureAwait(false);
            }
            var (pushed, _, pushError) = await Git(dir, ["push", "-u", "origin", branch], ct).ConfigureAwait(false);
            if (pushed != 0) throw new MigrationException($"push of {rel} ({branch}) failed: {FirstLine(pushError)}");
            list.Add(new(rel, remote, branch, (await MustAsync(dir, ["rev-parse", "HEAD"], ct).ConfigureAwait(false)).Trim(), dirty ? "WIP committed for the migration" : null));
        }
        return list;
    }

    private async Task<bool> IsDefaultBranchAsync(string dir, string branch, CancellationToken ct)
    {
        if (await Git(dir, ["symbolic-ref", "--short", "refs/remotes/origin/HEAD"], ct).ConfigureAwait(false) is (0, var head, _))
            return head.Trim() == "origin/" + branch;
        return branch is "main" or "master";
    }

    private async Task<string> MustAsync(string dir, IReadOnlyList<string> args, CancellationToken ct)
    {
        var (exit, output, err) = await Git(dir, args, ct).ConfigureAwait(false);
        if (exit != 0) throw new MigrationException($"git {args[0]} in {Path.GetFileName(dir)} failed: {FirstLine(err)}");
        return output;
    }

    /// <summary>Clones directly under the work directory or one level below it, most recently used first.</summary>
    public static IEnumerable<string> FindRepos(string workDir)
    {
        if (!Directory.Exists(workDir)) return [];
        static bool IsRepo(string d) => Directory.Exists(Path.Combine(d, ".git")) || File.Exists(Path.Combine(d, ".git"));
        var found = new List<string>();
        foreach (var d in Directory.EnumerateDirectories(workDir))
        {
            if (IsRepo(d)) { found.Add(d); continue; }
            try { found.AddRange(Directory.EnumerateDirectories(d).Where(IsRepo)); }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { }
        }
        return found.OrderByDescending(d => Directory.GetLastWriteTimeUtc(Path.Combine(d, ".git"))).Take(30);
    }

    /// <summary>The remote as anyone may read it: an https remote loses its userinfo (a token must never travel in a packet).</summary>
    public static string StripUserInfo(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0
            ? new UriBuilder(uri) { UserName = "", Password = "" }.Uri.ToString()
            : url;

    private string Briefing()
    {
        if (NotebookDir is not { Length: > 0 } dir || !Directory.Exists(dir)) return "";
        try
        {
            using var notebook = new NotebookStore(dir, Actor);
            return notebook.Briefing();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return "(the notebook could not be read: " + e.Message + ")";
        }
    }

    // --- resume -----------------------------------------------------------------------------------------------------

    private async Task<MigrationStart> ResumeAsync(long id, CancellationToken ct)
    {
        try
        {
            var got = await SendAsync(HttpMethod.Get, $"/seat/migrations/{id}/packet", null, TimeSpan.FromMinutes(3), ct).ConfigureAwait(false)
                ?? throw new MigrationException("the KB returned no packet");
            if (Mismatch(got["header"] as JsonObject) is { } bad)
                throw new MigrationException($"the packet was written by {bad}, this seat runs {Describe(_release)}");
            var data = Convert.FromBase64String(got["data"]?.GetValue<string>() ?? "");
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(data)), got["sha256"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                throw new MigrationException("the packet does not match its sha256");
            var packet = JsonNode.Parse(Gunzip(data)) as JsonObject ?? throw new MigrationException("the packet is not a JSON object");
            var session = packet["session"]?.GetValue<string>() ?? throw new MigrationException("the packet holds no session");
            var name = Path.GetFileName(packet["sessionName"]?.GetValue<string>() ?? $"migrated-{id}.jsonl");
            Directory.CreateDirectory(SessionDir);
            var path = Path.Combine(SessionDir, name);
            if (!File.Exists(path)) await File.WriteAllTextAsync(path, session, ct).ConfigureAwait(false);
            Log($"[migration] #{id} resuming {name} from @{packet["source"]?.GetValue<string>()}");
            return new(path, Note(id, packet), id);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log($"[migration] #{id} could not resume: {e.Message}");
            await RefuseAsync(id, "could not resume: " + e.Message, ct).ConfigureAwait(false);
            return MigrationStart.None;
        }
    }

    /// <summary>The note in front of the first prompt: whose conversation this is, where the work is, what the source knew.</summary>
    public string Note(long id, JsonObject packet)
    {
        var source = packet["source"]?.GetValue<string>() ?? "?";
        var items = string.Join(", ", (packet["items"] as JsonArray ?? []).Select(i => i!.GetValue<string>()));
        var sb = new StringBuilder();
        if (source == Actor)
        {
            sb.Append($"[migration #{id}] Your seat migration was given back: the target could not resume it, and {items} are yours again. ")
              .Append("The conversation below is your own, as you left it. Your work was pushed when you packed; continue where you stopped.\n");
        }
        else
        {
            var sourceCwd = packet["cwd"]?.GetValue<string>() ?? "";
            sb.Append($"[migration #{id}] This conversation was @{source}'s. Seat migration #{id} moved it to you, @{Actor}, with its work items {items}; ")
              .Append("they are assigned to you now. Continue it where it stopped; do not repeat calls whose results are above.\n")
              .Append($"Its clones were under {sourceCwd}; yours are under {WorkDir}, so a path above that starts with {sourceCwd} means the same place under {WorkDir}. ")
              .Append("Before you touch code, get each repository below into your work directory (clone it if it is missing, else fetch) and check out the branch at that commit:\n");
            foreach (var r in (packet["repos"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var line = $"- {r["path"]?.GetValue<string>()}: {r["remote"]?.GetValue<string>() ?? "(no remote)"} {r["branch"]?.GetValue<string>()} @ {r["sha"]?.GetValue<string>()}";
                sb.Append(line).Append(r["note"]?.GetValue<string>() is { } note ? $" ({note})" : "").Append('\n');
            }
            if (packet["notebook"]?.GetValue<string>() is { Length: > 0 } notebook)
                sb.Append($"@{source}'s notebook when it packed (yours is separate: write what you still need into your own):\n").Append(notebook).Append('\n');
        }
        return sb.ToString();
    }

    private string? Mismatch(JsonObject? header)
    {
        string? S(string n) => header?[n] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        int? I(string n) => header?[n] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
        if (S("harness") != _release.Harness || S("version") != _release.Version || I("sessionFormat") != _release.SessionFormat || I("journalVersion") != _release.JournalVersion)
            return $"{S("harness") ?? "?"} {S("version") ?? "?"} (session format {I("sessionFormat")?.ToString() ?? "?"}, journal {I("journalVersion")?.ToString() ?? "?"})";
        return null;
    }

    private static string Describe(SeatRelease r) => $"{r.Harness} {r.Version} (session format {r.SessionFormat}, journal {r.JournalVersion})";

    private JsonObject HeaderJson() => new()
    {
        ["harness"] = _release.Harness, ["version"] = _release.Version, ["sessionFormat"] = _release.SessionFormat, ["journalVersion"] = _release.JournalVersion,
    };

    // --- plumbing ---------------------------------------------------------------------------------------------------

    /// <summary>Says this seat cannot do its step (a source: the migration fails, its work stays; a target: the work goes back).</summary>
    public async Task RefuseAsync(long id, string reason, CancellationToken ct)
    {
        try { await SendAsync(HttpMethod.Post, $"/seat/migrations/{id}/refuse", new JsonObject { ["reason"] = reason }, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false); }
        catch (Exception e) when (e is HttpRequestException or MigrationException or TaskCanceledException)
        {
            Log($"[migration] #{id} refusal not delivered: {e.Message}");
        }
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, deadline.Token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = text.Length > 0 && text.TrimStart().StartsWith('{') ? JsonNode.Parse(text)?["error"]?.GetValue<string>() : null;
            throw new MigrationException($"{method} {path}: {(int)response.StatusCode} {error ?? FirstLine(text)}");
        }
        return text.Length == 0 ? null : JsonNode.Parse(text);
    }

    private static byte[] Gzip(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal)) gz.Write(raw);
        return output.ToArray();
    }

    private static string Gunzip(byte[] data)
    {
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var reader = new StreamReader(input, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string FirstLine(string text) => text.Trim().Split('\n', 2)[0].Trim();

    /// <summary>git with a deadline (a push that hangs must not freeze the seat) and no prompt for credentials.</summary>
    public static async Task<(int Exit, string Out, string Err)> RunGitAsync(string directory, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var p = Process.Start(start)!;
        try
        {
            var stderr = p.StandardError.ReadToEndAsync(timeout.Token);
            var stdout = await p.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return (p.ExitCode, stdout, await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (-1, "", "git timed out after 2 minutes");
        }
        finally { if (!p.HasExited) p.Kill(true); }
    }
}

public sealed class MigrationException(string message) : Exception(message);
