// Read-before-edit guard. Not in pi: a model that edits a file it never read, or one that changed under it
// (a job, a formatter, another seat's checkout), writes against contents it only imagined.
using System.Security.Cryptography;

namespace PiSharp.Tools;

/// <summary>
/// Content hash per file as the model last saw it: recorded when <c>read</c> returns the file (whole or
/// partial) and after <c>edit</c>/<c>write</c>/<c>apply_patch</c> write it. Shared by one session's tools.
/// </summary>
public sealed class FileReadTracker
{
    private readonly Dictionary<string, string> _hashes = new(StringComparer.Ordinal);

    /// <summary>Remember <paramref name="content"/> as what the model last saw of <paramref name="absolutePath"/>.</summary>
    public void Record(string absolutePath, ReadOnlySpan<byte> content)
    {
        var hash = Hash(content);
        lock (_hashes) _hashes[FileMutationQueue.QueueKey(absolutePath)] = hash;
    }

    /// <summary>Drop a deleted or moved-away file.</summary>
    public void Forget(string absolutePath)
    {
        lock (_hashes) _hashes.Remove(FileMutationQueue.QueueKey(absolutePath));
    }

    /// <summary>Null when <paramref name="current"/> is what the model last read or wrote, else why not.</summary>
    public string? Check(string absolutePath, ReadOnlySpan<byte> current, string displayPath)
    {
        string? seen;
        lock (_hashes) _hashes.TryGetValue(FileMutationQueue.QueueKey(absolutePath), out seen);
        if (seen is null) return $"{displayPath} has not been read in this session. Read it first, then edit.";
        return seen == Hash(current) ? null : $"{displayPath} changed on disk since it was last read or written. Read it again, then edit.";
    }

    /// <summary>Throw <see cref="InvalidOperationException"/> with <see cref="Check"/>'s reason, if any.</summary>
    public void EnsureFresh(string absolutePath, ReadOnlySpan<byte> current, string displayPath)
    {
        if (Check(absolutePath, current, displayPath) is { } reason) throw new InvalidOperationException(reason);
    }

    private static string Hash(ReadOnlySpan<byte> content) => Convert.ToHexString(SHA256.HashData(content));
}
