using System.Collections.Generic;
using Dtm.Docker;
using Dtm.Ui;

namespace Dtm.Tests;

public class RowBuilderTests
{
  static ContextPoller Poller(string name, string psOutput)
  {
    List<Container> containers = new();
    Parsing.Containers(psOutput, containers);
    ContextPoller p = new(name, new DockerCli("docker"));
    p.Seed(new ContextSnapshot(name, containers, new(), 4, 1L << 30, null, true));
    return p;
  }

  // Containers are sorted by project then name, as the poller publishes them.
  const string Ps = "id-api000001\tshop-api-1\tapi:1\trunning\tUp\tshop\n" + "id-db0000001\tshop-db-1\tpostgres\texited\tExited\tshop\n" + "id-solo00001\tsolo\talpine\trunning\tUp\t\n";

  static List<Row> Build(ContextPoller[] pollers, string filter, bool runningOnly, List<RowKey> collapsed)
  {
    List<Row> rows = new();
    List<Row> scratch = new();
    RowBuilder.Containers(pollers, filter, runningOnly, collapsed, new RowComparer(), scratch, rows);
    return rows;
  }

  [Fact]
  public void GroupsComposeProjectWithChildren()
  {
    List<Row> rows = Build([Poller("default", Ps)], "", false, new());

    Assert.Equal(4, rows.Count);
    Assert.Equal(RowKind.Project, rows[0].Kind);
    Assert.Equal("shop", rows[0].Name);
    Assert.Equal(2, rows[0].Count);
    Assert.Equal(1, rows[0].Running);
    Assert.True(rows[1].Child);
    Assert.Equal("shop-api-1", rows[1].Name);
    Assert.Equal("shop-db-1", rows[2].Name);
    Assert.Equal(RowKind.Container, rows[3].Kind);
    Assert.False(rows[3].Child);
    Assert.Equal("solo", rows[3].Name);
  }

  [Fact]
  public void CollapsedProjectHidesChildren()
  {
    List<RowKey> collapsed = new();
    collapsed.Add(new RowKey(RowKind.Project, "default", "shop"));
    List<Row> rows = Build([Poller("default", Ps)], "", false, collapsed);

    Assert.Equal(2, rows.Count);
    Assert.Equal("shop", rows[0].Name);
    Assert.Equal("solo", rows[1].Name);
  }

  [Fact]
  public void RunningOnlyHidesStoppedContainers()
  {
    List<Row> rows = Build([Poller("default", Ps)], "", true, new());

    Assert.Equal(3, rows.Count);
    Assert.Equal("shop-api-1", rows[1].Name);
    Assert.Equal("solo", rows[2].Name);
  }

  [Fact]
  public void FilterOnChildShowsOnlyMatchingChildren()
  {
    List<Row> rows = Build([Poller("default", Ps)], "DB", false, new());

    Assert.Equal(2, rows.Count);
    Assert.Equal("shop", rows[0].Name);
    Assert.Equal("shop-db-1", rows[1].Name);
  }

  [Fact]
  public void FilterOnContextKeepsEverythingInIt()
  {
    List<Row> rows = Build([Poller("default", Ps), Poller("remote", "id-x00000001\tx\talpine\trunning\tUp\t\n")], "remote", false, new());

    Assert.Single(rows);
    Assert.Equal("x", rows[0].Name);
    Assert.Equal("remote", rows[0].Snapshot.Name);
  }

  [Fact]
  public void UnreachableContextIsListedLast()
  {
    ContextPoller broken = new("broken", new DockerCli("docker"));
    broken.Seed(new ContextSnapshot("broken", new(), new(), 0, 0, "cannot connect", true));
    List<Row> rows = Build([broken, Poller("default", Ps)], "", false, new());

    Assert.Equal(RowKind.ContextError, rows[rows.Count - 1].Kind);
    Assert.Equal("broken", rows[rows.Count - 1].Snapshot.Name);
  }
}
