using System.Text.Json.Nodes;
using PiSharp.Core.Config;
using PiSharp.Core.Guard;

namespace PiSharp.Cli;

/// <summary>
/// <c>pi-sharp guard list</c> and <c>pi-sharp guard explain</c>: a read-only surface over the
/// seat's active guard rules (the read-only counterpart to the guard loader of the guard-rules
/// epic). It never changes enforcement: it only evaluates the existing detectors against the
/// given input and prints what the seat would do.
/// </summary>
public static class GuardCommand
{
    public const string Usage = "usage: pi-sharp guard list [--json] [--config <config dir>]\n" +
        "  Prints the guard rules active on this seat: rule id, file/source, mode (block/shadow),\n" +
        "  action and the effective predicate. Machine-readable with --json. Exits 0; prints\n" +
        "  nothing for an empty rule set.\n" +
        "\n" +
        "usage: pi-sharp guard explain <tool-call-json> [--json] [--config <config dir>]\n" +
        "  Dry-run: reports which rule would block or warn for the call, and why, without executing\n" +
        "  the tool. <tool-call-json> is a JSON object {\"name\":\"<tool>\",\"arguments\":{...}}; a\n" +
        "  plain non-JSON string is treated as a bash command. Exits 0.";

    public static bool Handles(IReadOnlyList<string> argv) =>
        argv.Count > 0 && argv[0] == "guard" && (argv.Count == 1
            || argv[1] is "list" or "explain" or "-h" or "--help");

    public static int Run(IReadOnlyList<string> argv, TextWriter output, TextWriter error, string? cwd = null)
    {
        cwd ??= Directory.GetCurrentDirectory();
        if (argv.Count < 2 || argv[1] is "-h" or "--help")
        {
            output.WriteLine(Usage);
            return 0;
        }
        bool json = false;
        string? configDir = null;
        var rest = new List<string>();
        for (var i = 2; i < argv.Count; i++)
        {
            switch (argv[i])
            {
                case "--json": json = true; break;
                case "--config" when i + 1 < argv.Count: configDir = argv[++i]; break;
                default: rest.Add(argv[i]); break;
            }
        }
        try
        {
            return argv[1] == "list"
                ? List(cwd, configDir, json, output, error)
                : Explain(cwd, configDir, json, rest, output, error);
        }
        catch (Exception ex)
        {
            error.WriteLine("guard: " + ex.Message);
            return 2;
        }
    }

    // ------------------------------------------------------------------ list

    private static int List(string cwd, string? configDir, bool json, TextWriter output, TextWriter error)
    {
        var (rules, rejected) = LoadRules(cwd, configDir);
        if (json)
        {
            var array = new JsonArray(rules.Select(r => (JsonNode)RuleJson(r)).ToArray());
            output.WriteLine(array.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        foreach (var rule in rules)
            output.WriteLine(FormatRule(rule));
        foreach (var line in rejected)
            error.WriteLine(line);
        return 0;
    }

    // ---------------------------------------------------------------- explain

    private static int Explain(string cwd, string? configDir, bool json, IReadOnlyList<string> rest,
        TextWriter output, TextWriter error)
    {
        if (rest.Count != 1)
        {
            error.WriteLine(Usage);
            return 2;
        }
        var (rules, rejected) = LoadRules(cwd, configDir);
        var (name, arguments) = ParseCall(rest[0], error);
        if (name is null) return 2;

        var hits = new List<JsonObject>();
        string? blockingId = null;
        var serialized = name + " " + arguments.ToJsonString();

        foreach (var rule in rules)
        {
            string? matchedOn = MatchedOn(rule.Rule, name, arguments);
            if (matchedOn is null) continue;
            var entry = (JsonObject)RuleJson(rule);
            entry["wouldApply"] = true;
            entry["matchedOn"] = matchedOn;
            if (ToolGuard.GuardOverrideMarker(serialized) is { } marker)
            {
                entry["reason"] = $"matched, but the call carries the '# guard-ok:' exemption marker (exempt: {marker})";
                hits.Add(entry);
                continue;
            }
            entry["reason"] = rule.Rule.Message;
            hits.Add(entry);
            if (blockingId is null && rule.Rule.Action == GuardAction.Block && rule.Source != "notebook")
                blockingId = rule.Rule.Id;
        }

        string verdict;
        if (hits.Count == 0) verdict = "allow";
        else if (blockingId is not null) verdict = "block";
        else verdict = "shadow (would block, not enforced)";

        foreach (var line in rejected)
            error.WriteLine(line);

        if (json)
        {
            var doc = new JsonObject
            {
                ["call"] = new JsonObject { ["name"] = name, ["arguments"] = arguments },
                ["rules"] = new JsonArray(rules.Select(r => (JsonNode)RuleJson(r)).ToArray()),
                ["wouldApply"] = new JsonArray(hits.Select(h => (JsonNode)h).ToArray()),
                ["verdict"] = verdict,
            };
            if (blockingId is not null)
                doc["blockingRuleId"] = blockingId;
            output.WriteLine(doc.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        output.WriteLine($"call: {name} {arguments.ToJsonString()}");
        if (hits.Count == 0)
        {
            output.WriteLine("verdict: allow (no active rule matches this call)");
            return 0;
        }
        output.WriteLine($"verdict: {verdict}");
        foreach (var entry in hits)
        {
            output.WriteLine($"rule: {entry["id"]!.GetValue<string>()}  " +
                $"mode: {entry["mode"]!.GetValue<string>()}  " +
                $"action: {entry["action"]!.GetValue<string>()}  would apply");
            output.WriteLine($"matched on: {entry["matchedOn"]!.GetValue<string>()}");
            output.WriteLine($"reason: {entry["reason"]!.GetValue<string>()}");
        }
        return 0;
    }

    // ---------------------------------------------------------------- shared

    private static string FormatRule(GuardSourceRule rule)
    {
        return $"rule: {rule.Rule.Id}  " +
               $"source: {rule.Source}  " +
               $"mode: {ModeOf(rule)}  " +
               $"action: {rule.Rule.Action.ToString().ToLowerInvariant()}  " +
               $"predicate: {PredicateOf(rule.Rule)}";
    }

    private static JsonObject RuleJson(GuardSourceRule rule) => new()
    {
        ["id"] = rule.Rule.Id,
        ["message"] = rule.Rule.Message,
        ["source"] = rule.Source,
        ["mode"] = ModeOf(rule),
        ["action"] = rule.Rule.Action.ToString().ToLowerInvariant(),
        ["point"] = rule.Rule.Point.ToString().ToLowerInvariant(),
        ["predicate"] = PredicateOf(rule.Rule),
    };

    /// <summary>The effective predicate of a rule: the token prefix (kind prefix) or the regex.</summary>
    private static string PredicateOf(GuardRule rule)
    {
        if (rule.Detector is { } detector)
            return "prefix " + string.Join(" ", detector.Tokens);
        return "regex " + rule.Pattern;
    }

    /// <summary>The seat's effective mode for a rule. Fleet rules (the fleet file or the seeded
    /// defaults) enforce; the seat's own notebook rules run in shadow (that is how the seat
    /// evaluates them, see GuardSeatHook in CliHost). A rule whose action is warn never blocks,
    /// regardless of source.</summary>
    private static string ModeOf(GuardSourceRule rule)
    {
        if (rule.Rule.Action == GuardAction.Warn) return "warn";
        return rule.Source == "notebook" ? "shadow" : "enforce";
    }

    /// <summary>Which detector of the rule matched, evaluated with the same priority the guard
    /// uses (prefix first, then the regex over the serialized call). Returns the matched
    /// predicate text, or null when the rule does not match.</summary>
    private static string? MatchedOn(GuardRule rule, string name, JsonObject arguments)
    {
        var command = arguments["command"]?.GetValue<string>() ?? string.Empty;
        if (rule.Detector is { } detector && PrefixRuleEngine.TryMatch(command, detector.Tokens))
            return "prefix " + string.Join(" ", detector.Tokens);
        if (rule.Pattern is null) return null;
        var text = name + " " + arguments.ToJsonString();
        try
        {
            var regex = ToolGuard.CompileForPattern(rule.Pattern);
            return regex.IsMatch(text) ? "regex " + rule.Pattern : null;
        }
        catch { return null; }
    }

    private static (string? Name, JsonObject Arguments) ParseCall(string text, TextWriter error)
    {
        try
        {
            var node = JsonNode.Parse(text);
            if (node is JsonObject { } obj)
            {
                var name = obj["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                {
                    error.WriteLine("guard explain: the call JSON needs a \"name\" field.");
                    return (null, new JsonObject());
                }
                // Deep-clone: the node still has its parent from the parsed call JSON and would
                // throw if re-attached to the output document.
                var args = (obj["arguments"] as JsonObject ?? new JsonObject()).DeepClone();
                return (name, (JsonObject)args!);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // not an object: treat the whole string as a bash command
        }
        return ("bash", new JsonObject { ["command"] = text });
    }

    // ---------------------------------------------------------------- loading

    /// <summary>Loads the seat's active rule set. Source order: the fleet (the
    /// <c>guardRulesFile</c> JSON array when configured and present, otherwise the seeded fleet
    /// rules) loaded through <see cref="ToolGuard.FromJson"/> so validation is identical to the
    /// seat's guard, then the seat's own notebook rules (kind <c>guard</c> rows with a
    /// <c>ruleId</c> and a <c>pattern</c>). Invalid rules are dropped and reported as the guard
    /// reports them, so the CLI shows the seat the same set the seat actually runs.</summary>
    internal static (IReadOnlyList<GuardSourceRule> Rules, IReadOnlyList<string> Rejected) LoadRules(
        string cwd, string? configDir)
    {
        var settings = SettingsLoader.Load(cwd, configDir);
        var rules = new List<GuardSourceRule>();
        var rejected = new List<string>();

        // Fleet rules, loaded through the guard's own JSON path.
        if (!string.IsNullOrWhiteSpace(settings.GuardRulesFile)
            && File.Exists(Path.GetFullPath(ConfigPaths.ExpandHome(settings.GuardRulesFile), cwd)))
        {
            var path = Path.GetFullPath(ConfigPaths.ExpandHome(settings.GuardRulesFile), cwd);
            string fleetSource = "fleet " + settings.GuardRulesFile;
            try
            {
                var fleet = ToolGuard.FromJson(File.ReadAllText(path), GuardMode.Shadow);
                foreach (var rule in fleet.Rules)
                    rules.Add(new GuardSourceRule(rule, fleetSource));
            }
            catch (Exception ex)
            {
                rejected.Add("fleet rules file could not be parsed: " + ex.Message);
            }
        }
        else
        {
            foreach (var rule in ToolGuard.SeededRules)
                rules.Add(new GuardSourceRule(rule, "fleet (seeded)"));
        }

        // Seat notebook rules: kind "guard" rows whose text body carries a ruleId and a pattern
        // (the same shape the seat's guard loads, see CliHost.RefreshPatternsLocked). The journal
        // is read directly with a shared read handle (the CLI must not take the seat's writer lock).
        if (!string.IsNullOrWhiteSpace(settings.NotebookDirectory))
        {
            var notebookDir = Path.GetFullPath(ConfigPaths.ExpandHome(settings.NotebookDirectory), cwd);
            foreach (var row in NotebookRuleRows(notebookDir))
            {
                var text = row["text"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text)) continue;
                JsonObject? body = null;
                try { body = JsonNode.Parse(text)?.AsObject(); } catch { body = null; }
                var id = body?["ruleId"]?.GetValue<string>();
                var pattern = body?["pattern"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(pattern)) continue;
                var rule = new GuardRule(id, body?["message"]?.GetValue<string>() ?? "guard rule " + id,
                    GuardPoint.ToolCall, GuardAction.Block, null, pattern);
                rules.Add(new GuardSourceRule(rule, "notebook"));
            }
        }

        // Load-time validation over the notebook rules (the fleet rules are already validated
        // by the guard's own loader above).
        if (rules.Count > 0)
        {
            var guard = new ToolGuard(rules.Select(r => r.Rule).ToArray(), GuardMode.Shadow);
            rejected.AddRange(guard.ValidationErrors);
            var validIds = new HashSet<string>(guard.Rules.Select(r => r.Id));
            rules = rules.Where(r => validIds.Contains(r.Rule.Id)).ToList();
        }

        return (rules, rejected);
    }

    /// <summary>A guard rule plus where it came from (the CLI's "file/source" column).</summary>
    public sealed record GuardSourceRule(GuardRule Rule, string Source);

    /// <summary>Reads the notebook journal directly (the CLI must not take the seat's writer
    /// lock, which a running seat holds). Rows are read with a shared read handle, so the seat
    /// can be running. Only rows with kind "guard" and a ruleId are considered.</summary>
    private static List<JsonObject> NotebookRuleRows(string notebookDir)
    {
        var rows = new List<JsonObject>();
        var journal = Path.Combine(notebookDir, "notebook.jsonl");
        if (!File.Exists(journal)) return rows;
        using var reader = new StreamReader(new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? row;
            try { row = JsonNode.Parse(line) as JsonObject; } catch { continue; }
            if (row is null) continue;
            // A journal op wraps the record in a "record" field; a bare record has no envelope.
            if (row["record"] is { } envelope && envelope is JsonObject inner) row = inner;
            if (row["kind"]?.GetValue<string>() != "guard") continue;
            rows.Add(row);
        }
        return rows;
    }
}

