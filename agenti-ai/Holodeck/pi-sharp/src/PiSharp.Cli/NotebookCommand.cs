using System.Text.Json.Nodes;
using PiSharp.Core.Config;
using PiSharp.Core.Notebook;

namespace PiSharp.Cli;

/// <summary><c>pi-sharp notebook import &lt;dir&gt;</c>: a one-off move of a seat's work/*.md files into its notebook
/// (see <see cref="NotebookImport"/>). It opens the notebook as its writer, so the seat must be stopped first.</summary>
public static class NotebookCommand
{
    public const string Usage = "usage: pi-sharp notebook import <dir> [--notebook <notebook directory>] [--config <config dir>]\n" +
        "  Imports every *.md under <dir> as a note keyed file:<path relative to the current directory>. Idempotent.\n" +
        "  The notebook defaults to settings.json notebookDirectory; stop the seat first (the notebook has one writer).";

    public static bool Handles(IReadOnlyList<string> argv) => argv.Count > 0 && argv[0] == "notebook";

    public static int Run(IReadOnlyList<string> argv, TextWriter output, TextWriter error, string? cwd = null)
    {
        cwd ??= Directory.GetCurrentDirectory();
        if (argv.Count < 2 || argv[1] != "import") { error.WriteLine(Usage); return 2; }
        string? dir = null, notebookDir = null, configDir = null;
        for (var i = 2; i < argv.Count; i++)
        {
            switch (argv[i])
            {
                case "--notebook" when i + 1 < argv.Count: notebookDir = argv[++i]; break;
                case "--config" when i + 1 < argv.Count: configDir = argv[++i]; break;
                case "-h" or "--help": output.WriteLine(Usage); return 0;
                default:
                    if (dir is null && !argv[i].StartsWith("--", StringComparison.Ordinal)) { dir = argv[i]; break; }
                    error.WriteLine($"unexpected argument: {argv[i]}\n{Usage}");
                    return 2;
            }
        }
        if (dir is null) { error.WriteLine(Usage); return 2; }
        var settings = SettingsLoader.Load(cwd, configDir);
        notebookDir ??= settings.NotebookDirectory;
        if (string.IsNullOrWhiteSpace(notebookDir)) { error.WriteLine("No notebook: pass --notebook <dir> or set notebookDirectory in settings.json."); return 2; }
        notebookDir = Path.GetFullPath(ConfigPaths.ExpandHome(notebookDir), cwd);
        var source = Path.GetFullPath(dir, cwd);
        var owner = JournalOwner(notebookDir) ?? settings.Factory?.Actor ?? "local";
        NotebookStore store;
        try { store = new NotebookStore(notebookDir, owner); }
        catch (IOException ex) { error.WriteLine($"The notebook at {notebookDir} is open by a running seat ({ex.Message}); stop the seat and run the import again."); return 1; }
        catch (InvalidDataException ex) { error.WriteLine($"The notebook at {notebookDir} cannot be opened as {owner}: {ex.Message}"); return 1; }
        using (store)
        {
            NotebookImport.Result result;
            try { result = NotebookImport.Run(store, source, cwd, line => error.WriteLine(line)); }
            catch (DirectoryNotFoundException ex) { error.WriteLine(ex.Message); return 2; }
            output.WriteLine($"notebook import: {result.Imported} new, {result.Updated} updated, {result.Unchanged} unchanged, {result.Skipped.Count} skipped " +
                $"({store.PendingCount} operations pending sync; the seat syncs them at its next start).");
            return result.Skipped.Count == 0 ? 0 : 1;
        }
    }

    /// <summary>The owner written in the journal's first row: the import must write as the seat that owns it.</summary>
    private static string? JournalOwner(string notebookDir)
    {
        var journal = Path.Combine(notebookDir, "notebook.jsonl");
        if (!File.Exists(journal)) return null;
        using var reader = new StreamReader(new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        var first = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(first)) return null;
        try { return (JsonNode.Parse(first) as JsonObject)?["owner"]?.GetValue<string>(); }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
