using System.Text;

namespace Humans;

// One continuing person on one serial path. Something wakes them; they notice what reached them, remember what it
// brings back, think, and their hands act on the thought. They keep going until they choose to wait. Code selects each
// stage from the hands' operation, never from words in a thought. A technical limit pauses the loop without becoming a
// decision of theirs.
public sealed class Person(ChatModel model, Hands hands, Recall recall, Store store, Body body, Page page, RuntimeOptions options, LivePrompts prompts, Holodeck transport, string workspace)
{
  private const string FocusKey = "focus";
  // A spoken line repeated word for word is the page's easiest continuation, not speech; it gets another try.
  private const int EchoRetries = 2;
  private readonly List<DateTimeOffset> cycles = new();
  private Summary? summary;
  private List<Entry> entries = new();
  private long recalledThrough;
  private long formedThrough;
  private bool misdirected;

  public async Task LoadAsync(CancellationToken token)
  {
    History history = await store.HistoryAsync(token);
    summary = history.Summary;
    entries = history.Entries;
    recalledThrough = entries.Count == 0 ? 0 : entries[^1].Id;
    formedThrough = await store.LastFormedAsync(token);
    string? focus = await store.StateAsync(FocusKey, token);
    body.Focus = focus is not null && focus.Length > 0 && body.RoomOf(focus) is not null ? focus : null;
  }

  public async Task RunAsync(CancellationToken token)
  {
    // Coming back after a stop is a moment of its own: the person looks around once.
    bool woken = true;
    while (!token.IsCancellationRequested)
    {
      if (!woken)
      {
        TimeSpan quiet = options.IdleWakeMinutes > 0 ? TimeSpan.FromMinutes(options.IdleWakeMinutes) : TimeSpan.Zero;
        await body.WaitAsync(quiet, token);
      }
      woken = false;
      try
      {
        await PaceAsync(token);
        bool left = await WakeAsync(token);
        if (left || Unformed() >= options.FormEntries)
        {
          await FormAsync(entries.Count, token);
        }
      }
      catch (Exception exception) when (exception is HttpRequestException || exception is IOException || (exception is OperationCanceledException && !token.IsCancellationRequested))
      {
        // Everything already experienced is stored; the next wake continues from it.
        Console.WriteLine("A model, embedding or transport request could not finish: " + exception.Message + " The saved history is kept; trying again shortly.");
        await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds * 5), token);
        woken = true;
      }
    }
  }

  // Returns true when the person left a conversation during the wake: the end of a visit is a natural memory boundary.
  private async Task<bool> WakeAsync(CancellationToken token)
  {
    bool left = false;
    await PinAsync(token);
    await body.RefreshAsync(page, token);
    string? reporting = body.Focus ?? AnyRoom();
    await using ActivityReporter? activity = reporting is null ? null : new ActivityReporter(transport, reporting);
    bool waiting = false;
    for (int cycle = 0; cycle < options.MaxCyclesPerWake && !waiting; cycle++)
    {
      if (cycle > 0)
      {
        await PinAsync(token);
      }
      cycles.Add(DateTimeOffset.UtcNow);
      await NoticeAsync(token);
      await RememberAsync(activity, token);
      await CompactIfFullAsync(token);
      await ThinkAsync(activity, token);
      Room? focus = FocusRoom();
      activity?.Begin("hands", "Acting");
      HandsChoice choice = await hands.ChooseAsync(page.Entries(entries, entries.Count - options.HandsEntries, body.Around) + await NowAsync(token), body.Around, focus, token);
      activity?.Append(choice.Action + (choice.Argument.Length > 0 ? " " + choice.Argument : ""));
      activity?.End();
      Console.WriteLine("Hands: " + choice.Action + (choice.Argument.Length > 0 ? " " + choice.Argument : ""));
      left = left || choice.Action == HandsActions.Close;
      waiting = await ActAsync(choice, activity, token);
      Snapshot();
    }
    if (!waiting)
    {
      Console.WriteLine("The wake reached its cycle limit; the person pauses until something new arrives.");
    }
    activity?.Finish(true);
    return left;
  }

  private int Unformed()
  {
    int count = 0;
    foreach (Entry entry in entries)
    {
      if (entry.Id > formedThrough)
      {
        count++;
      }
    }
    return count;
  }

  // Pins one prompt set for the next stretch; a set this version cannot run leaves the previous one in use.
  private async Task PinAsync(CancellationToken token)
  {
    try
    {
      page.Use(await prompts.ReadAsync(token));
      LivePrompts.Record(workspace, "last-prompts.json", page.Live!);
    }
    catch (ArgumentException exception)
    {
      if (page.Live is null)
      {
        throw new IOException("The live prompts cannot run this version: " + exception.Message);
      }
      Console.WriteLine("The live prompts were refused and the previous set stays in use: " + exception.Message);
    }
  }

  // What reached the person since they last looked: a buzz for each tagged conversation, and anything new in the one open.
  private async Task NoticeAsync(CancellationToken token)
  {
    string? focus = body.Focus;
    foreach (Buzz buzz in await store.TakeBuzzesAsync(body.Muted, token))
    {
      Room? room = body.RoomOf(buzz.Channel);
      if (buzz.Channel != focus && room is not null)
      {
        await AppendAsync(EntryKinds.Event, "", "", page.Event(buzz.Mention ? page.Text.Mention : page.Text.Buzz, room), token);
      }
    }
    if (focus is not null)
    {
      await ShowAsync(focus, false, token);
    }
  }

  private async Task ShowAsync(string channel, bool opening, CancellationToken token)
  {
    Room room = body.RoomOf(channel)!;
    int unseen = await store.UnseenCountAsync(channel, token);
    if (opening)
    {
      await AppendAsync(EntryKinds.Event, "", "", page.Event(unseen == 0 ? page.Text.OpenEmpty : page.Text.Open, room), token);
    }
    if (unseen > options.OpenMessages)
    {
      await AppendAsync(EntryKinds.Event, "", "", page.Event(page.Text.Earlier, room).Replace("{count}", (unseen - options.OpenMessages).ToString()), token);
    }
    foreach (Received message in await store.ReadAsync(channel, options.OpenMessages, token))
    {
      await AppendAsync(EntryKinds.Message, channel, message.Sender, message.Note, token);
    }
  }

  // Recollection follows new outside material, not every stretch of thought.
  private async Task RememberAsync(ActivityReporter? activity, CancellationToken token)
  {
    bool fresh = false;
    foreach (Entry entry in entries)
    {
      if (entry.Id > recalledThrough && entry.Kind == EntryKinds.Message)
      {
        fresh = true;
      }
    }
    if (!fresh)
    {
      return;
    }
    recalledThrough = entries[^1].Id;
    long firstLive = entries.Count == 0 ? long.MaxValue : entries[0].Id;
    foreach (string remembered in await recall.RememberAsync(page.Entries(entries, entries.Count - options.LookEntries, body.Around), firstLive, activity, token))
    {
      await AppendAsync(EntryKinds.Recollection, "", "", remembered, token);
    }
    recalledThrough = entries[^1].Id;
  }

  private async Task ThinkAsync(ActivityReporter? activity, CancellationToken token)
  {
    string written = page.Document(summary, entries, body.Around) + await NowAsync(token) + page.Thought;
    activity?.Begin("think", "Thinking");
    Completion thought = await model.ContinueAsync(written, page.Instruction("continue.txt"), [page.Text.Close, "\n\n"], options.ThoughtTokens, token, activity is null ? null : activity.Append, options.ThoughtTemperature);
    activity?.End();
    string text = Unechoed(thought.Text);
    Console.WriteLine("Thought: " + text);
    // A thought already on the page, word for word, is the page repeating itself, not a new thought.
    bool repeated = AlreadyThought(text);
    if (text.Length > 0 && !repeated)
    {
      await AppendAsync(EntryKinds.Thought, "", "", text, token);
    }
  }

  // Returns true when the person waits.
  private async Task<bool> ActAsync(HandsChoice choice, ActivityReporter? activity, CancellationToken token)
  {
    switch (choice.Action)
    {
      case HandsActions.Wait:
        return true;
      case HandsActions.Open:
        await OpenAsync(choice.Argument, token);
        return false;
      case HandsActions.Write:
        await WriteAsync(activity, token);
        return false;
      case HandsActions.Check:
        await CheckAsync(token);
        return false;
      case HandsActions.Close:
        await CloseAsync(token);
        return false;
      case HandsActions.Name:
        await NameAsync(choice.Argument, token);
        return false;
      case HandsActions.Work:
        await WorkAsync(choice.Argument, activity, token);
        return false;
      case HandsActions.Mute:
      case HandsActions.Unmute:
        await MuteAsync(choice.Action == HandsActions.Mute, token);
        return false;
      default:
        return false;
    }
  }

  private async Task OpenAsync(string label, CancellationToken token)
  {
    Room? room = FindRoom(label);
    if (room is null)
    {
      await AppendAsync(EntryKinds.Event, "", "", page.Fill(page.Text.Unavailable).Replace("{channel}", label), token);
      return;
    }
    body.Focus = room.ChannelId;
    await store.SetStateAsync(FocusKey, room.ChannelId, token);
    await ShowAsync(room.ChannelId, true, token);
  }

  private async Task WriteAsync(ActivityReporter? activity, CancellationToken token)
  {
    Room? room = FocusRoom();
    if (room is null)
    {
      await AppendAsync(EntryKinds.Event, "", "", page.Fill(page.Text.NoConversation), token);
      return;
    }
    // Something may have arrived while the person was deciding to write; they see it before they type.
    await ShowAsync(room.ChannelId, false, token);
    string speaking = page.Document(summary, entries, body.Around) + page.Speech(room);
    activity?.Begin("reply", "Writing", true);
    string reply = "";
    misdirected = false;
    for (int attempt = 0; attempt <= EchoRetries && !misdirected; attempt++)
    {
      // A blank line or a thought opening ends the message; the model sometimes starts it on the next line.
      Completion completion = await model.ContinueAsync(speaking, page.Instruction("continue.txt"), ["\n\n", "\n" + page.Thought[0]], options.SpeechTokens, token, activity is null ? null : activity.Append, options.SpeechTemperature);
      reply = Spoken(completion.Text, page.Speech(room));
      if (reply.Length > 0 && !AlreadySaid(reply))
      {
        break;
      }
      Console.WriteLine("The message was empty or repeated one already written (" + completion.Text.Length + " characters came back); writing again.");
    }
    activity?.End();
    if (misdirected)
    {
      Console.WriteLine("The message was meant for a conversation other than the open one; nothing was sent.");
      await AppendAsync(EntryKinds.Event, "", "", page.Event(page.Text.Elsewhere, room), token);
      return;
    }
    if (reply.Length == 0 || AlreadySaid(reply))
    {
      Console.WriteLine("Nothing new was written.");
      return;
    }
    entries.Add(await store.SayAsync(room.ChannelId, page.Name, reply, token));
    Console.WriteLine("Said in " + room.Label + ": " + reply);
    await body.FlushAsync(token);
  }

  // The here and now, as a glance at the phone shows it: the time, what is open, who can be reached at all, and what is
  // unread. It is rendered fresh before each thought and never stored, so it is always true.
  private async Task<string> NowAsync(CancellationToken token)
  {
    Surroundings world = body.Around;
    Room? focus = FocusRoom();
    List<string> reachable = new();
    foreach (Room room in world.Rooms.Values)
    {
      reachable.Add(room.Label + (room.Direct ? "" : " (" + string.Join(", ", room.Others) + ")"));
    }
    string screen = focus is null ? page.Fill(page.Text.ScreenIdle) : page.Event(page.Text.ScreenOpen, focus);
    string unread = await UnreadLinesAsync(world, token);
    string reach = page.Fill(page.Text.Reach).Replace("{list}", string.Join("; ", reachable));
    string now = page.Fill(page.Text.Now).Replace("{time}", page.Clock(DateTimeOffset.UtcNow)).Replace("{screen}", screen).Replace("{reach}", reach).Replace("{unread}", unread.Length == 0 ? page.Fill(page.Text.UnreadNone) : page.Fill(page.Text.Phone).Replace("{list}", unread));
    return now + "\n";
  }

  private async Task<string> UnreadLinesAsync(Surroundings world, CancellationToken token)
  {
    List<string> lines = new();
    foreach (Unread item in await store.UnreadAsync(token))
    {
      Room? room = body.RoomOf(item.ChannelId);
      if (room is not null)
      {
        lines.Add(page.Event(page.Text.PhoneLine, room).Replace("{count}", item.Count.ToString()).Replace("{senders}", string.Join(", ", Called(item.Senders, world))));
      }
    }
    return string.Join("; ", lines);
  }

  // Working something out on purpose: a focused, instructed pass in which the person reasons through one question in prose
  // and reaches a conclusion. Its working becomes their own reasoning on the page.
  private async Task WorkAsync(string question, ActivityReporter? activity, CancellationToken token)
  {
    if (question.Trim().Length == 0)
    {
      return;
    }
    string context = page.Entries(entries, entries.Count - options.HandsEntries, body.Around) + await NowAsync(token);
    string input = context + "\n" + page.Fill(page.Text.Worked) + " " + question.Trim();
    activity?.Begin("work", "Working it out");
    Completion worked = await model.SayAsync([new Message("system", page.Instruction("work.txt")), new Message("user", input)], token, activity is null ? null : activity.Append, options.UtilityTemperature);
    activity?.End();
    string text = worked.Text.Trim();
    Console.WriteLine("Worked out \"" + question + "\": " + text);
    if (text.Length > 0)
    {
      await AppendAsync(EntryKinds.Worked, "", "", question.Trim() + " " + text, token);
    }
  }

  private async Task CheckAsync(CancellationToken token)
  {
    List<Unread> unread = await store.UnreadAsync(token);
    Surroundings world = body.Around;
    List<string> lines = new();
    foreach (Unread item in unread)
    {
      Room? room = body.RoomOf(item.ChannelId);
      if (room is null)
      {
        continue;
      }
      lines.Add(page.Event(page.Text.PhoneLine, room).Replace("{count}", item.Count.ToString()).Replace("{senders}", string.Join(", ", Called(item.Senders, world))));
    }
    string text = lines.Count == 0 ? page.Fill(page.Text.PhoneEmpty) : page.Fill(page.Text.Phone).Replace("{list}", string.Join("; ", lines));
    await AppendAsync(EntryKinds.Event, "", "", text, token);
  }

  // A saved contact name replaces the unknown label everywhere, including on lines already read.
  private async Task NameAsync(string name, CancellationToken token)
  {
    Room? room = FocusRoom();
    string contact = name.Trim();
    if (room is null || room.Unnamed.Length == 0 || contact.Length == 0)
    {
      return;
    }
    await store.SaveContactAsync(room.Unnamed, contact, token);
    await body.RefreshAsync(page, token);
    await AppendAsync(EntryKinds.Event, "", "", page.Fill(page.Text.Named).Replace("{contact}", contact), token);
  }

  private async Task MuteAsync(bool mute, CancellationToken token)
  {
    Room? room = FocusRoom();
    if (room is null)
    {
      return;
    }
    await store.SetMutedAsync(room.ChannelId, mute, token);
    await body.RefreshAsync(page, token);
    await AppendAsync(EntryKinds.Event, "", "", page.Event(mute ? page.Text.Muted : page.Text.Unmuted, room), token);
  }

  private async Task CloseAsync(CancellationToken token)
  {
    Room? room = FocusRoom();
    if (room is null)
    {
      return;
    }
    body.Focus = null;
    await store.SetStateAsync(FocusKey, "", token);
    await AppendAsync(EntryKinds.Event, "", "", page.Event(page.Text.Closed, room), token);
  }

  // Older history folds into prose before the page outgrows its share of the model's capacity. The stored entries remain.
  private async Task CompactIfFullAsync(CancellationToken token)
  {
    double characters = page.Document(summary, entries, body.Around).Length;
    double budget = options.ContextTokens * (double)options.CompactPercent / 100;
    int cut = entries.Count - options.KeepEntries;
    if (characters / options.CharactersPerToken + options.SpeechTokens < budget || cut <= 0)
    {
      return;
    }
    await FormAsync(cut, token);
    StringBuilder input = new();
    if (summary is not null)
    {
      input.Append("What I already carried from before:\n").Append(summary.Text).Append("\n\n");
    }
    input.Append("What happened since:\n").Append(page.Entries(entries.GetRange(0, cut), 0, body.Around));
    Completion result = await model.SayAsync([new Message("system", page.Instruction("compact.txt")), new Message("user", input.ToString())], token, null, options.UtilityTemperature);
    if (string.IsNullOrWhiteSpace(result.Text))
    {
      throw new IOException("Compaction returned no prose; the history was kept whole.");
    }
    Summary folded = new(entries[cut - 1].Id, result.Text.Trim());
    await store.SaveSummaryAsync(folded, token);
    summary = folded;
    entries.RemoveRange(0, cut);
    Console.WriteLine("Folded " + cut + " older moments into the remembered summary.");
  }

  // A stretch with something in it from outside, or some thinking of their own, waits to become memory.
  private async Task FormAsync(int upto, CancellationToken token)
  {
    int start = -1;
    int outside = 0;
    int thoughts = 0;
    for (int i = 0; i < upto && i < entries.Count; i++)
    {
      if (entries[i].Id <= formedThrough)
      {
        continue;
      }
      if (start < 0)
      {
        start = i;
      }
      if (entries[i].Kind == EntryKinds.Message || entries[i].Kind == EntryKinds.Said)
      {
        outside++;
      }
      if (entries[i].Kind == EntryKinds.Thought)
      {
        thoughts++;
      }
    }
    if (start < 0 || (outside == 0 && thoughts < 3))
    {
      return;
    }
    int end = Math.Min(upto, entries.Count) - 1;
    int context = Math.Max(0, start - 2);
    StringBuilder text = new();
    if (context < start)
    {
      text.Append("Just before:\n").Append(page.Entries(entries.GetRange(context, start - context), 0, body.Around)).Append('\n');
    }
    text.Append("What happened:\n").Append(page.Entries(entries.GetRange(start, end - start + 1), 0, body.Around));
    await store.QueueFormationAsync(entries[start].Id, entries[end].Id, text.ToString(), token);
    formedThrough = entries[end].Id;
  }

  // A runaway loop is bounded by an hourly allowance; reaching it is a technical pause.
  private async Task PaceAsync(CancellationToken token)
  {
    DateTimeOffset hourAgo = DateTimeOffset.UtcNow.AddHours(-1);
    while (cycles.Count > 0 && cycles[0] < hourAgo)
    {
      cycles.RemoveAt(0);
    }
    if (cycles.Count < options.MaxCyclesPerHour)
    {
      return;
    }
    TimeSpan pause = cycles[0].AddHours(1) - DateTimeOffset.UtcNow;
    Console.WriteLine("The hourly allowance of cycles is used up; pausing for " + (int)pause.TotalMinutes + " minutes.");
    await Task.Delay(pause, token);
  }

  // The page shows each moment when the person met it.
  private async Task AppendAsync(string kind, string channel, string speaker, string text, CancellationToken token)
  {
    entries.Add(await store.AppendAsync(kind, channel, speaker, text, token));
  }

  private List<string> Called(List<string> members, Surroundings world)
  {
    List<string> called = new();
    foreach (string member in members)
    {
      called.Add(world.Names.TryGetValue(member, out string? name) ? name : page.Fill(page.Text.Human));
    }
    return called;
  }

  private Room? FocusRoom()
  {
    string? focus = body.Focus;
    return focus is null ? null : body.RoomOf(focus);
  }

  private string? AnyRoom()
  {
    foreach (Room room in body.Rooms.Values)
    {
      return room.ChannelId;
    }
    return null;
  }

  private Room? FindRoom(string label)
  {
    Dictionary<string, Room> rooms = body.Rooms;
    foreach (Room room in rooms.Values)
    {
      if (room.Label.Equals(label, StringComparison.OrdinalIgnoreCase))
      {
        return room;
      }
    }
    foreach (Room room in rooms.Values)
    {
      if (label.Length > 0 && room.Label.AsSpan().Contains(label.AsSpan().Trim("#"), StringComparison.OrdinalIgnoreCase))
      {
        return room;
      }
    }
    return null;
  }

  // The model sometimes writes the opener again before the thought itself.
  private string Unechoed(string continued)
  {
    ReadOnlySpan<char> text = continued.AsSpan().Trim();
    ReadOnlySpan<char> opener = page.Thought.AsSpan();
    while (text.StartsWith(opener, StringComparison.OrdinalIgnoreCase))
    {
      text = text[opener.Length..].TrimStart();
    }
    if (text.Length > 0 && text[0] == opener[0])
    {
      text = text[1..].TrimStart();
    }
    // A thought written as the person's own message line is speech in the wrong place, not a thought.
    string mine = OwnSpeakerPrefix();
    if (mine.Length > 0 && text.StartsWith(mine.AsSpan(), StringComparison.OrdinalIgnoreCase))
    {
      return "";
    }
    // A thought is one paragraph. Lines after it are the model writing the rest of the page (events, other people's
    // messages) that never happened, and must never enter the history.
    int line = text.IndexOf('\n');
    if (line >= 0)
    {
      text = text[..line].TrimEnd();
    }
    return text.ToString();
  }

  private bool AlreadyThought(string text)
  {
    foreach (Entry entry in entries)
    {
      if (entry.Kind == EntryKinds.Thought && entry.Text.AsSpan().Equals(text.AsSpan(), StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }
    return false;
  }

  private bool AlreadySaid(string reply)
  {
    foreach (Entry entry in entries)
    {
      if (entry.Kind == EntryKinds.Said && reply.AsSpan().Equals(entry.Text.AsSpan().Trim(), StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }
    return false;
  }

  // Brackets are the page's thought marks: a thought written before the message is skipped and one after it ends it.
  // A speaker line for another conversation means the message was never meant for this one, so nothing is sent.
  private string Spoken(string continued, string opener)
  {
    ReadOnlySpan<char> reply = continued.AsSpan().Trim();
    char open = page.Thought[0];
    while (reply.Length > 0 && reply[0] == open)
    {
      int closed = reply.IndexOf(page.Text.Close.AsSpan(), StringComparison.Ordinal);
      reply = closed < 0 ? ReadOnlySpan<char>.Empty : reply[(closed + page.Text.Close.Length)..].TrimStart();
    }
    while (reply.StartsWith(opener.AsSpan(), StringComparison.OrdinalIgnoreCase))
    {
      reply = reply[opener.Length..].TrimStart();
    }
    string mine = OwnSpeakerPrefix();
    if (mine.Length > 0 && reply.StartsWith(mine.AsSpan(), StringComparison.OrdinalIgnoreCase))
    {
      misdirected = true;
      return "";
    }
    int thought = reply.IndexOf(open);
    if (thought >= 0)
    {
      reply = reply[..thought];
    }
    // One message is one line: a further line is the page going on to someone else.
    reply = reply.Trim();
    int line = reply.IndexOf('\n');
    if (line >= 0)
    {
      reply = reply[..line];
    }
    return reply.Trim().ToString();
  }

  // "Chloe in " from "{speaker} in {channel}:": how every line the person writes begins, whichever the conversation.
  private string OwnSpeakerPrefix()
  {
    string said = page.Fill(page.Text.Said).Replace("{speaker}", page.Name);
    int channel = said.IndexOf("{channel}", StringComparison.Ordinal);
    return channel <= 0 ? "" : said[..channel];
  }

  private void Snapshot()
  {
    try
    {
      File.WriteAllText(Path.Combine(workspace, "page.txt"), page.Document(summary, entries, body.Around));
    }
    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
    {
      Console.WriteLine("The optional page snapshot could not be written; the history remains saved.");
    }
  }
}
