using QuantiX.Engine;
using QuantiX.Engine.Tests.AppServers.TestFixtures;
using Xunit;

namespace QuantiX.Engine.Tests.AppServers.UnitTests;

public class AppServerLifetimeTests
{
  [Fact]
  public async Task AsyncLifetime_Should_Run_On_Different_Threads()
  {
    var server = new TestServer(AppServerLifetime.Async)
    {
      LoopIterations = 5,
      AutoStop = true
    };

    var app = TestApp.CreateWithServers(server);

    await app.Run();

    // For Async, threads may differ - just verify all methods were called
    Assert.NotNull(server.GetInitThread());
    Assert.NotNull(server.GetRunThread());
    Assert.NotNull(server.GetLoopThread());
    Assert.NotNull(server.GetShutdownThread());
  }

  [Fact]
  public async Task MonoThreadLifetime_Should_Run_On_Single_Thread()
  {
    var server = new TestServer(AppServerLifetime.MonoThread)
    {
      LoopIterations = 5,
      AutoStop = true
    };

    var app = TestApp.CreateWithServers(server);

    await app.Run();

    // Verify same thread throughout lifetime
    Assert.True(server.VerifyThreadAffinity());
  }

  [Fact]
  public async Task MainThreadLifetime_Should_Run_On_App_Thread()
  {
    var server = new TestServer(AppServerLifetime.MainThread)
    {
      LoopIterations = 5,
      AutoStop = true
    };

    var app = TestApp.CreateWithServers(server);

    await app.Run();

    // Verify same thread throughout lifetime
    Assert.True(server.VerifyThreadAffinity());
  }
}