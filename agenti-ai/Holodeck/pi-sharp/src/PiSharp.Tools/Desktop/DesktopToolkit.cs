// The `desktop` toolkit (adr-visual-toolkits section 5): list windows, read a window's accessibility tree, act on it,
// capture it. Linux/X11 only; the adapter calls xdotool, ImageMagick and an AT-SPI helper one-shot, no engine.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;

namespace PiSharp.Tools.Desktop;

/// <summary>Creates the desktop tools when the environment can serve them, and holds their shared state.</summary>
public sealed class DesktopToolkit
{
    public const int DefaultMaxWidth = 1280;
    public static readonly TimeSpan StableTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan StableInterval = TimeSpan.FromMilliseconds(250);

    private readonly Dictionary<string, Capture> _captures = new(StringComparer.Ordinal);
    private int _revision;

    public DesktopToolkit(IDesktopRunner runner, ArtifactStore? artifacts, bool supportsImages)
    {
        Runner = runner;
        Artifacts = artifacts;
        SupportsImages = supportsImages;
    }

    public IDesktopRunner Runner { get; }
    public ArtifactStore? Artifacts { get; set; }
    public bool SupportsImages { get; }

    /// <summary>Where a capture is published (the factory KB); null on a seat that is not attached.</summary>
    public ICapturePublisher? Publisher { get; init; }

    /// <summary>Waits between stability frames; tests make it instant.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>
    /// The desktop tools, or none: offered only on Linux with a DISPLAY and xdotool plus ImageMagick on PATH;
    /// inspect only with python3 (the AT-SPI helper).
    /// </summary>
    public static IReadOnlyList<IAgentTool> Create(ArtifactStore? artifacts, bool supportsImages, IDesktopRunner? runner = null,
        Func<string, string?>? environment = null, bool? linux = null, ICapturePublisher? publisher = null)
    {
        runner ??= new ProcessDesktopRunner();
        environment ??= Environment.GetEnvironmentVariable;
        if (!(linux ?? OperatingSystem.IsLinux()) || string.IsNullOrEmpty(environment("DISPLAY"))) return [];
        if (!runner.Has("xdotool") || !runner.Has("import") || !runner.Has("convert")) return [];
        var kit = new DesktopToolkit(runner, artifacts, supportsImages) { Publisher = publisher };
        var accessible = runner.Has("python3");
        return accessible
            ? [new WindowsTool(kit), new InspectTool(kit), new ActTool(kit, accessible), new CaptureTool(kit)]
            : [new WindowsTool(kit), new ActTool(kit, accessible), new CaptureTool(kit)];
    }

    /// <summary>
    /// Why a shell command may not grab the screen on a seat with this toolkit, or null. A shot taken with import,
    /// scrot, xwd and the like stays a file on the seat's disk, so nobody can see the evidence; desktop__capture
    /// publishes every shot to the work item instead (adr-visual-toolkits section 6). Video (ffmpeg) is not a shot.
    /// </summary>
    public static string? ScreenGrabRefusal(string command)
    {
        foreach (var part in PiSharp.Core.Guard.SubCommandSplit.Split(command))
        {
            var words = part.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(w => w.Contains('=', StringComparison.Ordinal) && !w.StartsWith('-')).ToArray();
            if (words.Length == 0) continue;
            var tool = Path.GetFileName(words[0]);
            var grab = tool is "scrot" or "xwd" or "maim" or "grim" or "gnome-screenshot" or "xfce4-screenshooter"
                || (tool == "import" && words.Skip(1).Any(w => w is "-window" or "-screen"));
            if (grab)
                return $"Screen grabs go through desktop__capture on this seat, not `{tool}`: it takes the window, a region " +
                    "(x, y, width, height) or the whole screen (window \"screen\"), and publishes the shot to your work item " +
                    "as evidence people can see. Crop or scale the published original afterwards if you need to.";
        }
        return null;
    }

    /// <summary>A window as xdotool sees it: id, title, screen geometry.</summary>
    public sealed record Window(string Id, string Title, int X, int Y, int Width, int Height);

    /// <summary>The last capture of one window: what the model was shown, for coordinate actions.</summary>
    public sealed record Capture(int Revision, int OriginX, int OriginY, int Width, int Height, int SentWidth, int SentHeight);

    internal async Task<string> Run(string file, CancellationToken ct, params string[] args)
    {
        var run = await Runner.RunAsync(file, args, ct).ConfigureAwait(false);
        if (run.ExitCode != 0)
            throw new InvalidOperationException($"{file} {string.Join(' ', args)} failed ({run.ExitCode}): {run.Error.Trim()}");
        return run.Text;
    }

    internal async Task<IReadOnlyList<string>> WindowIds(CancellationToken ct)
    {
        var run = await Runner.RunAsync("xdotool", ["search", "--onlyvisible", "--name", "."], ct).ConfigureAwait(false);
        // xdotool exits 1 when nothing matches.
        return run.ExitCode == 0 ? run.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
    }

    internal async Task<Window> Describe(string id, CancellationToken ct)
    {
        var title = (await Run("xdotool", ct, "getwindowname", id).ConfigureAwait(false)).Trim();
        var shell = await Run("xdotool", ct, "getwindowgeometry", "--shell", id).ConfigureAwait(false);
        var values = shell.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        int Get(string key) => values.TryGetValue(key, out var v) && int.TryParse(v, CultureInfo.InvariantCulture, out var n) ? n : 0;
        return new Window(id, title, Get("X"), Get("Y"), Get("WIDTH"), Get("HEIGHT"));
    }

    /// <summary>A window by id (decimal or 0x hex), by title substring, or the active window when empty.</summary>
    internal async Task<Window> Resolve(string? window, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(window))
            return await Describe((await Run("xdotool", ct, "getactivewindow").ConfigureAwait(false)).Trim(), ct).ConfigureAwait(false);
        var w = window.Trim();
        if (w.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(w[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            return await Describe(hex.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        if (w.All(char.IsAsciiDigit)) return await Describe(w, ct).ConfigureAwait(false);
        var windows = new List<Window>();
        foreach (var id in await WindowIds(ct).ConfigureAwait(false))
        {
            var candidate = await Describe(id, ct).ConfigureAwait(false);
            if (candidate.Title.Contains(w, StringComparison.OrdinalIgnoreCase)) windows.Add(candidate);
        }
        return windows.OrderByDescending(c => string.Equals(c.Title, w, StringComparison.OrdinalIgnoreCase))
                   .ThenByDescending(c => c.Width * c.Height).FirstOrDefault()
               ?? throw new InvalidOperationException($"No visible window titled like '{w}'. List them with desktop__windows.");
    }

    internal Capture? LastCapture(string windowId) => _captures.GetValueOrDefault(windowId);

    internal int Record(string windowId, int originX, int originY, int width, int height, int sentWidth, int sentHeight)
    {
        var revision = ++_revision;
        _captures[windowId] = new Capture(revision, originX, originY, width, height, sentWidth, sentHeight);
        return revision;
    }

    /// <summary>Grabs a window (or the screen for "root") as PNG bytes.</summary>
    internal async Task<byte[]> Grab(string windowId, string? crop, CancellationToken ct)
    {
        string[] args = crop is null
            ? ["-silent", "-window", windowId, "png:-"]
            : ["-silent", "-window", windowId, "-crop", crop, "+repage", "png:-"];
        var run = await Runner.RunAsync("import", args, ct).ConfigureAwait(false);
        if (run.ExitCode != 0 || run.Output.Length == 0)
            throw new InvalidOperationException($"import failed ({run.ExitCode}): {run.Error.Trim()}");
        return run.Output;
    }

    /// <summary>Waits until two consecutive frames of the window are identical, up to <see cref="StableTimeout"/>.</summary>
    internal async Task<string> WaitStable(string windowId, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        string? previous = null;
        while (true)
        {
            await Delay(StableInterval, ct).ConfigureAwait(false);
            var frame = Convert.ToHexString(SHA256.HashData(await Grab(windowId, null, ct).ConfigureAwait(false)));
            var elapsed = DateTime.UtcNow - started;
            if (frame == previous) return $"stable after {elapsed.TotalMilliseconds:0} ms";
            if (elapsed >= StableTimeout) return $"still changing after {StableTimeout.TotalSeconds:0} s";
            previous = frame;
        }
    }

    /// <summary>JPEG quality of a published capture: tens of KiB for a full screen, UI text still readable. The KB stores
    /// what it gets as JPEG too, and shrinks captures older than 7 days to fit 640x480.</summary>
    public const int CaptureJpegQuality = 40;

    /// <summary>The PNG as a heavily compressed JPEG (ImageMagick, which the desktop seat already has); the PNG itself
    /// when the conversion fails, so a capture is never lost to it. 4:4:4, no chroma subsampling: at this quality 4:2:0
    /// smears small screen text (architect-1, architecture 637516).</summary>
    public async Task<byte[]> Jpeg(byte[] png, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"pi-sharp-capture-{Guid.NewGuid():n}.png");
        try
        {
            await File.WriteAllBytesAsync(temp, png, ct).ConfigureAwait(false);
            var run = await Runner.RunAsync("convert", [temp, "-strip", "-sampling-factor", "4:4:4", "-quality", CaptureJpegQuality.ToString(CultureInfo.InvariantCulture), "jpg:-"], ct).ConfigureAwait(false);
            return run.ExitCode == 0 && run.Output is [0xFF, 0xD8, 0xFF, ..] ? run.Output : png;
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>Width and height from a PNG's IHDR chunk.</summary>
    public static (int Width, int Height) PngSize(byte[] png) =>
        png.Length >= 24 && png[1] == (byte)'P' && png[2] == (byte)'N' && png[3] == (byte)'G'
            ? (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)))
            : throw new InvalidDataException("Not a PNG image.");

    internal static string? Str(JsonObject args, string key) => args[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    internal static int? Int(JsonObject args, string key) => args[key] is JsonValue v
        ? v.TryGetValue<int>(out var i) ? i : v.TryGetValue<double>(out var d) ? (int)Math.Round(d) : null
        : null;

    internal static bool? Bool(JsonObject args, string key) => args[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    internal static JsonObject Schema(string json) => JsonNode.Parse(json)!.AsObject();
}

/// <summary>desktop__windows: the visible windows with id, title and geometry.</summary>
public sealed class WindowsTool(DesktopToolkit kit) : IAgentTool
{
    public string Name => "desktop__windows";
    // One display, one pointer: desktop calls never share a wave.
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;
    public string Label => "desktop windows";
    public string Description => "List the visible desktop windows: id, title, position and size. Use an id or a title with the other desktop tools.";
    public JsonObject Parameters => DesktopToolkit.Schema("""{"type":"object","properties":{}}""");

    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        string? active = null;
        var run = await kit.Runner.RunAsync("xdotool", ["getactivewindow"], cancellationToken).ConfigureAwait(false);
        if (run.ExitCode == 0) active = run.Text.Trim();
        foreach (var id in (await kit.WindowIds(cancellationToken).ConfigureAwait(false)).Take(60))
        {
            var w = await kit.Describe(id, cancellationToken).ConfigureAwait(false);
            if (w.Title.Length == 0 || w.Width < 8 || w.Height < 8) continue;
            lines.Add($"0x{long.Parse(id, CultureInfo.InvariantCulture):x} \"{w.Title}\" @{w.X},{w.Y} {w.Width}x{w.Height}{(id == active ? " active" : "")}");
        }
        return AgentToolResult.FromText(lines.Count == 0 ? "No visible titled windows." : string.Join('\n', lines));
    }
}

/// <summary>desktop__inspect: the window's accessibility tree, the source of truth for checks (section 7).</summary>
public sealed class InspectTool(DesktopToolkit kit) : IAgentTool
{
    public string Name => "desktop__inspect";
    // One display, one pointer: desktop calls never share a wave.
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;
    public string Label => "desktop inspect";
    public string Description =>
        "Read a window's accessibility tree (AT-SPI): one line per visible control with its id, role, name, value, " +
        "position in the window, states and actions. Check what an app shows here, not from a capture.";
    public JsonObject Parameters => DesktopToolkit.Schema("""
        {"type":"object","properties":{
          "window":{"type":"string","description":"Window title (substring) or id; default the active window"},
          "max":{"type":"integer","minimum":1,"maximum":1000,"description":"Most nodes to list (default 200)"}}}
        """);

    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var window = await kit.Resolve(DesktopToolkit.Str(arguments, "window"), cancellationToken).ConfigureAwait(false);
        var max = Math.Clamp(DesktopToolkit.Int(arguments, "max") ?? 200, 1, 1000);
        var run = await kit.Runner.RunAsync("python3",
            [AtspiHelper.EnsureWritten(), "inspect", window.Title, max.ToString(CultureInfo.InvariantCulture)], cancellationToken).ConfigureAwait(false);
        return run.ExitCode == 0
            ? AgentToolResult.FromText(run.Text.TrimEnd())
            : AgentToolResult.Error(HelperError(run));
    }

    internal static string HelperError(DesktopRun run)
    {
        try
        {
            if (JsonNode.Parse(run.Text) is JsonObject o && o["message"] is JsonValue m) return m.GetValue<string>();
        }
        catch (System.Text.Json.JsonException) { }
        var detail = run.Error.Trim();
        return detail.Contains("Atspi", StringComparison.Ordinal) || detail.Contains("gi", StringComparison.Ordinal)
            ? $"The AT-SPI helper cannot run (needs python3-gi, gir1.2-atspi-2.0, at-spi2-core and the session bus): {detail}"
            : $"The AT-SPI helper failed ({run.ExitCode}): {detail}";
    }
}

/// <summary>desktop__act: act on a control by id or role:name; keys and coordinates go through xdotool.</summary>
public sealed class ActTool(DesktopToolkit kit, bool accessible) : IAgentTool
{
    public string Name => "desktop__act";
    // One display, one pointer: desktop calls never share a wave.
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;
    // The same key or click again is often the next step (an arrow through a list), not a loop.
    public bool InputAction => true;
    public string Label => "desktop act";
    public string Description =>
        "Act on a window and wait until it is stable. action: click, focus, set_text (target + text), type (text into " +
        "the target or the focused control), key (keys, e.g. \"ctrl+s Return\"), click_at (x, y in the last capture of " +
        "this window, with its revision; only for content without controls). target: an id from desktop__inspect or role:name.";
    public JsonObject Parameters => DesktopToolkit.Schema("""
        {"type":"object","properties":{
          "action":{"type":"string","enum":["click","focus","set_text","type","key","click_at"]},
          "window":{"type":"string","description":"Window title (substring) or id; default the active window"},
          "target":{"type":"string","description":"Control id from desktop__inspect, or role:name, or name"},
          "text":{"type":"string"},
          "keys":{"type":"string","description":"xdotool key names separated by spaces"},
          "x":{"type":"integer"},"y":{"type":"integer"},
          "revision":{"type":"integer","description":"The capture revision x and y refer to"}},
         "required":["action"]}
        """);

    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var action = DesktopToolkit.Str(arguments, "action") ?? "";
        var target = DesktopToolkit.Str(arguments, "target");
        var text = DesktopToolkit.Str(arguments, "text");
        var window = await kit.Resolve(DesktopToolkit.Str(arguments, "window"), cancellationToken).ConfigureAwait(false);
        string message;
        switch (action)
        {
            case "click" or "focus" or "set_text":
                if (string.IsNullOrEmpty(target)) return AgentToolResult.Error($"{action} needs a target.");
                if (action == "set_text" && text is null) return AgentToolResult.Error("set_text needs text.");
                if (!accessible) return AgentToolResult.Error("No AT-SPI helper here (python3 missing): use click_at, type or key.");
                var (ok, said, centre, error) = await Helper(window, target, action, text, cancellationToken).ConfigureAwait(false);
                if (error is not null) return AgentToolResult.Error(error);
                message = said;
                if (!ok && centre is { } c && action != "focus")
                {
                    // No accessible action or editable text: fall back to input at the control's centre.
                    await Activate(window, cancellationToken).ConfigureAwait(false);
                    await kit.Run("xdotool", cancellationToken, "mousemove", "--sync", c.X.ToString(CultureInfo.InvariantCulture), c.Y.ToString(CultureInfo.InvariantCulture), "click", "1").ConfigureAwait(false);
                    if (action == "set_text")
                    {
                        await kit.Run("xdotool", cancellationToken, "key", "--clearmodifiers", "ctrl+a").ConfigureAwait(false);
                        await kit.Run("xdotool", cancellationToken, "type", "--delay", "12", "--", text!).ConfigureAwait(false);
                    }
                    message += action == "set_text" ? "; typed at its centre instead" : "; clicked at its centre instead";
                }
                else if (!ok)
                {
                    return AgentToolResult.Error(message);
                }
                break;
            case "type":
                if (text is null) return AgentToolResult.Error("type needs text.");
                if (!string.IsNullOrEmpty(target) && accessible)
                {
                    var focus = await Helper(window, target, "focus", null, cancellationToken).ConfigureAwait(false);
                    if (focus.Error is not null) return AgentToolResult.Error(focus.Error);
                    if (!focus.Ok && focus.Centre is { } at)
                        await kit.Run("xdotool", cancellationToken, "mousemove", "--sync", at.X.ToString(CultureInfo.InvariantCulture), at.Y.ToString(CultureInfo.InvariantCulture), "click", "1").ConfigureAwait(false);
                }
                await Activate(window, cancellationToken).ConfigureAwait(false);
                await kit.Run("xdotool", cancellationToken, "type", "--delay", "12", "--", text).ConfigureAwait(false);
                message = $"typed {text.Length} characters";
                break;
            case "key":
                var keys = DesktopToolkit.Str(arguments, "keys");
                if (string.IsNullOrWhiteSpace(keys)) return AgentToolResult.Error("key needs keys.");
                await Activate(window, cancellationToken).ConfigureAwait(false);
                await kit.Run("xdotool", cancellationToken, ["key", "--clearmodifiers", "--", .. keys.Split(' ', StringSplitOptions.RemoveEmptyEntries)]).ConfigureAwait(false);
                message = $"pressed {keys}";
                break;
            case "click_at":
                if (DesktopToolkit.Int(arguments, "x") is not { } x || DesktopToolkit.Int(arguments, "y") is not { } y)
                    return AgentToolResult.Error("click_at needs x and y.");
                var last = kit.LastCapture(window.Id);
                if (last is null) return AgentToolResult.Error("Capture this window first: click_at coordinates refer to its last capture.");
                if (DesktopToolkit.Int(arguments, "revision") is not { } revision || revision != last.Revision)
                    return AgentToolResult.Error($"x and y must come from the last capture of this window (revision {last.Revision}); capture again if it changed.");
                if (x < 0 || y < 0 || x >= last.SentWidth || y >= last.SentHeight)
                    return AgentToolResult.Error($"({x},{y}) is outside the capture ({last.SentWidth}x{last.SentHeight}).");
                var sx = last.OriginX + (int)Math.Round(x * (double)last.Width / last.SentWidth);
                var sy = last.OriginY + (int)Math.Round(y * (double)last.Height / last.SentHeight);
                await Activate(window, cancellationToken).ConfigureAwait(false);
                await kit.Run("xdotool", cancellationToken, "mousemove", "--sync", sx.ToString(CultureInfo.InvariantCulture), sy.ToString(CultureInfo.InvariantCulture), "click", "1").ConfigureAwait(false);
                message = $"clicked ({x},{y}) of capture {revision}";
                break;
            default:
                return AgentToolResult.Error($"Unknown action '{action}'.");
        }
        var stable = await kit.WaitStable(window.Id, cancellationToken).ConfigureAwait(false);
        return AgentToolResult.FromText($"{message}; {stable}.");
    }

    private Task Activate(DesktopToolkit.Window window, CancellationToken ct) =>
        kit.Run("xdotool", ct, "windowactivate", "--sync", window.Id);

    private async Task<(bool Ok, string Message, (int X, int Y)? Centre, string? Error)> Helper(
        DesktopToolkit.Window window, string target, string action, string? text, CancellationToken ct)
    {
        List<string> args = [AtspiHelper.EnsureWritten(), "act", window.Title, target, action];
        if (text is not null) args.Add(text);
        var run = await kit.Runner.RunAsync("python3", args, ct).ConfigureAwait(false);
        if (run.ExitCode != 0) return (false, "", null, InspectTool.HelperError(run));
        var o = JsonNode.Parse(run.Text)!.AsObject();
        (int, int)? centre = o["centre"] is JsonArray { Count: 2 } a ? (a[0]!.GetValue<int>(), a[1]!.GetValue<int>()) : null;
        return (o["ok"]!.GetValue<bool>(), o["message"]?.GetValue<string>() ?? "", centre, null);
    }
}

/// <summary>desktop__capture: a window, a region of it, or the screen; downscaled for the model, original kept.</summary>
public sealed class CaptureTool(DesktopToolkit kit) : IAgentTool
{
    public string Name => "desktop__capture";
    // One display, one pointer: desktop calls never share a wave.
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;
    public string Label => "desktop capture";
    public string Description =>
        "Capture a window (default the active one), a region of it (x, y, width, height in window pixels) or the whole " +
        "screen (window \"screen\"). The image is downscaled to max_width for you; the original goes to the factory as a small JPEG. " +
        "look=false keeps it without showing it to you: look only when you must judge something visual. " +
        "Every capture is published to the factory as evidence (in your cubicle and on the work item you hold; the factory " +
        "is where it is kept, not your notebook), so say what it shows in caption: what you expected and what you see. " +
        "publish=false makes a scratch shot that is shown to you and not kept anywhere.";
    public JsonObject Parameters => DesktopToolkit.Schema("""
        {"type":"object","properties":{
          "window":{"type":"string","description":"Window title (substring) or id, or \"screen\"; default the active window"},
          "x":{"type":"integer","minimum":0},"y":{"type":"integer","minimum":0},
          "width":{"type":"integer","minimum":1},"height":{"type":"integer","minimum":1},
          "max_width":{"type":"integer","minimum":64,"maximum":4096,"description":"Default 1280"},
          "look":{"type":"boolean","description":"Show the image to you (default true)"},
          "publish":{"type":"boolean","description":"Publish it as evidence (default true on a factory seat); false for a scratch shot"},
          "caption":{"type":"string","description":"What the capture shows: expected, seen"},
          "work_item":{"type":"string","description":"The work item it belongs to (default: the one you hold in progress)"}}}
        """);

    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var name = DesktopToolkit.Str(arguments, "window");
        var screen = string.Equals(name, "screen", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "root", StringComparison.OrdinalIgnoreCase);
        var window = screen ? null : await kit.Resolve(name, cancellationToken).ConfigureAwait(false);
        var id = window?.Id ?? "root";
        int? x = DesktopToolkit.Int(arguments, "x"), y = DesktopToolkit.Int(arguments, "y");
        int? w = DesktopToolkit.Int(arguments, "width"), h = DesktopToolkit.Int(arguments, "height");
        string? crop = null;
        if (w is not null || h is not null)
        {
            if (w is null || h is null) return AgentToolResult.Error("A region needs both width and height.");
            crop = FormattableString.Invariant($"{w}x{h}+{x ?? 0}+{y ?? 0}");
        }
        var original = await kit.Grab(id, crop, cancellationToken).ConfigureAwait(false);
        var (width, height) = DesktopToolkit.PngSize(original);
        var maxWidth = Math.Clamp(DesktopToolkit.Int(arguments, "max_width") ?? DesktopToolkit.DefaultMaxWidth, 64, 4096);
        var sent = original;
        if (width > maxWidth || height > maxWidth)
        {
            var temp = Path.Combine(Path.GetTempPath(), $"pi-sharp-capture-{Guid.NewGuid():n}.png");
            try
            {
                await File.WriteAllBytesAsync(temp, original, cancellationToken).ConfigureAwait(false);
                var run = await kit.Runner.RunAsync("convert", [temp, "-resize", FormattableString.Invariant($"{maxWidth}x{maxWidth}>"), "png:-"], cancellationToken).ConfigureAwait(false);
                if (run.ExitCode != 0) throw new InvalidOperationException($"convert failed ({run.ExitCode}): {run.Error.Trim()}");
                sent = run.Output;
            }
            finally
            {
                File.Delete(temp);
            }
        }
        var (sentWidth, sentHeight) = DesktopToolkit.PngSize(sent);
        var originX = (window?.X ?? 0) + (x ?? 0);
        var originY = (window?.Y ?? 0) + (y ?? 0);
        var revision = kit.Record(id, originX, originY, width, height, sentWidth, sentHeight);
        var what = window is null ? "screen" : $"window 0x{long.Parse(window.Id, CultureInfo.InvariantCulture):x} \"{window.Title}\"";
        if (crop is not null) what += $" region {crop}";
        var meta = $"capture {revision}: {what}, {width}x{height}";
        // Images live in the KB, not in the notebook (digital-scribe, 2026-10-22: "how about we don't store images in the
        // notebook? only in KB?"), and as small JPEGs ("heavily compressed with JPEG so they are tiny tiny"). Publishing is
        // the default, not something the model must remember: evidence that stays on the seat's disk is evidence nobody
        // saw (adr-visual-toolkits section 6). Only an explicit publish=false makes a scratch shot, and then nothing is
        // kept. The notebook records the capture; it keeps the image only when it could not be published (a failed upload,
        // or a seat with no factory), so nothing is lost.
        string kept;
        if (DesktopToolkit.Bool(arguments, "publish") != false)
        {
            var jpeg = await kit.Jpeg(original, cancellationToken).ConfigureAwait(false);
            var (published, outcome) = await Publish(jpeg, window, crop, revision, arguments, cancellationToken).ConfigureAwait(false);
            kept = outcome;
            if (kit.Artifacts is { } store)
                kept += published
                    ? $"; noted as artifact {store.Save([new TextContent($"{meta}; {outcome}")])}"
                    : $"; kept as artifact {store.Save([new TextContent(meta), new ImageContent(Convert.ToBase64String(jpeg), "image/jpeg")])} until it can be published";
        }
        else kept = "not published, not kept (scratch shot)";
        var look = DesktopToolkit.Bool(arguments, "look") ?? true;
        var text = $"{meta}{(sent != original ? $", shown at {sentWidth}x{sentHeight}" : "")}; {kept}.";
        if (!look || !kit.SupportsImages)
            return AgentToolResult.FromText(text + (look ? " This model takes no images; check with desktop__inspect." : ""));
        return new AgentToolResult([new TextContent(text + $" click_at uses these pixels with revision {revision}."), new ImageContent(Convert.ToBase64String(sent), "image/png")]);
    }

    // A failed publish is reported in the result, never fails the capture: the image is still the seat's.
    private async Task<(bool Published, string Outcome)> Publish(byte[] image, DesktopToolkit.Window? window, string? crop, int revision, JsonObject arguments, CancellationToken ct)
    {
        if (kit.Publisher is not { } publisher) return (false, "not published (this seat is not attached to a factory)");
        var meta = new Dictionary<string, string>(StringComparer.Ordinal) { ["revision"] = revision.ToString(CultureInfo.InvariantCulture) };
        meta["window"] = window?.Title is { Length: > 0 } title ? (title.Length > 200 ? title[..200] : title) : window is null ? "screen" : "untitled";
        if (crop is not null) meta["region"] = crop;
        try
        {
            var id = await publisher.PublishAsync(image, DesktopToolkit.Str(arguments, "work_item"), DesktopToolkit.Str(arguments, "caption"), meta, ct).ConfigureAwait(false);
            return (true, $"published as capture #{id.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (false, $"not published ({e.Message})");
        }
    }
}
