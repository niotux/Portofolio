using PiSharp.Core.Skills;
using PiSharp.Tests.Mcp;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Skills;

public class SkillInstallerTests
{
    [Fact]
    public async Task InstallSkillWritesFilesAndLock()
    {
        using var tmp = new TempWorkspace();
        McpClientTests.SeedStore(tmp);
        var project = tmp.Dir("project");
        await using var session = await McpClientTests.ConnectStore(tmp.Path);

        var installed = await SkillInstaller.InstallAsync(session.Client, "demo", project);
        Assert.True(File.Exists(Path.Combine(installed.Directory, "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(installed.Directory, "notes.md")));
        Assert.Equal("1.0.0", installed.Lock.Version);
        Assert.Equal("store", installed.Lock.Store);
        Assert.Contains(installed.Lock.Files, f => f.Path == "SKILL.md" && f.Sha256.Length == 64);

        var lockFile = SkillInstaller.ReadLock(project);
        Assert.Contains(lockFile.Skills, s => s.Name == "demo");
        var json = File.ReadAllText(SkillInstaller.LockPath(project));
        Assert.Contains("\"demo\"", json);
    }

    [Fact]
    public async Task ReadToolResolvesSkillUriThroughManager()
    {
        using var tmp = new TempWorkspace();
        McpClientTests.SeedStore(tmp);
        await using var session = await McpClientTests.ConnectStore(tmp.Path);

        var resources = await session.Client.ListResourcesAsync();
        var mcp = new RecordingResolver(session.Client);
        foreach (var r in resources) mcp.Known.Add(r.Uri);

        var tools = PiSharp.Tools.BuiltinTools.Create(tmp.Path, skillUris: mcp);
        var read = tools.OfType<PiSharp.Tools.ReadTool>().Single();
        var result = await PiSharp.Tests.Tools.ToolRun.Exec(read, """{"path":"skill://store/demo/SKILL.md"}""");
        Assert.Contains("Do the demo thing", result.Text);
    }
}

internal sealed class RecordingResolver : PiSharp.Core.Skills.ISkillUriResolver
{
    private readonly PiSharp.Core.Mcp.McpClient _client;
    public HashSet<string> Known { get; } = new(StringComparer.Ordinal);

    public RecordingResolver(PiSharp.Core.Mcp.McpClient client) => _client = client;

    public bool CanResolve(string uri) =>
        uri.StartsWith("skill://", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ReadTextAsync(string uri, CancellationToken cancellationToken = default)
    {
        var contents = await _client.ReadResourceAsync(uri, cancellationToken);
        return contents[0].Text ?? "";
    }
}
