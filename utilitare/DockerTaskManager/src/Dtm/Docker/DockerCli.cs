using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dtm.Docker;

public readonly record struct CliResult
(
  int ExitCode,
  string Stdout,
  string Stderr
)
{
  public bool Ok
  {
    get { return ExitCode == 0; }
  }

  /// <summary>The last non-empty line of stderr (or stdout): the line worth showing when a command failed.</summary>
  public string ErrorLine()
  {
    ReadOnlySpan<char> text = Stderr.AsSpan().TrimEnd();
    if (text.IsEmpty)
    {
      text = Stdout.AsSpan().TrimEnd();
    }
    if (text.IsEmpty)
    {
      return "exit code " + ExitCode;
    }
    int nl = text.LastIndexOf('\n');
    return new string(nl < 0 ? text : text.Slice(nl + 1));
  }
}

/// <summary>Runs the <c>docker</c> executable. Every Docker interaction in the app goes through here.</summary>
public sealed class DockerCli
{
  readonly string _executable;

  public DockerCli(string executable)
  {
    _executable = executable;
  }

  /// <summary>Runs <c>docker [--context ctx] args...</c>. Never throws for failures, timeouts or cancellation.</summary>
  public async Task<CliResult> RunAsync(string? context, string[] args, TimeSpan timeout, CancellationToken ct)
  {
    ProcessStartInfo psi = new(_executable);
    psi.RedirectStandardOutput = true;
    psi.RedirectStandardError = true;
    psi.RedirectStandardInput = true;
    psi.UseShellExecute = false;
    psi.StandardOutputEncoding = Encoding.UTF8;
    psi.StandardErrorEncoding = Encoding.UTF8;
    if (context != null)
    {
      psi.ArgumentList.Add("--context");
      psi.ArgumentList.Add(context);
    }
    for (int i = 0; i < args.Length; i++)
    {
      psi.ArgumentList.Add(args[i]);
    }

    using Process process = new();
    process.StartInfo = psi;
    try
    {
      process.Start();
      process.StandardInput.Close();
    }
    catch (Exception ex)
    {
      return new CliResult(-1, "", "could not run '" + _executable + "': " + ex.Message);
    }

    using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeout);
    Task<string> stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
    Task<string> stderr = process.StandardError.ReadToEndAsync(cts.Token);
    try
    {
      await process.WaitForExitAsync(cts.Token);
      return new CliResult(process.ExitCode, await stdout, await stderr);
    }
    catch (OperationCanceledException)
    {
      try
      {
        process.Kill(true);
      }
      catch (InvalidOperationException)
      {
        // Already exited.
      }
      return new CliResult(-1, "", ct.IsCancellationRequested ? "cancelled" : "timed out after " + (int)timeout.TotalSeconds + "s");
    }
  }
}
