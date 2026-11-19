namespace PiSharp.Core.Factory;

/// <summary>
/// The seat's standing text (its role texts from the KB, <see cref="FactoryClient.KbSeatStandingTextAsync"/>), read again
/// while the seat runs. digital-scribe, 2026-10-26: "also the re-reading of the role is needed": until 0.5.136 a seat read
/// its role texts once, at start, so a text edited in the KB reached it only when its process restarted. Now a new task and
/// the gap between two turns read them again, at most every <see cref="Every"/>; a changed text is the seat's from its next
/// request. The KB away keeps the text the seat has.
/// </summary>
public sealed class SeatStanding
{
    /// <summary>The most bytes of role texts in the prompt: 96 KiB, about 24,000 tokens, 15% of a 160,000-token context.
    /// digital-scribe, 2026-10-26: "increase the 32k limit to what would fit fully inside a 160k context". Until 0.5.137 the
    /// role texts shared the 32 KiB cap of project instruction files, which the largest seat's texts (28 KiB) neared.</summary>
    public const int DefaultMaxBytes = 96 * 1024;

    /// <summary>How often the texts are read again at most: the factory switches' interval.</summary>
    public static readonly TimeSpan DefaultEvery = TimeSpan.FromMinutes(2);

    /// <summary>How long a read again may take: it runs between two turns, and a slow KB must not hold the seat.</summary>
    public static readonly TimeSpan RefreshLimit = TimeSpan.FromSeconds(30);

    private readonly Func<bool, CancellationToken, Task<string?>> _read;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _reading = new(1, 1);
    private DateTimeOffset _readAt;

    /// <param name="read">Reads the texts; its flag asks for them whole (true once the seat has them: a part of them is no
    /// answer), and null is no answer.</param>
    public SeatStanding(Func<bool, CancellationToken, Task<string?>> read, TimeSpan? every = null, TimeProvider? time = null)
    {
        _read = read;
        Every = every ?? DefaultEvery;
        _time = time ?? TimeProvider.System;
        _readAt = DateTimeOffset.MinValue;
    }

    public TimeSpan Every { get; }

    /// <summary>The text in force; null before the first answer.</summary>
    public string? Current { get; private set; }

    /// <summary>Reads the texts now (the seat's start); true when the KB answered.</summary>
    public async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        await _reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadLockedAsync(null, cancellationToken).ConfigureAwait(false) is not null; }
        finally { _reading.Release(); }
    }

    /// <summary>Reads the texts again when <see cref="Every"/> has passed since the last read; true only when they changed.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        if (_time.GetUtcNow() - _readAt < Every) return false;
        await _reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_time.GetUtcNow() - _readAt < Every) return false;
            var before = Current;
            return await ReadLockedAsync(RefreshLimit, cancellationToken).ConfigureAwait(false) is { } text && !string.Equals(text, before, StringComparison.Ordinal);
        }
        finally { _reading.Release(); }
    }

    private async Task<string?> ReadLockedAsync(TimeSpan? limit, CancellationToken cancellationToken)
    {
        // Stamped before the read: a KB that does not answer is asked again only after the interval, not on every turn.
        _readAt = _time.GetUtcNow();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (limit is { } cap) deadline.CancelAfter(cap);
        string? text;
        try { text = await _read(Current is not null, deadline.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) { return null; }
        if (text is null) return null;
        Current = text;
        return text;
    }
}
