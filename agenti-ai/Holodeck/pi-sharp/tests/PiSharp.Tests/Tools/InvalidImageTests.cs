using PiSharp.Cli;
using PiSharp.Core.Agent;
using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Session;
using PiSharp.Llm.OpenAi;
using PiSharp.Llm.Responses;
using PiSharp.Tests.Agent;
using PiSharp.Tools;
using PiSharp.Tests.Mcp;
using System.Text.Json.Nodes;

namespace PiSharp.Tests.Tools;

public class InvalidImageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalImagesAreRejectedBeforeHistory(bool empty)
    {
        using var tmp = new TempWorkspace();
        var path = Path.Combine(tmp.Path, "flip.png");
        File.WriteAllBytes(path, empty ? [] : Convert.FromBase64String(ImageFixtures.Png(448, 312, true).Data));
        var read = new ReadTool(tmp.Path);
        var stream = new ScriptedStream().Then(Fakes.ToolCalls(Fakes.Call("read-flip", "read",
            new System.Text.Json.Nodes.JsonObject { ["path"] = path }))).Then(Fakes.Text("recapture"));
        var session = SessionManager.InMemory(tmp.Path);
        var agent = new AgentSession(session, new AgentContext { Tools = [read] });
        await Fakes.Collect(agent.Run([new UserMessage("look")], Fakes.Config(), stream.Stream));
        var result = Assert.Single(session.BuildContextMessages().OfType<ToolResultMessage>());
        Assert.True(result.IsError);
        Assert.Empty(result.Content.OfType<ImageContent>());
        var diagnostic = string.Concat(result.Content.OfType<TextContent>().Select(p => p.Text));
        Assert.Contains("flip.png", diagnostic);
        Assert.Contains(empty ? "0 bytes" : "scanline", diagnostic);
        var error = Assert.ThrowsAny<Exception>(() => Attachments.Process([path], tmp.Path));
        Assert.Contains("flip.png", error.Message);
        Assert.Contains(empty ? "0 bytes" : "scanline", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [InlineData("not base64!")]
    public void McpImageAndResourceRejectInvalidPayloads(string data)
    {
        var parts = McpAgentTool.MapContent([
            new McpTextPart("before"), new McpImagePart(data, "image/png"),
            new McpResourcePart(new McpResourceContents("capture://flip", "image/png", null, data)),
            new McpImagePart(ImageFixtures.Png().Data, "image/png"), new McpTextPart("after")]);
        Assert.Equal(5, parts.Count);
        Assert.Contains("image", Assert.IsType<TextContent>(parts[1]).Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resource part 2 uri-sha256", Assert.IsType<TextContent>(parts[2]).Text);
        Assert.DoesNotContain("capture://flip", Assert.IsType<TextContent>(parts[2]).Text);
        Assert.IsType<ImageContent>(parts[3]);
        Assert.Equal("after", Assert.IsType<TextContent>(parts[4]).Text);
    }

    [Fact]
    public void McpTruncatedNonemptyPngIsRejected()
    {
        var image = ImageFixtures.Png(448, 312, true);
        var bytes = Convert.FromBase64String(image.Data);
        Assert.Equal(486, bytes.Length);
        Assert.Equal("f5c315df5af56e759256a3f70541131ef0bb4b6d6434e6130f2ad9cabed78aa5",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
        var text = Assert.IsType<TextContent>(Assert.Single(McpAgentTool.MapContent([new McpImagePart(image.Data, image.MimeType)])));
        Assert.Contains("scanline", text.Text);
        Assert.Contains("419640", text.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplayOmitsOnlyInvalidImagesAndPreservesSession(bool empty)
    {
        using var tmp = new TempWorkspace();
        var invalid = empty ? new ImageContent("", "image/png") : ImageFixtures.Png(448, 312, true);
        var valid = ImageFixtures.Png();
        var session = SessionManager.Create(tmp.Path, tmp.Path);
        session.AppendMessage(new UserMessage([new TextContent("user before"), invalid, valid]));
        session.AppendMessage(Fakes.ToolCalls(Fakes.Call("retained-flip", "read")));
        session.AppendMessage(new ToolResultMessage { ToolCallId = "retained-flip", ToolName = "read",
            Content = [new TextContent("retained text"), invalid, valid] });
        var path = session.SessionFile!;
        var original = File.ReadAllBytes(path);
        var model = Fakes.Model with { Input = [ModelInput.Text, ModelInput.Image] };
        // Reopening simulates each automatic resume; both supported provider formats use the guard.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var reopened = SessionManager.Open(path);
            var messages = reopened.BuildContextMessages();
            var context = new LlmContext("", messages, []);
            var wire = RequestBuilder.Build(model, context, new()).ToJsonString();
            Assert.Contains("invalid image omitted", wire);
            Assert.Contains("retained-flip", wire);
            Assert.Contains("retained text", wire);
            Assert.Equal(2, Count(wire, "data:image/png;base64," + valid.Data));
            var responses = ResponsesRequest.Build(model with { Provider = "openai-codex" }, context, new()).ToJsonString();
            Assert.Contains("invalid image omitted", responses);
            Assert.Equal(2, Count(responses, "data:image/png;base64," + valid.Data));
            Assert.Equal(invalid, messages.OfType<UserMessage>().Single().Content[1]);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public async Task ValidLocalImageIsUnchangedAndEmptyTextStillWorks()
    {
        using var tmp = new TempWorkspace();
        var valid = ImageFixtures.Png();
        File.WriteAllBytes(Path.Combine(tmp.Path, "ok.png"), Convert.FromBase64String(valid.Data));
        File.WriteAllBytes(Path.Combine(tmp.Path, "empty.txt"), []);
        var read = await ToolRun.Exec(new ReadTool(tmp.Path), "{\"path\":\"ok.png\"}");
        Assert.Equal(valid, Assert.Single(read.Content.OfType<ImageContent>()));
        Assert.Equal(valid, Assert.Single(Attachments.Process(["ok.png"], tmp.Path).Images));
        Assert.Empty(Attachments.Process(["empty.txt"], tmp.Path).Images);
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(0, 2)] [InlineData(0, 4)] [InlineData(0, 8)] [InlineData(0, 16)]
    [InlineData(2, 8)] [InlineData(2, 16)] [InlineData(3, 1)] [InlineData(3, 2)] [InlineData(3, 4)] [InlineData(3, 8)]
    [InlineData(4, 8)] [InlineData(4, 16)] [InlineData(6, 8)] [InlineData(6, 16)]
    public void LegalPngVariantsRemainUnchanged(byte colour, byte depth)
    {
        foreach (var interlaced in new[] { false, true })
        {
            var image = ImageFixtures.Png(9, 9, depth: depth, colour: colour, interlaced: interlaced, splitIdat: true);
            Assert.Null(ImageValidation.Problem(image));
            var projected = ImageValidation.Project([image], "control", out var rejected);
            Assert.False(rejected);
            Assert.Same(image, projected[0]);
        }
    }

    [Fact]
    public void NonPngAndUnsupportedLargeExtentAreNotChanged()
    {
        var gif = new ImageContent("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7", "image/gif");
        Assert.Null(ImageValidation.Problem(gif));
        Assert.Same(gif, ImageValidation.Project([gif], "gif", out _)[0]);
        // Work budget is fail-open, not a hard pixel policy. The provider boundary handles misses.
        Assert.Null(ImageValidation.Problem(ImageFixtures.Png(10000, 10000, rawLength: 1)));
        var model = Fakes.Model with { Input = [ModelInput.Text, ModelInput.Image] };
        var wire = RequestBuilder.Build(model, new LlmContext("", [new UserMessage([gif])], []), new()).ToJsonString();
        Assert.Contains("data:image/gif;base64," + gif.Data, wire);
        Assert.Contains("signature", ImageValidation.Problem(new ImageContent("YWJj", "image/jpeg")));
    }

    [Fact]
    public void TruncatedContainerAndBadCrcAreRejected()
    {
        var image = ImageFixtures.Png();
        var bytes = Convert.FromBase64String(image.Data);
        Assert.Contains("truncated", ImageValidation.Problem(bytes[..^12], "image/png"));
        bytes[45] ^= 1;
        Assert.Contains("checksum", ImageValidation.Problem(bytes, "image/png"));
        Assert.Contains("scanline", ImageValidation.Problem(ImageFixtures.Png(rawLength: 15)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McpExecutionPersistsOnlyDiagnosticAndValidNeighbour(bool resource)
    {
        const string uri = "capture://fixture-user:fixture-password@host.invalid/private-fixture-path?token=fixture-query-secret#fixture-fragment";
        using var tmp = new TempWorkspace();
        await using var pipes = new RpcPipes();
        var bad = ImageFixtures.Png(448, 312, true);
        var good = ImageFixtures.Png();
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut, (method, _, _) =>
            Task.FromResult<JsonNode?>(method == "initialize" ? new JsonObject
            {
                ["protocolVersion"] = McpClient.ProtocolVersion, ["capabilities"] = new JsonObject(),
                ["serverInfo"] = new JsonObject { ["name"] = "images", ["version"] = "1" },
            } : new JsonObject { ["content"] = new JsonArray(
                new JsonObject { ["type"] = "text", ["text"] = "capture metadata" },
                resource ? new JsonObject { ["type"] = "resource", ["resource"] = new JsonObject
                    { ["uri"] = uri, ["mimeType"] = "image/png", ["blob"] = bad.Data } }
                    : new JsonObject { ["type"] = "image", ["mimeType"] = "image/png", ["data"] = bad.Data },
                new JsonObject { ["type"] = "image", ["mimeType"] = "image/png", ["data"] = good.Data }) }));
        await using var client = await McpClient.ConnectAsync("images", new PeerTransport(new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut)));
        var tool = new McpAgentTool(client, new McpToolInfo("shot", "test", new JsonObject()));
        var stream = new ScriptedStream().Then(Fakes.ToolCalls(Fakes.Call("mcp-attachment-ref", tool.Name))).Then(Fakes.Text("recapture"));
        var session = SessionManager.Create(tmp.Path, tmp.Path);
        var agent = new AgentSession(session, new AgentContext { Tools = [tool] });
        await Fakes.Collect(agent.Run([new UserMessage("look")], Fakes.Config(), stream.Stream));
        var result = Assert.Single(session.BuildContextMessages().OfType<ToolResultMessage>());
        Assert.True(result.IsError);
        Assert.Equal(good, Assert.Single(result.Content.OfType<ImageContent>()));
        var diagnostic = Assert.IsType<TextContent>(result.Content[1]).Text;
        Assert.Contains("mcp-attachment-ref", diagnostic);
        Assert.Contains(resource ? "resource part 1 uri-sha256" : "image part", diagnostic);
        Assert.Contains("486 bytes", diagnostic);
        Assert.DoesNotContain(bad.Data, diagnostic);
        var model = Fakes.Model with { Input = [ModelInput.Text, ModelInput.Image] };
        var context = new LlmContext("", session.BuildContextMessages(), []);
        var outputs = new[] { diagnostic, File.ReadAllText(session.SessionFile!),
            RequestBuilder.Build(model, context, new()).ToJsonString(),
            ResponsesRequest.Build(model with { Provider = "openai-codex" }, context, new()).ToJsonString() };
        foreach (var output in outputs)
            foreach (var sensitive in new[] { "fixture-user", "fixture-password", "private-fixture-path", "fixture-query-secret", "fixture-fragment" })
                Assert.DoesNotContain(sensitive, output);
    }

    [Fact]
    public void OverBudgetGeometrySkipsBeforeIdatInspection()
    {
        var bytes = Convert.FromBase64String(ImageFixtures.Png(10000, 10000, rawLength: 1).Data);
        bytes[45] ^= 1; // IHDR remains valid; IDAT CRC must not be inspected beyond the geometry work bound.
        Assert.Null(ImageValidation.Problem(bytes, "image/png"));
    }

    [Fact]
    public void OversizedEncodedInputIsNotCopiedIntoValidationBuffers()
    {
        var bytes = Convert.FromBase64String(ImageFixtures.Png(compressedPadding: 4 * 1024 * 1024).Data);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var problem = ImageValidation.Problem(bytes, "image/png");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 128 * 1024, $"validation allocated {allocated} bytes");
        Assert.Null(problem); // Unsupported validation is not a hard input-size rejection.
    }

    [Fact]
    public void DataResourceUriCannotLeakItsPayloadIntoDiagnostic()
    {
        const string uri = "data:application/octet-stream;base64,c3ludGhldGljLXNlY3JldC1maXh0dXJl";
        var mapped = McpAgentTool.MapContent([new McpResourcePart(new McpResourceContents(uri, "image/png", null, ""))]);
        var diagnostic = Assert.IsType<TextContent>(Assert.Single(mapped)).Text;
        Assert.DoesNotContain("c3ludGhldGlj", diagnostic);
        Assert.DoesNotContain("data:", diagnostic);
        Assert.Contains("resource part 0 uri-sha256", diagnostic);
    }

    private static int Count(string text, string value) => text.Split(value).Length - 1;
}
