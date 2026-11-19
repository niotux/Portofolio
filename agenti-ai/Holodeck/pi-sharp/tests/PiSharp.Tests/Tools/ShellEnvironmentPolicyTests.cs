using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

// plan-pi-sharp-codex item 3 (Codex shell_environment_policy): the model's commands do not see the seat's secrets.
public sealed class ShellEnvironmentPolicyTests
{
    [Fact]
    public void SecretNamesAreTakenOutAndTheSetIsApplied()
    {
        var policy = new ShellEnvironmentPolicy(ShellEnvironmentPolicy.SecretNames, new Dictionary<string, string> { ["NO_COLOR"] = "1" });
        var env = new Dictionary<string, string?>
        {
            ["PI_FACTORY_TOKEN"] = "t", ["GITLAB_TOKEN"] = "t", ["OPENAI_API_KEY"] = "k", ["DB_PASSWORD"] = "p", ["my_secret_x"] = "s",
            ["HOME"] = "/home/seat", ["PATH"] = "/usr/bin", ["TOKENIZERS_PARALLELISM"] = "false",
        };
        var removed = policy.Apply(env);
        Assert.Equal(["DB_PASSWORD", "GITLAB_TOKEN", "OPENAI_API_KEY", "PI_FACTORY_TOKEN", "my_secret_x"], removed.Order(StringComparer.Ordinal));
        Assert.Equal(["HOME", "NO_COLOR", "PATH", "TOKENIZERS_PARALLELISM"], env.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("1", env["NO_COLOR"]);
    }

    [Fact]
    public async Task ACommandDoesNotSeeAWithheldVariable()
    {
        using var tmp = new TempWorkspace();
        const string name = "PI_SHELL_POLICY_TEST_SECRET";
        Environment.SetEnvironmentVariable(name, "do-not-leak");
        var before = BashTool.EnvironmentPolicy;
        try
        {
            BashTool.EnvironmentPolicy = new ShellEnvironmentPolicy([name], new Dictionary<string, string>());
            var result = await ToolRun.Exec(new BashTool(tmp.Path), $$"""{"command":"echo \"[${{{name}}:-unset}]\""}""");
            Assert.Contains("[unset]", result.Text);
            Assert.DoesNotContain("do-not-leak", result.Text);
        }
        finally
        {
            BashTool.EnvironmentPolicy = before;
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
