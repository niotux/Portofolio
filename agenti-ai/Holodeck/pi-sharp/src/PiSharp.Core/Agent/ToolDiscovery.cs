using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;

namespace PiSharp.Core.Agent;

/// <summary>
/// The <c>toolkit</c> tool: every permitted tool beyond the core set is reached through it, grouped in toolkits (the part of
/// a tool's name before <c>__</c>: an MCP server alias, or a native toolkit such as <c>desktop</c>; the rest is
/// <c>session</c>). The advertised tool list never changes mid-task, because the tool list is part of the cached prompt
/// prefix and any change to it re-reads the whole context (adr-visual-toolkits section 3). Schemas reach the model as tool
/// results, which extend the cache, and <c>toolkit call</c> runs a tool through the agent loop's own lookup, validation and
/// guards (<see cref="Dispatch"/>). Tools used during a task are promoted into the advertised list only when the history
/// shrinks (compaction, a fresh task, a rewind), where the prefix is lost anyway. Never owns executable copies.
/// </summary>
public sealed class ToolDiscovery(Func<IReadOnlyList<IAgentTool>> registry) : IAgentTool
{
    public const string ToolName = "toolkit";
    /// <summary>Schema characters advertised per request, core tools included. 24,000 left room for about two
    /// integration tools next to the core set, so searches kept releasing each other (40 releases and refusals in
    /// 48 h, 2026-09-27); schemas sit in the cached prefix, so the room costs little once the list is stable.
    /// 64,000 since 2026-10-20, with the KB and chat tools every seat uses in the core set (<see cref="IsCore"/>).</summary>
    public const int MaxSchemaCharacters = 64000;
    /// <summary>An <c>open</c> answer larger than this lists names and summaries; the model then opens single tools.</summary>
    public const int MaxOpenCharacters = 12000;
    private const string MemoryKey = "runtime-tool-activation";

    // Advertised beyond the core set, oldest first. Changes only in Rebase.
    private readonly List<string> _promoted = [];
    // Used since the last rebase, oldest first; promoted at the next one.
    private readonly List<string> _used = [];
    private readonly HashSet<string> _closed = new(StringComparer.Ordinal);
    private NotebookStore? _memory;
    private int _messages;
    private string? _description;

    public void Restore(NotebookStore memory)
    {
        _memory = memory;
        if (memory.Get(MemoryKey) is { } row)
            foreach (var name in JsonNode.Parse(row["text"]!.GetValue<string>())!.AsArray())
                if (!_promoted.Contains(name!.GetValue<string>())) _promoted.Add(name.GetValue<string>());
        _description = null;
        FitToBudget(registry());
    }

    /// <summary>
    /// Called by the agent loop before each request with the session's message count. A shorter history than last time
    /// means the cached prefix is gone (compaction, fresh task, rewind): the tools used since the last rebase join the
    /// advertised list then, and nowhere else.
    /// </summary>
    public void Observe(int messageCount)
    {
        if (messageCount < _messages) Rebase();
        _messages = messageCount;
    }

    /// <summary>Promotes the tools used since the last rebase, drops closed toolkits, and fits the schema budget.</summary>
    public IReadOnlyList<string> Rebase()
    {
        foreach (var name in _used) { _promoted.Remove(name); _promoted.Add(name); }
        _used.Clear();
        _promoted.RemoveAll(n => _closed.Contains(ToolkitOf(n)));
        _closed.Clear();
        _description = null;
        return FitToBudget(registry());
    }

    /// <summary>
    /// Releases the oldest promotions until the advertised schemas fit. Only at a rebase or a restore: a restored set, a
    /// newly core tool or an MCP refresh can push it over; coder-06-pi1 crash-looped on 0.5.23 when this was a throw.
    /// </summary>
    public IReadOnlyList<string> FitToBudget(IReadOnlyList<ITool> tools)
    {
        var sizes = Sizes(tools);
        var core = sizes.Where(s => IsCore(s.Key)).Sum(s => s.Value);
        int Size() => core + _promoted.Where(n => !IsCore(n)).Sum(n => sizes.GetValueOrDefault(n));
        var released = new List<string>();
        while (Size() > MaxSchemaCharacters && _promoted.Count > 0) { released.Add(_promoted[0]); _promoted.RemoveAt(0); }
        Save();
        return released;
    }

    /// <summary>Notes a tool call for promotion at the next rebase; the loop calls it for direct and dispatched calls.</summary>
    public void NoteUse(string name)
    {
        if (IsCore(name)) return;
        _used.Remove(name); _used.Add(name);
    }

    public bool IsActive(ITool tool) => IsCore(tool.Name) || _promoted.Contains(tool.Name);

    public static bool IsCore(string name) => name is "read" or "write" or "edit" or "bash" or "grep" or "find" or "ls" or "apply_patch" or "notebook" or "artifact" or ToolName or "job"
        || name.EndsWith("search_skills", StringComparison.Ordinal)
        // Always active: a seat that cannot post cannot deliver. architect-2, 2026-09-27 14:35Z: search_tools refused
        // on a full budget, chat_post never activated, and 64 notebook calls narrated a reply it had no tool to send.
        || name is "chat__chat_post"
        // The tools every seat reaches for (digital-scribe, 2026-10-20: "preload the common tool groups"): in the 24 h
        // before, 891 calls went through toolkit call and 360 more only opened, searched or listed toolkits, most for
        // these. chat_manage stays behind toolkit: status is set on a state change, not as a reflex (StatusGateTool).
        || name is "kb__kb_get" or "kb__kb_list" or "kb__kb_write" or "kb__kb_transition" or "kb__kb_suite" or "chat__chat_read"
        // A desktop seat's own work (the demo seat: 177 desktop calls through toolkit in the same 24 h).
        || name is "desktop__act" or "desktop__capture" or "desktop__inspect"
        // The working plan, used at every step while the switch plan-tool is on (the seat leaves it out of its list while
        // off). Behind toolkit, seats guessed "session.plan" and "session__plan" and were refused (pi5, pi7, 2026-10-26).
        || name is "plan";

    /// <summary>A permitted tool by its name, or by the toolkit-qualified forms models write: <c>kb.kb_get</c> for
    /// <c>kb__kb_get</c>, and <c>session.plan</c> or <c>session__plan</c> for a session tool's bare name.</summary>
    private static IAgentTool? Find(IEnumerable<IAgentTool> permitted, string name)
    {
        if (permitted.FirstOrDefault(t => t.Name == name) is { } exact) return exact;
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var qualified = dot > 0 ? name[..dot] + "__" + name[(dot + 1)..] : name;
        if (permitted.FirstOrDefault(t => t.Name == qualified) is { } byKit) return byKit;
        const string session = "session__";
        return qualified.StartsWith(session, StringComparison.Ordinal)
            ? permitted.FirstOrDefault(t => t.Name == qualified[session.Length..]) : null;
    }

    /// <summary>The toolkit a tool belongs to: the part of its name before <c>__</c>, otherwise <c>session</c>.</summary>
    public static string ToolkitOf(string name) => name.IndexOf("__", StringComparison.Ordinal) is > 0 and var i ? name[..i] : "session";

    public string Name => ToolName;
    public string Label => Name;
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;

    /// <summary>Lists the toolkits as they were at the last rebase, so the advertised list stays byte-stable mid-task.</summary>
    public string Description => _description ??=
        "Reach every tool not listed directly. Toolkits (tools):\n" + string.Join("\n", Toolkits().Select(g => $"- {g.Key}: {string.Join(", ", g.Select(Short))}"))
        + "\nop=open name=<toolkit or tool> shows schemas; op=call name=<tool> args={...} runs it; op=search query=<words> finds tools; "
        + "op=list shows the toolkits now; op=close name=<toolkit> keeps it out of the next context. Call tools listed directly by their own name.";

    public JsonObject Parameters => JsonNode.Parse("""
        {"type":"object","properties":{
          "op":{"type":"string","enum":["open","call","search","list","close"]},
          "name":{"type":"string","description":"open: a toolkit or a tool; call: the tool's full name, e.g. kb__kb_get; close: a toolkit"},
          "args":{"type":"object","description":"call: the tool's arguments, as its schema from open says"},
          "query":{"type":"string","description":"search: literal words from tool names or descriptions"}},
         "required":["op"]}
        """)!.AsObject();

    /// <summary>True when the arguments ask to run another tool; the agent loop then calls <see cref="Dispatch"/>.</summary>
    public static bool IsCall(JsonObject? arguments) => arguments?["op"]?.GetValue<string>() == "call";

    /// <summary>
    /// Resolves <c>op=call</c> to the permitted tool and its arguments. The loop then prepares, validates, guards and runs
    /// that tool exactly as if the model had called it by name; the result keeps this call's id.
    /// </summary>
    public (IAgentTool? Tool, JsonObject Arguments, string? Error) Dispatch(JsonObject arguments)
    {
        var name = arguments["name"]?.GetValue<string>()?.Trim() ?? "";
        if (name.Length == 0) return (null, [], "op=call needs name=<tool>.");
        if (name == ToolName) return (null, [], "toolkit cannot call itself.");
        var tool = Find(Permitted(), name);
        if (tool is null) return (null, [], $"No permitted tool '{name}'. {Suggest(name)}");
        var args = arguments["args"] switch
        {
            JsonObject o => (JsonObject)o.DeepClone(),
            JsonValue v when v.TryGetValue<string>(out var s) && JsonNode.Parse(s) is JsonObject parsed => parsed,
            null => [],
            _ => null,
        };
        return args is null ? (null, [], "args must be a JSON object.") : (tool, args, null);
    }

    public Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var op = arguments["op"]?.GetValue<string>() ?? "";
        var name = arguments["name"]?.GetValue<string>()?.Trim() ?? "";
        return Task.FromResult(op switch
        {
            "list" => AgentToolResult.FromText(string.Join("\n", Toolkits().Select(g => $"{g.Key} ({g.Count()}): {string.Join(", ", g.Select(Short))}"))),
            "open" => Open(name),
            "search" => Search(arguments["query"]?.GetValue<string>() ?? ""),
            "close" => Close(name),
            // The loop dispatches op=call before it reaches here; only a call it could not resolve lands here.
            "call" => AgentToolResult.Error(Dispatch(arguments).Error ?? "op=call is run by the agent loop."),
            _ => AgentToolResult.Error("op must be open, call, search, list or close."),
        });
    }

    private AgentToolResult Open(string name)
    {
        if (name.Length == 0) return AgentToolResult.Error("op=open needs name=<toolkit or tool>.");
        var permitted = Permitted();
        var tools = Find(permitted, name) is { } one ? [one]
            : permitted.Where(t => ToolkitOf(t.Name) == name).ToArray();
        if (tools.Length == 0) return AgentToolResult.Error($"No permitted toolkit or tool '{name}'. {Suggest(name)}");
        _closed.Remove(ToolkitOf(tools[0].Name));
        var full = string.Join("\n\n", tools.Select(Schema));
        if (full.Length <= MaxOpenCharacters || tools.Length == 1) return AgentToolResult.FromText(full);
        return AgentToolResult.FromText($"{name} has {tools.Length} tools, too large to show whole; open one by name:\n"
            + string.Join("\n", tools.Select(t => $"- {t.Name}: {t.ShortDescription}")));
    }

    private AgentToolResult Search(string query)
    {
        var words = query.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return AgentToolResult.Error("A specific search query is required.");
        var found = Permitted().Select(t => (Tool: t, Score: words.Sum(w =>
            t.Name.Contains(w, StringComparison.OrdinalIgnoreCase) ? 3 : t.Description.Contains(w, StringComparison.OrdinalIgnoreCase) ? 1 : 0)))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Tool.Name, StringComparer.Ordinal).Take(4).Select(x => x.Tool).ToArray();
        return found.Length == 0 ? AgentToolResult.FromText("No tool matches; op=list shows every toolkit.")
            : AgentToolResult.FromText(string.Join("\n\n", found.Select(Schema)));
    }

    private AgentToolResult Close(string name)
    {
        if (!Toolkits().Any(g => g.Key == name)) return AgentToolResult.Error($"No toolkit '{name}'.");
        _closed.Add(name);
        _used.RemoveAll(n => ToolkitOf(n) == name);
        return AgentToolResult.FromText($"{name} stays out of the next context; its tools still answer op=call until then.");
    }

    private IEnumerable<IAgentTool> Permitted() => registry().Where(t => t.Name != ToolName);

    private IEnumerable<IGrouping<string, IAgentTool>> Toolkits() =>
        Permitted().Where(t => !IsActive(t)).GroupBy(t => ToolkitOf(t.Name)).OrderBy(g => g.Key, StringComparer.Ordinal);

    private static string Short(IAgentTool tool) => ToolkitOf(tool.Name) is var kit && kit != "session" ? tool.Name[(kit.Length + 2)..] : tool.Name;

    private static string Schema(IAgentTool tool) => $"{tool.Name}: {tool.Description}\nargs schema: {tool.Parameters.ToJsonString()}";

    private string Suggest(string name)
    {
        var kit = ToolkitOf(name);
        var near = Permitted().Where(t => ToolkitOf(t.Name) == kit || t.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).Take(8).Select(t => t.Name).ToArray();
        return near.Length > 0 ? "Did you mean: " + string.Join(", ", near) + "?" : "op=list shows every toolkit.";
    }

    private static Dictionary<string, int> Sizes(IEnumerable<ITool> tools) => tools.GroupBy(t => t.Name, StringComparer.Ordinal).ToDictionary(g => g.Key,
        g => g.First().Parameters.ToJsonString().Length + g.First().Description.Length + g.Key.Length + 64, StringComparer.Ordinal);

    private void Save()
    {
        if (_memory is not { } memory) return;
        var row = memory.Get(MemoryKey);
        var text = new JsonArray(_promoted.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray()).ToJsonString();
        if (row?["text"]?.GetValue<string>() == text) return;
        memory.Put(MemoryKey, "attempt", text, "", "open", row?["revision"]?.GetValue<long>() ?? 0);
    }
}
