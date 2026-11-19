using System.Diagnostics;
using System.Text;

namespace PiSharp.Core.Processes;

public sealed record ProcessResult(int ExitCode, string Output, string Error);

/// <summary>Finite process execution with concurrent bounded pipe drains. Shell/PTY tools retain their group-specific runner.</summary>
public static class ProcessRunner
{
    public const int MaxCharacters = 16 * 1024 * 1024;

    public static async Task<ProcessResult> RunAsync(ProcessStartInfo start, TimeSpan timeout, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(timeout);
        using var process = Process.Start(start) ?? throw new IOException($"Could not start {start.FileName}");
        async Task<string> DrainAsync(StreamReader reader)
        {
            var text = new StringBuilder();
            var buffer = new char[8192];
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), stop.Token).ConfigureAwait(false);
                if (count == 0) return text.ToString();
                if (text.Length + count > MaxCharacters)
                { stop.Cancel(); throw new IOException("Process output exceeded its bound"); }
                text.Append(buffer, 0, count);
            }
        }
        var output = DrainAsync(process.StandardOutput);
        var error = DrainAsync(process.StandardError);
        var exit = process.WaitForExitAsync(stop.Token);
        var completion = Task.WhenAll(output, error, exit);
        try
        {
            await completion.WaitAsync(stop.Token).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException($"{start.FileName} did not finish within {timeout.TotalSeconds:0.###} seconds (or exceeded its output bound)"); }
        finally
        {
            if (!process.HasExited)
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            stop.Cancel();
            _ = completion.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
