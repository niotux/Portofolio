using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using PiSharp.Core.Mcp;
using PiSharp.Core.Notebook;

if (args.Length > 0 && args[0] is "install" or "install-deps") return Microsoft.Playwright.Program.Main(args);
var origin = Environment.GetEnvironmentVariable("PI_BROWSER_ORIGIN") ?? throw new InvalidOperationException("PI_BROWSER_ORIGIN must be the deployment-assigned local app origin.");
var allowed = new Uri(origin);
if (!allowed.IsLoopback || allowed.Scheme != "http") throw new InvalidOperationException("Browser companion accepts an assigned loopback HTTP origin only.");
var root = Path.GetFullPath(Environment.GetEnvironmentVariable("PI_BROWSER_STATE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pi-browser"));
Directory.CreateDirectory(root);
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
using var lease = new FileStream(Path.Combine(root, "browser.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
using var playwright = await Playwright.CreateAsync();
IBrowser? browser = null;
IBrowserContext? context = null;
IPage? page = null;
var diagnostics = new Queue<string>();
var gate = new SemaphoreSlim(1);
var revision = 0;
var touched = DateTimeOffset.UtcNow;
void Note(string value) { lock (diagnostics) { diagnostics.Enqueue(value.Length > 1000 ? value[..1000] : value); while (diagnostics.Count > 20) diagnostics.Dequeue(); } }
async Task Close()
{
    if (context is not null) await context.CloseAsync();
    if (browser is not null) await browser.CloseAsync();
    context = null; browser = null; page = null; revision++; lock (diagnostics) diagnostics.Clear();
}
using var idle = new CancellationTokenSource();
var cleanup = Task.Run(async () =>
{
    try { while (true) { await Task.Delay(TimeSpan.FromSeconds(30), idle.Token); await gate.WaitAsync(idle.Token); try { if (DateTimeOffset.UtcNow - touched > TimeSpan.FromMinutes(3)) await Close(); } finally { gate.Release(); } } }
    catch (OperationCanceledException) { }
});
var server = new McpServer("pi-sharp-browser") { Capabilities = new JsonObject { ["tools"] = new JsonObject() } };
server.Handle("tools/list", _ => new JsonObject { ["tools"] = new JsonArray(new JsonObject
{
    ["name"] = "browser",
    ["description"] = $"Inspect and test the local web app at {allowed.GetLeftPart(UriPartial.Authority)}. Launch the app on this assigned origin before opening it; other origins are rejected. One isolated page per agent. open, inspect, act, capture, close. Actions use exact accessible role/name from inspection and current revision; optional act index selects a zero-based match in current document order (not persistent identity), otherwise matching stays strict. Invalid or out-of-range indices are refused. hover moves the real pointer onto the element, drag presses on its centre and moves by value \"dx,dy\" pixels (each -2000..2000) before releasing. Capture returns an image plus private original path/hash; include task and testedBuild to link evidence (reported build is a caller claim). No arbitrary script execution. Downloads and external requests disabled.",
    ["inputSchema"] = JsonNode.Parse("""
    {"type":"object","properties":{"op":{"type":"string","enum":["open","inspect","act","capture","close"]},"url":{"type":"string"},"width":{"type":"integer","minimum":320,"maximum":1440},"height":{"type":"integer","minimum":240,"maximum":1000},"revision":{"type":"integer"},"role":{"type":"string"},"name":{"type":"string"},"index":{"type":"integer","minimum":0},"action":{"type":"string","enum":["click","fill","press","select","hover","drag"]},"value":{"type":"string"},"task":{"type":"string"},"testedBuild":{"type":"string"}},"required":["op"]}
    """)
}) });
server.Handle("tools/call", async (parameters, ct) =>
{
    await gate.WaitAsync(ct);
    try
    {
        touched = DateTimeOffset.UtcNow;
        if (parameters?["name"]?.GetValue<string>() != "browser") throw new ArgumentException("Unknown tool.");
        var input = parameters["arguments"]!.AsObject();
        string? S(string name) => input[name]?.GetValue<string>();
        var content = new JsonArray();
        switch (S("op"))
        {
            case "open":
                var url = new Uri(S("url") ?? origin);
                if (url.GetLeftPart(UriPartial.Authority) != allowed.GetLeftPart(UriPartial.Authority)) throw new ArgumentException("URL must use the assigned app origin.");
                await Close();
                browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
                context = await browser.NewContextAsync(new() { AcceptDownloads = false, ServiceWorkers = ServiceWorkerPolicy.Block,
                    ViewportSize = new ViewportSize { Width = Math.Clamp(input["width"]?.GetValue<int>() ?? 1280, 320, 1440), Height = Math.Clamp(input["height"]?.GetValue<int>() ?? 800, 240, 1000) } });
                await context.RouteAsync("**/*", async route =>
                {
                    if (Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var request) && request.GetLeftPart(UriPartial.Authority) == allowed.GetLeftPart(UriPartial.Authority)) await route.ContinueAsync();
                    else await route.AbortAsync();
                });
                page = await context.NewPageAsync(); page.SetDefaultTimeout(10000); page.SetDefaultNavigationTimeout(15000);
                page.Console += (_, message) => Note("console " + message.Type + ": " + message.Text);
                page.PageError += (_, error) => Note("page error: " + error);
                page.RequestFailed += (_, request) => Note("request failed: " + request.Url + " " + request.Failure);
                page.Response += (_, response) => { if (response.Status >= 400) Note("HTTP " + response.Status + " " + response.Url); };
                page.Popup += async (_, popup) => await popup.CloseAsync();
                await page.GotoAsync(url.ToString(), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                revision++;
                break;
            case "inspect":
                if (page is null) throw new InvalidOperationException("Open a page first.");
                var snapshot = await page.Locator("body").AriaSnapshotAsync();
                content.Add(new JsonObject { ["type"] = "text", ["text"] = snapshot.Length <= 12000 ? snapshot : snapshot[..12000] + "\n[Snapshot truncated]" });
                break;
            case "act":
                if (page is null) throw new InvalidOperationException("Open a page first.");
                if (input["revision"]?.GetValue<int>() != revision) throw new InvalidOperationException("Stale page revision; inspect again.");
                if (!Enum.TryParse<AriaRole>(S("role"), true, out var role)) throw new ArgumentException("Use an accessible role from inspection.");
                var locator = page.GetByRole(role, new() { Name = S("name") ?? "", Exact = true });
                if (input.ContainsKey("index"))
                {
                    if (input["index"] is not JsonValue indexValue || !indexValue.TryGetValue<int>(out var index) || index < 0)
                        throw new ArgumentException("index must be a nonnegative int32 integer.");
                    if (index >= await locator.CountAsync()) throw new ArgumentException("index is out of range for the current exact role/name matches; inspect again.");
                    locator = locator.Nth(index);
                }
                switch (S("action"))
                {
                    case "click": await locator.ClickAsync(); break;
                    case "fill": await locator.FillAsync(S("value") ?? ""); break;
                    case "press": await locator.PressAsync(S("value") ?? "Enter"); break;
                    case "select": await locator.SelectOptionAsync(S("value") ?? ""); break;
                    case "hover": await locator.HoverAsync(); break;
                    case "drag":
                        var offset = (S("value") ?? "").Split(',');
                        if (offset.Length != 2 || !int.TryParse(offset[0].Trim(), out var dx) || !int.TryParse(offset[1].Trim(), out var dy)
                            || Math.Abs(dx) > 2000 || Math.Abs(dy) > 2000) throw new ArgumentException("drag value is \"dx,dy\" in whole pixels, each -2000..2000.");
                        var box = await locator.BoundingBoxAsync() ?? throw new InvalidOperationException("Element is not visible; inspect again.");
                        var (x, y) = (box.X + box.Width / 2, box.Y + box.Height / 2);
                        await page.Mouse.MoveAsync(x, y); await page.Mouse.DownAsync();
                        // The press never outlives the act: a failed move still releases the button.
                        try { await page.Mouse.MoveAsync(x + dx, y + dy, new() { Steps = 10 }); }
                        finally { await page.Mouse.UpAsync(); }
                        break;
                    default: throw new ArgumentException("Unknown action.");
                }
                revision++;
                break;
            case "capture":
                if (page is null) throw new InvalidOperationException("Open a page first.");
                var bytes = await page.ScreenshotAsync(new() { Type = ScreenshotType.Jpeg, Quality = 80, FullPage = false });
                if (bytes.Length > 1_500_000) throw new InvalidOperationException("Screenshot byte budget exceeded; use a smaller viewport.");
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                var path = Path.Combine(root, hash + ".jpg");
                if (new DirectoryInfo(root).EnumerateFiles().Sum(f => f.Length) + bytes.Length > 128L * 1024 * 1024) throw new IOException("Browser evidence quota exceeded; export evidence first.");
                await File.WriteAllBytesAsync(path, bytes, ct);
                var metadata = new JsonObject { ["sha256"] = hash, ["path"] = path, ["task"] = S("task"), ["testedBuildClaim"] = S("testedBuild"),
                    ["url"] = page.Url, ["capturedAt"] = DateTimeOffset.UtcNow.ToString("O"), ["revision"] = revision, ["width"] = page.ViewportSize!.Width, ["height"] = page.ViewportSize.Height };
                metadata["workspaceAtCapture"] = await WorkspaceEvidence.CaptureAsync(Environment.GetEnvironmentVariable("PI_BROWSER_WORKSPACE") ?? Environment.CurrentDirectory, ct);
                metadata["provenanceScope"] = "Workspace fingerprint at capture; running app build must be matched to its launch job. Caller build labels are not verification.";
                await File.WriteAllTextAsync(Path.Combine(root, hash + ".json"), metadata.ToJsonString(), ct);
                content.Add(new JsonObject { ["type"] = "image", ["mimeType"] = "image/jpeg", ["data"] = Convert.ToBase64String(bytes) });
                content.Add(new JsonObject { ["type"] = "text", ["text"] = metadata.ToJsonString() });
                break;
            case "close": await Close(); break;
            default: throw new ArgumentException("Unknown operation.");
        }
        string[] notes; lock (diagnostics) notes = diagnostics.ToArray();
        content.Add(new JsonObject { ["type"] = "text", ["text"] = $"revision={revision}; url={page?.Url ?? "closed"}\n" + string.Join("\n", notes) });
        return new JsonObject { ["content"] = content, ["isError"] = false };
    }
    catch (Exception e) when (e is PlaywrightException or System.TimeoutException or ArgumentException or InvalidOperationException or IOException)
    { return new JsonObject { ["isError"] = true, ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = e.Message }) }; }
    finally { gate.Release(); }
});
try { await server.ServeAsync(Console.OpenStandardInput(), Console.OpenStandardOutput()); }
finally { idle.Cancel(); await cleanup; await Close(); }
return 0;
