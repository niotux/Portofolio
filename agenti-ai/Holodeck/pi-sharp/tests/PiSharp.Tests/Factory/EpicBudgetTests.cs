using PiSharp.Core.Factory;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Factory;

// plan-pi-sharp-codex item 14: a budget per epic, with notices; it informs, it never stops the work.
public sealed class EpicBudgetTests
{
    [Fact]
    public void NoticesComeOnceAt50_80And100PercentThenEvery50_AcrossARestart()
    {
        using var tmp = new TempWorkspace();
        var dir = tmp.Dir("nb");
        string?[] charged;
        using (var store = new NotebookStore(dir, "seat1"))
        {
            var budget = new EpicBudget(store, EpicBudget.DefaultOutputBudget);
            charged = [budget.Charge("sound-epic", 400, budget: 1_000), budget.Charge("sound-epic", 200, budget: 1_000), budget.Charge("sound-epic", 50, budget: 1_000)];
        }
        Assert.Null(charged[0]);
        Assert.Contains("used 60% of its output budget (600 of 1,000 tokens)", charged[1]); // crossed 50: the 50% notice, said at 60
        Assert.Null(charged[2]);

        using var reopened = new NotebookStore(dir, "seat1");
        var again = new EpicBudget(reopened, EpicBudget.DefaultOutputBudget);
        var at80 = again.Charge("sound-epic", 200, budget: 1_000);        // 850: the count survived the restart
        Assert.Contains("85%", at80);
        var at100 = again.Charge("sound-epic", 200, budget: 1_000);       // 1050
        Assert.Contains("does not stop you", at100);
        Assert.Null(again.Charge("sound-epic", 100, budget: 1_000));      // 1150: nothing new
        Assert.Contains("155%", again.Charge("sound-epic", 400, budget: 1_000)); // 1550 crosses 150
        Assert.Null(again.Charge("other-epic", 10, budget: 1_000));       // epics count apart
    }

    [Fact]
    public void WithoutItsOwnBudgetAnEpicUsesTheSeatsDefault()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        var budget = new EpicBudget(store, 100);
        Assert.Contains("of 100 tokens", budget.Charge("e", 60));
        Assert.Null(new EpicBudget(store, 0).Charge("f", 1_000)); // no budget at all: no notices
    }
}
