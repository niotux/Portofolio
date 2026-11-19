using System.Threading.Channels;

namespace Humans;

// The body meets the world: it keeps everything that arrives in the inbox, wakes the person for a tag or for something
// arriving in the conversation they have open, and delivers what they decided to write. A tag carries only its channel.
// Which conversations exist and who is in them come from Holodeck; IDs never reach model prose.
public sealed class Body(Holodeck transport, Store store, RuntimeOptions options)
{
  // Long enough to keep requests rare, short enough that queued sends never wait long.
  public const int ReceiveSeconds = 10;
  private static readonly string[] EveryoneMarks = ["@everyone", "@here", "@all"];
  private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
  private readonly Lock gate = new();
  private Dictionary<string, Room> rooms = new();
  private Dictionary<string, string> names = new();
  private Identity? identity;
  private string? focus;

  public string MemberId => identity?.MemberId ?? "";

  public string? Focus
  {
    get
    {
      lock (gate)
      {
        return focus;
      }
    }
    set
    {
      lock (gate)
      {
        focus = value;
      }
    }
  }

  public Dictionary<string, Room> Rooms => Around.Rooms;

  public Surroundings Around
  {
    get
    {
      lock (gate)
      {
        return new Surroundings(new Dictionary<string, Room>(rooms), new Dictionary<string, string>(names));
      }
    }
  }

  public async Task<Identity> RefreshAsync(Page page, CancellationToken token)
  {
    Identity current = await transport.MeAsync(token);
    Dictionary<string, string> contacts = await store.ContactsAsync(token);
    Dictionary<string, Room> known = new();
    Dictionary<string, string> called = new();
    foreach (AgentChannel channel in current.Channels)
    {
      List<string> others = new();
      string unnamed = "";
      foreach (Participant participant in channel.Participants)
      {
        if (participant.MemberId == current.MemberId)
        {
          continue;
        }
        bool hasContact = contacts.TryGetValue(participant.MemberId, out string? contact);
        // The room's own human has no name until the person saves one; everyone else arrives with theirs.
        string name = hasContact ? contact! : participant.IsYou ? page.Fill(page.Text.Human) : participant.Name;
        if (participant.IsYou && !hasContact)
        {
          unnamed = participant.MemberId;
        }
        called[participant.MemberId] = name;
        others.Add(name);
      }
      bool direct = others.Count == 1;
      string label = direct ? page.Fill(page.Text.Direct) : page.Fill(page.Text.Group);
      label = label.Replace("{channel}", channel.Channel).Replace("{others}", string.Join(", ", others));
      known.Add(channel.ChannelId, new Room(channel.ChannelId, label, direct, others, unnamed));
    }
    lock (gate)
    {
      identity = current;
      rooms = known;
      names = called;
    }
    return current;
  }

  // Runs beside the person for as long as it lives: receipt is durable before the next request acknowledges it.
  public async Task ReceiveAsync(Page page, CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      try
      {
        await FlushAsync(token);
        Delivery? packet = await transport.NextAsync(ReceiveSeconds, token);
        if (packet is null)
        {
          continue;
        }
        Room? room = RoomOf(packet.ChannelId);
        if (room is null)
        {
          await RefreshAsync(page, token);
          room = RoomOf(packet.ChannelId);
        }
        bool tag = room is not null && (room.Direct || Mentions(packet.Note, page.Name));
        await store.ReceiveAsync(packet, tag, token);
        if (tag || packet.ChannelId == Focus)
        {
          wake.Writer.TryWrite(true);
        }
      }
      catch (Exception exception) when (exception is HttpRequestException || exception is IOException || (exception is OperationCanceledException && !token.IsCancellationRequested))
      {
        Console.WriteLine("Holodeck could not be reached for new messages: " + exception.Message + " Trying again.");
        await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), token);
      }
    }
  }

  // Waits for a reason to wake: a tag, something in the open conversation, or the end of a quiet stretch. Returns false
  // when only time passed.
  public async Task<bool> WaitAsync(TimeSpan most, CancellationToken token)
  {
    using CancellationTokenSource quiet = CancellationTokenSource.CreateLinkedTokenSource(token);
    if (most > TimeSpan.Zero)
    {
      quiet.CancelAfter(most);
    }
    try
    {
      await wake.Reader.ReadAsync(quiet.Token);
      return true;
    }
    catch (OperationCanceledException) when (!token.IsCancellationRequested)
    {
      return false;
    }
  }

  // Something already waiting means the person should not rest.
  public bool Pending()
  {
    return wake.Reader.Count > 0;
  }

  public void Nudge()
  {
    wake.Writer.TryWrite(true);
  }

  // A send already decided is delivered with its own key, so a retry after an uncertain outcome is never a second message.
  public async Task FlushAsync(CancellationToken token)
  {
    PendingSend? send;
    while ((send = await store.NextSendAsync(token)) is not null)
    {
      if (RoomOf(send.Channel) is not null)
      {
        await transport.SendAsync(send, token);
      }
      // A message for a conversation that no longer exists stays in the history, undelivered.
      await store.SentAsync(send.Id, token);
    }
  }

  public Room? RoomOf(string channel)
  {
    lock (gate)
    {
      return rooms.TryGetValue(channel, out Room? room) ? room : null;
    }
  }

  // A tag is the person's name, or a call to everyone, written in the message. Code routes; the person interprets.
  public static bool Mentions(string note, string name)
  {
    ReadOnlySpan<char> text = note.AsSpan();
    foreach (string mark in EveryoneMarks)
    {
      if (text.Contains(mark.AsSpan(), StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }
    ReadOnlySpan<char> rest = name.AsSpan();
    while (rest.Length > 0)
    {
      int space = rest.IndexOf(' ');
      ReadOnlySpan<char> word = space < 0 ? rest : rest[..space];
      rest = space < 0 ? ReadOnlySpan<char>.Empty : rest[(space + 1)..];
      if (word.Length >= 3 && ContainsWord(text, word))
      {
        return true;
      }
    }
    return false;
  }

  private static bool ContainsWord(ReadOnlySpan<char> text, ReadOnlySpan<char> word)
  {
    int offset = 0;
    while (offset < text.Length)
    {
      int found = text[offset..].IndexOf(word, StringComparison.OrdinalIgnoreCase);
      if (found < 0)
      {
        return false;
      }
      int start = offset + found;
      int end = start + word.Length;
      bool before = start == 0 || !char.IsLetterOrDigit(text[start - 1]);
      bool after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
      if (before && after)
      {
        return true;
      }
      offset = start + 1;
    }
    return false;
  }
}
