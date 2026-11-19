using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using PiSharp.Core.Verification;
using Xunit;

namespace PiSharp.Tests.Verification;

public sealed class DoneCheckProvenanceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pi-approved-checks-").FullName;
    private const string Manifest = "{\"task\":\"task-1\",\"checks\":[{\"id\":\"build\",\"kind\":\"command\",\"spec\":\"dotnet build\"}]}\n";

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void ChecksAndHashComeFromImmutableProtectedRefNotWritableWorkspace()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".pi"));
        File.WriteAllText(Path.Combine(_root, ".pi", "done-checks.json"), Manifest);
        Git("init", "-q");
        Git("config", "user.email", "tests@example.invalid");
        Git("config", "user.name", "tests");
        Git("add", ".pi/done-checks.json");
        Git("commit", "-qm", "approved baseline");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        var source = new ApprovedDoneCheckSource(_root);

        var checks = source.For("task-1");
        var provenance = source.ProvenanceFor("task-1")!;
        Assert.Single(checks);
        Assert.Equal("refs/remotes/origin/main:.pi/done-checks.json", provenance.Source);
        Assert.Matches("^[0-9a-f]{40,64}$", provenance.Revision);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Manifest))).ToLowerInvariant(), provenance.Sha256);
        Assert.True(provenance.IsValid);

        // A branch edit cannot change either the checks or the source hash selected from the pinned commit.
        File.WriteAllText(Path.Combine(_root, ".pi", "done-checks.json"), "{}");
        Assert.Single(source.For("task-1"));
        Assert.Equal(provenance, source.ProvenanceFor("task-1"));
    }

    [Fact]
    public void MissingApprovedRefAndWorkspacePinDoNotProduceTrustedProvenance()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".pi"));
        File.WriteAllText(Path.Combine(_root, ".pi", "done-checks.json"), Manifest);
        var source = new ApprovedDoneCheckSource(_root);
        Assert.Empty(source.For("task-1"));
        Assert.Null(source.ProvenanceFor("task-1"));

        var pinOnly = new DoneCheckProvenance(".pi/done-checks.json", "workspace-head", new string('a', 64), false);
        Assert.False(pinOnly.IsValid);
    }

    private void Git(params string[] args)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("git") { WorkingDirectory = _root, RedirectStandardError = true } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {error}");
    }
}
