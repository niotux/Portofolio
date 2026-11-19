using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CreativeStudio;

// Only authored graph inputs are exposed. The model never edits ComfyUI graph structure.
public sealed class WorkflowCatalog
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private readonly string root;
  private readonly List<WorkflowDefinition> workflows = new();
  private readonly Dictionary<string, string> directories = new(StringComparer.Ordinal);

  public WorkflowCatalog(string libraryPath)
  {
    root = Path.GetFullPath(Path.Combine(libraryPath, "workflows"));
    foreach (string directory in Directory.EnumerateDirectories(root))
    {
      if (new DirectoryInfo(directory).LinkTarget is not null)
      {
        throw new IOException("Workflow definitions must not be symbolic links.");
      }
      string manifest = Path.Combine(directory, "Workflow.json");
      if (!File.Exists(manifest))
      {
        continue;
      }

      if (new FileInfo(manifest).LinkTarget is not null)
      {
        throw new IOException("Workflow manifests must not be symbolic links.");
      }

      WorkflowDefinition workflow = JsonSerializer.Deserialize<WorkflowDefinition>(File.ReadAllText(manifest), Json) ?? throw new IOException("A workflow manifest is empty.");
      Validate(workflow);
      if (!directories.TryAdd(workflow.Id, directory))
      {
        throw new IOException("Two workflows declare the same identity.");
      }

      workflows.Add(workflow);
    }
  }

  public IReadOnlyList<WorkflowDefinition> List() => workflows;

  public WorkflowDefinition Load(string id)
  {
    foreach (WorkflowDefinition workflow in workflows)
    {
      if (workflow.Id == id)
      {
        return workflow;
      }
    }

    throw new ArgumentException("That workflow is not installed in this library.");
  }

  public GenerationPlan Compile(WorkflowDefinition workflow, GenerationInput input, string promptId)
  {
    RequirePromptId(promptId);
    ValidateInput(workflow, input);
    string directory = directories[workflow.Id];
    string graphPath = ResolveGraph(directory, workflow.GraphFile);
    string source = File.ReadAllText(graphPath);
    JsonObject graph = JsonNode.Parse(source)?.AsObject() ?? throw new IOException("The workflow graph is empty.");
    Inputs(graph, workflow.Bindings.Prompt)["text"] = input.Prompt;
    if (workflow.Bindings.NegativePrompt is not null)
    {
      Inputs(graph, workflow.Bindings.NegativePrompt)["text"] = input.NegativePrompt;
    }

    Inputs(graph, workflow.Bindings.Size)["width"] = input.Width;
    Inputs(graph, workflow.Bindings.Size)["height"] = input.Height;
    Inputs(graph, workflow.Bindings.Sampler)["seed"] = input.Seed;
    Inputs(graph, workflow.Bindings.Output)["filename_prefix"] = "creative/" + promptId;
    string revision = Hash(JsonSerializer.Serialize(workflow, Json) + "\n" + source);
    return new GenerationPlan(promptId, workflow.Id, revision, WorkflowHash.Compute(graph), input, graph, workflow.Artifacts);
  }

  public static void RequirePromptId(string promptId)
  {
    if (!Guid.TryParseExact(promptId, "D", out Guid parsed) || parsed.ToString("D") != promptId)
    {
      throw new ArgumentException("The generation identity must be a canonical UUID.");
    }
  }

  private static void Validate(WorkflowDefinition workflow)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(workflow.Id);
    ArgumentException.ThrowIfNullOrWhiteSpace(workflow.GraphFile);
    if (workflow.Bindings is null || workflow.Size is null)
    {
      throw new IOException("The workflow has no control bindings or size allowances.");
    }

    bool validSize = workflow.Size.Minimum > 0 && workflow.Size.Maximum >= workflow.Size.Minimum && workflow.Size.Multiple > 0;
    if (!validSize)
    {
      throw new IOException("The workflow size allowances are invalid.");
    }
  }

  private static void ValidateInput(WorkflowDefinition workflow, GenerationInput input)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(input.Prompt);
    bool validWidth = input.Width >= workflow.Size.Minimum && input.Width <= workflow.Size.Maximum && input.Width % workflow.Size.Multiple == 0;
    bool validHeight = input.Height >= workflow.Size.Minimum && input.Height <= workflow.Size.Maximum && input.Height % workflow.Size.Multiple == 0;
    if (!validWidth || !validHeight || input.Seed < 0)
    {
      throw new ArgumentException("The requested size or seed is outside this workflow's controls.");
    }

    if (workflow.Bindings.NegativePrompt is null && !string.IsNullOrWhiteSpace(input.NegativePrompt))
    {
      throw new ArgumentException("This workflow does not accept a negative prompt.");
    }
  }

  private static string ResolveGraph(string directory, string relative)
  {
    if (Path.IsPathRooted(relative))
    {
      throw new IOException("The workflow graph path must be relative to its definition.");
    }
    string path = Path.GetFullPath(Path.Combine(directory, relative));
    if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
    {
      throw new IOException("The workflow graph must be inside its definition directory.");
    }

    string current = path;
    while (current.Length > directory.Length)
    {
      FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
      if (entry.LinkTarget is not null)
      {
        throw new IOException("Workflow graph paths must not pass through symbolic links.");
      }
      current = Path.GetDirectoryName(current)!;
    }

    return path;
  }

  private static JsonObject Inputs(JsonObject graph, string node)
  {
    return graph[node]?["inputs"]?.AsObject() ?? throw new IOException("The workflow control refers to a missing node.");
  }

  private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
