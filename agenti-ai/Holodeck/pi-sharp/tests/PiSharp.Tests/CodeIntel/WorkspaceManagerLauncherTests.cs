using System.Diagnostics;
using PiSharp.CodeIntel;

namespace PiSharp.Tests.CodeIntel;

public sealed class WorkspaceManagerLauncherTests
{
    [Fact]
    public async Task StartLsProcessStartsRedirectedChildBeforeReturning()
    {
        const string variable = "CODEINTEL_LS_CSHARPLS_COMMAND";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "/bin/echo codeintel-started");
            using var process = WorkspaceManager.StartLsProcess("csharp");

            Assert.True(process.StartInfo.RedirectStandardOutput);
            var output = await process.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, process.ExitCode);
            Assert.Equal("codeintel-started", output.Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }
}
