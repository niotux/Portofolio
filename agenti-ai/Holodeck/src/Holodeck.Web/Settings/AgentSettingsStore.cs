using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Holodeck.Web.Projects;
using Holodeck.Web.Computers;
using Holodeck.Web.Rooms;

namespace Holodeck.Web.Settings;

// Reads and writes each agent's settings file. With a schema from the agent's definition, saves are validated here
// and form changes touch only their own fields. Without one (or while the definition is unavailable) the file is
// opaque text. Conditional writes compare revisions under one lock, so a stale editor never replaces newer content.
public sealed class AgentSettingsStore(RoomPaths paths, AgentLibrary library, ProjectLibraryStore projectLibraries, LibraryAgentRuntime? shared = null)
{
  public const string FormMode = "form";
  public const string RawMode = "raw";
  private readonly SemaphoreSlim writes = new(1, 1);

  public async Task<SettingsView> ReadAsync(RoomMember member)
  {
    await writes.WaitAsync();
    try
    {
      SettingsSource source = await FindSourceAsync(member);
      return View(member, source, await ReadBytesAsync(member));
    }
    finally
    {
      writes.Release();
    }
  }

  // The original text endpoint: an unconditional replace, still validated when the definition has a schema.
  public async Task<SettingsWriteResult> ReplaceAsync(RoomMember member, string content)
  {
    await writes.WaitAsync();
    try
    {
      SettingsSource source = await FindSourceAsync(member);
      SettingsView current = View(member, source, await ReadBytesAsync(member));
      return await SaveAsync(member, source, current, new SettingsEdit(content, new List<SettingsFieldError>()));
    }
    finally
    {
      writes.Release();
    }
  }

  public async Task<SettingsWriteResult> WriteAsync(RoomMember member, SettingsWrite input)
  {
    await writes.WaitAsync();
    try
    {
      SettingsSource source = await FindSourceAsync(member);
      SettingsView current = View(member, source, await ReadBytesAsync(member));
      string? conflict = FindConflict(current, input);
      if (conflict is not null)
      {
        return new SettingsWriteResult(SettingsWriteStatus.Conflict, current, conflict, new List<SettingsFieldError>());
      }

      return await SaveAsync(member, source, current, Edit(source, current, input));
    }
    finally
    {
      writes.Release();
    }
  }

  private async Task<SettingsWriteResult> SaveAsync(RoomMember member, SettingsSource source, SettingsView current, SettingsEdit edit)
  {
    List<SettingsFieldError> errors = edit.Errors;
    if (edit.Text is not null && source.Schema is not null)
    {
      errors = ValidateText(source.Schema, edit.Text);
    }

    if (edit.Text is null || errors.Count > 0)
    {
      return new SettingsWriteResult(SettingsWriteStatus.Invalid, current, "Some settings are not valid. Nothing was saved.", errors);
    }

    byte[] bytes = Encoding.UTF8.GetBytes(edit.Text);
    if (bytes.Length > RoomLimits.MaxSettingsBytes)
    {
      throw new ArgumentException($"Keep settings under {RoomLimits.MaxSettingsBytes / 1024} KB.");
    }

    // Written in place: the running agent sees its settings through a mount of this one file, which a rename would detach.
    await File.WriteAllBytesAsync(paths.SettingsFile(member.ProjectId, member.Id), bytes);
    if (shared is not null)
    {
      await shared.RefreshSettingsAsync(member, edit.Text, CancellationToken.None);
    }
    return new SettingsWriteResult(SettingsWriteStatus.Saved, View(member, source, bytes), "Saved.", new List<SettingsFieldError>());
  }

  private static string? FindConflict(SettingsView current, SettingsWrite input)
  {
    if (input.BaseRevision != current.Revision)
    {
      return "These settings were changed since you opened them. Nothing was saved.";
    }

    if (input.SchemaRevision != current.SchemaRevision)
    {
      return "This agent's settings form changed since you opened it. Review your values against the new form. Nothing was saved.";
    }

    return null;
  }

  private static SettingsEdit Edit(SettingsSource source, SettingsView current, SettingsWrite input)
  {
    bool hasContent = input.Content is not null;
    bool hasChanges = input.Changes is not null;
    if (hasContent == hasChanges)
    {
      throw new ArgumentException("Send either the settings content or a list of changes.");
    }

    if (hasContent)
    {
      return new SettingsEdit(input.Content, new List<SettingsFieldError>());
    }

    if (source.Schema is null)
    {
      throw new ArgumentException("This agent has no settings form. Save its settings as text.");
    }

    if (current.NeedsRepair)
    {
      List<SettingsFieldError> errors = [new SettingsFieldError("", "Repair the settings text before using the form.")];
      return new SettingsEdit(null, errors);
    }

    return SettingsChanges.Apply(source.Schema.Root, (JsonObject)SettingsDocument.Parse(current.Content)!, input.Changes!);
  }

  private static List<SettingsFieldError> ValidateText(SettingsSchema schema, string text)
  {
    try
    {
      return SettingsValidator.Validate(schema.Root, SettingsDocument.Parse(text));
    }
    catch (JsonException exception)
    {
      return [new SettingsFieldError("", $"This is not valid JSON: {exception.Message}")];
    }
  }

  private async Task<SettingsSource> FindSourceAsync(RoomMember member)
  {
    if (member.Library is null)
    {
      return new SettingsSource(null, null);
    }

    string? error = await projectLibraries.ErrorForAsync(member.ProjectId, member.Library);
    if (error is not null)
    {
      return new SettingsSource(null, $"{error} Its stored settings are shown as text. Restore the definition to edit them with its form and validation.");
    }

    return new SettingsSource(library.Get(member.Library).SettingsSchema, null);
  }

  private async Task<byte[]> ReadBytesAsync(RoomMember member)
  {
    string file = paths.SettingsFile(member.ProjectId, member.Id);
    if (!File.Exists(file))
    {
      return [];
    }

    return await File.ReadAllBytesAsync(file);
  }

  private static SettingsView View(RoomMember member, SettingsSource source, byte[] bytes)
  {
    string content = Encoding.UTF8.GetString(bytes);
    string revision = Convert.ToHexStringLower(SHA256.HashData(bytes));
    if (source.Schema is null)
    {
      return new SettingsView(RawMode, member.Library, content, revision, null, null, source.Notice, false, new List<SettingsFieldError>());
    }

    List<SettingsFieldError> errors = ValidateText(source.Schema, content);
    bool needsRepair = !IsObject(content);
    return new SettingsView(FormMode, member.Library, content, revision, source.Schema.Root, source.Schema.Revision, null, needsRepair, errors);
  }

  private static bool IsObject(string content)
  {
    try
    {
      return SettingsDocument.Parse(content) is JsonObject;
    }
    catch (JsonException)
    {
      return false;
    }
  }
}
