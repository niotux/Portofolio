using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Notebook;

/// <summary>Execution-derived source identity; never trusts a model-supplied revision label.</summary>
public static class WorkspaceEvidence
{
    public static async Task<JsonObject> CaptureAsync(string directory, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            async Task<string> Git(params string[] arguments)
            {
                var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in arguments) start.ArgumentList.Add(arg);
                using var p = Process.Start(start)!;
                try
                {
                    var stderr = p.StandardError.ReadToEndAsync(timeout.Token);
                    var text = await Mcp.BoundedReader.ReadToEndAsync(p.StandardOutput, timeout.Token);
                    await p.WaitForExitAsync(timeout.Token); await stderr;
                    if (p.ExitCode != 0) throw new IOException("Git source identity unavailable.");
                    return text;
                }
                finally { if (!p.HasExited) p.Kill(true); }
            }
            var root = (await Git("rev-parse", "--show-toplevel")).Trim();
            var head = (await Git("rev-parse", "HEAD")).Trim();
            var paths = (await Git("-C", root, "ls-files", "--cached", "--others", "--exclude-standard", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct().Order(StringComparer.Ordinal).ToArray();
            if (paths.Length > 20000) throw new IOException("Source fingerprint file budget exceeded.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            foreach (var relative in paths)
            {
                timeout.Token.ThrowIfCancellationRequested();
                hash.AppendData(Encoding.UTF8.GetBytes(relative + "\0"));
                var file = new FileInfo(Path.Combine(root, relative));
                if (!file.Exists) { hash.AppendData("deleted\0"u8); continue; }
                if (file.LinkTarget is { } link) { hash.AppendData(Encoding.UTF8.GetBytes("link:" + link)); continue; }
                var length = file.Length; var modified = file.LastWriteTimeUtc;
                total += length; if (total > 128L * 1024 * 1024) throw new IOException("Source fingerprint byte budget exceeded.");
                using var stream = file.OpenRead(); hash.AppendData(await SHA256.HashDataAsync(stream, timeout.Token));
                file.Refresh(); if (file.Length != length || file.LastWriteTimeUtc != modified) throw new IOException("Source changed during fingerprint.");
            }
            return new JsonObject { ["available"] = true, ["head"] = head, ["sha256"] = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), ["files"] = paths.Length };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { return new JsonObject { ["available"] = false, ["reason"] = ex.Message }; }
    }
}
