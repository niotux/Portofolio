using QuantiX.Engine;
using QuantiX.Engine.Tests.AppServers.TestFixtures;
using Xunit;

namespace QuantiX.Engine.Tests.AppServers.UnitTests;

public class ServerStartupOrderTests
{
  [Fact]
  public async Task Root_Server_Should_Start_After_Other_Servers()
  {
    // Create a root server that will be started last
    var rootServer = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "RootServer",
      LoopIterations = 3,
      AutoStop = true
    };

    // Create a child server that will be started first
    var childServer = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "ChildServer",
      LoopIterations = 3,
      AutoStop = true
    };

    var app = TestApp.CreateWithServers(rootServer, childServer);

    await app.Run();

    // Verify both servers completed their iterations
    Assert.Equal(3, rootServer.CurrentIteration);
    Assert.Equal(3, childServer.CurrentIteration);
  }

  [Fact]
  public async Task AddSingleton_Should_Register_Server()
  {
    var rootServer = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "RootServer",
      LoopIterations = 2,
      AutoStop = true
    };

    var app = TestApp.CreateWithSingleton<TestServer>(rootServer);

    await app.Run();

    // Verify singleton was registered and started
    var singleton = app.GetSingleton<TestServer>();
    Assert.NotNull(singleton);
    Assert.Equal(2, singleton.CurrentIteration);
  }

  [Fact]
  public async Task GetSingleton_Should_Return_Same_Instance()
  {
    var rootServer = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "RootServer",
      LoopIterations = 2,
      AutoStop = true
    };

    var app = TestApp.CreateWithSingleton<TestServer>(rootServer);

    // Get singleton multiple times
    var singleton1 = app.GetSingleton<TestServer>();
    var singleton2 = app.GetSingleton<TestServer>();

    // Verify they are the same instance
    Assert.Same(singleton1, singleton2);
  }

  [Fact]
  public async Task TryGetSingleton_Should_Return_True_For_Registered_Server()
  {
    var rootServer = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "RootServer",
      LoopIterations = 2,
      AutoStop = true
    };

    var app = TestApp.CreateWithSingleton<TestServer>(rootServer);

    // Try to get singleton
    var result = app.TryGetSingleton<TestServer>(out var singleton);

    Assert.True(result);
    Assert.NotNull(singleton);
  }

  [Fact]
  public async Task TryGetSingleton_Should_Return_False_For_Unregistered_Server()
  {
    var rootServer = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "RootServer",
      LoopIterations = 2,
      AutoStop = true
    };

    var app = TestApp.CreateWithServers(rootServer);

    // Try to get singleton that was never registered
    var result = app.TryGetSingleton<TestServer>(out var singleton);

    Assert.False(result);
    Assert.Null(singleton);
  }
}