// Run under the deployed seat identity, with its existing service environment. No chat writes or LLM calls.
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PiSharp.Core.Config;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var settings = SettingsLoader.Load(Path.Combine(home, "work"), Path.Combine(home, ".config/pi-sharp"));
var factory = settings.Factory ?? throw new InvalidOperationException("Factory identity required");
var token = factory.ResolveToken() ?? throw new InvalidOperationException("Seat environment required");
var owner = factory.Actor ?? throw new InvalidOperationException("Configured actor required");
var directory = settings.NotebookDirectory ?? throw new InvalidOperationException("Notebook profile required");
var prefix = "canary-" + Guid.NewGuid().ToString("N");
using var sync = new NotebookSync(factory.KbUrl!, token);
using (var store = new NotebookStore(directory, owner))
{
    await sync.FlushAsync(store, default);
    store.Put(prefix + "-source", "source", new string('x', 350) + "Required late instruction: retain all twelve commitments.", prefix, "open", 0);
    for (var i = 0; i < 12; i++) store.Put(prefix + "-c" + i, "commitment", "Complete exact obligation " + i, prefix, "open", 0, prefix + "-source");
    while (store.PendingCount > 0) await sync.FlushAsync(store, default);
}
using (var restored = new NotebookStore(directory, owner))
{
    for (var cycle = 0; cycle < 5; cycle++)
    {
        var view = NotebookContext.Prepare(restored, []); // transcript-free reconstruction, as after compaction
        var text = ((UserMessage)view.Last()).Text;
        for (var i = 0; i < 12; i++) if (!text.Contains(prefix + "-c" + i)) throw new Exception("Lost commitment");
        if (!restored.Get(prefix + "-source")!["text"]!.GetValue<string>().EndsWith("retain all twelve commitments.")) throw new Exception("Lost exact source tail");
    }
    var artifacts = new ArtifactStore(Path.Combine(directory, "artifacts")) { Notebook = restored };
    sync.Artifacts = artifacts;
    if (args.Length > 0)
    {
        var bytes = await File.ReadAllBytesAsync(args[0]);
        var artifact = artifacts.Save([new TextContent("Deployment screenshot fixture; does not claim a product task is verified."), new ImageContent(Convert.ToBase64String(bytes), "image/jpeg")]);
        Console.WriteLine(new JsonObject { ["evidenceArtifact"] = artifact }.ToJsonString());
    }
    foreach (var row in restored.List(task: prefix))
        restored.Put(row["id"]!.GetValue<string>(), row["kind"]!.GetValue<string>(), row["text"]!.GetValue<string>(), prefix, "completed", row["revision"]!.GetValue<long>(), row["sourceId"]?.GetValue<string>());
    while (restored.PendingCount > 0) await sync.FlushAsync(restored, default);
    await sync.FlushAsync(restored, default);
}
using var http = new HttpClient { BaseAddress = new Uri(new Uri(factory.KbUrl!).GetLeftPart(UriPartial.Authority)) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Add("X-Factory-UI", "1");
using var observer = await http.GetAsync("/api/workspace/notebooks");
if (observer.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)) throw new Exception("Seat reached operator notebooks");
Console.WriteLine(new JsonObject { ["passed"] = true, ["owner"] = owner, ["commitments"] = 12, ["transcriptFreeRebuilds"] = 5,
    ["restart"] = true, ["remoteCommitted"] = true, ["operatorDenied"] = true }.ToJsonString());
