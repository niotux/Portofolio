using PiSharp.Core.Mcp;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Mcp;

public class McpConfigTests
{
    [Fact]
    public void ParsesStdioAndHttpEntries()
    {
        var cfg = McpConfig.ParseJson("""
            {
              "mcpServers": {
                "store": {
                  "command": "dotnet",
                  "args": ["PiSharp.SkillStore.dll", "/tmp/skills"],
                  "env": { "FOO": "bar" }
                },
                "remote": {
                  "url": "http://127.0.0.1:9/mcp",
                  "headers": { "Authorization": "Bearer x" }
                }
              }
            }
            """);

        Assert.Equal(2, cfg.Servers.Count);
        var stdio = cfg.Servers.Single(s => s.Alias == "store");
        Assert.Equal(McpTransportKind.Stdio, stdio.Kind);
        Assert.Equal("dotnet", stdio.Command);
        Assert.Equal(["PiSharp.SkillStore.dll", "/tmp/skills"], stdio.Args);
        Assert.Equal("bar", stdio.Env["FOO"]);

        var http = cfg.Servers.Single(s => s.Alias == "remote");
        Assert.Equal(McpTransportKind.Http, http.Kind);
        Assert.Equal("http://127.0.0.1:9/mcp", http.Url);
        Assert.Equal("Bearer x", http.Headers["Authorization"]);
    }

    [Fact]
    public void WebServerProjectConfigUsesSeatTokenEnvironmentReference()
    {
        var cfg = McpConfig.ParseJson("""
            { "mcpServers": { "web": {
              "type": "web",
              "url": "http://127.0.0.1:8082",
              "headers": { "Authorization": "Bearer ${PI_FACTORY_TOKEN}" }
            } } }
            """);

        var web = Assert.Single(cfg.Servers);
        Assert.Equal("web", web.Alias);
        Assert.Equal("web", web.Protocol);
        Assert.Equal(McpTransportKind.Http, web.Kind);
        Assert.Equal("Bearer ${PI_FACTORY_TOKEN}", web.Headers["Authorization"]);
        Environment.SetEnvironmentVariable("PI_FACTORY_TOKEN", "seat-token");
        try
        {
            Assert.Equal("Bearer seat-token", McpConfig.ExpandEnv(web.Headers["Authorization"]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PI_FACTORY_TOKEN", null);
        }
    }

    [Fact]
    public void ProjectFileOverridesGlobalAlias()
    {
        using var tmp = new TempWorkspace();
        var global = tmp.File("global.json", """{"mcpServers":{"a":{"command":"global"},"b":{"command":"keep"}}}""");
        var project = tmp.File("project.json", """{"mcpServers":{"a":{"url":"http://localhost/mcp"}}}""");

        var cfg = McpConfig.Load(global, project);
        var a = cfg.Servers.Single(s => s.Alias == "a");
        Assert.Equal(McpTransportKind.Http, a.Kind);
        Assert.Contains(cfg.Servers, s => s.Alias == "b" && s.Command == "keep");
    }

    [Fact]
    public void LoadDefaultUsesGlobalOverrideAndProjectFile()
    {
        using var tmp = new TempWorkspace();
        var cwd = tmp.Dir("project");
        var global = tmp.Dir("global");
        File.WriteAllText(Path.Combine(global, "mcp.json"),
            "{\"mcpServers\":{\"global\":{\"command\":\"global-server\"}}}");
        File.WriteAllText(Path.Combine(tmp.Dir("project/.pi-sharp"), "mcp.json"),
            "{\"mcpServers\":{\"project\":{\"command\":\"project-server\"}}}");

        var cfg = McpConfig.LoadDefault(cwd, global);

        Assert.Equal(["global", "project"], cfg.Servers.Select(s => s.Alias).OrderBy(x => x));
    }

    [Fact]
    public void RejectsEntryWithNeitherOrBoth()
    {
        Assert.Throws<InvalidOperationException>(() => McpConfig.ParseJson("""{"mcpServers":{"x":{}}}"""));
        Assert.Throws<InvalidOperationException>(() =>
            McpConfig.ParseJson("""{"mcpServers":{"x":{"command":"c","url":"http://x"}}}"""));
    }

    [Fact]
    public void ExpandEnvSubstitutesDollarAndBrace()
    {
        Environment.SetEnvironmentVariable("PI_SHARP_TEST_MCP", "xyz");
        try
        {
            Assert.Equal("pre-xyz-post", McpConfig.ExpandEnv("pre-$PI_SHARP_TEST_MCP-post"));
            Assert.Equal("xyz!", McpConfig.ExpandEnv("${PI_SHARP_TEST_MCP}!"));
            Assert.Equal("", McpConfig.ExpandEnv("${PI_SHARP_TEST_MCP_MISSING}"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PI_SHARP_TEST_MCP", null);
        }
    }
}
