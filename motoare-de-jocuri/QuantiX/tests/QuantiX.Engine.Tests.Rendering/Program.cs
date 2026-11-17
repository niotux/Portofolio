using System.Diagnostics.Tracing;
using System.Runtime;
using QuantiX.Engine;
using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Tests.Rendering;

internal static class Program
{
  public static async Task Main()
  {
    GCSettings.LatencyMode = GCLatencyMode.Interactive;
    using AllocationTracker tracker = new();


    MainServer main = new();

    App app = new(main);
    app.AddService(main.Windowing);
    app.AddService(main.Audio);
    app.OnServerException += HandleExcetion;

    Console.ForegroundColor = ConsoleColor.White;
    await app.Run();
  }

  public static AppServerException.HandlerResult HandleExcetion(Exception? ex)
  {
    for (var e = ex; e is not null; e = e.InnerException)
    {
      Console.WriteLine(e.Message);
      Console.WriteLine(e.StackTrace);
      Console.WriteLine("---");
    }
    return AppServerException.HandlerResult.KillProcess;
  }
}



public sealed class AllocationTracker : EventListener
{
  public event Action<string, long>? OnAllocation; // (typeName, size)

  protected override void OnEventSourceCreated(EventSource source)
  {
    if (source.Name == "Microsoft-Windows-DotNETRuntime")
      EnableEvents(source, EventLevel.Informational, (EventKeywords)0x8000); // GC
  }

  protected override void OnEventWritten(EventWrittenEventArgs e)
  {
    if (e.EventName != "GC/AllocationTick" || e.Payload is null) return;

    var typeName = e.Payload[3] as string ?? "unknown";
    var size = (long)(uint)e.Payload[3]!; // cumulative bytes
    OnAllocation?.Invoke(typeName, size);
  }
}