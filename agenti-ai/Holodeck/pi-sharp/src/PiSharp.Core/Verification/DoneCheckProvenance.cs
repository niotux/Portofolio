using System.Security.Cryptography;
using System.Text;

namespace PiSharp.Core.Verification;

/// <summary>Evidence that a check manifest came from an independently approved immutable source.</summary>
public sealed record DoneCheckProvenance(string Source, string Revision, string Sha256, bool Approved)
{
    public bool IsValid => Approved && !string.IsNullOrWhiteSpace(Source) && !string.IsNullOrWhiteSpace(Revision)
        && Sha256.Length == 64 && Sha256.All(Uri.IsHexDigit);

    internal static DoneCheckProvenance ForApprovedBytes(string source, string immutableRevision, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(immutableRevision);
        ArgumentNullException.ThrowIfNull(content);
        return new DoneCheckProvenance(source, immutableRevision,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant(), true);
    }
}

/// <summary>Loads checks only from the protected origin/main commit. Candidate-workspace files and caller-supplied
/// revision/hash claims cannot create approved provenance. The selected commit and manifest hash are captured together.</summary>
public sealed class ApprovedDoneCheckSource : IDoneCheckSource
{
    private const string ApprovedRef = "refs/remotes/origin/main";
    private readonly string _cwd;
    private string? _loadedTask;
    private IReadOnlyList<DoneCheck> _checks = [];
    private DoneCheckProvenance? _provenance;

    public ApprovedDoneCheckSource(string cwd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);
        _cwd = Path.GetFullPath(cwd);
    }

    public DoneCheckProvenance? ProvenanceFor(string task) { Load(task); return _provenance; }

    public IReadOnlyList<DoneCheck> For(string task) { Load(task); return _checks; }

    private void Load(string task)
    {
        if (_loadedTask == task) return;
        _loadedTask = task;
        _checks = [];
        _provenance = null;
        try
        {
            var revision = Git("rev-parse", "--verify", ApprovedRef + "^{commit}").Trim();
            if (revision.Length is not (40 or 64) || revision.Any(c => !Uri.IsHexDigit(c))) return;
            var text = Git("show", revision + ":.pi/done-checks.json");
            _provenance = DoneCheckProvenance.ForApprovedBytes(ApprovedRef + ":.pi/done-checks.json", revision, text);
            var root = System.Text.Json.Nodes.JsonNode.Parse(text);
            System.Text.Json.Nodes.JsonArray? list = root switch
            {
                System.Text.Json.Nodes.JsonArray array => array,
                System.Text.Json.Nodes.JsonObject obj when obj["task"]?.GetValue<string>() is null
                    || obj["task"]!.GetValue<string>() == task => obj["checks"] as System.Text.Json.Nodes.JsonArray,
                _ => null,
            };
            if (list is not null)
                _checks = list.OfType<System.Text.Json.Nodes.JsonObject>().Select(DoneCheck.Parse).Where(c => c is not null).Select(c => c!).ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception
            or System.Text.Json.JsonException or ArgumentException or TimeoutException) { }
    }

    private string Git(params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = _cwd };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        var result = PiSharp.Core.Processes.ProcessRunner.RunAsync(start, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        if (result.ExitCode != 0) throw new IOException(result.Error);
        return result.Output;
    }
}
