using Xunit.Sdk;

namespace PiSharp.Tests.Llm;

public sealed class LiveLlmSkipTests
{
    [Fact]
    public void SharedRouterGenericError_IsRecognised()
    {
        Assert.True(LiveLlm.IsSharedRouterGenericError("model_tool_router: no model can perform this task"));
        Assert.True(LiveLlm.IsSharedRouterGenericError("HTTP 200 OK: no model can perform this task"));
        Assert.False(LiveLlm.IsSharedRouterGenericError("HTTP 401 Unauthorized: Invalid API Key"));
        Assert.False(LiveLlm.IsSharedRouterGenericError("context size has been exceeded"));
        Assert.False(LiveLlm.IsSharedRouterGenericError(null));
        Assert.False(LiveLlm.IsSharedRouterGenericError(""));
    }

    [Fact]
    public void SharedRouterGenericError_SkipsWithNamedReason()
    {
        var ex = Assert.Throws<SkipException>(() =>
            LiveLlm.SkipIfSharedRouterGenericError(
                "model_tool_router: no model can perform this task",
                "401"));
        Assert.Contains("HTTP 200", ex.Message, StringComparison.Ordinal);
        Assert.Contains("generic error", ex.Message, StringComparison.Ordinal);
        Assert.Contains("401", ex.Message, StringComparison.Ordinal);
        Assert.Contains("model_tool_router", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Real401OrOverflow_DoesNotSkip()
    {
        LiveLlm.SkipIfSharedRouterGenericError("HTTP 401 Unauthorized: Invalid API Key", "401");
        LiveLlm.SkipIfSharedRouterGenericError("HTTP 400: context size has been exceeded", "context-overflow");
    }
}
