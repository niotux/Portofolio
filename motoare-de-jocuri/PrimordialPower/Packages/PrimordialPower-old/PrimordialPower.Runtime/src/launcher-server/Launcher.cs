using System.Threading.Tasks;

namespace PrimordialPower.Runtime;

public class Launcher
{
  private Launcher() { }
  public static Launcher Main { get; } = new();



  private class ServerBatch
  {
    private readonly List<IServerNode> servers;
    private readonly List<Task> lifetimes;
    private readonly AsyncCountdownEvent ready;
    public ServerBatch(IServerNode[]? servers = null)
    {
      int count = servers?.Length ?? 0;

      this.servers = new(count);
      this.servers.AddRange(servers ?? Array.Empty<IServerNode>());
      this.lifetimes = new(count);
      this.ready = new(count);
    }



    public void AddServers(IServerNode[] servers)
    {
      this.servers.AddRange(servers);
    }



    public Task Finished => Task.WhenAll(this.lifetimes);

    public Task Launch()
    {
      foreach (var server in this.servers)
      {
        if (!Launcher.Main.serverSet.Contains(server))
        {
          Launcher.Main.serverSet.Add(server);
          switch (server.Type)
          {
            case ServerType.MonoThread:
              this.lifetimes.Add(Task.Run(() => RunMonoThread(server)));
              break;

            case ServerType.Async:
              this.lifetimes.Add(Task.Run(() => RunAsync(server)));
              break;
          }
          //server.Init();
        }
      }

      // return Task.CompletedTask;
      return this.ready.WaitAsync();
    }



    private void RunMonoThread(IServer server)
    {
      server.Init();
      ready.Signal();

      //server.Start();
      while (server.Loop()) { }
      server.Shutdown();
    }

    private async Task RunAsync(IServer server)
    {
      await server.InitAsync();
      ready.Signal();

      //await server.StartAsync();
      while (await server.LoopAsync()) { }
      await server.ShutdownAsync();
    }
  }



  private void AddToBatch(int index, IServerNode[] servers)
  {
    if (index + 1 > this.batches.Count)
    {
      this.batches.Add(new(servers));
    }
    else
    {
      this.batches[index].AddServers(servers);
    }
  }



  public void AddServer(IServerNode server)
  {
    AddToBatch(0, [server]);
    AddServerDFS(server);
  }

  private readonly List<ServerBatch> batches = new();
  private readonly HashSet<IServerNode> serverSet = new();
  private void AddServerDFS(IServerNode lastServer)
  {
    this.serverSet.Add(lastServer);

    AddToBatch(this.serverSet.Count, lastServer.Servers);

    foreach (var server in lastServer.Servers)
    {
      if (this.serverSet.Contains(server))
      {
        throw new Exception($"Cycle in dependency trace caused in server type \"{lastServer.GetType().FullName}\", hash code <{lastServer.GetHashCode()}>!");
      }

      AddServerDFS(server);
    }
    this.serverSet.Remove(lastServer);
  }



  public async Task Run()
  {
    int count = this.batches.Count;
    for (int i = count - 1; i >= 0; i--)
    {
      await this.batches[i].Launch();
      Console.WriteLine();
    }
  }



  public void Shutdown()
  {

  }
}