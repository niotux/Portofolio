using System.Net;
using System.Text;
using PiSharp.Core.Messages;
using PiSharp.Llm;
using PiSharp.Llm.Http;

namespace PiSharp.Tests.Llm;

public sealed class SseReaderTests
{
    private static async Task<List<string>> Read(string wire)
    {
        var list = new List<string>();
        await foreach (var d in SseReader.ReadDataAsync(new MemoryStream(Encoding.UTF8.GetBytes(wire)))) list.Add(d);
        return list;
    }

    [Fact]
    public async Task ParsesLlamaCppStream()
    {
        var wire = "data: {\"a\":1}\n\ndata: {\"b\":2}\n\ndata: [DONE]\n\n";
        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], await Read(wire));
    }

    [Fact]
    public async Task HandlesCrlfCommentsMultilineDataAndMissingTrailingBlank()
    {
        var wire = ": keep-alive\r\nevent: message\r\ndata: {\"a\":\r\ndata: 1}\r\n\r\ndata:{\"b\":2}";
        Assert.Equal(["{\"a\":\n1}", "{\"b\":2}"], await Read(wire));
    }

    [Fact]
    public async Task StopsAtDoneEvenWhenMoreFollows()
    {
        Assert.Equal(["x"], await Read("data: x\n\ndata: [DONE]\n\ndata: y\n\n"));
    }
}

public sealed class RetryPolicyTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.Conflict, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void RetryableStatuses(HttpStatusCode status, bool expected) =>
        Assert.Equal(expected, RetryPolicy.IsRetryableStatus(status));

    [Fact]
    public void HeaderOverridesStatus()
    {
        using var yes = new HttpResponseMessage(HttpStatusCode.BadRequest);
        yes.Headers.TryAddWithoutValidation("x-should-retry", "true");
        Assert.True(RetryPolicy.IsRetryableStatus(yes.StatusCode, yes.Headers));

        using var no = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        no.Headers.TryAddWithoutValidation("x-should-retry", "false");
        Assert.False(RetryPolicy.IsRetryableStatus(no.StatusCode, no.Headers));
    }

    [Fact]
    public void ExponentialBackoffWithJitterCapsAtEightSeconds()
    {
        var policy = new RetryPolicy { MaxRetries = 5, Random = new Random(1) };
        var d0 = policy.GetDelay(0, null).TotalSeconds;
        var d10 = policy.GetDelay(10, null).TotalSeconds;
        Assert.InRange(d0, 0.375, 0.5);
        Assert.InRange(d10, 6, 8);
    }

    [Fact]
    public void PatientPolicyBacksOffToTwoMinutesAndAcceptsLongServerDelays()
    {
        var policy = RetryPolicy.Patient;
        Assert.InRange(policy.GetDelay(10, null).TotalSeconds, 90, 120);

        using var busy = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        busy.Headers.TryAddWithoutValidation("retry-after", "600");
        Assert.Equal(TimeSpan.FromMinutes(10), policy.GetDelay(0, busy.Headers));
    }

    [Fact]
    public void HonorsRetryAfterHeaders()
    {
        var policy = new RetryPolicy { MaxRetries = 1 };

        using var ms = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        ms.Headers.TryAddWithoutValidation("retry-after-ms", "1500");
        Assert.Equal(TimeSpan.FromMilliseconds(1500), policy.GetDelay(0, ms.Headers));

        using var seconds = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        seconds.Headers.TryAddWithoutValidation("retry-after", "3");
        Assert.Equal(TimeSpan.FromSeconds(3), policy.GetDelay(0, seconds.Headers));

        using var tooLong = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        tooLong.Headers.TryAddWithoutValidation("retry-after", "120");
        var ex = Assert.Throws<RetryDelayTooLongException>(() => policy.GetDelay(0, tooLong.Headers));
        Assert.StartsWith("Server requested 120s retry delay (max: 60s)", ex.Message);
    }

    [Fact]
    public void DefaultPolicyRetriesTwiceOnTransportFailures()
    {
        Assert.Equal(0, RetryPolicy.None.MaxRetries);
        Assert.Equal(2, RetryPolicy.Default.MaxRetries);
        Assert.Equal(8, RetryPolicy.Patient.MaxRetries);
        Assert.Equal(TimeSpan.FromMinutes(15), RetryPolicy.Patient.MaxRetryDelay);
        Assert.True(RetryPolicy.IsRetryableException(new HttpRequestException("connection reset")));
        Assert.True(RetryPolicy.IsRetryableException(new IOException("broken pipe")));
        Assert.False(RetryPolicy.IsRetryableException(new RetryDelayTooLongException("nope")));
        Assert.False(RetryPolicy.IsRetryableException(new OperationCanceledException()));
    }
}

public sealed class OverflowDetectorTests
{
    private static AssistantMessage Msg(StopReason stop, string? error = null, Usage? usage = null, string? code = null) => new()
    {
        Content = [],
        Model = "m",
        StopReason = stop,
        ErrorMessage = error,
        ErrorCode = code,
        Usage = usage ?? Usage.Zero,
    };

    [Theory]
    [InlineData("HTTP 400 Bad Request: the request exceeds the available context size, try increasing it", true)]
    [InlineData("Context size has been exceeded.", true)]
    [InlineData("This model's maximum context length is 32768 tokens", true)]
    [InlineData("context_length_exceeded", true)]
    [InlineData("Rate limit exceeded: too many tokens per minute", false)]
    [InlineData("Throttling error: too many tokens", false)]
    [InlineData("connection refused", false)]
    public void ErrorMessages(string message, bool expected) =>
        Assert.Equal(expected, OverflowDetector.IsContextOverflow(Msg(StopReason.Error, message)));

    [Fact]
    public void UsageBasedDetection()
    {
        Assert.True(OverflowDetector.IsContextOverflow(Msg(StopReason.Stop, usage: new Usage(Input: 900, CacheRead: 200)), 1000));
        Assert.False(OverflowDetector.IsContextOverflow(Msg(StopReason.Stop, usage: new Usage(Input: 500)), 1000));
        Assert.True(OverflowDetector.IsContextOverflow(Msg(StopReason.Length, usage: new Usage(Input: 995, Output: 0)), 1000));
        Assert.False(OverflowDetector.IsContextOverflow(Msg(StopReason.Length, usage: new Usage(Input: 995, Output: 5)), 1000));
        Assert.False(OverflowDetector.IsContextOverflow(Msg(StopReason.Stop, usage: new Usage(Input: 5000))));
    }

    [Fact]
    public void RecoverableLength()
    {
        Assert.True(OverflowDetector.IsRecoverableLength(Msg(StopReason.Length, usage: new Usage(Output: 100)), 4096));
        Assert.False(OverflowDetector.IsRecoverableLength(Msg(StopReason.Length, usage: new Usage(Output: 4096)), 4096));
        Assert.False(OverflowDetector.IsRecoverableLength(Msg(StopReason.Stop, usage: new Usage(Output: 10)), 4096));
    }

    // Criterion 6 (wi-8): recognition must come from the parsed machine-readable signal, never from
    // a locally configured contextWindow being correct. A parsed overflow code is decisive even when
    // there is no local window at all and the free text is unrecognisable prose.
    [Theory]
    [InlineData("context_length_exceeded")]
    [InlineData("context_window_exceeded")]
    [InlineData("input_length_too_long")]
    [InlineData("prompt_too_long")]
    [InlineData("request_too_large")]
    public void ParsedCodeIsRecognisedWithoutAnyLocalWindow(string code)
    {
        // Error text is prose no regex matches; only the parsed code can decide. No contextWindow
        // passed, so counter arithmetic cannot fire either. This is exactly the wrong-large-window
        // shape that killed the runs: the local config never saw the wall.
        var msg = Msg(StopReason.Error, "the slot filled up on a shared box", code: code);
        Assert.True(OverflowDetector.IsContextOverflow(msg));
    }

    [Fact]
    public void ParsedNonOverflowCodeFallsBackToPatterns()
    {
        // A code that is not an overflow code does not force a true: recognition then falls back to
        // the free-text patterns, and prose that is not an overflow phrase stays false.
        Assert.False(OverflowDetector.IsContextOverflow(
            Msg(StopReason.Error, "upstream model not loaded", code: "model_not_loaded")));
        // The same fallback still recognises the llama.cpp prose phrase that killed run 4638.
        Assert.True(OverflowDetector.IsContextOverflow(
            Msg(StopReason.Error, "Context size has been exceeded.", code: "too_long")));
    }

    [Fact]
    public void LlamaCppPhraseIsRecognisedWithoutLocalWindow()
    {
        // The verbatim llama.cpp refusal, with no parsed code and no local window: the pattern
        // fallback must catch it (the run-4638 death).
        Assert.True(OverflowDetector.IsContextOverflow(
            Msg(StopReason.Error, "Context size has been exceeded.")));
    }
}
