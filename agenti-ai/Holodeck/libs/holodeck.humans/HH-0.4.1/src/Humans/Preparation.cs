using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Humans;

// At startup the person's self-portrait is prepared from the personality prompt in settings, once. It is kept and reused
// until that personality prompt changes; prompt edits and other settings never trigger it. An incomplete preparation is
// never stored, so it is simply done again on the next start.
public sealed class Preparation(ChatModel model, Store store, Page page, RuntimeOptions options)
{
  private const int MostLives = 16;
  private const string SetClock = "set_clock";
  // Variety is the point of this one pass.
  private const double LivesTemperature = 1.0;

  public async Task<Prepared> PortraitAsync(Personality personality, CancellationToken token)
  {
    string description = personality.Description ?? "";
    string source = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(page.Name + "\n" + description)));
    Prepared? kept = await store.PreparedAsync(source, token);
    if (kept is not null)
    {
      Console.WriteLine("The prepared self-portrait matches the personality prompt; reusing it.");
      return kept;
    }
    Console.WriteLine("Preparing the self-portrait from the personality prompt.");
    string input = description.Length > 0 ? description : await ChooseLifeAsync(token);
    Completion result = await model.SayAsync([new Message("system", page.Instruction("prepare.txt")), new Message("user", input)], token, null, options.ThoughtTemperature);
    string portrait = result.Text.Trim();
    if (portrait.Length == 0)
    {
      throw new IOException("Preparation returned no portrait; nothing was stored and it will be prepared again.");
    }
    string zone = await ClockAsync(portrait, token);
    Prepared prepared = new(source, portrait, zone);
    await store.SavePreparedAsync(prepared, token);
    Console.WriteLine("The self-portrait was prepared and stored; the phone clock is set to " + (zone.Length > 0 ? zone : "the configured time zone") + ".");
    return prepared;
  }

  // The phone's clock is set where the person's life is, through a control like the hands use: code checks the zone
  // exists, and the configured one stays when it does not.
  private async Task<string> ClockAsync(string portrait, CancellationToken token)
  {
    const string Schema = """{"type":"object","properties":{"zone":{"type":"string","description":"IANA time zone, for example Europe/Lisbon"}},"required":["zone"]}""";
    using JsonDocument parameters = JsonDocument.Parse(Schema);
    List<Tool> tools = [new Tool("function", new ToolFunction(SetClock, page.Fill(page.Text.Clock), parameters.RootElement.Clone()))];
    ToolCall? call = await model.ChooseAsync([new Message("system", page.Instruction("clock.txt")), new Message("user", portrait)], tools, 0, token);
    if (call is null)
    {
      return "";
    }
    try
    {
      using JsonDocument arguments = JsonDocument.Parse(call.Function.Arguments);
      if (arguments.RootElement.TryGetProperty("zone", out JsonElement value) && value.ValueKind == JsonValueKind.String && TimeZoneInfo.TryFindSystemTimeZoneById(value.GetString()!, out TimeZoneInfo? found))
      {
        return found.Id;
      }
    }
    catch (JsonException)
    {
    }
    Console.WriteLine("The phone clock could not be set from the portrait; it keeps the configured time zone.");
    return "";
  }

  // With nothing given, one model asked for one life always writes the same person. It is asked for many different
  // lives instead, and one is drawn at random: chance, not a judgement about who the person should be.
  private async Task<string> ChooseLifeAsync(CancellationToken token)
  {
    string given = page.Fill(page.Text.Unprepared);
    Completion result = await model.SayAsync([new Message("system", page.Instruction("lives.txt")), new Message("user", given)], token, null, LivesTemperature);
    List<string> lives = Recall.Lines(result.Text, MostLives);
    if (lives.Count == 0)
    {
      return given;
    }
    string life = lives[Random.Shared.Next(lives.Count)];
    Console.WriteLine("Drawn from " + lives.Count + " possible lives: " + life);
    return life;
  }
}
