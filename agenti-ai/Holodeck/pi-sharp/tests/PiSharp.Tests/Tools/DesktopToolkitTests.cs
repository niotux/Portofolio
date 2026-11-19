using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;
using PiSharp.Tools.Desktop;

namespace PiSharp.Tests.Tools;

public class DesktopToolkitTests
{
    [Fact]
    public void TheToolkitIsOfferedOnlyWithADisplayAndItsBinaries()
    {
        var all = new FakeRunner("xdotool", "import", "convert", "python3");
        Func<string, string?> display = name => name == "DISPLAY" ? ":1" : null;

        Assert.Empty(DesktopToolkit.Create(null, true, all, _ => null, linux: true));
        Assert.Empty(DesktopToolkit.Create(null, true, all, display, linux: false));
        Assert.Empty(DesktopToolkit.Create(null, true, new FakeRunner("xdotool", "convert"), display, linux: true));
        Assert.Equal(["desktop__windows", "desktop__inspect", "desktop__act", "desktop__capture"],
            DesktopToolkit.Create(null, true, all, display, linux: true).Select(t => t.Name));
        // Without python3 there is no AT-SPI helper: no inspect.
        Assert.Equal(["desktop__windows", "desktop__act", "desktop__capture"],
            DesktopToolkit.Create(null, true, new FakeRunner("xdotool", "import", "convert"), display, linux: true).Select(t => t.Name));
        Assert.All(DesktopToolkit.Create(null, true, all, display, linux: true), t => Assert.Equal("desktop", ToolDiscovery.ToolkitOf(t.Name)));
        // They drive one display: the scheduler must never put two of them in a parallel wave.
        Assert.All(DesktopToolkit.Create(null, true, all, display, linux: true), t => Assert.Equal(ToolExecutionMode.Sequential, t.ExecutionMode));
    }

    [Fact]
    public async Task ACaptureIsDownscaledForTheModelAndKeptAsAJpegOnlyWhenNoFactoryTakesIt()
    {
        using var tmp = new TempWorkspace();
        var store = new ArtifactStore(Path.Combine(tmp.Path, "artifacts"));
        var runner = Fixture(new FakeRunner("xdotool", "import", "convert"));
        var big = Png(2560, 1440);
        var small = Png(1280, 720);
        runner.On("import", _ => new DesktopRun(0, big, ""));
        runner.On("convert", a => a[^1] == "jpg:-" ? new DesktopRun(0, Jpeg, "") : Resized(a, small));
        var capture = new CaptureTool(new DesktopToolkit(runner, store, supportsImages: true));

        var result = await capture.ExecuteAsync("1", new JsonObject { ["window"] = "Fixture" }, null, default);

        Assert.Contains("capture 1: window 0x2a \"Toolkit Fixture\", 2560x1440, shown at 1280x720", result.Text);
        var image = Assert.Single(result.Content.OfType<ImageContent>());
        Assert.Equal(Convert.ToBase64String(small), image.Data);
        // No factory to publish to: the notebook keeps the small JPEG, never the PNG.
        Assert.Contains("not published (this seat is not attached to a factory)", result.Text);
        var id = result.Text.Split("kept as artifact ")[1][..64];
        var kept = store.Read(id)[1]!;
        Assert.Equal(("image/jpeg", Convert.ToBase64String(Jpeg)), (kept["mimeType"]?.GetValue<string>() ?? kept["mime_type"]?.GetValue<string>(), kept["data"]!.GetValue<string>()));
    }

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    private static DesktopRun Resized(string[] args, byte[] small)
    {
        Assert.Equal("1280x1280>", args[2]);
        return new DesktopRun(0, small, "");
    }

    [Fact]
    public async Task LookFalseKeepsTheCaptureWithoutShowingIt()
    {
        var runner = Fixture(new FakeRunner("xdotool", "import", "convert"));
        runner.On("import", _ => new DesktopRun(0, Png(400, 200), ""));
        var capture = new CaptureTool(new DesktopToolkit(runner, null, supportsImages: true));

        var result = await capture.ExecuteAsync("1", new JsonObject { ["look"] = false, ["window"] = "Fixture" }, null, default);

        Assert.Empty(result.Content.OfType<ImageContent>());
        Assert.DoesNotContain(runner.Calls, c => c[0] == "convert" && c.Contains("-resize"));
    }

    [Fact]
    public async Task PublishSendsASmallJpegToTheFactoryAndTheNotebookKeepsNoImage()
    {
        using var tmp = new TempWorkspace();
        var store = new ArtifactStore(Path.Combine(tmp.Path, "artifacts"));
        var runner = Fixture(new FakeRunner("xdotool", "import", "convert"));
        var big = Png(2560, 1440);
        runner.On("import", _ => new DesktopRun(0, big, ""));
        runner.On("convert", a => a[^1] == "jpg:-" ? new DesktopRun(0, Jpeg, "") : new DesktopRun(0, Png(1280, 720), ""));
        var publisher = new FakePublisher(id: 17);
        var capture = new CaptureTool(new DesktopToolkit(runner, store, supportsImages: true) { Publisher = publisher });

        var result = await capture.ExecuteAsync("1", new JsonObject
        {
            ["window"] = "Fixture", ["publish"] = true, ["caption"] = "the greeting", ["work_item"] = "wi-9", ["look"] = false,
        }, null, default);

        Assert.Contains("published as capture #17", result.Text);
        var sent = Assert.Single(publisher.Sent);
        Assert.Equal(Jpeg, sent.Image);
        Assert.Contains(runner.Calls, c => c[0] == "convert" && c.Contains("-quality") && c.Contains("4:4:4") && c[^1] == "jpg:-");
        Assert.Equal(("wi-9", "the greeting"), (sent.WorkItem, sent.Caption));
        Assert.Equal("Toolkit Fixture", sent.Meta["window"]);
        Assert.Equal("1", sent.Meta["revision"]);
        // The notebook notes the capture and keeps no image: the KB has it.
        var id = result.Text.Split("noted as artifact ")[1][..64];
        Assert.DoesNotContain(store.Read(id), part => part?["data"] is not null);

        // An explicit publish=false is a scratch shot: not sent, not kept.
        var scratch = await capture.ExecuteAsync("2", new JsonObject { ["window"] = "Fixture", ["publish"] = false, ["look"] = false }, null, default);
        Assert.Single(publisher.Sent);
        Assert.Contains("not published, not kept (scratch shot)", scratch.Text);
    }

    [Fact]
    public async Task AJpegConversionThatFailsStillPublishesThePng()
    {
        var runner = Fixture(new FakeRunner("xdotool", "import", "convert"));
        var png = Png(400, 200);
        runner.On("import", _ => new DesktopRun(0, png, ""));
        runner.On("convert", _ => new DesktopRun(1, [], "no delegate"));
        var publisher = new FakePublisher(id: 3);
        var capture = new CaptureTool(new DesktopToolkit(runner, null, supportsImages: true) { Publisher = publisher });

        var result = await capture.ExecuteAsync("1", new JsonObject { ["window"] = "Fixture", ["look"] = false }, null, default);

        Assert.Contains("published as capture #3", result.Text);
        Assert.Equal(png, Assert.Single(publisher.Sent).Image);
    }

    [Fact]
    public async Task AnAttachedSeatPublishesEveryCaptureWithoutBeingAsked()
    {
        var runner = Fixture(new FakeRunner("xdotool", "import", "convert"));
        runner.On("import", _ => new DesktopRun(0, Png(400, 200), ""));
        var publisher = new FakePublisher(id: 5);
        var attached = new CaptureTool(new DesktopToolkit(runner, null, supportsImages: true) { Publisher = publisher });
        var detached = new CaptureTool(new DesktopToolkit(runner, null, supportsImages: true));

        var shot = await attached.ExecuteAsync("1", new JsonObject { ["window"] = "Fixture", ["look"] = false }, null, default);
        var local = await detached.ExecuteAsync("2", new JsonObject { ["window"] = "Fixture", ["look"] = false }, null, default);

        Assert.Contains("published as capture #5", shot.Text);
        var sent = Assert.Single(publisher.Sent);
        Assert.Null(sent.WorkItem); // the KB attaches it to the item the seat holds
        Assert.Contains("not published (this seat is not attached to a factory)", local.Text);
    }

    [Theory]
    [InlineData("import -window root /tmp/a.png", true)]
    [InlineData("export DISPLAY=:1\nimport -window $W /tmp/b.png 2>/dev/null", true)]
    [InlineData("DISPLAY=:1 scrot /tmp/c.png", true)]
    [InlineData("xdotool key Return && xwd -root -out /tmp/d.xwd", true)]
    [InlineData("/usr/bin/import -screen -window root x.png", true)]
    [InlineData("convert /tmp/a.png -resize 200% /tmp/b.png", false)]
    [InlineData("python3 -c 'import json'", false)]
    [InlineData("cat <<'PY' | python3\nimport os\nPY", false)]
    [InlineData("ffmpeg -f x11grab -i :1 out.mp4", false)]
    public void TheShellMayNotGrabTheScreenOnAPublishingSeat(string command, bool refused)
    {
        var why = DesktopToolkit.ScreenGrabRefusal(command);
        Assert.Equal(refused, why is not null);
        if (refused) Assert.Contains("desktop__capture", why);
    }

    [Fact]
    public async Task AFailedPublishIsReportedButTheCaptureStands()
    {
        var runner = Fixture(new FakeRunner("xdotool", "import", "convert"));
        runner.On("import", _ => new DesktopRun(0, Png(400, 200), ""));
        var failing = new CaptureTool(new DesktopToolkit(runner, null, true) { Publisher = new FakePublisher(fail: "the KB refused the capture (401)") });
        var detached = new CaptureTool(new DesktopToolkit(runner, null, true));

        var refused = await failing.ExecuteAsync("1", new JsonObject { ["window"] = "Fixture", ["publish"] = true }, null, default);
        var nowhere = await detached.ExecuteAsync("2", new JsonObject { ["window"] = "Fixture", ["publish"] = true }, null, default);

        Assert.False(refused.IsError);
        Assert.Contains("not published (the KB refused the capture (401))", refused.Text);
        Assert.Single(refused.Content.OfType<ImageContent>());
        Assert.Contains("not published (this seat is not attached to a factory)", nowhere.Text);
    }

    [Fact]
    public async Task TheHttpPublisherPostsTheImageWithTheSeatTokenToTheKbAuthority()
    {
        var handler = new RecordingHandler("""{"id":42,"sha256":"x","width":2,"height":1,"url":"/capture-image/42"}""");
        using var publisher = new HttpCapturePublisher("http://kb.test:8081/mcp", "seat-token", handler);

        var id = await publisher.PublishAsync(Png(2, 1), "wi-9", "a b&c", new Dictionary<string, string> { ["window"] = "HC2000" }, default);

        Assert.Equal(42, id);
        var request = handler.Request!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://kb.test:8081/captures?work_item=wi-9&caption=a%20b%26c&meta.window=HC2000", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer seat-token", request.Headers.Authorization!.ToString());
        Assert.Equal(Png(2, 1), handler.Body);
    }

    [Fact]
    public async Task ClickAtMapsCapturePixelsToTheScreenAndRefusesAStaleRevision()
    {
        var runner = Fixture(new FakeRunner("xdotool", "import", "convert"));
        runner.On("import", _ => new DesktopRun(0, Png(840, 440), ""));
        runner.On("convert", _ => new DesktopRun(0, Png(420, 220), ""));
        var kit = new DesktopToolkit(runner, null, supportsImages: true) { Delay = (_, _) => Task.CompletedTask };
        var act = new ActTool(kit, accessible: true);

        var before = await act.ExecuteAsync("0", new JsonObject { ["action"] = "click_at", ["window"] = "Fixture", ["x"] = 1, ["y"] = 1, ["revision"] = 1 }, null, default);
        Assert.True(before.IsError);
        Assert.Contains("Capture this window first", before.Text);

        await new CaptureTool(kit).ExecuteAsync("1", new JsonObject { ["window"] = "Fixture", ["max_width"] = 420 }, null, default);
        var stale = await act.ExecuteAsync("2", new JsonObject { ["action"] = "click_at", ["window"] = "Fixture", ["x"] = 10, ["y"] = 10, ["revision"] = 7 }, null, default);
        Assert.True(stale.IsError);

        var done = await act.ExecuteAsync("3", new JsonObject { ["action"] = "click_at", ["window"] = "Fixture", ["x"] = 10, ["y"] = 20, ["revision"] = 1 }, null, default);

        Assert.False(done.IsError, done.Text);
        // Window at (750,455), capture 840x440 shown at 420x220: (10,20) is (770,495) on the screen.
        Assert.Contains(runner.Calls, c => c is ["xdotool", "mousemove", "--sync", "770", "495", "click", "1"]);
        Assert.Contains("stable after", done.Text);
    }

    [Fact]
    public async Task AClickWithoutAnAccessibleActionFallsBackToTheControlsCentre()
    {
        var runner = Fixture(new FakeRunner("xdotool", "import", "convert", "python3"));
        runner.On("import", _ => new DesktopRun(0, Png(10, 10), ""));
        runner.On("python3", a =>
        {
            Assert.Equal(["act", "Toolkit Fixture", "push button:Greet", "click"], a.Skip(1));
            return Ok("""{"ok":false,"centre":[795,526],"message":"push button 'Greet' has no accessible action"}""");
        });
        var act = new ActTool(new DesktopToolkit(runner, null, true) { Delay = (_, _) => Task.CompletedTask }, accessible: true);

        var result = await act.ExecuteAsync("1", new JsonObject { ["action"] = "click", ["window"] = "Fixture", ["target"] = "push button:Greet" }, null, default);

        Assert.False(result.IsError, result.Text);
        Assert.Contains("clicked at its centre instead", result.Text);
        Assert.Contains(runner.Calls, c => c is ["xdotool", "mousemove", "--sync", "795", "526", "click", "1"]);
    }

    [Fact]
    public async Task AHelperFailureIsReportedAsItsMessage()
    {
        var runner = Fixture(new FakeRunner("xdotool", "import", "convert", "python3"));
        runner.On("python3", _ => new DesktopRun(3, "{\"ok\": false, \"message\": \"No accessible window matches 'Toolkit Fixture'. Accessible windows: Desktop\"}"u8.ToArray(), ""));
        var inspect = new InspectTool(new DesktopToolkit(runner, null, true));

        var result = await inspect.ExecuteAsync("1", new JsonObject { ["window"] = "Fixture" }, null, default);

        Assert.True(result.IsError);
        Assert.StartsWith("No accessible window matches", result.Text);
    }

    [Fact]
    public void TheHelperScriptIsWrittenOncePerContent()
    {
        var path = AtspiHelper.EnsureWritten();
        Assert.Equal(path, AtspiHelper.EnsureWritten());
        Assert.Equal(AtspiHelper.Script, File.ReadAllText(path));
    }

    /// <summary>
    /// Live: drives the Avalonia fixture on a real X display. Runs only where PI_SHARP_DESKTOP_FIXTURE names the
    /// fixture executable (the demo box; adr-visual-toolkits step 2) and DISPLAY is set.
    /// </summary>
    [LiveDesktopFact]
    public async Task Live_InspectSetTextClickAndCaptureTheAvaloniaFixture()
    {
        var tools = DesktopToolkit.Create(null, supportsImages: true).ToDictionary(t => t.Name);
        Assert.Equal(4, tools.Count);
        using var app = System.Diagnostics.Process.Start(Environment.GetEnvironmentVariable("PI_SHARP_DESKTOP_FIXTURE")!)!;
        try
        {
            AgentToolResult listed = AgentToolResult.FromText("");
            for (var i = 0; i < 40 && !listed.Text.Contains("Toolkit Fixture"); i++)
            {
                await Task.Delay(250);
                listed = await tools["desktop__windows"].ExecuteAsync("w", [], null, default);
            }
            Assert.Contains("\"Toolkit Fixture\"", listed.Text);

            var tree = await Retry(() => tools["desktop__inspect"].ExecuteAsync("i", new JsonObject { ["window"] = "Toolkit Fixture" }, null, default));
            Assert.Contains("push button 'Greet'", tree.Text);
            Assert.Contains("label 'Not clicked'", tree.Text);

            var typed = await tools["desktop__act"].ExecuteAsync("t", new JsonObject { ["action"] = "set_text", ["window"] = "Toolkit Fixture", ["target"] = "entry:TextBox", ["text"] = "Ana" }, null, default);
            Assert.False(typed.IsError, typed.Text);
            var clicked = await tools["desktop__act"].ExecuteAsync("c", new JsonObject { ["action"] = "click", ["window"] = "Toolkit Fixture", ["target"] = "push button:Greet" }, null, default);
            Assert.False(clicked.IsError, clicked.Text);

            var after = await tools["desktop__inspect"].ExecuteAsync("i2", new JsonObject { ["window"] = "Toolkit Fixture" }, null, default);
            Assert.Contains("label 'Hello Ana (1)'", after.Text);

            var shot = await tools["desktop__capture"].ExecuteAsync("s", new JsonObject { ["window"] = "Toolkit Fixture" }, null, default);
            var image = Assert.Single(shot.Content.OfType<ImageContent>());
            Assert.True(DesktopToolkit.PngSize(Convert.FromBase64String(image.Data)).Width >= 300, shot.Text);
        }
        finally
        {
            app.Kill(entireProcessTree: true);
        }
    }

    private static async Task<AgentToolResult> Retry(Func<Task<AgentToolResult>> call)
    {
        var result = await call();
        for (var i = 0; i < 20 && result.IsError; i++)
        {
            await Task.Delay(250);
            result = await call();
        }
        Assert.False(result.IsError, result.Text);
        return result;
    }

    private static FakeRunner Fixture(FakeRunner runner)
    {
        runner.On("xdotool", a => a[0] switch
        {
            "search" => Ok("42\n"),
            "getactivewindow" => Ok("42\n"),
            "getwindowname" => Ok("Toolkit Fixture\n"),
            "getwindowgeometry" => Ok("WINDOW=42\nX=750\nY=455\nWIDTH=420\nHEIGHT=220\nSCREEN=0\n"),
            _ => Ok(""),
        });
        return runner;
    }

    private static DesktopRun Ok(string text) => new(0, System.Text.Encoding.UTF8.GetBytes(text), "");

    /// <summary>A PNG header with the given size: enough for <see cref="DesktopToolkit.PngSize"/>.</summary>
    private static byte[] Png(int width, int height)
    {
        var png = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(png, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
        png[32] = (byte)(width ^ height); // distinct sizes, distinct bytes
        return png;
    }

    private sealed class FakePublisher(long id = 1, string? fail = null) : ICapturePublisher
    {
        public List<(byte[] Image, string? WorkItem, string? Caption, IReadOnlyDictionary<string, string> Meta)> Sent { get; } = [];

        public Task<long> PublishAsync(byte[] image, string? workItem, string? caption, IReadOnlyDictionary<string, string> meta, CancellationToken cancellationToken)
        {
            if (fail is not null) throw new InvalidOperationException(fail);
            Sent.Add((image, workItem, caption, meta));
            return Task.FromResult(id);
        }
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public byte[]? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }

    private sealed class FakeRunner(params string[] present) : IDesktopRunner
    {
        private readonly Dictionary<string, Func<string[], DesktopRun>> _handlers = new();
        public List<string[]> Calls { get; } = [];
        public void On(string file, Func<string[], DesktopRun> handler) => _handlers[file] = handler;
        public bool Has(string file) => present.Contains(file);

        public Task<DesktopRun> RunAsync(string file, IReadOnlyList<string> args, CancellationToken cancellationToken)
        {
            Calls.Add([file, .. args]);
            return Task.FromResult(_handlers.TryGetValue(file, out var handler) ? handler([.. args]) : new DesktopRun(127, [], $"{file}: not faked"));
        }
    }
}

/// <summary>A fact that runs only with a fixture executable and an X display.</summary>
public sealed class LiveDesktopFactAttribute : FactAttribute
{
    public LiveDesktopFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PI_SHARP_DESKTOP_FIXTURE"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            Skip = "Needs PI_SHARP_DESKTOP_FIXTURE (the Avalonia fixture) and DISPLAY.";
    }
}
