using QuantiX.Engine;
using QuantiX.Engine.Tests.AppServers.TestFixtures;
using Xunit;

namespace QuantiX.Engine.Tests.AppServers.UnitTests;

public class ExceptionHandlingTests
{
  [Fact]
  public async Task Exception_In_Init_Should_Trigger_OnException_Event()
  {
    var server = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "ExceptionServer",
      ThrowInInit = true,
      LoopIterations = 0,
      AutoStop = false
    };

    var app = TestApp.CreateWithServers(server);
    var exceptionTriggered = false;

    app.OnServerException += (ex) =>
    {
      exceptionTriggered = true;
      Assert.NotNull(ex);
      Assert.NotNull(ex.InnerException);
      Assert.Contains("OnInit", ex.InnerException.Message);
      return AppServerException.HandlerResult.StopApplication;
    };

    await app.Run();

    Assert.True(exceptionTriggered);
  }

  [Fact]
  public async Task Exception_In_Run_Should_Trigger_OnException_Event()
  {
    var server = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "ExceptionServer",
      ThrowInRun = true,
      LoopIterations = 0,
      AutoStop = false
    };

    var app = TestApp.CreateWithServers(server);
    var exceptionTriggered = false;

    app.OnServerException += (ex) =>
    {
      exceptionTriggered = true;
      Assert.NotNull(ex);
      Assert.NotNull(ex.InnerException);
      Assert.Contains("OnRun", ex.InnerException.Message);
      return AppServerException.HandlerResult.StopApplication;
    };

    await app.Run();

    Assert.True(exceptionTriggered);
  }

  [Fact]
  public async Task Exception_In_Loop_Should_Trigger_OnException_Event()
  {
    var server = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "ExceptionServer",
      ThrowInLoop = true,
      LoopIterations = 5,
      AutoStop = false
    };

    var app = TestApp.CreateWithServers(server);
    var exceptionTriggered = false;

    app.OnServerException += (ex) =>
    {
      exceptionTriggered = true;
      Assert.NotNull(ex);
      Assert.NotNull(ex.InnerException);
      Assert.Contains("OnLoop", ex.InnerException.Message);
      return AppServerException.HandlerResult.StopApplication;
    };

    await app.Run();

    Assert.True(exceptionTriggered);
  }

  [Fact]
  public async Task Exception_In_Shutdown_Should_Trigger_OnException_Event()
  {
    var server = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "ExceptionServer",
      ThrowInShutdown = true,
      LoopIterations = 2,
      AutoStop = true
    };

    var app = TestApp.CreateWithServers(server);
    var exceptionTriggered = false;

    app.OnServerException += (ex) =>
    {
      exceptionTriggered = true;
      Assert.NotNull(ex);
      Assert.NotNull(ex.InnerException);
      Assert.Contains("OnShutdown", ex.InnerException.Message);
      return AppServerException.HandlerResult.StopApplication;
    };

    await app.Run();

    Assert.True(exceptionTriggered);
  }

  [Fact]
  public async Task HandlerResult_StopServer_Should_Stop_Server()
  {
    var server = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "ExceptionServer",
      ThrowInLoop = true,
      LoopIterations = 5,
      AutoStop = false
    };

    var app = TestApp.CreateWithServers(server);
    var exceptionTriggered = false;

    app.OnServerException += (ex) =>
    {
      exceptionTriggered = true;
      return AppServerException.HandlerResult.StopServer;
    };

    await app.Run();

    Assert.True(exceptionTriggered);
    Assert.Equal(AppServerState.Stopped, server.State);
  }

  [Fact]
  public async Task HandlerResult_StopApplication_Should_Stop_App()
  {
    var server = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "ExceptionServer",
      ThrowInLoop = true,
      LoopIterations = 5,
      AutoStop = false
    };

    var app = TestApp.CreateWithServers(server);
    var exceptionTriggered = false;

    app.OnServerException += (ex) =>
    {
      exceptionTriggered = true;
      return AppServerException.HandlerResult.StopApplication;
    };

    await app.Run();

    Assert.True(exceptionTriggered);
    Assert.Equal(AppServerState.Stopped, server.State);
    Assert.Equal(AppServerState.Stopped, app.Root.State);
  }

  [Fact]
  public async Task HandlerResult_RestartServer_Should_Restart_Server()
  {
    var server = new TestServer(AppServerLifetime.MonoThread)
    {
      Name = "ExceptionServer",
      ThrowInLoop = true,
      LoopIterations = 5,
      AutoStop = false
    };

    var app = TestApp.CreateWithServers(server);
    var exceptionCount = 0;

    app.OnServerException += (ex) =>
    {
      exceptionCount++;
      return AppServerException.HandlerResult.RestartServer;
    };

    await app.Run();

    // Server should have restarted after exception
    Assert.Equal(2, exceptionCount);
  }
}