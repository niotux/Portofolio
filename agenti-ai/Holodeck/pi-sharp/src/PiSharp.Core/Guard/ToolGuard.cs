using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;

namespace PiSharp.Core.Guard;

public enum GuardPoint { ToolCall }
/// <summary>What a matching rule decides, Codex's rule decisions (2026-10-25, plan-pi-sharp-codex item 1): <c>Block</c> is
/// Codex's <c>forbidden</c>, <c>Ask</c> its <c>prompt</c> (an unattended seat has nobody to ask: a reviewer decides, see
/// <see cref="IActionReviewer"/>), <c>Warn</c> only logs. When several rules match, the strictest wins: Block, then Ask.</summary>
public enum GuardAction { Block, Warn, Ask }
public enum GuardMode { Off, Shadow, Enforce }

/// <summary>A rule detector: an ordered token prefix (kind <c>prefix</c>, the Codex execpolicy
/// shape) or a regex over the serialized call (kind <c>regex</c>, the legacy shape).</summary>
public sealed record GuardRuleDetector
{
    /// <summary>The token prefix (kind prefix): one entry per argv position; an entry is a single
    /// token or <c>a|b|c</c> alternatives. <see cref="PrefixRuleEngine.TryMatch"/> evaluates it.</summary>
    public required string[] Tokens { get; init; }
}

/// <summary>A deterministic rule evaluated against a tool invocation. Exactly one detector is
/// set: <see cref="Detector"/> (kind prefix) or <see cref="Pattern"/> (kind regex, the legacy
/// serialized-call match). <see cref="Examples"/> are checked at load: every match example must
/// match and every not_match example must not; a rule that fails validation is rejected by the
/// guard (one ops line) and never runs.</summary>
public sealed record GuardRule(string Id, string Message,
    GuardPoint Point = GuardPoint.ToolCall, GuardAction Action = GuardAction.Block,
    GuardRuleDetector? Detector = null, string? Pattern = null,
    (string Command, bool ExpectMatch)[] Examples = null!)
{
    /// <summary>True for the tokenised sub-command prefix kind.</summary>
    public bool IsPrefix => Detector is not null;

    /// <summary>The roles the rule applies to (a seat holding any of them); null = every seat.</summary>
    public IReadOnlyList<string>? Roles { get; init; }
    /// <summary>Roles the rule never applies to (a seat holding any of them), e.g. the merge owners for push-to-master.</summary>
    public IReadOnlyList<string>? ExceptRoles { get; init; }
    /// <summary>What to do instead, told to the model with the refusal (Codex: "for forbidden, suggest an alternative").</summary>
    public string? Alternative { get; init; }

    /// <summary>Whether the rule applies to a seat holding <paramref name="roles"/> (null = roles unknown: every rule that
    /// names no roles of its own applies, and no rule that does).</summary>
    public bool AppliesTo(IReadOnlyCollection<string>? roles)
    {
        if (roles is null) return Roles is null;
        if (ExceptRoles is { Count: > 0 } except && except.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase))) return false;
        return Roles is not { Count: > 0 } only || only.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Legacy positional shape (id, pattern, message, ...); a regex rule.</summary>
    public GuardRule(string id, string pattern, string message,
        GuardPoint point = GuardPoint.ToolCall, GuardAction action = GuardAction.Block)
        : this(id, message, point, action, null, pattern, null!)
    {
    }
}

/// <summary>Splits a shell command into sub-commands the way a shell would start a new simple
/// command: the separators are <c>&amp;&amp;</c>, <c>||</c>, <c>;</c>, <c>|</c>, newlines and
/// command substitutions <c>$(...)</c> and backticks. Quoted text is skipped whole (a separator
/// inside quotes is data), <c>#</c> comments are dropped, and redirections (<c>&gt;</c>,
/// <c>&lt;</c>, <c>&gt;&amp;</c>, ...) do not split. Each sub-command is returned with leading
/// and trailing whitespace removed; empty pieces are dropped.</summary>
public static class SubCommandSplit
{
    public static IReadOnlyList<string> Split(string command)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        void PushCurrent()
        {
            var text = current.ToString().Trim();
            current.Clear();
            if (text.Length > 0) result.Add(text);
        }
        int i = 0;
        char? quote = null;
        while (i < command.Length)
        {
            var c = command[i];
            if (quote is not null)
            {
                current.Append(c);
                if (c == quote && quote != '\'')
                {
                    // an escaped quote does not close a double-quoted section
                    if (i > 0 && command[i - 1] == '\\') current.Append(c);
                    else quote = null;
                }
                i++;
                continue;
            }
            if (c == '\'' || c == '"')
            {
                quote = c;
                current.Append(c);
                i++;
                continue;
            }
            if (c == '#')
            {
                // comment to end of line
                while (i < command.Length && command[i] != '\n') i++;
                continue;
            }
            if (c == '(' && i > 0 && command[i - 1] == '$')
            {
                // $( ... ) command substitution: find the matching close, split on the boundary
                int depth = 1;
                int j = i + 1;
                char? innerQuote = null;
                while (j < command.Length && depth > 0)
                {
                    var cj = command[j];
                    if (innerQuote is not null)
                    {
                        if (cj == innerQuote) innerQuote = null;
                    }
                    else if (cj == '\'' || cj == '"') innerQuote = cj;
                    else if (cj == '(') depth++;
                    else if (cj == ')') depth--;
                    j++;
                }
                PushCurrent();
                i = j;
                continue;
            }
            if (c == '`')
            {
                int close = command.IndexOf('`', i + 1);
                if (close < 0) { current.Append(command[i..]); break; }
                PushCurrent();
                // the backticked body is its own sub-command boundary; the outer command resumes
                i = close + 1;
                continue;
            }
            if (c == '&')
            {
                if (i + 1 < command.Length && command[i + 1] == '&')
                {
                    PushCurrent();
                    i += 2;
                    continue;
                }
                current.Append(c);
                i++;
                continue;
            }
            if (c == '|')
            {
                if (i + 1 < command.Length && command[i + 1] == '|')
                {
                    PushCurrent();
                    i += 2;
                    continue;
                }
                PushCurrent();
                i++;
                continue;
            }
            if (c == ';')
            {
                PushCurrent();
                i++;
                continue;
            }
            if (c == '\n')
            {
                PushCurrent();
                i++;
                continue;
            }
            // a plain '>' or '<' starts a redirection: swallow it and its target token
            if ((c == '>' || c == '<') && i + 1 < command.Length && command[i + 1] == c)
            {
                i += 2;
                i = SkipTarget(command, i);
                continue;
            }
            current.Append(c);
            i++;
        }
        PushCurrent();
        return result;

        static int SkipTarget(string cmd, int at)
        {
            int n = at;
            while (n < cmd.Length && char.IsWhiteSpace(cmd[n])) n++;
            while (n < cmd.Length && !char.IsWhiteSpace(cmd[n]) && cmd[n] is not (';' or '|' or '&' or '\n')) n++;
            return n;
        }
    }

    /// <summary>Splits and returns only the non-empty sub-commands (see <see cref="Split"/>).</summary>
    public static IReadOnlyList<string> SubCommands(string command) => Split(command);
}

/// <summary>The tokenised prefix matcher. Each pattern position is a single token or
/// <c>a|b|c</c> alternatives, and every pattern token must match the sub-command token at the
/// <em>same index</em> (so <c>git push</c> matches <c>git push origin master</c> and
/// <c>git push -u origin feature/x</c>, but <c>git commit</c> does not, and an anchored
/// position like <c>192.168.1.101|192.168.1.104</c> only matches at argv[1]). The match is
/// case-insensitive and runs in linear time, so it cannot blow a regex timeout.</summary>
public static class PrefixRuleEngine
{
    /// <summary>True when <paramref name="command"/> contains a sub-command whose tokens match
    /// <paramref name="tokens"/> position by position.</summary>
    public static bool TryMatch(string command, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0) return false;
        foreach (var subCommand in SubCommandSplit.SubCommands(command))
        {
            var argv = Tokenize(subCommand);
            if (argv.Length < tokens.Count) continue;
            if (MatchTokens(tokens, argv))
                return true;
        }
        return false;
    }

    /// <summary>True when <paramref name="argv"/> position-matches <paramref name="tokens"/>
    /// (each pattern token is <c>a|b</c> alternatives matched case-insensitively at its index).
    /// A pattern token prefixed with <c>*</c> matches any token that ends with the rest.
    /// Exposed for the example validator and for tests.</summary>
    public static bool MatchTokens(IReadOnlyList<string> tokens, IReadOnlyList<string> argv)
    {
        if (argv.Count < tokens.Count) return false;
        for (int index = 0; index < tokens.Count; index++)
        {
            var alternatives = tokens[index].Split('|');
            bool any = false;
            foreach (var alternative in alternatives)
            {
                if (alternative.Length >= 2 && alternative[0] == '*')
                {
                    var suffix = alternative[1..];
                    if (argv[index].Length >= suffix.Length
                        && string.Equals(argv[index][(argv[index].Length - suffix.Length)..], suffix,
                            StringComparison.OrdinalIgnoreCase))
                        any = true;
                }
                else if (string.Equals(alternative, argv[index], StringComparison.OrdinalIgnoreCase))
                    any = true;
            }
            if (!any) return false;
        }
        return true;
    }

    /// <summary>The argv tokens of a sub-command: maximal runs of non-whitespace, non-shell-metachar
    /// characters. Quoted text is kept whole (the quotes are stripped); <c>@</c>, <c>:</c>, <c>.</c>,
    /// <c>=</c> are token characters so <c>user@192.168.1.104</c> is one token.</summary>
    public static string[] Tokenize(string subCommand)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        char? quote = null;
        int i = 0;
        void Flush()
        {
            if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
        }
        while (i < subCommand.Length)
        {
            var c = subCommand[i];
            if (quote is not null)
            {
                if (c == quote && quote != '\'')
                {
                    if (i > 0 && subCommand[i - 1] == '\\') current.Append(c);
                    else quote = null;
                }
                else current.Append(c);
                i++;
                continue;
            }
            if (c == '\'' || c == '"') { quote = c; i++; continue; }
            if (char.IsWhiteSpace(c)) { Flush(); i++; continue; }
            if (c is ';' or '|' or '&' or '<' or '>' or '\n') { Flush(); i++; continue; }
            current.Append(c);
            i++;
        }
        Flush();
        return result.ToArray();
    }
}

/// <summary>Compiles and evaluates tool-call rules. Regex rules run with a 250 ms timeout and
/// one retry; a <c>Block</c> rule that times out twice in a row refuses the call with a
/// <c># guard-ok: guard-timeout</c> hint (fail closed), while <c>Warn</c> rules keep failing
/// open. Prefix rules are tokenised and cannot time out. Rules carry match/not_match examples
/// that are validated at load: a rule whose examples fail is rejected with one ops line and
/// never runs.
/// When constructed with a notebook + task key, each rule that fires (enforce mode) or would
/// fire (shadow mode) is counted via <see cref="GuardCounters.RecordFire"/>; rules with a
/// <c># guard-ok: &lt;reason&gt;</c> marker in the serialized arguments are exempted and
/// recorded via <see cref="GuardCounters.RecordOverride"/>; a rule whose <c>fire</c> count for
/// the current task reaches <paramref name="maxBlocksPerRulePerTask"/> (0 = unlimited) stops
/// returning refusals for that rule in that task and emits one ops line. Retired rules
/// (<see cref="GuardCounters.IsRetired"/>) never block or warn.</summary>
public sealed class ToolGuard
{
    public const int RegexTimeoutMs = 250;
    internal static TimeSpan RegexTimeout { get; set; } = TimeSpan.FromMilliseconds(RegexTimeoutMs);
    private readonly (GuardRule Rule, Regex? Regex)[] _rules;
    private readonly Action<string>? _log;
    private readonly NotebookStore? _counters;
    private readonly Func<string>? _taskKey;
    private readonly int _maxBlocksPerRulePerTask;
    private readonly object _countersGate = new();
    private readonly Dictionary<string, int> _taskBlockCounts = new(StringComparer.Ordinal);
    private string? _currentTask;
    private readonly Dictionary<string, bool> _warned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _timeoutCounts = new(StringComparer.Ordinal);

    public GuardMode Mode { get; }

    /// <summary>Rules that survived validation (load-time example checks). Invalid rules are
    /// dropped with one ops line each; see <see cref="ValidationErrors"/>.</summary>
    public IReadOnlyList<GuardRule> Rules { get; private set; } = Array.Empty<GuardRule>();

    /// <summary>One line per rule rejected at load (empty when every rule validated).</summary>
    public IReadOnlyList<string> ValidationErrors { get; }

    public ToolGuard(IEnumerable<GuardRule> rules, GuardMode mode = GuardMode.Shadow, Action<string>? log = null,
        NotebookStore? counters = null, Func<string>? taskKey = null, int maxBlocksPerRulePerTask = 0)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Mode = mode;
        _log = log;
        _counters = counters;
        _taskKey = taskKey;
        _maxBlocksPerRulePerTask = Math.Max(0, maxBlocksPerRulePerTask);

        var accepted = new List<(GuardRule Rule, Regex? Regex)>();
        var errors = new List<string>();
        int ruleCount = 0;
        foreach (var rule in rules)
        {
            ruleCount++;
            string? problem = Validate(rule);
            if (problem is not null)
            {
                errors.Add($"ToolGuard rule rejected at load: {rule.Id} ({problem})");
                Log($"ToolGuard rule rejected at load: {rule.Id} ({problem})");
                continue;
            }
            Regex? regex = null;
            if (rule.Pattern is not null)
            {
                try { regex = Compile(rule.Pattern); }
                catch (Exception ex)
                {
                    errors.Add($"ToolGuard rule rejected at load: {rule.Id} (pattern does not compile: {ex.Message})");
                    Log($"ToolGuard rule rejected at load: {rule.Id} (pattern does not compile)");
                    continue;
                }
            }
            if (rule.Detector is null && regex is null)
            {
                errors.Add($"ToolGuard rule rejected at load: {rule.Id} (no detector and no pattern)");
                Log($"ToolGuard rule rejected at load: {rule.Id} (no detector and no pattern)");
                continue;
            }
            accepted.Add((rule, regex));
        }
        _rules = accepted.ToArray();
        Rules = accepted.Select(a => a.Rule).ToArray();
        ValidationErrors = errors;
        if (ruleCount > 64) throw new ArgumentException("At most 64 guard rules are supported.", nameof(rules));
    }

    /// <summary>Returns the first blocking rule's message; Warn rules are logged only. A Block
    /// regex rule that times out twice in a row refuses the call (fail closed) with a
    /// <c># guard-ok: guard-timeout</c> hint; other detector errors fail open. When counters are
    /// wired, a fired rule is counted, its exemption marker is honored, and the per-task cap
    /// degrades further fires to a one-time ops line.</summary>
    public string? Check(ToolCallContent call) => Evaluate(call) is { } verdict ? verdict.Message : null;

    /// <summary>The strictest decision of the rules matching <paramref name="call"/>: a Block (Codex <c>forbidden</c>) over
    /// an Ask (Codex <c>prompt</c>); null = the call may run. Warn rules are logged only. Shadow mode logs and decides
    /// nothing. A Block's message carries the rule's reason, its alternative, and "do not reach the same result another
    /// way" (Codex auto-review's denial rule).</summary>
    public GuardVerdict? Evaluate(ToolCallContent call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (Mode == GuardMode.Off) return null;
        GuardVerdict? ask = null;
        string text;
        try { text = call.Name + " " + call.Arguments.ToJsonString(PlainJson); }
        catch (Exception ex) { Log($"ToolGuard serialization failed open: {ex.GetType().Name}"); return null; }

        foreach (var (rule, regex) in _rules)
        {
            if (_counters is not null && GuardCounters.IsRetired(_counters, rule.Id))
            { Log($"ToolGuard skip retired rule: {rule.Id}"); continue; }
            if (GuardOverrideMarker(text) is { } reason)
            {
                if (_counters is not null && _taskKey is not null)
                {
                    GuardCounters.RecordOverride(_counters, rule.Id, reason, _taskKey());
                    Log($"ToolGuard override marker exempted: {rule.Id}");
                }
                ResetTimeoutCount(rule.Id);
                continue;
            }

            bool matched;
            bool timedOut = false;
            try
            {
                // Try the prefix detector first (it cannot time out); fall back to the regex
                // when the prefix is absent or does not match.
                if (rule.Detector is { } detector)
                {
                    // A prefix rule is about shell commands: a call without one (a kb_write note, a chat post) never
                    // matches it, and its regex reads each sub-command, not the whole call. pi4, 2026-10-26: a commit
                    // whose message said "main", chained before a push of its own branch, was refused as a push to main.
                    string? command = call.Arguments["command"] is JsonValue value && value.TryGetValue<string>(out var c) ? c : null;
                    if (command is null)
                        matched = false;
                    else if (PrefixRuleEngine.TryMatch(command, detector.Tokens))
                        matched = true;
                    else
                    {
                        matched = false;
                        if (regex is not null)
                            foreach (var part in SubCommands(command))
                                if (MatchWithRetry(rule.Id, regex, part, out timedOut) || timedOut) { matched = !timedOut; break; }
                    }
                }
                else
                {
                    matched = MatchWithRetry(rule.Id, regex!, text, out timedOut);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                timedOut = true;
                matched = false;
            }
            catch (Exception ex)
            {
                Log($"ToolGuard detector failed open: {rule.Id} ({ex.GetType().Name})");
                ResetTimeoutCount(rule.Id);
                continue;
            }

            if (timedOut)
            {
                int count = TimeoutCount(rule.Id);
                if (rule.Action == GuardAction.Block)
                {
                    Log($"ToolGuard regex timeout {count}x; refusing (fail closed): {rule.Id}");
                    if (_counters is not null)
                    {
                        var task = _taskKey?.Invoke();
                        GuardCounters.RecordFire(_counters, rule.Id, task);
                        Log($"ToolGuard timeout recorded for {rule.Id}");
                    }
                    if (Mode == GuardMode.Enforce)
                        return new GuardVerdict(GuardAction.Block, rule,
                            $"{rule.Message} [{rule.Id}] (guard timeout; retry after the host recovers or exempt with '# guard-ok: guard-timeout <reason>')");
                    // shadow: report the would-block and keep going
                    Log($"ToolGuard would block (timeout, fail closed): {rule.Id}");
                }
                else
                {
                    Log($"ToolGuard regex timeout; allowed (Warn fails open): {rule.Id}");
                    ResetTimeoutCount(rule.Id);
                }
                continue;
            }

            if (!matched) { ResetTimeoutCount(rule.Id); continue; }
            ResetTimeoutCount(rule.Id);

            if (rule.Id == "network-or-test-without-timeout" && HasFiniteTimeout(call.Arguments)) continue;

            Log($"ToolGuard {(Mode == GuardMode.Shadow ? "would " + (rule.Action == GuardAction.Ask ? "ask" : "block") : rule.Action.ToString().ToLowerInvariant())}: {rule.Id}");
            if (_counters is not null)
            {
                var task = _taskKey?.Invoke();
                GuardCounters.RecordFire(_counters, rule.Id, task);
                if (Mode == GuardMode.Enforce && rule.Action == GuardAction.Block
                    && _maxBlocksPerRulePerTask > 0
                    && BlockCapReached(rule.Id, task))
                    continue; // cap reached: degrade, one ops line, no more refusals in this task
            }
            if (Mode != GuardMode.Enforce) continue;
            if (rule.Action == GuardAction.Block) return new GuardVerdict(GuardAction.Block, rule, Refusal(rule));
            if (rule.Action == GuardAction.Ask) ask ??= new GuardVerdict(GuardAction.Ask, rule, $"{rule.Message} [{rule.Id}]");
        }
        return ask;
    }

    /// <summary>The call's arguments as the rules read them: '>' and '&amp;' as written, not \u003E and \u0026 (System.Text.Json's
    /// default escaping hid every shell redirect and every &amp;&amp; from the rules' patterns).</summary>
    private static readonly System.Text.Json.JsonSerializerOptions PlainJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The sub-commands of a shell command line: split at <c>&amp;&amp;</c>, <c>||</c>, <c>;</c>, <c>|</c> and line breaks.</summary>
    internal static IEnumerable<string> SubCommands(string command) =>
        Regex.Split(command, @"&&|\|\||;|\||\r?\n", RegexOptions.CultureInvariant, RegexTimeout)
            .Select(part => part.Trim()).Where(part => part.Length > 0);

    /// <summary>A refusal as the model reads it: the reason, what to do instead, and not to work around it.</summary>
    public static string Refusal(GuardRule rule, string? rationale = null) =>
        $"{rule.Message} [{rule.Id}]" + (rationale is { Length: > 0 } why ? " " + why : "")
        + (rule.Alternative is { Length: > 0 } instead ? " Instead: " + instead : "")
        + " Do not reach the same result another way; choose a safer path, or take it to the lane that decides.";

    /// <summary>Regex match with one retry after a timeout (the host is often starved at exactly
    /// the moment a regex is compiled). Returns the match; reports a timeout when both attempts
    /// timed out.</summary>
    internal bool MatchWithRetry(string ruleId, Regex regex, string text, out bool timedOut)
    {
        timedOut = false;
        try { return regex.IsMatch(text); }
        catch (RegexMatchTimeoutException)
        {
            Log($"ToolGuard regex timeout, retrying: {ruleId}");
            try { return regex.IsMatch(text); }
            catch (RegexMatchTimeoutException) { timedOut = true; return false; }
        }
    }

    internal int TimeoutCount(string ruleId)
    {
        lock (_timeoutCounts)
        {
            _timeoutCounts.TryGetValue(ruleId, out var count);
            _timeoutCounts[ruleId] = count + 1;
            return count + 1;
        }
    }

    internal void ResetTimeoutCount(string ruleId)
    {
        lock (_timeoutCounts) _timeoutCounts.Remove(ruleId);
    }

    /// <summary>Validates a rule's examples and detector shape at load. Returns a problem
    /// string when the rule must be rejected, or null when it may run. A rule needs at least
    /// one detector (prefix or regex); a prefix rule needs at least one token; a <c>network-
    /// or-test-without-timeout</c> rule exempts an inline <c>timeout N</c> the same way the
    /// check does. Examples run against the rule's detectors (prefix first, then regex; a rule
    /// matches when either matches).</summary>
    private static string? Validate(GuardRule rule)
    {
        if (rule.Detector is not null && (rule.Detector.Tokens is null || rule.Detector.Tokens.Length == 0))
            return "prefix rule needs at least one token";
        if (rule.Detector is null && string.IsNullOrWhiteSpace(rule.Pattern))
            return "regex rule needs a pattern";
        foreach (var (command, expectMatch) in rule.Examples ?? Array.Empty<(string, bool)>())
        {
            bool matched;
            try
            {
                // Try prefix first; if it matches, the rule matches. Otherwise try the regex.
                if (rule.Detector is { } detector && PrefixRuleEngine.TryMatch(command, detector.Tokens))
                    matched = true;
                else
                    matched = rule.Pattern is not null
                        && Regex.IsMatch(command, rule.Pattern,
                            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, RegexTimeout);
            }
            catch (RegexMatchTimeoutException) { return $"match example timed out: '{command}'"; }
            catch (Exception ex) { return $"match example errored ({ex.GetType().Name}): '{command}'"; }
            if (rule.Id == "network-or-test-without-timeout")
                matched = matched && !HasFiniteTimeoutJson(command);
            if (matched == expectMatch) continue;
            return expectMatch
                ? $"match example did not match: '{command}'"
                : $"not_match example matched: '{command}'";
        }
        return null;
    }

    /// <summary>The <c># guard-ok: &lt;reason&gt;</c> exemption marker in the serialized arguments
    /// (an in-band override; the call is allowed and the override is counted).</summary>
    public static string? GuardOverrideMarker(string serialized)
    {
        const string marker = "# guard-ok:";
        var index = serialized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var reason = serialized[(index + marker.Length)..].Trim();
        int stop;
        for (stop = 0; stop < reason.Length; stop++)
            if (char.IsWhiteSpace(reason[stop])) break;
        return stop == 0 ? "(no reason given)" : reason[..Math.Min(stop, 80)];
    }

    private bool BlockCapReached(string ruleId, string? task)
    {
        if (_maxBlocksPerRulePerTask == 0) return false;
        var key = (task ?? "") + "|" + ruleId;
        int count;
        lock (_countersGate)
        {
            var t = task ?? "";
            if (!string.Equals(_currentTask, t, StringComparison.Ordinal))
            {
                _currentTask = t;
                _taskBlockCounts.Clear();
                _warned.Clear();
            }
            _taskBlockCounts.TryGetValue(key, out count);
            count++;
            _taskBlockCounts[key] = count;
        }
        if (count > _maxBlocksPerRulePerTask)
        {
            lock (_countersGate)
            {
                if (!_warned.GetValueOrDefault(key))
                {
                    _warned[key] = true;
                    Log("ToolGuard WARN: rule '" + ruleId + "' blocked " + _maxBlocksPerRulePerTask
                        + " times in task; stopped blocking in this task (ops: review the rule or exempt with '# guard-ok: <reason>').");
                }
            }
            return true;
        }
        return false;
    }

    private void Log(string message)
    {
        try { _log?.Invoke(message); }
        catch { /* diagnostics must never affect tool execution */ }
    }

    private static Regex Compile(string pattern) => new(pattern,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking,
        RegexTimeout);

    /// <summary>Compiles a pattern with the guard's deterministic settings; used by the distil
    /// pipeline's replay gate and by shadow-mode evaluation of seat rules.</summary>
    public static Regex CompileForPattern(string pattern) =>
        new(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, RegexTimeout);

    public static ToolGuard FromJson(string json, GuardMode mode = GuardMode.Shadow, Action<string>? log = null) =>
        new(ParseRules(json), mode, log);

    public static ToolGuard LoadFile(string path, GuardMode mode = GuardMode.Shadow, Action<string>? log = null) =>
        File.Exists(path) ? FromJson(File.ReadAllText(path), mode, log) : new ToolGuard(SeededRules, mode, log);

    /// <summary>Evaluates one pattern against a tool call without constructing a guard; the same
    /// serialization as <see cref="Check"/>. Returns true when the pattern matches. A regex
    /// timeout or error is a non-match (fail open).</summary>
    public static bool Matches(string pattern, ToolCallContent call)
    {
        ArgumentNullException.ThrowIfNull(call);
        Regex regex;
        string text;
        try
        {
            regex = CompileForPattern(pattern);
            text = call.Name + " " + call.Arguments.ToJsonString(PlainJson);
        }
        catch { return false; }
        try { return regex.IsMatch(text); }
        catch (RegexMatchTimeoutException) { return false; }
        catch { return false; }
    }

    /// <summary>Builds rules from a local JSON array and fenced JSON blocks in KB rule documents.</summary>
    public static ToolGuard FromSources(string? localJson, IEnumerable<string> ruleDocuments,
        GuardMode mode = GuardMode.Shadow, Action<string>? log = null) =>
        new(RulesFromSources(localJson, ruleDocuments, null, log), mode, log);

    /// <summary>The rules a seat runs: the built-in ones, then the fleet file's and the active KB rule documents'
    /// <c>```guard</c> blocks (library assets, approved by the owner), a later rule replacing an earlier one with the same
    /// id; then only the rules that apply to the seat's <paramref name="roles"/>. Until 2026-10-25 any KB or file rule
    /// dropped every built-in one. A block that does not parse is skipped with one log line.</summary>
    public static IReadOnlyList<GuardRule> RulesFromSources(string? localJson, IEnumerable<string> ruleDocuments,
        IReadOnlyCollection<string>? roles, Action<string>? log = null)
    {
        var rules = SeededRules.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var order = SeededRules.Select(r => r.Id).ToList();
        void Add(IEnumerable<GuardRule> more)
        {
            foreach (var rule in more)
            {
                if (!rules.ContainsKey(rule.Id)) order.Add(rule.Id);
                rules[rule.Id] = rule;
            }
        }
        if (localJson is not null) Add(ParseRules(localJson));
        foreach (var document in ruleDocuments)
        foreach (Match block in Regex.Matches(document, @"```guard\s*(.*?)```", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, RegexTimeout))
        {
            try { Add(ParseRules(block.Groups[1].Value)); }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
            { log?.Invoke($"ToolGuard: a guard block in a KB rule does not parse ({ex.Message}); skipped"); }
        }
        return [.. order.Select(id => rules[id]).Where(r => r.AppliesTo(roles))];
    }

    /// <summary>The operators, who merge and release (rule work-item-assignment): the rules about master and VERSION.md are
    /// not theirs.</summary>
    public static readonly string[] OperatorRoles = ["agent-maker", "admin", "devops"];

    /// <summary>Parses a rule JSON array. Each entry needs <c>id</c>, <c>message</c> and either
    /// <c>pattern</c> (kind regex, the default) or <c>kind:"prefix"</c> with a
    /// <c>prefix</c> token array. Optional: <c>point</c>, <c>action</c> and <c>examples</c>
    /// (<c>[{"command":"...","expect_match":true|false}]</c>).</summary>
    private static GuardRule[] ParseRules(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Guard rules JSON must be an array.", nameof(json));
        return document.RootElement.EnumerateArray().Select(item =>
        {
            string id = item.GetProperty("id").GetString() ?? throw new JsonException("Rule id is required.");
            string message = item.GetProperty("message").GetString() ?? throw new JsonException("Rule message is required.");
            var kind = item.TryGetProperty("kind", out var kindElement)
                ? (kindElement.GetString() ?? "regex")
                : (item.TryGetProperty("prefix", out _) ? "prefix" : "regex");
            GuardRuleDetector? detector = null;
            string? pattern = null;
            if (kind.Equals("prefix", StringComparison.OrdinalIgnoreCase))
            {
                string[] tokens = ParseTokens(item);
                detector = new GuardRuleDetector { Tokens = tokens };
            }
            else
            {
                pattern = item.TryGetProperty("pattern", out var patternElement)
                    ? patternElement.GetString()
                    : null;
                if (pattern is null) throw new JsonException($"Rule {id}: a regex rule needs a pattern.");
            }
            (string Command, bool ExpectMatch)[] examples = Array.Empty<(string, bool)>();
            if (item.TryGetProperty("examples", out var examplesElement) && examplesElement.ValueKind == JsonValueKind.Array)
            {
                examples = examplesElement.EnumerateArray().Select(example =>
                {
                    string command = example.GetProperty("command").GetString()
                        ?? throw new JsonException("Example command is required.");
                    bool expect = example.TryGetProperty("expect_match", out var expectElement)
                        ? expectElement.GetBoolean()
                        : true;
                    return (command, expect);
                }).ToArray();
            }
            var point = item.TryGetProperty("point", out var pointElement)
                ? Enum.Parse<GuardPoint>(pointElement.GetString()!, true)
                : GuardPoint.ToolCall;
            var action = item.TryGetProperty("decision", out var decisionElement)
                ? decisionElement.GetString()?.ToLowerInvariant() switch
                {
                    "forbid" or "forbidden" or "block" => GuardAction.Block,
                    "ask" or "prompt" => GuardAction.Ask,
                    "warn" => GuardAction.Warn,
                    var other => throw new JsonException($"Rule {id}: decision '{other}' is not forbid, ask or warn."),
                }
                : item.TryGetProperty("action", out var actionElement)
                    ? Enum.Parse<GuardAction>(actionElement.GetString()!, true)
                    : GuardAction.Block;
            static string[]? Strings(JsonElement item, string name) =>
                item.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Array
                    ? [.. element.EnumerateArray().Select(e => e.GetString()).OfType<string>().Where(s => s.Length > 0)]
                    : null;
            return new GuardRule(id, message, point, action, detector, pattern, examples)
            {
                Roles = Strings(item, "roles"),
                ExceptRoles = Strings(item, "except_roles"),
                Alternative = item.TryGetProperty("alternative", out var alternative) ? alternative.GetString() : null,
            };
        }).ToArray();
    }

    private static string[] ParseTokens(JsonElement item)
    {
        if (!item.TryGetProperty("prefix", out var prefix) || prefix.ValueKind != JsonValueKind.Array)
            throw new JsonException("A prefix rule needs a prefix array.");
        return prefix.EnumerateArray().Select(t =>
        {
            string token = t.GetString() ?? throw new JsonException("Prefix tokens must be strings.");
            if (string.IsNullOrWhiteSpace(token)) throw new JsonException("Prefix tokens must not be empty.");
            return token;
        }).ToArray();
    }

    /// <summary>Initial fleet rules for unsafe canonical operations. Each unsafe rule is a prefix
    /// rule (tokenised sub-command match, cannot time out) plus its legacy regex as a second
    /// layer; the prefix layer is what makes <c>cd repo &amp;&amp; git push origin master</c>
    /// block while <c>git commit -m "fix main loop"</c> does not.</summary>
    public static IReadOnlyList<GuardRule> SeededRules { get; } =
    [
        new("push-to-master", "Direct pushes to master/main are prohibited.",
            GuardPoint.ToolCall, GuardAction.Block,
            Detector: new GuardRuleDetector { Tokens = ["git", "push", "master|main"] },
            Pattern: @"\bgit\s+push\b.*(?:\bmaster\b|\bmain\b)",
            Examples: [
                ("git push origin master", true),
                ("git push origin main", true),
                ("git push --force origin main", true),
                ("cd repo && git push origin master", true),
                ("git commit -m \"fix main loop\"", false),
                ("git push -u origin feature/x", false)
            ])
        {
            ExceptRoles = OperatorRoles,
            Alternative = "push your own branch (git push -u origin <your-seat>/<topic>) and hand the epic in; the merge owner merges it.",
        },
        new("edit-version-file", "Do not edit VERSION.md; the merge owner updates it.",
            GuardPoint.ToolCall, GuardAction.Block,
            // Only a file tool aimed at the file, or a shell command that writes it: the rule used to read the whole call,
            // so a kb_write of an epic memory that mentioned VERSION.md was refused (pi4, 2026-10-26).
            Pattern: @"^(?:write|edit|apply_patch)\s[^\r\n]*\bVERSION\.md\b|^bash\s[^\r\n]*(?:>|\btee\b|\bsed\s+-i)[^;&|]*\bVERSION\.md\b",
            Examples: [
                ("write {\"path\":\"/path/VERSION.md\",\"content\":\"0.1.2\"}", true),
                ("apply_patch {\"input\":\"*** Update File: src/VERSION.md\"}", true),
                ("bash {\"command\":\"echo 0.1.2 > src/VERSION.md\"}", true),
                ("bash {\"command\":\"sed -i s/1/2/ VERSION.md\"}", true),
                ("edit README.md", false),
                ("bash {\"command\":\"cat VERSION.md\"}", false),
                ("kb__kb_write {\"op\":\"doc\",\"body\":\"never edit VERSION.md\"}", false)
            ])
        {
            ExceptRoles = OperatorRoles,
            Alternative = "leave VERSION.md as it is; the merge owner bumps it once, at merge time.",
        },
        new("ssh-model-host", "Do not SSH to shared model hosts.",
            GuardPoint.ToolCall, GuardAction.Block,
            Detector: new GuardRuleDetector { Tokens = ["ssh", "*192.168.1.101|*192.168.1.104"] },
            Pattern: @"\bssh\b.*192\.168\.1\.(?:101|104)\b",
            Examples: [
                ("ssh user@192.168.1.104", true),
                ("ssh user@192.168.1.101", true),
                ("ssh user@10.0.0.5", false)
            ])
        {
            Alternative = "reach models through the router on your box's llm forward; report model-host trouble in ops.",
        },
        new("force-push", "A force push rewrites a branch others may have; a reviewer decides it first.",
            GuardPoint.ToolCall, GuardAction.Ask,
            Detector: new GuardRuleDetector { Tokens = ["git", "push", "--force|-f|--force-with-lease|--force-if-includes"] },
            Examples: [
                ("git push --force origin coder-06-pi4/disk-library", true),
                ("git push -f origin x", true),
                ("git push -u origin coder-06-pi4/disk-library", false)
            ])
        {
            Alternative = "push a new branch, or rebase only your own branch before anyone else has fetched it.",
        },
        new("kill-by-name", "Killing processes by name can stop other seats' work or this seat itself; a reviewer decides it first.",
            GuardPoint.ToolCall, GuardAction.Ask,
            Detector: new GuardRuleDetector { Tokens = ["pkill|killall"] },
            Examples: [
                ("pkill -f dotnet", true),
                ("killall node", true),
                ("kill 12345", false)
            ])
        {
            Alternative = "kill the process by its own PID (kill <pid>), found with the job tool or ps for your own command.",
        },
        new("network-or-test-without-timeout", "Network and test commands must have a finite timeout.",
            GuardPoint.ToolCall, GuardAction.Warn,
            Detector: new GuardRuleDetector { Tokens = ["ssh|curl|timeout"] },
            Pattern: @"\b(?:ssh|curl|dotnet\s+test)\b",
            Examples: [
                ("curl https://example.test", true),
                ("timeout 120 curl https://example.test", false),
                ("ssh user@host", true),
                ("timeout 30 ssh user@host", false)
            ]),
        new("status-only-chat-post", "Status belongs in chat status, not a room post.",
            GuardPoint.ToolCall, GuardAction.Block,
            Pattern: @"\bchat_post\b[^\r\n]*\bstatus\s*:",
            Examples: [
                ("chat_post status: busy", true),
                ("chat_post Deployment completed", false)
            ])
        {
            Alternative = "set it with chat_manage op=status; post only when someone must act.",
        },
    ];

    /// <summary>True when the serialized bash arguments carry an inline finite
    /// <c>timeout N</c> command (the exemption for network-or-test-without-timeout).</summary>
    private static bool HasFiniteTimeout(JsonObject arguments)
    {
        var command = arguments["command"]?.GetValue<string>();
        return command is not null && HasFiniteTimeoutJson(command);
    }

    private static bool HasFiniteTimeoutJson(string command)
    {
        foreach (var subCommand in SubCommandSplit.SubCommands(command))
        {
            var argv = PrefixRuleEngine.Tokenize(subCommand);
            if (argv.Length >= 2 && argv[0].Equals("timeout", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(argv[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                && seconds > 0)
                return true;
        }
        return false;
    }
}
