using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace CreativeStudio;

// Coalesces live text per phase so slow status transport cannot queue every token or delay model generation.
public sealed class ActivityReporter : IAsyncDisposable
{
  private const int UpdateMilliseconds = 350;
  private const int HeartbeatSeconds = 5;
  private const int RequestSeconds = 3;
  private const int ShutdownSeconds = 4;
  private readonly HolodeckClient transport;
  private readonly StoredReceipt packet;
  private readonly string id = Guid.NewGuid().ToString("N");
  private readonly Lock gate = new();
  private readonly SortedDictionary<int, ActivityUpdate> pending = new();
  private readonly Channel<bool> changed = Channel.CreateBounded<bool>(1);
  private readonly CancellationTokenSource stop = new();
  private readonly Task sending;
  private readonly StringBuilder text = new();
  private readonly Stopwatch updateClock = Stopwatch.StartNew();
  private ActivityUpdate? current;
  private int ordinal = -1;
  private bool finishing;
  private bool warned;

  public ActivityReporter(HolodeckClient transport, StoredReceipt packet)
  {
    this.transport = transport;
    this.packet = packet;
    sending = SendLoopAsync();
  }

  public void Begin(string phase, string label, bool typing = false)
  {
    lock (gate)
    {
      ordinal++;
      text.Clear();
      current = new ActivityUpdate(id, packet.ChannelId, packet.PacketId, ordinal, phase, label, "", typing, false, "working");
      Queue();
    }
  }

  public void Append(string fragment)
  {
    lock (gate)
    {
      text.Append(fragment);
      if (current is not null && updateClock.ElapsedMilliseconds >= UpdateMilliseconds)
      {
        current = current with { Text = text.ToString() };
        Queue();
        updateClock.Restart();
      }
    }
  }

  public void End()
  {
    lock (gate)
    {
      if (current is not null)
      {
        current = current with { Text = text.ToString(), IsComplete = true, IsTyping = false };
        Queue();
      }
    }
  }

  public void Finish(bool succeeded)
  {
    lock (gate)
    {
      if (current is not null)
      {
        current = current with { Text = text.ToString(), State = succeeded ? "completed" : "failed", IsTyping = false, IsComplete = true };
        Queue();
      }
      finishing = true;
      changed.Writer.TryWrite(true);
    }
  }

  private void Queue()
  {
    pending[current!.Ordinal] = current;
    changed.Writer.TryWrite(true);
  }

  private async Task SendLoopAsync()
  {
    while (!stop.IsCancellationRequested)
    {
      ActivityUpdate? next = null;
      lock (gate)
      {
        foreach (ActivityUpdate value in pending.Values)
        {
          next = value;
          break;
        }
        if (next is not null)
        {
          pending.Remove(next.Ordinal);
        }
        else if (finishing)
        {
          return;
        }
      }
      if (next is not null)
      {
        try
        {
          using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
          request.CancelAfter(TimeSpan.FromSeconds(RequestSeconds));
          await transport.ActivityAsync(next, request.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is OperationCanceledException)
        {
          if (!warned)
          {
            Console.WriteLine("Live activity transport is unavailable; conversation and memory processing continue.");
            warned = true;
          }
        }
        continue;
      }
      using CancellationTokenSource heartbeat = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
      heartbeat.CancelAfter(TimeSpan.FromSeconds(HeartbeatSeconds));
      try
      {
        await changed.Reader.ReadAsync(heartbeat.Token);
      }
      catch (OperationCanceledException) when (!stop.IsCancellationRequested)
      {
        lock (gate)
        {
          if (current is not null && !finishing)
          {
            current = current with { Text = text.ToString() };
            Queue();
          }
        }
      }
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (!finishing)
    {
      Finish(false);
    }
    try
    {
      await sending.WaitAsync(TimeSpan.FromSeconds(ShutdownSeconds));
    }
    catch (TimeoutException)
    {
      await stop.CancelAsync();
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
      await stop.CancelAsync();
      try
      {
        await sending;
      }
      catch (OperationCanceledException)
      {
      }
      stop.Dispose();
    }
  }
}
