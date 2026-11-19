using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using PiSharp.Cli;
using PiSharp.Tests.Llm;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Cli;

/// <summary>The seat's own update from the Artifactory (digital-scribe, 2026-10-16: "can we have pi-sharp auto-update when
/// a new version appears in artifactory? and it can decide itself when it does it").</summary>
public sealed class SelfUpdaterTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static byte[] Release(string version, params (string Name, string Body)[] extra)
    {
        using var raw = new MemoryStream();
        using (var gzip = new GZipStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            void Add(string name, string body)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(body)) };
                tar.WriteEntry(entry);
            }
            Add("bin/pi-sharp.dll", "dll " + version);
            Add("bin/pi-sharp.runtimeconfig.json", "{}");
            Add("VERSION", version);
            Add("install.sh", "#!/bin/sh\n");
            Add("browser/PiSharp.Browser.dll", "browser");
            foreach (var (name, body) in extra) Add(name, body);
        }
        return raw.ToArray();
    }

    private static (SelfUpdater Updater, Clock Clock, CeilingFakeHandler Handler) Updater(TempWorkspace tmp, string running, string head, byte[] tarball, string? sha = null)
    {
        sha ??= Convert.ToHexStringLower(SHA256.HashData(tarball));
        var handler = new CeilingFakeHandler(request => Task.FromResult(request.RequestUri!.AbsolutePath.StartsWith("/artifacts/")
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"component":"pi-sharp","channel":"stable","version":"{{head}}","url":"http://store.test/pi-sharp/pi-sharp-{{head}}.tar.gz","sha256":"{{sha}}"}"""),
            }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(tarball) }));
        var clock = new Clock();
        var updater = new SelfUpdater(new Uri("http://kb.test/"), "stable", running, Path.Combine(tmp.Path, "self"), new HttpClient(handler), clock)
        {
            Starts = (_, _) => Task.FromResult(true),
            SharedBin = Path.Combine(tmp.Path, "shared-bin"),
        };
        return (updater, clock, handler);
    }

    [Fact]
    public async Task ANewerStableBuildIsStagedWithItsBinOnlyAndMadeCurrent()
    {
        using var tmp = new TempWorkspace();
        tmp.File("shared-bin/pi-browser", "#!/bin/sh\nexec browser\n");
        var (updater, clock, handler) = Updater(tmp, "0.5.97", "0.5.98", Release("0.5.98"));
        updater.MarkGood();
        Assert.Equal("0.5.97\n", File.ReadAllText(Path.Combine(updater.Root, "good")));

        await updater.TickAsync(CancellationToken.None);
        Assert.Equal("0.5.98", updater.Ready);
        Assert.Equal(new Uri("http://kb.test/artifacts/pi-sharp/stable"), handler.Uris[0]);
        var release = Path.Combine(updater.Root, "releases", "0.5.98");
        Assert.True(File.Exists(Path.Combine(release, "bin", "pi-sharp.dll")));
        Assert.True(File.Exists(Path.Combine(release, "bin", "pi-browser"))); // the shared wrapper: making it needs root
        Assert.False(File.Exists(Path.Combine(release, "install.sh")));
        Assert.False(Directory.Exists(Path.Combine(release, "browser")));
        Assert.StartsWith("0.5.98 ", File.ReadAllText(Path.Combine(release, "VERSION")));
        Assert.Equal("0.5.98\n", File.ReadAllText(Path.Combine(updater.Root, "current")));

        // Staged, it waits for a safe point: idle at once, between turns only after MaxDefer.
        Assert.False(updater.Overdue);
        clock.Now += TimeSpan.FromHours(3);
        Assert.True(updater.Overdue);
    }

    [Fact]
    public async Task OlderBadOrMismatchedBuildsAreNeverStaged()
    {
        using var tmp = new TempWorkspace();
        var tarball = Release("0.5.98");
        // The stable head is older than what the seat runs (stable was 0.5.86 while the seats ran 0.5.96).
        var older = Updater(tmp, "0.5.97", "0.5.86", tarball).Updater;
        await older.TickAsync(CancellationToken.None);
        Assert.Null(older.Ready);

        var wrongSha = Updater(tmp, "0.5.97", "0.5.98", tarball, sha: new string('0', 64)).Updater;
        await wrongSha.TickAsync(CancellationToken.None);
        Assert.Null(wrongSha.Ready);
        Assert.False(File.Exists(Path.Combine(wrongSha.Root, "current")));

        var bad = Updater(tmp, "0.5.97", "0.5.98", tarball).Updater;
        Directory.CreateDirectory(bad.Root);
        File.WriteAllText(Path.Combine(bad.Root, "bad"), "0.5.98\n");
        await bad.TickAsync(CancellationToken.None);
        Assert.Null(bad.Ready);
    }

    [Fact]
    public async Task ATarEntryCannotLeaveTheReleaseFolder()
    {
        using var tmp = new TempWorkspace();
        var dest = Path.Combine(tmp.Path, "release");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SelfUpdater.ExtractBinAsync(new MemoryStream(Release("0.5.98", ("bin/../../escaped", "x"))), dest, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(tmp.Path, "escaped")));
    }

    [Fact]
    public void TheInstalledBuildHandsOverToANewerStagedOneAndDropsOneThatNeverStartedWell()
    {
        using var tmp = new TempWorkspace();
        var root = Path.Combine(tmp.Path, "self");
        tmp.File("self/releases/0.5.99/bin/pi-sharp.dll", "dll");
        tmp.File("self/current", "0.5.99\n");
        Assert.Null(SelfUpdater.HandOverTarget(root, "0.5.99"));   // the staged build itself: nothing to hand over
        Assert.Null(SelfUpdater.HandOverTarget(root, "0.6.0"));    // the installed one is newer

        var dll = SelfUpdater.HandOverTarget(root, "0.5.98");
        Assert.EndsWith(Path.Combine("releases", "0.5.99", "bin", "pi-sharp.dll"), dll);
        Assert.Equal("0.5.99\n", File.ReadAllText(Path.Combine(root, "trying")));

        // It never marked itself good: at the next start it is dropped, and never handed over to again.
        Assert.Null(SelfUpdater.HandOverTarget(root, "0.5.98"));
        Assert.Contains("0.5.99", File.ReadAllText(Path.Combine(root, "bad")));
        Assert.False(File.Exists(Path.Combine(root, "current")));
        tmp.File("self/current", "0.5.99\n");
        Assert.Null(SelfUpdater.HandOverTarget(root, "0.5.98"));

        // A good build keeps being handed over to, start after start.
        File.Delete(Path.Combine(root, "bad"));
        tmp.File("self/good", "0.5.99\n");
        Assert.NotNull(SelfUpdater.HandOverTarget(root, "0.5.98"));
        Assert.NotNull(SelfUpdater.HandOverTarget(root, "0.5.98"));
    }

    [Fact]
    public async Task ASelfContainedBuildKeepsItsExecutableAndStartsWithoutDotnet()
    {
        if (OperatingSystem.IsWindows()) return;
        using var tmp = new TempWorkspace();
        using var raw = new MemoryStream();
        using (var gzip = new GZipStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "bin/pi-sharp")
            {
                DataStream = new MemoryStream("#!/bin/sh\n"u8.ToArray()),
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute,
            });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "bin/pi-sharp.dll") { DataStream = new MemoryStream("dll"u8.ToArray()) });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "bin/pi-sharp.runtimeconfig.json")
            {
                DataStream = new MemoryStream("""{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.12"}]}}"""u8.ToArray()),
            });
        }
        var dest = Path.Combine(tmp.Path, "release");
        raw.Position = 0;
        await SelfUpdater.ExtractBinAsync(raw, dest, CancellationToken.None);
        var app = Path.Combine(dest, "bin", "pi-sharp");
        Assert.NotEqual(UnixFileMode.None, File.GetUnixFileMode(app) & UnixFileMode.UserExecute);

        // Its own executable runs it, on the .NET it carries; a box's .NET update cannot reach it.
        var info = SelfUpdater.StartInfo(Path.Combine(dest, "bin", "pi-sharp.dll"));
        Assert.Equal(app, info.FileName);
        Assert.Empty(info.ArgumentList);
    }

    [Fact]
    public void AFrameworkDependentBuildStillStartsThroughDotnet()
    {
        using var tmp = new TempWorkspace();
        var dll = tmp.File("bin/pi-sharp.dll", "dll");
        tmp.File("bin/pi-sharp", "#!/bin/sh\n");
        tmp.File("bin/pi-sharp.runtimeconfig.json", """{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""");
        var info = SelfUpdater.StartInfo(dll);
        Assert.Equal("dotnet", info.FileName);
        Assert.Equal([dll], info.ArgumentList);
        Assert.False(SelfUpdater.SelfContained(dll));
    }

    [Fact]
    public async Task StagingKeepsOnlyTheReleasesTheSeatMayStillRun()
    {
        using var tmp = new TempWorkspace();
        tmp.File("self/releases/0.5.90/bin/pi-sharp.dll", "old");
        tmp.File("self/releases/0.5.95/bin/pi-sharp.dll", "good");
        tmp.File("self/releases/0.5.97/bin/pi-sharp.dll", "running");
        tmp.File("self/good", "0.5.95\n");
        var (updater, _, _) = Updater(tmp, "0.5.97", "0.5.98", Release("0.5.98"));

        await updater.TickAsync(CancellationToken.None);
        Assert.Equal("0.5.98", updater.Ready);
        var left = Directory.EnumerateDirectories(Path.Combine(updater.Root, "releases")).Select(d => Path.GetFileName(d)!).Order().ToArray();
        Assert.Equal(new[] { "0.5.95", "0.5.97", "0.5.98" }, left);
    }
}
