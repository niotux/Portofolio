// Runs the desktop toolkit's OS binaries one-shot (xdotool, ImageMagick, python3 + AT-SPI), as BashTool runs a
// shell: no engine process (adr-visual-toolkits section 2).
using System.Diagnostics;

namespace PiSharp.Tools.Desktop;

/// <summary>One finished command: exit code, raw stdout, stderr text.</summary>
public sealed record DesktopRun(int ExitCode, byte[] Output, string Error)
{
    public string Text => System.Text.Encoding.UTF8.GetString(Output);
}

/// <summary>Runs one desktop command. Tests replace it with a fake.</summary>
public interface IDesktopRunner
{
    Task<DesktopRun> RunAsync(string file, IReadOnlyList<string> args, CancellationToken cancellationToken);

    /// <summary>True when <paramref name="file"/> can be started (found on PATH).</summary>
    bool Has(string file);
}

/// <summary>Runs commands as child processes with a fixed timeout, inheriting DISPLAY and the session bus.</summary>
public sealed class ProcessDesktopRunner : IDesktopRunner
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public bool Has(string file) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, file)));

    public async Task<DesktopRun> RunAsync(string file, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Cannot start {file}.");
        process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await Task.WhenAll(copy, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{file} did not finish within {Timeout.TotalSeconds:0} s.");
        }
        return new DesktopRun(process.ExitCode, output.ToArray(), await error.ConfigureAwait(false));
    }
}
