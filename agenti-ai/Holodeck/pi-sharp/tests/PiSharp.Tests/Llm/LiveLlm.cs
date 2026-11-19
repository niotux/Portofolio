// Live test target: a llama.cpp server on the LAN. Live tests are OPT-IN:
//   PI_SHARP_TEST_LLM=1    enables them; without it every [LiveLlmFact] skips (see OptedIn).
//   A seat runs the default suite without any live credentials; hitting a shared model server
//   is a deliberate act, not a side effect of `dotnet test` (pi-sharp-wi-tests-clean-seat-default).
// Override the endpoint with environment variables:
//   PI_SHARP_TEST_BASE_URL (default http://192.168.1.104:8000/v1)
//   PI_SHARP_TEST_API_KEY  (default dummy)
//   PI_SHARP_TEST_MODEL    (default maxiclaude)
// Tests marked [LiveLlmFact] are skipped, with the reason, when the flag is missing or the
// server does not answer.
using System.IO.Compression;
using System.Net.Http.Headers;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace PiSharp.Tests.Llm;

internal static class LiveLlm
{
    public static readonly string BaseUrl = Env("PI_SHARP_TEST_BASE_URL", "http://192.168.1.104:8000/v1");
    public static readonly string ApiKey = Env("PI_SHARP_TEST_API_KEY", "dummy");
    public static readonly string ModelId = Env("PI_SHARP_TEST_MODEL", "maxiclaude");

    /// <summary>Opt-in switch: live tests only run when PI_SHARP_TEST_LLM is set to a truthy value.</summary>
    public const string EnableEnv = "PI_SHARP_TEST_LLM";

    /// <summary>True when the seat explicitly asked for live-LLM tests.</summary>
    public static bool OptedIn => IsTruthy(Environment.GetEnvironmentVariable(EnableEnv));

    private static bool IsTruthy(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";

    private static readonly Lazy<string?> s_unavailable = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Null when the tests may run, otherwise the reason they are skipped (not opted in, or server unreachable).</summary>
    public static string? Unavailable => OptedIn ? s_unavailable.Value : EnableEnv + " is not set";

    /// <summary>Qwen3-family model served by llama.cpp: vision, thinking toggled through chat_template_kwargs.</summary>
    public static ModelConfig Model(string? apiKey = null) => new()
    {
        Id = ModelId,
        Name = "live test model",
        BaseUrl = BaseUrl,
        ApiKey = apiKey ?? ApiKey,
        Input = [ModelInput.Text, ModelInput.Image],
        ContextWindow = 200192,
        MaxOutputTokens = 2048,
        Reasoning = true,
        Compat = new ModelCompat { ThinkingFormat = ThinkingFormat.QwenChatTemplate },
    };

    public static LlmContext Context(string userText, params ITool[] tools) =>
        new("You are a terse assistant used in an automated test. Follow instructions literally.", [new UserMessage(userText)], tools);

    public static StreamOptions NoThinking => new() { Reasoning = ThinkingLevel.Off, Temperature = 0 };

    public static CancellationTokenSource Deadline() => new(TimeSpan.FromMinutes(4));

    public static async Task<List<AssistantMessageEvent>> Collect(IAsyncEnumerable<AssistantMessageEvent> events)
    {
        var list = new List<AssistantMessageEvent>();
        await foreach (var e in events) list.Add(e);
        return list;
    }

    /// <summary>
    /// The shared llama.cpp router (default <c>192.168.1.104:8000</c>) answers some probes with
    /// HTTP 200 + <c>model_tool_router: no model can perform this task</c> instead of 401 or a
    /// context-overflow error. Measured 2026-09-20 on origin/main @ e2ae5e0
    /// (pi-sharp-wi-live-llm-probes-broken-on-shared-router).
    /// </summary>
    public static bool IsSharedRouterGenericError(string? message) =>
        !string.IsNullOrEmpty(message)
        && (message.Contains("model_tool_router", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no model can perform this task", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Skip-not-fail (wi-75): named skip when the shared router cannot produce the asserted
    /// signal. Does not pass silently. On a server that returns 401/overflow this does not fire
    /// and the assertion still runs.
    /// </summary>
    public static void SkipIfSharedRouterGenericError(string? errorMessage, string expectedSignal, ITestOutputHelper? output = null)
    {
        if (!IsSharedRouterGenericError(errorMessage)) return;
        var reason = "shared llama.cpp router returned HTTP 200 + generic error "
            + $"(instead of {expectedSignal}): {errorMessage}";
        output?.WriteLine("SKIPPED (not passed) - " + reason);
        throw SkipException.ForSkip(reason);
    }

    public static string? LastErrorText(IReadOnlyList<AssistantMessageEvent> events) => events.Count == 0
        ? null
        : events[^1] switch
        {
            AssistantError e => e.Error.ErrorMessage,
            AssistantDone d => d.Message.ErrorMessage ?? d.Message.Text,
            _ => null,
        };

    private static string Env(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? Probe()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl.TrimEnd('/') + "/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            using var response = http.Send(request);
            return response.IsSuccessStatusCode ? null : $"GET /models returned HTTP {(int)response.StatusCode}";
        }
        catch (Exception ex)
        {
            return ex.GetBaseException().Message;
        }
    }

    /// <summary>A solid-color PNG built without any imaging library (zlib via ZLibStream, CRC32 by hand).</summary>
    public static ImageContent SolidPng(int width, int height, byte r, byte g, byte b)
    {
        var raw = new byte[height * (1 + width * 3)];
        var pos = 0;
        for (var y = 0; y < height; y++)
        {
            raw[pos++] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                raw[pos++] = r;
                raw[pos++] = g;
                raw[pos++] = b;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // color type: truecolor
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);

        return new ImageContent(Convert.ToBase64String(png.ToArray()), "image/png");
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var header = new byte[4];
        WriteBigEndian(header, 0, data.Length);
        output.Write(header);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, 0xFFFFFFFF);
        crc = Crc32(data, crc) ^ 0xFFFFFFFF;
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, (int)crc);
        output.Write(crcBytes);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data, uint crc)
    {
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return crc;
    }
}

/// <summary>A fact that runs only on a seat that opted in (PI_SHARP_TEST_LLM) and whose live LLM server answers.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class LiveLlmFactAttribute : FactAttribute
{
    public LiveLlmFactAttribute()
    {
        if (LiveLlm.Unavailable is { } reason)
        {
            Skip = LiveLlm.OptedIn
                ? $"Live LLM at {LiveLlm.BaseUrl} unavailable: {reason}"
                : $"Live LLM tests are opt-in: set {LiveLlm.EnableEnv}=1 to run them ({reason})";
        }
    }
}

/// <summary>Live tests share one collection so they hit the server one at a time.</summary>
[CollectionDefinition("live-llm", DisableParallelization = true)]
public sealed class LiveLlmCollection;
