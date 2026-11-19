using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CreativeStudio;

// Vision gets a bounded CPU preview. The notebook continues to refer to the original workspace file.
public sealed class ReferencePreview(string workspace, int maximumEdge, int timeoutSeconds)
{
  private const int PreviewThreads = 1;
  private readonly WorkspaceFiles files = new(workspace);
  private readonly string directory = Path.Combine(Path.GetFullPath(workspace), ".previews");

  public async Task<string> CreateAsync(string sourcePath, CancellationToken token)
  {
    if (maximumEdge < 1 || timeoutSeconds < 1)
    {
      throw new InvalidOperationException("Configure a positive reference-preview size and timeout.");
    }

    string source = files.Resolve(sourcePath);
    if (!WorkspaceFiles.IsImage(source))
    {
      throw new ArgumentException("Select an image for visual inspection.");
    }
    EnsureDirectory();
    string originalHash = await HashAsync(source, token);
    string identity = originalHash + ":" + maximumEdge.ToString(CultureInfo.InvariantCulture);
    string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    string target = Path.Combine(directory, key + ".png");
    if (File.Exists(target))
    {
      return files.Resolve(Path.GetRelativePath(workspace, target));
    }

    string temporary = Path.Combine(directory, key + "." + Guid.NewGuid().ToString("N") + ".tmp.png");
    try
    {
      await ConvertAsync(source, temporary, token);
      if (originalHash != await HashAsync(source, token))
      {
        throw new IOException("The reference changed while its preview was being prepared. Open it again.");
      }
      token.ThrowIfCancellationRequested();
      Publish(temporary, target);
      return files.Resolve(Path.GetRelativePath(workspace, target));
    }
    finally
    {
      File.Delete(temporary);
    }
  }

  private void EnsureDirectory()
  {
    DirectoryInfo cache = new(directory);
    if (cache.LinkTarget is not null)
    {
      throw new IOException("The reference preview directory cannot be a symbolic link.");
    }
    Directory.CreateDirectory(directory);
  }

  private async Task ConvertAsync(string source, string target, CancellationToken token)
  {
    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
    using Process process = new() { StartInfo = StartInfo(source, target) };
    try
    {
      if (!process.Start())
      {
        throw new IOException("The reference preview could not start.");
      }
    }
    catch (Win32Exception exception)
    {
      throw new IOException("The reference preview needs ffmpeg in the agent environment.", exception);
    }

    Task<string> errors = process.StandardError.ReadToEndAsync();
    try
    {
      await process.WaitForExitAsync(timeout.Token);
      await errors;
      if (process.ExitCode != 0 || !File.Exists(target))
      {
        throw new IOException("The selected image could not be decoded for visual inspection.");
      }
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
      await KillAsync(process);
      await errors;
      if (token.IsCancellationRequested)
      {
        throw;
      }
      throw new IOException("Preparing the reference preview exceeded its time allowance.");
    }
  }

  private ProcessStartInfo StartInfo(string source, string target)
  {
    string edge = maximumEdge.ToString(CultureInfo.InvariantCulture);
    string scale = "scale=w='min(" + edge + ",iw)':h='min(" + edge + ",ih)':force_original_aspect_ratio=decrease";
    ProcessStartInfo start = new("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
    string[] arguments = ["-nostdin", "-hide_banner", "-loglevel", "error", "-hwaccel", "none", "-threads", PreviewThreads.ToString(CultureInfo.InvariantCulture), "-filter_threads", PreviewThreads.ToString(CultureInfo.InvariantCulture), "-i", source, "-map", "0:v:0", "-frames:v", "1", "-vf", scale, "-threads", PreviewThreads.ToString(CultureInfo.InvariantCulture), "-f", "image2", target];
    foreach (string argument in arguments)
    {
      start.ArgumentList.Add(argument);
    }
    return start;
  }

  private static async Task<string> HashAsync(string path, CancellationToken token)
  {
    await using FileStream stream = File.OpenRead(path);
    return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
  }

  private static void Publish(string temporary, string target)
  {
    try
    {
      File.Move(temporary, target);
    }
    catch (IOException) when (File.Exists(target))
    {
      // Concurrent inspection of the same original may have already published the identical cache entry.
    }
  }

  private static async Task KillAsync(Process process)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill(true);
      }
    }
    catch (InvalidOperationException)
    {
    }
    await process.WaitForExitAsync();
  }
}
