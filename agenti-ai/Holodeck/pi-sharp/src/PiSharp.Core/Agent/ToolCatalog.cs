// New in pi-sharp (docs/03 Phase 7): small models degrade past ~8 tools and waste
// context on long descriptions. Present a subset with shorter copy; execution still
// uses the full AgentContext.Tools list.
using System.Text.Json.Nodes;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;

namespace PiSharp.Core.Agent;

/// <summary>Chooses which tools the model sees and how they are described.</summary>
public static class ToolCatalog
{
    public const int SmallContextWindow = 16384;
    public const int SmallContextMaxTools = 8;
    public const int ShortDescriptionLimit = 160;

    /// <summary>
    /// Tools advertised on the next request. History-used tools are kept first when we have
    /// to drop some; descriptions are shortened under <see cref="SmallContextWindow"/>.
    /// </summary>
    public static IReadOnlyList<ITool> Present(
        IReadOnlyList<ITool> tools,
        ModelConfig model,
        IReadOnlyList<Message>? history = null)
    {
        if (tools.OfType<ToolDiscovery>().FirstOrDefault() is { } discovery)
        {
            // Pure: the set changes only at a rebase (ToolDiscovery.Observe), so the cached prefix holds mid-task.
            return tools.Where(discovery.IsActive).OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        }
        var max = model.MaxTools
            ?? (model.ContextWindow > 0 && model.ContextWindow < SmallContextWindow
                ? SmallContextMaxTools
                : int.MaxValue);
        var selected = Select(tools, max, history);
        var shorten = model.ContextWindow > 0 && model.ContextWindow < SmallContextWindow;
        if (!shorten) return selected;

        ITool[] presented = new ITool[selected.Count];
        for (var i = 0; i < selected.Count; i++)
        {
            var tool = selected[i];
            var description = Shorten(tool);
            presented[i] = ReferenceEquals(description, tool.Description)
                ? tool
                : new PresentedTool(tool.Name, description, tool.Parameters);
        }
        return presented;
    }

    public static IReadOnlyList<ITool> Select(
        IReadOnlyList<ITool> tools,
        int max,
        IReadOnlyList<Message>? history = null)
    {
        if (max <= 0 || tools.Count <= max) return tools;

        var byName = new Dictionary<string, ITool>(StringComparer.Ordinal);
        foreach (var tool in tools) byName.TryAdd(tool.Name, tool);

        var pinned = tools.Where(IsPinned).ToList();
        var effectiveMax = Math.Max(max, pinned.Count);
        var picked = new List<ITool>(effectiveMax);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tool in pinned)
        {
            if (seen.Add(tool.Name)) picked.Add(tool);
        }

        if (history is not null)
        {
            for (var i = history.Count - 1; i >= 0 && picked.Count < effectiveMax; i--)
            {
                switch (history[i])
                {
                    case AssistantMessage assistant:
                        foreach (var call in assistant.ToolCalls.Reverse())
                        {
                            if (seen.Add(call.Name) && byName.TryGetValue(call.Name, out var used))
                            {
                                picked.Add(used);
                                if (picked.Count >= effectiveMax) break;
                            }
                        }
                        break;
                    case ToolResultMessage result:
                        if (seen.Add(result.ToolName) && byName.TryGetValue(result.ToolName, out var named))
                        {
                            picked.Add(named);
                        }
                        break;
                }
            }
            picked.Reverse();
        }

        foreach (var tool in tools)
        {
            if (picked.Count >= effectiveMax) break;
            if (seen.Add(tool.Name)) picked.Add(tool);
        }

        return picked;
    }

    private static bool IsPinned(ITool tool) =>
        tool.Name.Equals("search_skills", StringComparison.OrdinalIgnoreCase)
        || tool.Name.EndsWith("__search_skills", StringComparison.OrdinalIgnoreCase);

    public static string Shorten(ITool tool)
    {
        var text = tool.ShortDescription;
        if (string.IsNullOrEmpty(text)) text = tool.Description;
        if (text.Length <= ShortDescriptionLimit) return text;
        var cut = text.LastIndexOf(' ', ShortDescriptionLimit - 1);
        if (cut < ShortDescriptionLimit / 2) cut = ShortDescriptionLimit - 1;
        return text[..cut].TrimEnd() + "…";
    }

    /// <summary>Canonical signature of a tool-call batch, used by the repetition guard.</summary>
    public static string BatchSignature(IEnumerable<ToolCallContent> calls)
    {
        var parts = calls.Select(c => c.Name + ":" + Canonical(c.Arguments));
        return string.Join("\n", parts);
    }

    public static string Canonical(JsonObject? args)
    {
        if (args is null || args.Count == 0) return "{}";
        var ordered = new JsonObject();
        foreach (var (key, value) in args.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            ordered[key] = value?.DeepClone();
        }
        return ordered.ToJsonString();
    }

    private sealed record PresentedTool(string Name, string Description, JsonObject Parameters) : ITool
    {
        public string ShortDescription => Description;
    }
}
