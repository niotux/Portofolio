// Pulls steering notes from the cubicle between turns and turns them into user messages.
// See docs adr-pi-sharp-cubicle-client: wait=0 for steering, wait up to 90 s for follow-ups only when the
// harness opts in; an unavailable server yields no messages and backs off 30 s doubling to 5 min, reset on
// the first 2xx. Nothing here throws into AgentLoop.
using PiSharp.Core.Messages;

namespace PiSharp.Core.Harness;

public sealed class CubicleSteering
{
    public static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private readonly CubicleClient _client;
    private readonly TimeProvider _time;
    private readonly HashSet<long> _seen = [];
    private readonly object _gate = new();
    private long _since;
    private bool _primed;
    private TimeSpan _backoff = TimeSpan.Zero;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;

    public CubicleSteering(CubicleClient client, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>When true (the default) the first successful poll only moves the cursor to the room's current
    /// <c>last</c> and delivers nothing, so steering notes left before this process started are not replayed
    /// into the run. Set false to consume the room's whole history.</summary>
    public bool SkipHistory { get; init; } = true;

    /// <summary>Seconds the follow-up poll may long-poll (0 = follow-ups disabled, the default).</summary>
    public int FollowUpWaitSeconds { get; init; }

    /// <summary>Last message id acknowledged to the server (the <c>since</c> cursor).</summary>
    public long Since => Interlocked.Read(ref _since);

    /// <summary>Current back-off delay; zero while the server answers.</summary>
    public TimeSpan Backoff { get { lock (_gate) return _backoff; } }

    /// <summary>For <see cref="Agent.AgentLoopConfig.GetSteeringMessages"/>: non-blocking poll after each turn.</summary>
    public ValueTask<IReadOnlyList<Message>> GetSteeringMessages() => PollAsync(0);

    /// <summary>For <see cref="Agent.AgentLoopConfig.GetFollowUpMessages"/>: long-polls only when opted in.</summary>
    public ValueTask<IReadOnlyList<Message>> GetFollowUpMessages() =>
        FollowUpWaitSeconds <= 0 ? new ValueTask<IReadOnlyList<Message>>([]) : PollAsync(FollowUpWaitSeconds);

    public async ValueTask<IReadOnlyList<Message>> PollAsync(int waitSeconds, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = _time.GetUtcNow();
            lock (_gate)
            {
                if (now < _retryAt) return [];
            }
            var reply = await _client.GetAsync(Since, waitSeconds, cancellationToken).ConfigureAwait(false);
            if (reply is null)
            {
                lock (_gate)
                {
                    _backoff = _backoff == TimeSpan.Zero ? InitialBackoff : TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, MaxBackoff.Ticks));
                    _retryAt = now + _backoff;
                }
                return [];
            }
            var messages = new List<Message>();
            lock (_gate)
            {
                _backoff = TimeSpan.Zero;
                _retryAt = DateTimeOffset.MinValue;
                if (reply.Last > _since) Interlocked.Exchange(ref _since, reply.Last);
                if (!_primed)
                {
                    _primed = true;
                    if (SkipHistory) return [];
                }
                foreach (var m in reply.Messages)
                {
                    if (!_seen.Add(m.Id)) continue;
                    if (m.Id > _since) Interlocked.Exchange(ref _since, m.Id);
                    messages.Add(new UserMessage($"[steering from @{m.From}] {m.Text}"));
                }
            }
            return messages;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
        }
    }
}
