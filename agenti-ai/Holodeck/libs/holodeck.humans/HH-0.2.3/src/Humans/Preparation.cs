using System.Security.Cryptography;
using System.Text;

namespace Humans;

// At startup the person's self-portrait is prepared from the personality prompt in settings, once. It is kept and reused
// until that personality prompt changes; prompt edits and other settings never trigger it. An incomplete preparation is
// never stored, so it is simply done again on the next start.
public sealed class Preparation(ChatModel model, Store store, Page page, RuntimeOptions options)
{
  private const int MostLives = 16;
  // Variety is the point of this one pass.
  private const double LivesTemperature = 1.0;

  public async Task<string> PortraitAsync(Personality personality, CancellationToken token)
  {
    string description = personality.Description ?? "";
    string source = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(page.Name + "\n" + description)));
    Prepared? kept = await store.PreparedAsync(source, token);
    if (kept is not null)
    {
      Console.WriteLine("The prepared self-portrait matches the personality prompt; reusing it.");
      return kept.Portrait;
    }
    Console.WriteLine("Preparing the self-portrait from the personality prompt.");
    string input = description.Length > 0 ? description : await ChooseLifeAsync(token);
    Completion result = await model.SayAsync([new Message("system", page.Instruction("prepare.txt")), new Message("user", input)], token, null, options.ThoughtTemperature);
    string portrait = result.Text.Trim();
    if (portrait.Length == 0)
    {
      throw new IOException("Preparation returned no portrait; nothing was stored and it will be prepared again.");
    }
    await store.SavePreparedAsync(new Prepared(source, portrait), token);
    Console.WriteLine("The self-portrait was prepared and stored.");
    return portrait;
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
