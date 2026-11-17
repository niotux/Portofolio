using System.Collections.Generic;
using Dtm.Docker;

namespace Dtm.Tests;

public class ParsingTests
{
  [Fact]
  public void Containers_ReadsFieldsAndProject()
  {
    string output = "566882c98114\tllms-ollama-1\tollama/ollama:0.40.8\trunning\tUp 2 hours\tllms\n" + "0123456789ab\tloose\talpine\texited\tExited (0) 3 days ago\t\n";
    List<Container> list = new();
    Parsing.Containers(output, list);

    Assert.Equal(2, list.Count);
    Assert.Equal("566882c98114", list[0].Id);
    Assert.Equal("llms-ollama-1", list[0].Name);
    Assert.Equal("ollama/ollama:0.40.8", list[0].Image);
    Assert.Equal(ContainerState.Running, list[0].State);
    Assert.Equal("Up 2 hours", list[0].Status);
    Assert.Equal("llms", list[0].Project);
    Assert.True(list[0].IsRunning);
    Assert.Null(list[1].Project);
    Assert.Equal(ContainerState.Exited, list[1].State);
    Assert.False(list[1].IsRunning);
  }

  [Fact]
  public void ApplyStats_MatchesByIdAndParsesUnits()
  {
    List<Container> list = new();
    Parsing.Containers("566882c98114\ta\ti\trunning\tUp\t\naaaaaaaaaaaa\tb\ti\trunning\tUp\t\n", list);
    Parsing.ApplyStats("566882c98114\t12.50%\t1.5GiB / 31.19GiB\naaaaaaaaaaaa\t0.00%\t640MiB / 31.19GiB\nffffffffffff\t9%\t1B / 1B\n", list);

    Assert.Equal(12.5, list[0].Cpu);
    Assert.Equal((long)(1.5 * (1L << 30)), list[0].MemBytes);
    Assert.Equal(640L << 20, list[1].MemBytes);
  }

  [Fact]
  public void Bytes_HandlesBinaryAndDecimalUnits()
  {
    Assert.Equal(512, Parsing.Bytes("512B"));
    Assert.Equal(12500, Parsing.Bytes("12.5kB"));
    Assert.Equal(5_020_000_000, Parsing.Bytes("5.02GB"));
    Assert.Equal(2L << 20, Parsing.Bytes(" 2MiB "));
    Assert.Equal(0, Parsing.Bytes("garbage"));
  }

  [Fact]
  public void Contexts_ReadsCurrentAndError()
  {
    List<DockerContext> list = new();
    Parsing.Contexts("default\tfalse\t\ndesktop-linux\ttrue\tbroken meta\n", list);

    Assert.Equal(2, list.Count);
    Assert.False(list[0].Current);
    Assert.Null(list[0].Error);
    Assert.True(list[1].Current);
    Assert.Equal("broken meta", list[1].Error);
  }

  [Fact]
  public void ApplyDeviceRequests_MatchesFullIdsAgainstShortOnes()
  {
    List<Container> list = new();
    Parsing.Containers("566882c98114\tgpu\ti\trunning\tUp\t\naaaaaaaaaaaa\tplain\ti\trunning\tUp\t\n", list);
    Parsing.ApplyDeviceRequests("566882c9811484eafda246bffbc8eb82a304649da3714efd5bb42e77541092b3 1\naaaaaaaaaaaabbbbbbbb 0\n", list);

    Assert.True(list[0].GpuChecked);
    Assert.True(list[0].WantsGpu);
    Assert.True(list[1].GpuChecked);
    Assert.False(list[1].WantsGpu);
  }

  [Fact]
  public void Host_AndNvidia()
  {
    Assert.True(Parsing.Host("12 33491869696\n", out int cpus, out long mem));
    Assert.Equal(12, cpus);
    Assert.Equal(33491869696, mem);
    Assert.False(Parsing.Host("nope", out _, out _));

    Assert.Equal(50, Parsing.NvidiaUtilization("40\n60\n"));
    Assert.Equal(-1, Parsing.NvidiaUtilization("NVIDIA-SMI has failed"));
  }

  [Fact]
  public void CliResult_ErrorLineIsLastStderrLine()
  {
    CliResult r = new(1, "", "warning: x\nError: no such container\n");
    Assert.Equal("Error: no such container", r.ErrorLine());
    Assert.Equal("exit code 3", new CliResult(3, "", "").ErrorLine());
  }
}
