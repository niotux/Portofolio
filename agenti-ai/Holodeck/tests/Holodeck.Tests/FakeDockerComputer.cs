using System.Text.Json;

namespace Holodeck.Tests;

internal sealed class FakeDockerComputer(string id, string name, JsonElement config)
{
  public string Id { get; } = id;
  public string Name { get; } = name;
  public JsonElement Config { get; } = config;
  public bool Running { get; set; }
  public Dictionary<string, object> Networks { get; } = new();
  public TaskCompletionSource<int> Exit { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
