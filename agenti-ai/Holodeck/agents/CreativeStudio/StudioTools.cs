using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CreativeStudio;

public sealed class StudioTools(CreativeStore store, EmbeddingClient embedding, WorkflowCatalog workflows, WorkspaceFiles files, ReferencePreview previews, RuntimeOptions options)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task ApplyAsync(ToolCall call, StudioTurn turn, CancellationToken token)
  {
    switch (call.Name)
    {
      case "search_notebook":
        await SearchAsync(Read<NotebookSearchInput>(call), turn, token);
        break;
      case "open_reference":
        await OpenAsync(Read<OpenReferenceInput>(call), turn, token);
        break;
      case "remember":
        await RememberAsync(Read<RememberInput>(call), turn, token);
        break;
      case "generate_image":
        Prepare(Read<GenerateImageInput>(call), turn);
        break;
      default:
        throw new ArgumentException("That instrument is not available.");
    }
  }

  private async Task SearchAsync(NotebookSearchInput input, StudioTurn turn, CancellationToken token)
  {
    RequireText(input.Query, options.MaxNotebookCharacters);
    float[] vector = await embedding.EmbedAsync(input.Query, token);
    List<NotebookNote> notes = await store.SearchNotesAsync(turn.Receipt.ChannelId, vector, embedding.Tag, options.SearchResults, token);
    StringBuilder text = new("I have searched the notebook for " + input.Query + ". These are the matching retained entries:\n");
    foreach (NotebookNote note in notes)
    {
      text.Append("\nNotebook ").Append(turn.AddEntry(note.Id)).Append(": ").Append(note.Title).Append("\n").Append(note.Prose);
      foreach (string reference in note.Files)
      {
        text.Append("\nLinked file ").Append(turn.AddReference(reference)).Append(": ").Append(Path.GetFileName(reference));
      }
    }

    AddExperience(turn, notes.Count == 0 ? "No notebook entries matched that descriptive search." : text.ToString());
  }

  private async Task OpenAsync(OpenReferenceInput input, StudioTurn turn, CancellationToken token)
  {
    string relative = turn.Reference(input.Reference);
    string path = files.Resolve(relative);
    turn.Experiences.RemoveAll(message => message.ImageCount > 0);
    if (WorkspaceFiles.IsImage(path))
    {
      string preview = await previews.CreateAsync(relative, token);
      turn.Experiences.Add(ChatMessage.ImageUser("I am looking at the selected image, " + Path.GetFileName(relative) + ".", preview, options.Context.MaxImageBytes));
    }
    else if (WorkspaceFiles.IsText(path))
    {
      using StreamReader reader = new(path);
      char[] buffer = new char[options.MaxToolResultCharacters];
      int length = await reader.ReadAsync(buffer.AsMemory(), token);
      AddExperience(turn, "Selected file excerpt from " + Path.GetFileName(relative) + ":\n" + new string(buffer, 0, length));
    }
    else
    {
      throw new ArgumentException("This file is retained, but visual inspection currently supports images and bounded text excerpts.");
    }

    if (!turn.InspectedFiles.Contains(relative))
    {
      turn.InspectedFiles.Add(relative);
    }
  }

  private async Task RememberAsync(RememberInput input, StudioTurn turn, CancellationToken token)
  {
    RequireText(input.Title, options.MaxNotebookCharacters);
    RequireText(input.Text, options.MaxNotebookCharacters);
    List<string> references = new();
    if (input.References is not null)
    {
      foreach (string handle in input.References)
      {
        string path = turn.Reference(handle);
        files.Resolve(path);
        if (!references.Contains(path))
        {
          references.Add(path);
        }
      }
    }

    string id = string.IsNullOrWhiteSpace(input.Entry) ? StableIdentity.For(turn.Receipt.ChannelId + ":note:" + input.Title.Trim().ToLowerInvariant()) : turn.Entry(input.Entry);
    if (input.References is null)
    {
      NotebookNote? existing = await store.GetNoteAsync(turn.Receipt.ChannelId, id, token);
      if (existing is not null)
      {
        foreach (string reference in existing.Files)
        {
          references.Add(reference);
        }
      }
    }

    float[] vector = await embedding.EmbedAsync(input.Title + "\n" + input.Text, token);
    NotebookWrite write = new(turn.Receipt.ChannelId, input.Title, input.Text, references, vector, embedding.Tag, turn.Receipt.PacketId, id);
    NotebookNote saved = await store.SaveNoteAsync(write, token);
    turn.AddEntry(saved.Id);
    AddExperience(turn, "I kept this notebook entry, " + saved.Title + ":\n" + saved.Prose);
  }

  private void Prepare(GenerateImageInput input, StudioTurn turn)
  {
    if (turn.Generation is not null)
    {
      throw new ArgumentException("One image request is already prepared for this message.");
    }

    RequireText(input.Prompt, options.MaxPromptCharacters);
    WorkflowDefinition workflow = workflows.Load(input.Workflow);
    long seed = input.Seed ?? RandomNumberGenerator.GetInt32(int.MaxValue);
    GenerationInput controls = new(input.Prompt, input.NegativePrompt ?? "", input.Width ?? workflow.DefaultWidth, input.Height ?? workflow.DefaultHeight, seed);
    string id = StableIdentity.For("generation:" + turn.Receipt.ChannelId + ":" + turn.Receipt.PacketId);
    turn.Generation = workflows.Compile(workflow, controls, id);
    AddExperience(turn, "I prepared an image using " + workflow.Name + ". It has not rendered yet. Its visual prompt is:\n" + input.Prompt);
  }

  private void AddExperience(StudioTurn turn, string text)
  {
    if (text.Length > options.MaxToolResultCharacters)
    {
      text = text[..options.MaxToolResultCharacters] + "\nThe retained entry is longer; this is the selected excerpt.";
    }

    turn.Experiences.Add(ChatMessage.Text("user", text));
  }

  private static T Read<T>(ToolCall call)
  {
    return call.Arguments.Deserialize<T>(Json) ?? throw new ArgumentException("The instrument received no controls.");
  }

  private static void RequireText(string? value, int maximum)
  {
    if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
    {
      throw new ArgumentException("Supply nonempty prose within the configured working allowance.");
    }
  }
}
