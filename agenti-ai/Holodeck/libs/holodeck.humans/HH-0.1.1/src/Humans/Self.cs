using System.Text;

namespace Humans;

// Adapted from Person.Self. For each message: remember what is worth remembering, think, then speak. Each step
// continues the same page, and a thought is the model's own length. Memory forms afterwards.
public sealed class Self(ChatModel model, Embedder embedder, MemoryStore store, Page page, RuntimeOptions options, string workspace, int contextSize, Holodeck? transport = null, LivePrompts? prompts = null)
{
  private const int EchoRetries = 2;
  private readonly Dictionary<string, Conversation> conversations = new();
  private readonly Dictionary<string, Mind> minds = new();
  private readonly Hearing hearing = new(model, page, options);

  public async Task<ResponseResult?> SayAsync(Delivery packet, CancellationToken token)
  {
    if (prompts is not null)
    {
      page.Use(await prompts.ReadAsync(token));
      embedder.UseQueryInstruction(page.QueryInstruction);
      LivePrompts.Record(workspace, "last-prompts.json", page.Live!);
    }
    if (!conversations.TryGetValue(packet.ChannelId, out Conversation? chat))
    {
      chat = new Conversation(options);
      chat.Restore(await store.SessionAsync(packet.ChannelId, token));
      conversations.Add(packet.ChannelId, chat);
      minds.Add(packet.ChannelId, new Mind(embedder, store, options));
    }
    await using ActivityReporter? activity = transport is null ? null : new ActivityReporter(transport, packet);
    try
    {
      return await RespondAsync(packet, chat, activity, token);
    }
    catch
    {
      activity?.Finish(false);
      throw;
    }
  }

  private async Task<ResponseResult?> RespondAsync(Delivery packet, Conversation chat, ActivityReporter? activity, CancellationToken token)
  {
    PageText text = page.Text;
    string input = packet.Note;
    int position = chat.Position;
    string earlier = page.Lines(chat.Turns, Math.Max(0, chat.Turns.Count - options.RecentTurns));
    string document = page.Document(chat, input);
    // No space after an opener: Qwen puts a separate space token before digits, so a page ending on a space makes thoughts
    // and replies start with numbers ("5pm?") whether or not one was on their mind.
    string opener = page.Fill(text.Thought);
    string openerAfterRecall = page.Fill(text.ThoughtAfterRecall);
    // A blank line means the model has left the page (it once wrote an exam question about the chat), so it ends a thought.
    List<string> stop = [text.Close, "\n" + text.Other + ":", "\n\n"];
    Action<string>? onText = activity is null ? null : activity.Append;
    Mind memory = minds[packet.ChannelId];
    memory.NextTurn();
    // Before I think: who this is, if I know them, and my remembering of whatever is worth remembering.
    string recollection = await HearAsync(packet, chat, memory, activity, token);
    string written = document + recollection;
    activity?.Begin("think", "Thinking");
    string firstOpener = recollection.Length > 0 ? openerAfterRecall : opener;
    Completion first = await model.ContinueAsync(written + firstOpener, page.Instruction("continue"), stop, options.ThoughtTokens, token, onText);
    token.ThrowIfCancellationRequested();
    activity?.End();
    string mind = first.Text.Trim();
    written += firstOpener + " " + mind + text.Close + "\n";
    string speaking = written + page.Name + ":";
    chat.AddUser(input);
    Completion completion;
    string reply;
    try
    {
      activity?.Begin("reply", "Writing", true);
      // A chat message ends where the next line of the page begins: their turn, a thought, or a blank line.
      List<string> end = ["\n" + text.Other + ":", "\n[", "\n\n"];
      completion = await model.ContinueAsync(speaking, page.Instruction("continue"), end, 0, token, onText, options.SpeechTemperature);
      reply = Spoken(completion.Text, text);
      // Copying a line already on the page is its easiest continuation, not a reply; it gets a few more tries.
      for (int retry = 0; retry < EchoRetries && AlreadySaid(reply, chat.Turns); retry++)
      {
        Console.WriteLine("The reply repeated a line already in the chat; speaking again.");
        completion = await model.ContinueAsync(speaking, page.Instruction("continue"), end, 0, token, onText, options.SpeechTemperature);
        reply = Spoken(completion.Text, text);
      }
      token.ThrowIfCancellationRequested();
      activity?.End();
      if (reply.Length == 0)
      {
        chat.DropUser();
        await store.HeardWithoutReplyAsync(packet.PacketId, token);
        activity?.Finish(true);
        return null;
      }
      await store.SaveExchangeAsync(packet, position, input, reply, mind, recollection, earlier, page.Exchange(input, mind, reply), token);
    }
    catch
    {
      chat.DropUser();
      throw;
    }
    chat.AddReply(reply, mind, recollection);
    try
    {
      File.WriteAllText(Path.Combine(workspace, "last-page.txt"), speaking + " " + reply + "\n");
    }
    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
    {
      Console.WriteLine("The optional page snapshot could not be written; the conversation remains saved.");
    }
    activity?.Finish(true);
    return new ResponseResult(packet.ChannelId, completion.Usage);
  }

  // Returns my remembering, one page line for each thing that brought something back.
  private async Task<string> HearAsync(Delivery packet, Conversation chat, Mind memory, ActivityReporter? activity, CancellationToken token)
  {
    PageText text = page.Text;
    Action<string>? onText = activity is null ? null : activity.Append;
    StringBuilder lines = new();
    await RecogniseAsync(packet, chat, lines, activity, token);
    activity?.Begin("look", "What to remember");
    List<string> lookups = await hearing.WorthRememberingAsync(hearing.Context(chat.Turns, packet.Note), token, onText);
    activity?.End();
    foreach (string lookup in lookups)
    {
      List<string> found = await memory.FindAsync(lookup, chat, packet.ChannelId, token);
      Console.WriteLine("Looked for \"" + lookup + "\": " + found.Count + " came back.");
      if (found.Count == 0)
      {
        continue;
      }
      activity?.Begin("remember", "Remembering");
      string remembering = await hearing.RememberAsync(lookup, found, token, onText);
      activity?.End();
      if (remembering.Length > 0)
      {
        lines.Append(page.Fill(text.Remember)).Append(' ').Append(remembering).Append(text.Close).Append('\n');
      }
    }
    return lines.ToString();
  }

  // At the start of a chat I see who it is; if I have talked with them before, I remember them before anything else.
  private async Task RecogniseAsync(Delivery packet, Conversation chat, StringBuilder lines, ActivityReporter? activity, CancellationToken token)
  {
    bool starting = chat.Turns.Count == 0 && chat.Summary.Length == 0;
    if (!starting || options.KnownMemories == 0)
    {
      return;
    }
    List<string> known = await store.KnownByAsync(packet.SenderId, packet.ChannelId, options.KnownMemories, token);
    if (known.Count == 0)
    {
      return;
    }
    activity?.Begin("recognise", "Recognising them");
    string remembering = await hearing.RecogniseAsync(known, token, activity is null ? null : activity.Append);
    activity?.End();
    if (remembering.Length > 0)
    {
      PageText text = page.Text;
      lines.Append(page.Fill(text.Recognise)).Append(' ').Append(remembering).Append(text.Close).Append('\n');
    }
  }

  private static bool AlreadySaid(string reply, IReadOnlyList<Turn> turns)
  {
    foreach (Turn turn in turns)
    {
      if (reply.AsSpan().Equals(turn.Text.AsSpan().Trim(), StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }
    return false;
  }

  // Brackets are the page's thought marks, so a message ends where one opens. A thought written before the message is
  // skipped, and the model writing a second message line ends the first.
  private string Spoken(string continued, PageText text)
  {
    ReadOnlySpan<char> reply = continued.AsSpan().Trim();
    string opening = page.Fill(text.Thought);
    if (reply.StartsWith(opening[0]))
    {
      int closed = reply.IndexOf(text.Close.AsSpan(), StringComparison.Ordinal);
      reply = closed < 0 ? ReadOnlySpan<char>.Empty : reply[(closed + text.Close.Length)..].TrimStart();
      if (reply.StartsWith((page.Name + ":").AsSpan(), StringComparison.Ordinal))
      {
        reply = reply[(page.Name.Length + 1)..];
      }
    }
    int thought = reply.IndexOf(opening[0]);
    if (thought >= 0)
    {
      reply = reply[..thought];
    }
    int again = reply.IndexOf(("\n" + page.Name + ":").AsSpan(), StringComparison.Ordinal);
    if (again >= 0)
    {
      reply = reply[..again];
    }
    return reply.Trim().ToString();
  }

  public async Task CompactIfFullAsync(ResponseResult result, CancellationToken token)
  {
    if ((result.Usage.PromptTokens + (long)result.Usage.CompletionTokens) * 100 < contextSize * (long)options.CompactPercent)
    {
      return;
    }
    Conversation chat = conversations[result.Channel];
    if (await chat.CompactAsync(model, page.Instruction("compact"), options.UtilityTemperature, token))
    {
      await store.SaveSummaryAsync(result.Channel, chat, token);
    }
  }
}
