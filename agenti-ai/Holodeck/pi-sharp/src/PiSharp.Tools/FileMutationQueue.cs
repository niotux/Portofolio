// Ported from pi: packages/coding-agent/src/core/tools/file-mutation-queue.ts @ 71dca871b
//
// Serializes mutations that target the same file; different files still run concurrently.
// pi keys the queue on realpath(); here the key is the fully resolved path, lowercased on
// Windows, with symlinks resolved when the target exists.
namespace PiSharp.Tools;

/// <summary>Per-file mutation lock shared by the write and edit tools.</summary>
public static class FileMutationQueue
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Waiters;
    }

    private static readonly Dictionary<string, Entry> s_queues = new(StringComparer.Ordinal);

    /// <summary>Run <paramref name="action"/> with exclusive access to <paramref name="filePath"/>.</summary>
    public static async Task<T> RunAsync<T>(string filePath, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        var key = QueueKey(filePath);
        Entry entry;
        lock (s_queues)
        {
            if (!s_queues.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                s_queues[key] = entry;
            }
            entry.Waiters++;
        }

        var acquired = false;
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            return await action().ConfigureAwait(false);
        }
        finally
        {
            if (acquired) entry.Semaphore.Release();
            lock (s_queues)
            {
                if (--entry.Waiters == 0) s_queues.Remove(key);
            }
        }
    }

    /// <summary>Identity of a file on disk: full path, symlinks resolved, case-folded on Windows.</summary>
    internal static string QueueKey(string filePath)
    {
        var resolved = Path.GetFullPath(filePath);
        try
        {
            // Two paths that point at the same file through a symlink share one queue.
            var target = File.ResolveLinkTarget(resolved, returnFinalTarget: true);
            if (target is not null) resolved = target.FullName;
        }
        catch (IOException)
        {
            // Missing or unreadable path: the resolved path is a good enough key.
        }
        catch (UnauthorizedAccessException)
        {
        }
        return OperatingSystem.IsWindows() ? resolved.ToLowerInvariant() : resolved;
    }
}
