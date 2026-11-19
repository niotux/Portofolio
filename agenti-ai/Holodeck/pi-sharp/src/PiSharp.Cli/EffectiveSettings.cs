using System.Text.Json.Nodes;
using PiSharp.Core.Config;

namespace PiSharp.Cli;

/// <summary>An allowlisted, credential-free operator view. Values come from the running state, not a dump of config files.</summary>
public static class EffectiveSettings
{
    public static string Describe(CliState state)
    {
        var s = state.Settings;
        var global = Read(ConfigPaths.SettingsFile(ConfigPaths.GlobalConfigDir(state.ConfigDir)));
        var project = Read(ConfigPaths.SettingsFile(ConfigPaths.ProjectConfigDir(state.Cwd)));
        string Origin(string key) => project[key] is not null ? "project (or runtime override)"
            : global[key] is not null ? "global (or runtime override)" : "default/runtime";
        var lines = new List<string> { "Effective settings (running session; credentials excluded):",
            $"model: {state.Model.Id} [runtime]", $"contextWindow: {state.Model.ContextWindow} [model/assignment]",
            $"maxOutputTokens: {state.Model.MaxOutputTokens} [model/assignment]" };
        void Add(string key, object? value, string activation) => lines.Add($"{key}: {value?.ToString() ?? "unset"} [{Origin(key)}; {activation}]");
        Add("toolCallTimeoutSeconds", s.ToolCallTimeoutSeconds is > 0 ? s.ToolCallTimeoutSeconds.Value : 900, "next run");
        Add("maxJobs", s.MaxJobs is > 0 ? s.MaxJobs.Value : 4, "startup");
        Add("taskTool", state.Context.Tools.Any(t => t.Name == "task"), "installed at startup");
        Add("reviewTool", state.Context.Tools.Any(t => t.Name == "review"), "installed at startup");
        Add("verifyOnStop", s.VerifyOnStop ?? true, "next run");
        Add("goalAudits", s.GoalAudits, "next run; unset uses factory switches");
        Add("serverCompaction", s.ServerCompaction == true, "next run");
        Add("telemetry", state.Telemetry is not null, "active sender");
        Add("freeformApplyPatch", s.FreeformApplyPatch == true, "next run");
        Add("epicOutputBudget", s.EpicOutputBudget, "next run; epic override takes precedence");
        if (state.Factory is { } factory)
        {
            var cached = factory.SwitchSnapshot();
            lines.Add(cached.Switches is null ? "Factory switches: not read yet" : $"Factory switches last read: {cached.At:O}");
            foreach (var name in new[] { "handin-criteria", "blocked-audit", "epic-budget" })
                lines.Add($"  {name}: {(s.GoalAudits is { } forced ? forced.ToString() + " (settings override)" : cached.Switches?.GetValueOrDefault(name).ToString() ?? "unknown")}");
        }
        return string.Join('\n', lines);
    }

    private static JsonObject Read(string path)
    {
        try { return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return new(); }
    }
}
