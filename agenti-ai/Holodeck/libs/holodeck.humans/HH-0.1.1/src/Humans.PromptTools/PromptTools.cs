using System.ComponentModel;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Humans.PromptTools;

[McpServerToolType]
public sealed class PromptTools(PromptStore store)
{
  // Holodeck's statement limit plus the trip.
  public const int RequestSeconds = 45;
  private const int FirstLineCharacters = 100;
  private const string About = "Humans people run the live prompts of their HH version, shared by every person of that version. An edit is live from each person's next message or memory. Publishing keeps a numbered, immutable copy.";
  private const string Version = "HH version, for example HH-0.1.1. list_versions shows them.";

  [McpServerTool(Name = "list_versions", ReadOnly = true), Description(About + " Lists every HH version with prompts, its live revision and whether it has unpublished changes.")]
  public Task<string> ListVersionsAsync(CancellationToken token)
  {
    return RunAsync(async () =>
    {
      StringBuilder text = new();
      foreach (PromptLive live in await store.VersionsAsync(token))
      {
        text.Append(live.Version).Append("  revision ").Append(live.Revision).Append("  ");
        AppendState(text, live);
        text.Append('\n');
      }
      return text.Length == 0 ? "No HH version has prompts yet." : text.ToString();
    });
  }

  [McpServerTool(Name = "list_prompts", ReadOnly = true), Description(About + " Lists the live prompts of one HH version with their first lines.")]
  public Task<string> ListPromptsAsync([Description(Version)] string version, CancellationToken token)
  {
    return RunAsync(async () =>
    {
      PromptLive live = await store.RequireLiveAsync(version, token);
      StringBuilder text = new();
      text.Append(live.Version).Append(" live revision ").Append(live.Revision).Append(", ");
      AppendState(text, live);
      text.Append("\n\n");
      List<string> names = new(live.Content.Files.Keys);
      names.Sort(StringComparer.Ordinal);
      foreach (string name in names)
      {
        text.Append(name).Append("  ");
        AppendFirstLine(text, live.Content.Files[name]);
        text.Append('\n');
      }
      return text.ToString();
    });
  }

  [McpServerTool(Name = "get_prompt", ReadOnly = true), Description("Returns the full text of one prompt: the live text, or the text in a given publication.")]
  public Task<string> GetPromptAsync([Description(Version)] string version, [Description("Prompt name, for example continue.txt or page.json")] string name, [Description("Publication number; omit for the live text")] int? publication = null, CancellationToken token = default)
  {
    return RunAsync(async () =>
    {
      if (publication is null)
      {
        PromptLive live = await store.RequireLiveAsync(version, token);
        return Header(live.Version + " " + name + " live revision " + live.Revision, live.Content, name);
      }
      PromptPublication published = await store.PublicationAsync(version, publication.Value, token);
      return Header(version + " " + name + " from v" + published.Number + " (" + published.Label + ")", published.Content, name);
    });
  }

  [McpServerTool(Name = "edit_prompt"), Description("Replaces the live text of one prompt. Every person of this HH version uses it from their next message or memory. Send the complete text, not a diff. page.json must stay valid page labels.")]
  public Task<string> EditPromptAsync([Description(Version)] string version, [Description("Prompt name, for example continue.txt")] string name, [Description("The complete new text")] string text, CancellationToken token)
  {
    return RunAsync(async () =>
    {
      long revision = await store.EditAsync(version, name, text, token);
      return version + " " + name + " is live as revision " + revision + ". Publish when it works.";
    });
  }

  [McpServerTool(Name = "publish_prompts"), Description("Publishes the live prompts of an HH version as the next numbered, immutable publication.")]
  public Task<string> PublishPromptsAsync([Description(Version)] string version, [Description("What this publication is, one line")] string label, CancellationToken token)
  {
    return RunAsync(async () =>
    {
      int number = await store.PublishAsync(version, label, token);
      return version + " prompts published as v" + number + ".";
    });
  }

  [McpServerTool(Name = "list_publications", ReadOnly = true), Description("Lists the publications of an HH version, newest first.")]
  public Task<string> ListPublicationsAsync([Description(Version)] string version, CancellationToken token)
  {
    return RunAsync(async () =>
    {
      StringBuilder text = new();
      foreach (PromptPublication publication in await store.PublicationsAsync(version, token))
      {
        text.Append('v').Append(publication.Number).Append("  ").Append(publication.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append("  ").Append(publication.Label).Append('\n');
      }
      return text.Length == 0 ? version + " has no publications." : text.ToString();
    });
  }

  [McpServerTool(Name = "restore_publication"), Description("Makes a publication's prompts live again for an HH version, replacing unpublished edits. Publications are never deleted.")]
  public Task<string> RestorePublicationAsync([Description(Version)] string version, [Description("Publication number to make live")] int publication, CancellationToken token)
  {
    return RunAsync(async () =>
    {
      long revision = await store.RestoreAsync(version, publication, token);
      return version + " now runs v" + publication + " as live revision " + revision + ".";
    });
  }

  [McpServerTool(Name = "start_version"), Description("Gives a new HH version its prompts, copied from the latest publication of the version it grew from.")]
  public Task<string> StartVersionAsync([Description("The new HH version, for example HH-0.1.2")] string version, [Description("The HH version to copy from")] string from, CancellationToken token)
  {
    return RunAsync(async () =>
    {
      await store.StartFromAsync(version, from, token);
      return version + " starts with " + from + "'s latest published prompts as its v1.";
    });
  }

  private static async Task<string> RunAsync(Func<Task<string>> operation)
  {
    try
    {
      return await operation();
    }
    catch (Exception exception) when (exception is ArgumentException || exception is KeyNotFoundException || exception is InvalidOperationException || exception is IOException || exception is HttpRequestException)
    {
      throw new McpException(exception.Message, exception);
    }
  }

  private static void AppendState(StringBuilder text, PromptLive live)
  {
    if (live.Publication is not null && live.Publication == live.Latest)
    {
      text.Append("same as latest publication v").Append(live.Latest);
    }
    else if (live.Publication is not null)
    {
      text.Append("same as v").Append(live.Publication).Append(", latest is v").Append(live.Latest);
    }
    else
    {
      text.Append("unpublished changes since v").Append(live.Latest);
    }
  }

  private static string Header(string title, PromptBundle content, string name)
  {
    return title + "\n\n" + content.Get(name);
  }

  private static void AppendFirstLine(StringBuilder text, string body)
  {
    ReadOnlySpan<char> line = body.AsSpan().TrimStart();
    int newline = line.IndexOf('\n');
    if (newline >= 0)
    {
      line = line[..newline];
    }
    if (line.Length > FirstLineCharacters)
    {
      text.Append(line[..FirstLineCharacters]).Append("...");
      return;
    }
    text.Append(line);
  }
}
