// A seat's own update from the Artifactory (digital-scribe, 2026-10-16: "can we have pi-sharp auto-update when a new
// version appears in artifactory? and it can decide itself when it does it (between turns and so on)"; the gate is his:
// a seat follows the stable channel, and every promotion to stable is approved in his decision queue).
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PiSharp.Cli;

/// <summary>
/// Follows a channel of pi-sharp in the Artifactory. Every <see cref="Interval"/> it reads the channel head; a release
/// strictly newer than the running one, and not one that already failed to start here, is downloaded, checked against
/// its sha256, unpacked (its <c>bin/</c> only) under <see cref="Root"/><c>/releases/&lt;version&gt;</c>, proven to start
/// (<c>--version</c>) and made <c>current</c>. The seat then restarts at a safe point it picks itself: idle with its last
/// task delivered, or, after <see cref="MaxDefer"/> of work that never paused, between two turns. pi-seat-exec runs
/// <c>current</c> when it is the newest build on the box, and drops a build that never reported a good start.
/// </summary>
internal sealed class SelfUpdater
{
    public const string Component = "pi-sharp";
    private readonly Uri _registry;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private DateTimeOffset _nextCheck;

    public SelfUpdater(Uri registry, string channel, string running, string root, HttpClient http, TimeProvider? time = null)
    {
        _registry = registry;
        Channel = channel;
        Running = running;
        Root = root;
        _http = http;
        _time = time ?? TimeProvider.System;
    }

    public string Channel { get; }
    public string Running { get; }
    /// <summary>The seat's own install root: <c>~/.local/share/pi-sharp</c>.</summary>
    public string Root { get; }
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>How long a staged update may wait for an idle moment before the seat restarts between two turns.</summary>
    public TimeSpan MaxDefer { get; init; } = TimeSpan.FromHours(3);
    /// <summary>Where a shared install keeps its browser wrapper, copied next to a staged build (it needs root to make).</summary>
    public string SharedBin { get; init; } = "/opt/pi-sharp-attached/bin";
    public Action<string>? Log { get; init; }
    /// <summary>For tests: proves a staged build starts.</summary>
    internal Func<string, CancellationToken, Task<bool>>? Starts { get; init; }

    /// <summary>The version staged and made current, waiting for the restart that runs it; null while none.</summary>
    public string? Ready { get; private set; }
    public DateTimeOffset ReadySince { get; private set; }
    public bool Overdue => Ready is not null && _time.GetUtcNow() - ReadySince >= MaxDefer;

    /// <summary>The default root, the seat's own: nothing here needs root.</summary>
    public static string DefaultRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "pi-sharp");

    public static int Compare(string a, string b) =>
        Version.TryParse(a, out var va) && Version.TryParse(b, out var vb) ? va.CompareTo(vb) : string.CompareOrdinal(a, b);

    /// <summary>Versions that were made current here and never reported a good start (pi-seat-exec writes them).</summary>
    public IReadOnlySet<string> Bad() => File.Exists(Path.Combine(Root, "bad"))
        ? File.ReadAllLines(Path.Combine(Root, "bad")).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal)
        : new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Called once the seat got past its start: the running build is good (pi-seat-exec keeps it then).</summary>
    public void MarkGood()
    {
        Directory.CreateDirectory(Root);
        WriteAtomic(Path.Combine(Root, "good"), Running + "\n");
    }

    /// <summary>Reads the channel head at most once per <see cref="Interval"/> and stages a newer build. Never throws.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (Ready is not null || _time.GetUtcNow() < _nextCheck) return;
        _nextCheck = _time.GetUtcNow() + Interval;
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Get, new Uri(_registry, $"artifacts/{Component}/{Channel}"));
            using var response = await _http.SendAsync(head, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) as JsonObject;
            if ((string?)json?["version"] is not { Length: > 0 } version || (string?)json["url"] is not { } url
                || (string?)json["sha256"] is not { Length: 64 } sha) return;
            if (Compare(version, Running) <= 0 || Bad().Contains(version)) return;
            await StageAsync(version, new Uri(url), sha, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log?.Invoke($"self-update: check failed ({ex.Message}); staying on {Running}");
        }
    }

    private async Task StageAsync(string version, Uri url, string sha256, CancellationToken ct)
    {
        var releases = Path.Combine(Root, "releases");
        var target = Path.Combine(releases, version);
        Directory.CreateDirectory(releases);
        if (!File.Exists(Path.Combine(target, "bin", "pi-sharp.dll")))
        {
            var stage = Path.Combine(releases, "." + version + ".new");
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            var download = Path.Combine(releases, "." + version + ".tar.gz");
            try
            {
                await using (var file = File.Create(download))
                {
                    using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    await (await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false)).CopyToAsync(file, ct).ConfigureAwait(false);
                }
                string got;
                await using (var file = File.OpenRead(download)) got = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct).ConfigureAwait(false));
                if (!string.Equals(got, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log?.Invoke($"self-update: {version} does not match its sha256 ({got}); not staged");
                    return;
                }
                await using (var file = File.OpenRead(download))
                    await ExtractBinAsync(file, stage, ct).ConfigureAwait(false);
            }
            finally { File.Delete(download); }
            var dll = Path.Combine(stage, "bin", "pi-sharp.dll");
            if (!File.Exists(dll) || !await (Starts ?? StartsAsync)(dll, ct).ConfigureAwait(false))
            {
                Log?.Invoke($"self-update: {version} does not start (--version); not staged");
                Directory.Delete(stage, recursive: true);
                return;
            }
            var browser = Path.Combine(SharedBin, "pi-browser");
            if (File.Exists(browser) && !File.Exists(Path.Combine(stage, "bin", "pi-browser")))
                File.Copy(browser, Path.Combine(stage, "bin", "pi-browser"));
            await File.WriteAllTextAsync(Path.Combine(stage, "VERSION"), $"{version} {_time.GetUtcNow():O} self-update\n", ct).ConfigureAwait(false);
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(stage, target);
        }
        WriteAtomic(Path.Combine(Root, "current"), version + "\n");
        Ready = version;
        ReadySince = _time.GetUtcNow();
        Prune(releases, version);
        Log?.Invoke($"self-update: {version} staged from {Channel}; restarting at the next safe point (idle, or between turns after {MaxDefer.TotalHours:0.#}h)");
    }

    /// <summary>The release's <c>bin/</c> and <c>VERSION</c> into <paramref name="dest"/>; nothing may land outside it.</summary>
    internal static async Task ExtractBinAsync(Stream tarGz, string dest, CancellationToken ct)
    {
        var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        await using var gzip = new GZipStream(tarGz, CompressionMode.Decompress);
        await using var tar = new TarReader(gzip);
        while (await tar.GetNextEntryAsync(copyData: false, ct).ConfigureAwait(false) is { } entry)
        {
            var name = entry.Name.Replace('\\', '/').TrimStart('.', '/');
            if (!(name.StartsWith("bin/", StringComparison.Ordinal) || name == "VERSION")) continue;
            var path = Path.GetFullPath(Path.Combine(root, name));
            if (!path.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException($"{entry.Name} leaves the release folder");
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(path);
                    break;
                case TarEntryType.RegularFile or TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await entry.ExtractToFileAsync(path, overwrite: true, ct).ConfigureAwait(false);
                    break;
                // Links and devices are not part of a pi-sharp build: skipped, never followed.
            }
        }
    }

    /// <summary>The environment variable a hand-over sets in the child it starts, so the child never hands over again.</summary>
    public const string LaunchedEnv = "PI_SHARP_SELF_LAUNCHED";

    /// <summary>
    /// At start: the newest build this seat staged itself, when it is newer than the running one, else null. The start
    /// script does not need to know about self-updates (a seat on a box the operator cannot reach keeps its old one): the
    /// installed pi-sharp hands over to the staged build. A build still "trying" at the next start never marked itself
    /// good, so it is listed bad and dropped, and the installed build runs.
    /// </summary>
    public static string? HandOverTarget(string root, string running, Action<string>? log = null)
    {
        if (Environment.GetEnvironmentVariable(LaunchedEnv) == "1" || Environment.GetEnvironmentVariable("PI_SHARP_SELF_UPDATE") == "0") return null;
        string Read(string name) => File.Exists(Path.Combine(root, name)) ? File.ReadAllText(Path.Combine(root, name)).Trim() : "";
        try
        {
            var current = Read("current");
            if (current.Length == 0 || Compare(current, running) <= 0) return null;
            var dll = Path.Combine(root, "releases", current, "bin", "pi-sharp.dll");
            if (!File.Exists(dll)) return null;
            var good = Read("good");
            if (Read("trying") == current && good != current)
            {
                File.AppendAllText(Path.Combine(root, "bad"), current + "\n");
                File.Delete(Path.Combine(root, "current"));
                File.Delete(Path.Combine(root, "trying"));
                log?.Invoke($"self-update: {current} never reported a good start; dropped it, running the installed {running}");
                return null;
            }
            var badFile = Path.Combine(root, "bad");
            if (File.Exists(badFile) && File.ReadAllLines(badFile).Any(l => l.Trim() == current)) return null;
            if (good != current) WriteAtomic(Path.Combine(root, "trying"), current + "\n");
            return dll;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"self-update: hand-over check failed ({ex.Message}); running the installed {running}");
            return null;
        }
    }

    /// <summary>Runs the staged build in this process's place: same arguments, same streams, its exit code. systemd's stop
    /// reaches it through the unit's cgroup.</summary>
    public static async Task<int> HandOverAsync(string dll, string[] args)
    {
        var info = StartInfo(dll);
        foreach (var a in args) info.ArgumentList.Add(a);
        info.Environment[LaunchedEnv] = "1";
        using var child = Process.Start(info)!;
        await child.WaitForExitAsync().ConfigureAwait(false);
        return child.ExitCode;
    }

    private static async Task<bool> StartsAsync(string dll, CancellationToken ct)
    {
        var info = StartInfo(dll);
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.ArgumentList.Add("--version");
        try
        {
            var result = await PiSharp.Core.Processes.ProcessRunner.RunAsync(info, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException) { return false; }
    }

    /// <summary>
    /// How a build starts. A self-contained build (2026-10-30 on: it carries its own .NET) runs its own executable, so no
    /// update of the box's .NET can reach it: Ubuntu's overnight .NET update on strata left seats that were running unable
    /// to load the parts of .NET they had not loaded yet (self-update failed on System.Formats.Tar; demo-03's model calls
    /// failed). An older, framework-dependent build runs through dotnet as before.
    /// </summary>
    internal static ProcessStartInfo StartInfo(string dll)
    {
        var app = Path.Combine(Path.GetDirectoryName(dll)!, Path.GetFileNameWithoutExtension(dll));
        if (SelfContained(dll) && File.Exists(app) && (OperatingSystem.IsWindows() || (File.GetUnixFileMode(app) & UnixFileMode.UserExecute) != 0))
            return new ProcessStartInfo(app) { UseShellExecute = false };
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        info.ArgumentList.Add(dll);
        return info;
    }

    /// <summary>Whether the build next to <paramref name="dll"/> carries its own .NET (its runtimeconfig lists included frameworks).</summary>
    internal static bool SelfContained(string dll)
    {
        var config = Path.ChangeExtension(dll, ".runtimeconfig.json");
        try { return File.Exists(config) && File.ReadAllText(config).Contains("\"includedFrameworks\"", StringComparison.Ordinal); }
        catch (IOException) { return false; }
    }

    /// <summary>
    /// Keeps the releases this seat may still run: the one just staged, the one running and the last one that started well;
    /// the rest go. A self-contained build is some 80 MB, and every promotion used to leave one more behind in each seat's home.
    /// </summary>
    internal void Prune(string releases, string staged)
    {
        string Read(string name) => File.Exists(Path.Combine(Root, name)) ? File.ReadAllText(Path.Combine(Root, name)).Trim() : "";
        var keep = new HashSet<string>(StringComparer.Ordinal) { staged, Running, Read("good") };
        foreach (var dir in Directory.EnumerateDirectories(releases))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.') || keep.Contains(name)) continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log?.Invoke($"self-update: could not remove the old release {name} ({ex.Message})");
            }
        }
    }

    private static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }
}
