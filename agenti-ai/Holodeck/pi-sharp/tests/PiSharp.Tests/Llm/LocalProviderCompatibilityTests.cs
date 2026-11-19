using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PiSharp.Cli;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Llm;

public class LocalProviderCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingLocalConfigurationUsesChatCompletionsWithoutSubscriptionStorage(bool explicitProvider)
    {
        using var tmp = new TempWorkspace();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        var requests = 0;
        app.MapGet("/props", () => Results.Json(new { default_generation_settings = new { n_ctx = 200192 }, total_slots = 4 }));
        app.MapGet("/v1/models", () => Results.Json(new { data = new[] { new { id = "existing-local-model" } } }));
        app.MapPost("/v1/chat/completions", async (HttpContext ctx) =>
        {
            Interlocked.Increment(ref requests);
            Assert.Equal("Bearer local-key", ctx.Request.Headers.Authorization.ToString());
            var body = (await JsonNode.ParseAsync(ctx.Request.Body))!;
            Assert.Equal("existing-local-model", body["model"]!.GetValue<string>());
            Assert.False(body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
            Assert.NotNull(body["messages"]);
            Assert.Null(body["input"]);
            ctx.Response.ContentType = "text/event-stream";
            await ctx.Response.WriteAsync("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"local reply\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var model = new JsonObject
        {
            ["id"] = "existing-local-model", ["baseUrl"] = address + "/v1",
            ["contextWindow"] = 100000, ["maxOutputTokens"] = 4096,
            ["compat"] = new JsonObject { ["thinkingFormat"] = "qwen-chat-template" },
        };
        if (explicitProvider) model["provider"] = "openai-completions";
        tmp.File("models.json", new JsonObject { ["models"] = new JsonArray(model) }.ToJsonString());
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var cli = new CliHost
        {
            ConfigDir = tmp.Path, SubscriptionBrokerUrl = "", Out = stdout, Err = stderr,
            In = new StringReader(""), Color = false, HookCancelKey = false, ToolsFactory = (_, _) => [],
        };
        var code = await cli.RunAsync(["--cwd", tmp.Path, "--api-key", "local-key", "--thinking", "off",
            "--no-session", "--no-context-files", "--no-skills", "--no-prompt-templates", "--print", "hello"]);
        Assert.True(code == 0, stderr.ToString());
        Assert.Contains("local reply", stdout.ToString());
        Assert.Equal(1, requests);
        Assert.False(Directory.Exists(Path.Combine(tmp.Path, "subscriptions")));
    }
}
