using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Cli;
using PiSharp.Core.Guard;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Cli;

public sealed class GuardCommandTests
{
    private const string RulesJson =
        "[{\"id\":\"seed-rule\",\"message\":\"no pushing to master\",\"kind\":\"prefix\",\"prefix\":[\"git\",\"push\",\"master|main\"],\"action\":\"block\"}," +
        "{\"id\":\"regex-rule\",\"message\":\"no force flags\",\"pattern\":\"--force\",\"action\":\"block\"}," +
        "{\"id\":\"warn-rule\",\"message\":\"watch out\",\"pattern\":\"rm -rf\",\"action\":\"warn\"}]";

    private static TempWorkspace Setup(out string cwd, out string? configDir, out string? rulesFile, string? notebookDir = null)
    {
        var tmp = new TempWorkspace();
        cwd = tmp.Path;
        configDir = tmp.Dir("config");
        rulesFile = null;
        var settings = new Dictionary<string, string?>();
        if (notebookDir is not null) settings["notebookDirectory"] = notebookDir;
        File.WriteAllText(Path.Combine(configDir, "settings.json"),
            JsonSerializer.Serialize(settings.Count > 0 ? settings : new Dictionary<string, string?>()));
        Directory.CreateDirectory(Path.Combine(cwd, ".pi-sharp"));
        return tmp;
    }

    private static (int Code, string Out, string Err) RunCmd(string cwd, string? configDir, params string[] args)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var code = GuardCommand.Run(["guard", args[0], "--config", configDir!, .. args[1..]], outWriter, errWriter, cwd);
        return (code, outWriter.ToString(), errWriter.ToString());
    }

    [Fact]
    public void List_prints_seeded_rules_with_mode_action_and_predicate()
    {
        using var tmp = Setup(out var cwd, out var configDir, out _);
        var (code, outText, _) = RunCmd(cwd, configDir, "list");
        Assert.Equal(0, code);
        Assert.Contains("rule: push-to-master", outText);
        Assert.Contains("mode: enforce", outText);
        Assert.Contains("action: block", outText);
        Assert.Contains("source: fleet (seeded)", outText);
        Assert.Contains("prefix git push master|main", outText);
    }

    [Fact]
    public void List_json_is_stable_machine_readable_shape()
    {
        using var tmp = Setup(out var cwd, out var configDir, out _);
        var (code, outText, _) = RunCmd(cwd, configDir, "list", "--json");
        Assert.Equal(0, code);
        var array = Assert.IsType<JsonArray>(JsonNode.Parse(outText));
        var rules = array.Select(r => (JsonObject)r!).ToList();
        var seeded = rules.First(r => (string?)r["id"] == "push-to-master");
        Assert.Equal("enforce", (string?)seeded["mode"]);
        Assert.Equal("block", (string?)seeded["action"]);
        Assert.Equal("fleet (seeded)", (string?)seeded["source"]);
        Assert.StartsWith("prefix", (string?)seeded["predicate"]);
    }

    [Fact]
    public void List_uses_fleet_file_when_configured_and_lists_its_rules_in_enforce_mode()
    {
        using var tmp = Setup(out var cwd, out var configDir, out string? rulesFile, null);
        var file = tmp.File("fleet-rules.json", RulesJson);
        File.WriteAllText(Path.Combine(configDir!, "settings.json"),
            "{\"guardRulesFile\":\"" + Esc(file) + "\"}");
        var (code, outText, _) = RunCmd(cwd, configDir, "list");
        Assert.Equal(0, code);
        Assert.Contains("rule: seed-rule", outText);
        Assert.Contains("rule: regex-rule", outText);
        Assert.Contains("mode: enforce", outText);
        Assert.DoesNotContain("rule: push-to-master", outText);
    }

    private static string Esc(string path) => path.Replace('\\', '/');

    [Fact]
    public void List_includes_promoted_seat_notebook_rules_in_shadow_mode()
    {
        using var tmp = Setup(out var cwd, out var configDir, out string? rulesFile, null);
        var notebookDir = tmp.Dir("notebook");
        File.WriteAllText(Path.Combine(notebookDir, "notebook.jsonl"),
            "{\"version\":1,\"owner\":\"cli-test\",\"operationId\":\"op1\",\"record\":{" +
            "\"id\":\"guard-r1\",\"kind\":\"guard\",\"task\":\"t1\",\"status\":\"open\",\"revision\":1," +
            "\"text\":\"{\\\"ruleId\\\":\\\"guard-r1\\\",\\\"pattern\\\":\\\"danger-pattern\\\",\\\"message\\\":\\\"seat rule\\\"}\"}}\n");
        File.WriteAllText(Path.Combine(notebookDir, "writer.lock"), "");
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "settings.json"),
            "{\"notebookDirectory\":\"" + notebookDir.Replace("\\", "\\\\") + "\"}");
        var (code, outText, _) = RunCmd(cwd, configDir, "list");
        Assert.Equal(0, code);
        Assert.Contains("rule: guard-r1", outText);
        Assert.Contains("mode: shadow", outText);
        Assert.Contains("source: notebook", outText);
    }

    [Fact]
    public void List_empty_rule_set_prints_nothing_and_exits_zero()
    {
        using var tmp = Setup(out var cwd, out var configDir, out string? rulesFile, null);
        // An explicit empty array file: the fleet set is empty, no notebook rules.
        var file = tmp.File("empty.json", "[]");
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "settings.json"),
            "{\"guardRulesFile\":\"" + Esc(file) + "\"}");
        var (code, outText, _) = RunCmd(cwd, configDir, "list");
        Assert.Equal(0, code);
        Assert.Equal("", outText);
    }

    [Fact]
    public void Explain_matching_rule_reports_block_with_rule_id_and_predicate()
    {
        using var tmp = Setup(out var cwd, out var configDir, out string? rulesFile, null);
        var file = tmp.File("fleet-rules.json", RulesJson);
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "settings.json"),
            "{\"guardRulesFile\":\"" + Esc(file) + "\"}");
        var call = "{\"name\":\"bash\",\"arguments\":{\"command\":\"git push master\"}}";
        var (code, outText, _) = RunCmd(cwd, configDir, "explain", call);
        Assert.Equal(0, code);
        Assert.Contains("verdict: block", outText);
        Assert.Contains("rule: seed-rule", outText);
        Assert.Contains("would apply", outText);
        Assert.Contains("prefix git push master|main", outText);
    }

    [Fact]
    public void Explain_non_matching_call_is_allowed()
    {
        using var tmp = Setup(out var cwd, out var configDir, out string? rulesFile, null);
        var file = tmp.File("fleet-rules.json", RulesJson);
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "settings.json"),
            "{\"guardRulesFile\":\"" + Esc(file) + "\"}");
        var call = "{\"name\":\"bash\",\"arguments\":{\"command\":\"git push -u origin feature/x\"}}";
        var (code, outText, _) = RunCmd(cwd, configDir, "explain", call);
        Assert.Equal(0, code);
        Assert.Contains("verdict: allow", outText);
        Assert.DoesNotContain("would apply", outText);
    }

    [Fact]
    public void Explain_distinguishes_shadow_seat_rule_from_enforce_fleet_rule()
    {
        using var tmp = Setup(out var cwd, out var configDir, out string? rulesFile, null);
        var file = tmp.File("fleet-rules.json", RulesJson);
        var notebookDir = tmp.Dir("notebook");
        File.WriteAllText(Path.Combine(notebookDir, "notebook.jsonl"),
            "{\"version\":1,\"owner\":\"cli-test\",\"operationId\":\"op1\",\"record\":{" +
            "\"id\":\"guard-r1\",\"kind\":\"guard\",\"task\":\"t1\",\"status\":\"open\",\"revision\":1," +
            "\"text\":\"{\\\"ruleId\\\":\\\"guard-r1\\\",\\\"pattern\\\":\\\"force-flag\\\",\\\"message\\\":\\\"seat shadow rule\\\"}\"}}\n");
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "settings.json"),
            "{\"guardRulesFile\":\"" + Esc(file) + "\",\"notebookDirectory\":\"" + Esc(notebookDir) + "\"}");
        // Matches only the seat's shadow rule, not any enforce rule.
        var call = "{\"name\":\"bash\",\"arguments\":{\"command\":\"echo force-flag\"}}";
        var (code, outText, _) = RunCmd(cwd, configDir, "explain", call);
        Assert.Equal(0, code);
        Assert.Contains("verdict: shadow (would block, not enforced)", outText);
        Assert.Contains("rule: guard-r1", outText);
        Assert.Contains("mode: shadow", outText);
        // A call matching an enforce rule blocks even if the shadow rule also matches.
        var call2 = "{\"name\":\"bash\",\"arguments\":{\"command\":\"git push --force origin force-flag\"}}";
        var (code2, outText2, _) = RunCmd(cwd, configDir, "explain", call2);
        Assert.Equal(0, code2);
        Assert.Contains("verdict: block", outText2);
    }

    [Fact]
    public void Explain_json_shape_reports_blocking_rule_id_and_matched_predicate()
    {
        using var tmp = Setup(out var cwd, out var configDir, out string? rulesFile, null);
        var file = tmp.File("fleet-rules.json", RulesJson);
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "settings.json"),
            "{\"guardRulesFile\":\"" + Esc(file) + "\"}");
        var call = "{\"name\":\"bash\",\"arguments\":{\"command\":\"git push main\"}}";
        var (code, outText, _) = RunCmd(cwd, configDir, "explain", call, "--json");
        Assert.Equal(0, code);
        var doc = (JsonObject)JsonNode.Parse(outText)!;
        Assert.Equal("block", (string?)doc["verdict"]);
        Assert.Equal("seed-rule", (string?)doc["blockingRuleId"]);
        var hit = ((JsonArray)doc["wouldApply"]!).OfType<JsonObject>().First(h => (string?)h["id"] == "seed-rule");
        Assert.True((bool)hit["wouldApply"]!);
        Assert.Contains("git push", (string?)hit["matchedOn"]);
    }

    [Fact]
    public void Explain_treats_plain_command_string_as_bash_call()
    {
        using var tmp = Setup(out var cwd, out var configDir, out string? rulesFile, null);
        var file = tmp.File("fleet-rules.json", RulesJson);
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "settings.json"),
            "{\"guardRulesFile\":\"" + Esc(file) + "\"}");
        var (code, outText, _) = RunCmd(cwd, configDir, "explain", "git push master");
        Assert.Equal(0, code);
        Assert.Contains("verdict: block", outText);
        Assert.Contains("rule: seed-rule", outText);
    }
}
