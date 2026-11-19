using System.Text.Json.Nodes;
using Xunit;

namespace CreativeStudio.Tests;

public sealed class ComfyClientTests
{
  [Fact]
  public async Task LostSubmissionResponseReconcilesWithoutSubmittingAgain()
  {
    GenerationPlan plan = Plan();
    using ComfyTestHandler handler = new(plan.PromptId) { LoseSubmissionResponse = true };
    using HttpClient http = new(handler);
    ComfyClient client = new(http, new ComfyOptions("http://comfy.test/", 30, 1), Path.GetTempPath());
    await Assert.ThrowsAsync<HttpRequestException>(() => client.SubmitAsync(plan, CancellationToken.None));
    ComfyJobState state = await client.FindAsync(plan.PromptId, CancellationToken.None);
    Assert.Equal("running", state.Status);
    Assert.Equal(1, handler.SubmissionCount);
  }

  [Fact]
  public void GraphHashSurvivesObjectReorderingAndStillDetectsChanges()
  {
    JsonObject original = JsonNode.Parse("{\"2\":{\"class_type\":\"SaveImage\",\"inputs\":{\"images\":[\"1\",0]}},\"1\":{\"inputs\":{\"seed\":42,\"text\":\"A quiet station\"},\"class_type\":\"Example\"}}")!.AsObject();
    JsonObject reordered = JsonNode.Parse("{\"1\":{\"class_type\":\"Example\",\"inputs\":{\"text\":\"A quiet station\",\"seed\":42}},\"2\":{\"inputs\":{\"images\":[\"1\",0]},\"class_type\":\"SaveImage\"}}")!.AsObject();
    Assert.Equal(WorkflowHash.Compute(original), WorkflowHash.Compute(reordered));
    Assert.Equal(WorkflowHash.Compute(JsonNode.Parse("{\"value\":1.0}")!), WorkflowHash.Compute(JsonNode.Parse("{\"value\":1e0}")!));
    reordered["1"]!["inputs"]!["seed"] = 43;
    Assert.NotEqual(WorkflowHash.Compute(original), WorkflowHash.Compute(reordered));
  }

  [Fact]
  public async Task OutputCollectionRejectsPathsOutsideItsGenerationDirectory()
  {
    GenerationPlan plan = Plan();
    using ComfyTestHandler handler = new(plan.PromptId) { Completed = true, ReturnTraversalPath = true };
    using HttpClient http = new(handler);
    ComfyClient client = new(http, new ComfyOptions("http://comfy.test/", 30, 1), Path.GetTempPath());
    await Assert.ThrowsAsync<IOException>(() => client.CollectAsync(plan, CancellationToken.None));
    Assert.Equal(0, handler.DownloadCount);
  }

  [Fact]
  public async Task CollectedOutputSurvivesASecondCollectionWithoutDownloadingAgain()
  {
    string workspace = Path.Combine(Path.GetTempPath(), "creative-comfy-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workspace);
    try
    {
      GenerationPlan plan = Plan();
      using ComfyTestHandler handler = new(plan.PromptId) { Completed = true };
      using HttpClient http = new(handler);
      ComfyClient client = new(http, new ComfyOptions("http://comfy.test/", 30, 1), workspace);
      GenerationResult first = await client.CollectAsync(plan, CancellationToken.None);
      GenerationResult second = await client.CollectAsync(plan, CancellationToken.None);
      Assert.Equal(first.Files, second.Files);
      Assert.Equal(1, handler.DownloadCount);
      Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(workspace, Assert.Single(first.Files))));
    }
    finally
    {
      Directory.Delete(workspace, true);
    }
  }

  private static GenerationPlan Plan()
  {
    JsonObject graph = JsonNode.Parse("{\"1\":{\"class_type\":\"SaveImage\",\"inputs\":{\"filename_prefix\":\"creative\"}}}")!.AsObject();
    return new GenerationPlan(Guid.NewGuid().ToString("D"), "test", "revision", WorkflowHash.Compute(graph), new GenerationInput("A quiet station", "", 512, 512, 42), graph, new List<WorkflowArtifact>());
  }
}
