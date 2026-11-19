using System.Globalization;

namespace Humans;

// One subject is one search of long-term memory, done by pgvector in my own database. What comes back is whatever is
// close enough, never memories whose exchange is still in the conversation or that already came back recently.
public sealed class Mind(Embedder embedder, MemoryStore store, RuntimeOptions options)
{
  private readonly Dictionary<long, int> surfaced = new();
  private int turn;

  public void NextTurn()
  {
    turn++;
  }

  public async Task<List<string>> FindAsync(string subject, Conversation chat, string channel, CancellationToken token)
  {
    List<string> found = new();
    if (string.IsNullOrWhiteSpace(subject))
    {
      return found;
    }
    List<long> skip = new();
    foreach (KeyValuePair<long, int> recalled in surfaced)
    {
      if (turn - recalled.Value <= options.QuietTurns)
      {
        skip.Add(recalled.Key);
      }
    }

    float[] query = await embedder.EmbedAsync(subject, true, token);
    int characters = 0;
    foreach (MemoryMatch match in await store.NearestAsync(query, embedder.Tag, channel, chat.FirstKept, skip, options.PerCue, token))
    {
      Console.WriteLine("Recall candidate " + match.Similarity.ToString("0.000", CultureInfo.InvariantCulture) + ": " + match.Text);
      if (match.Similarity < options.MinSimilarity)
      {
        continue;
      }
      characters += match.Text.Length;
      if (characters > options.MaxRecallCharacters)
      {
        break;
      }
      found.Add(match.Text.Trim());
      surfaced[match.Id] = turn;
    }
    return found;
  }
}
