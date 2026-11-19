using System.Diagnostics;
using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

// pi-sharp-job-cancel-nested-process-groups: on Linux a bash command runs as its own session
// (/usr/bin/setsid /bin/bash), but GNU timeout moves its child into a nested process group that
// shares the session but not the group. Stopping only the outer group left the nested tree alive
// holding the output pipes, so a cancel or a deadline surfaced as a 137 "descendants retained
// output pipes" report. These tests pin the owned-tree stop: the nested group dies within a bounded
// wait, the independent control session never does, and the result never claims a group stop.
public class NestedProcessGroupCancelTests
{
    private static bool Prereqs() =>
        OperatingSystem.IsLinux() && File.Exists("/bin/bash") && File.Exists("/usr/bin/timeout") && File.Exists("/usr/bin/setsid");

    private static string NestedCommand(string dir) =>
        // < /dev/null keeps timeout out of its tty-foreground mode, so its watchdog deterministically
        // starts the nested tree in a new process group (and here a new session), sharing only the pipes.
        $"echo $$ > {dir}/outer.pid; timeout 60 /bin/bash -c 'echo $$ > {dir}/inner.pid; exec sleep 60' < /dev/null";

    private static string CommandJson(string dir, int? timeout = null) =>
        timeout is null ? $"{{\"command\":\"{NestedCommand(dir)}\"}}"
                        : $"{{\"command\":\"{NestedCommand(dir)}\",\"timeout\":{timeout}}}";

    // An independent session (setsid) and not a descendant of the command under test: a cancel or a
    // deadline must never reach it.
    private static Process StartControl()
    {
        var control = Process.Start(new ProcessStartInfo("/usr/bin/setsid", "sleep 300")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
        Assert.NotNull(control);
        return control!;
    }

    private static void StopControl(Process control)
    {
        try { control.Kill(entireProcessTree: true); control.WaitForExit(3000); }
        catch { /* already gone */ }
        finally { control.Dispose(); }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool DiesWithin(int pid, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            if (!IsAlive(pid)) return true;
            Thread.Sleep(100);
        }
        return !IsAlive(pid);
    }

    private static (int outer, int inner) ReadPids(string dir)
    {
        Assert.True(File.Exists($"{dir}/outer.pid"), "outer pid file missing");
        Assert.True(File.Exists($"{dir}/inner.pid"), "inner pid file missing");
        return (int.Parse(File.ReadAllText($"{dir}/outer.pid").Trim()), int.Parse(File.ReadAllText($"{dir}/inner.pid").Trim()));
    }

    private static int PgrpOf(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var fields = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return int.Parse(fields[2]); // after "(comm)": state, ppid, pgrp
    }

    [Fact]
    public async Task CancelStopsTheNestedTimeoutGroup_AndLeavesTheIndependentControlAlone()
    {
        if (!Prereqs()) return;
        using var tmp = new TempWorkspace();
        var dir = tmp.Dir("cancel-nested");
        var control = StartControl();
        try
        {
            using var cts = new CancellationTokenSource();
            var task = ToolRun.Exec(new BashTool(dir), CommandJson(dir), cts.Token);
            await Task.Delay(700);
            var (outer, inner) = ReadPids(dir);
            Assert.True(IsAlive(outer), "outer shell not running");
            Assert.True(IsAlive(inner), "inner process not running");
            Assert.True(PgrpOf(outer) == outer, $"topology: the outer shell leads its own group (setsid), pgrp={PgrpOf(outer)} pid={outer}");
            Assert.True(PgrpOf(inner) != PgrpOf(outer), $"topology: GNU timeout's watchdog put its child in a nested group, inner pgrp={PgrpOf(inner)} outer pgrp={PgrpOf(outer)}");

            cts.Cancel();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
            Assert.Contains("Command aborted", ex.Message);
            Assert.DoesNotContain("descendants retained output pipes", ex.Message);
            Assert.True(DiesWithin(inner, 5_000), $"nested timeout group pid {inner} survived the cancel");
            Assert.True(IsAlive(control.Id), "independent control job was killed by the cancel");
        }
        finally { StopControl(control); }
    }

    [Fact]
    public async Task DeadlineStopsTheNestedTimeoutGroup_AndLeavesTheIndependentControlAlone()
    {
        if (!Prereqs()) return;
        using var tmp = new TempWorkspace();
        var dir = tmp.Dir("deadline-nested");
        var control = StartControl();
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ToolRun.Exec(new BashTool(dir), CommandJson(dir, timeout: 2)));
            Assert.Contains("Command timed out", ex.Message);
            Assert.DoesNotContain("descendants retained output pipes", ex.Message);
            var (_, inner) = ReadPids(dir);
            Assert.True(DiesWithin(inner, 5_000), $"nested timeout group pid {inner} survived the deadline");
            Assert.True(IsAlive(control.Id), "independent control job was killed by the deadline");
        }
        finally { StopControl(control); }
    }

    [Fact]
    public async Task JobCancelStopsTheNestedTimeoutGroup_AndLeavesTheIndependentControlAlone()
    {
        if (!Prereqs()) return;
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        await using var tool = new JobTool(tmp.Path, store);
        var dir = tmp.Dir("job-cancel");
        var control = StartControl();
        try
        {
            var id = await Start(tool, NestedCommand(dir));
            await Task.Delay(700);
            var (outer, inner) = ReadPids(dir);
            Assert.True(PgrpOf(outer) != PgrpOf(inner), $"topology: the nested group exists before the cancel, outer pgrp={PgrpOf(outer)} inner pgrp={PgrpOf(inner)}");

            var result = await tool.ExecuteAsync("t", new JsonObject { ["op"] = "cancel", ["id"] = id }, null, CancellationToken.None);
            Assert.False(result.IsError, result.Text);
            Assert.DoesNotContain("descendants retained output pipes", result.Text);
            Assert.Contains("Command aborted", result.Text);
            Assert.True(DiesWithin(inner, 5_000), $"nested timeout group pid {inner} survived the job cancel");
            Assert.True(IsAlive(control.Id), "independent control job was killed by the job cancel");
        }
        finally { StopControl(control); }
    }

    // pi-sharp-job-cancel-nested-process-groups (return): the drain path. The shell exits on its own
    // (the nested timeout was started in the background); by the time the drain runs the root pid is
    // gone and its orphan has been reparented to init, so the ppid walk cannot see the nested group.
    // Only the private output pipes still prove who holds the write ends — the fallback must find the
    // group through them, stop it, and leave the independent control session alone.
    [Fact]
    public async Task DisposalStopsTheOrphanedNestedGroup_WhenTheRootIsGone_AndLeavesTheIndependentControlAlone()
    {
        if (!Prereqs()) return;
        using var tmp = new TempWorkspace();
        var dir = tmp.Dir("disposal-nested");
        var control = StartControl();
        try
        {
            // The command's last statement is a background nested timeout: bash exits immediately, so the
            // root shell is dead when the drain engages (no live root, no reparented live child).
            var command = $"echo $$ > {dir}/outer.pid; timeout 60 /bin/bash -c 'echo $$ > {dir}/inner.pid; exec sleep 60' < /dev/null & echo started";
            var sw = Stopwatch.StartNew();
            var task = ToolRun.Exec(new BashTool(dir), $"{{\"command\":\"{command}\"}}");
            await Task.Delay(700);
            var (outer, inner) = ReadPids(dir);
            Assert.True(IsAlive(inner), "nested timeout group not running");
            var innerPgrp = PgrpOf(inner);
            Assert.True(innerPgrp != outer, $"topology: the nested group exists, inner pgrp={innerPgrp} outer pgrp={outer}");
            Assert.True(!IsAlive(outer), "root-gone precondition: the root shell must have exited before the drain engages");

            var result = await task;
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"disposal took {sw.Elapsed.TotalSeconds:F1}s; the drain is bounded to 5s plus a kill");
            Assert.False(result.IsError, result.Text);
            Assert.Contains("started", result.Text);
            Assert.DoesNotContain("descendants retained output pipes", result.Text);
            Assert.True(DiesWithin(inner, 5_000), $"nested timeout group pid {inner} (pgrp {innerPgrp}) survived the disposal");
            Assert.True(IsAlive(control.Id), "independent control job was killed by the disposal");
            Assert.True(PgrpOf(control.Id) != innerPgrp, $"group-signal safety: the control sits in a different group, control pgrp={PgrpOf(control.Id)} inner pgrp={innerPgrp}");
        }
        finally { StopControl(control); }
    }

    // pi-sharp-job-cancel-nested-process-groups (return): the pid-reuse guard. A collected pid is only
    // signalled while the process at that pid is the one observed at enumeration time (same start time),
    // so a recycled pid is never signalled — and a dead pid never passes the guard at all.
    [Fact]
    public void RecycledPidsAreNeverSignalled_StartTimeReverification()
    {
        if (!Prereqs()) return;
        var self = Environment.ProcessId;
        var selfInfo = BashTool.ReadStartAndPgrp(self)!.Value;
        Assert.Equal(self, selfInfo.pid);
        Assert.True(BashTool.PidMatchesObserved(self, selfInfo.starttime), "a live pid with its own observed start time passes the guard");
        Assert.False(BashTool.PidMatchesObserved(self, selfInfo.starttime + 1), "a pid whose start time no longer matches the observation (recycled) never passes the guard");

        var shortLived = Process.Start(new ProcessStartInfo("/bin/bash", "-c \"sleep 0.3\"") { UseShellExecute = false })!;
        try
        {
            var observed = BashTool.ReadStartAndPgrp(shortLived.Id)!.Value.starttime;
            shortLived.WaitForExit(5000);
            Assert.False(BashTool.PidMatchesObserved(shortLived.Id, observed), "a dead pid never passes the guard, even with the start time observed while it lived");
        }
        finally { shortLived.Dispose(); }
    }

    // pi-sharp-job-cancel-nested-process-groups (return): the write-end filter. A foreign process that
    // holds only the READ end of one of the command's private output pipes is not a descendant and must
    // never be collected (and therefore never signalled); only a holder whose fdinfo flags show write
    // access (O_WRONLY/O_RDWR) qualifies. Controlled with a real FIFO and the production seams, no
    // timing races: two live processes hold the same pipe inode, one read-only, one write-only,
    // each by the same pid that opens it (exec keeps the fd), so the fdinfo flags observed are the
    // exact ones the PipeHolders scan reads.
    [Fact]
    public void ForeignReadEndHoldersAreNeverCollected_WriteEndIsTheOwnershipProof()
    {
        if (!Prereqs()) return;
        var dir = Path.Combine(Path.GetTempPath(), $"pi-sharp-pipe-access-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        var fifo = Path.Combine(dir, "pipe");
        Process.Start(new ProcessStartInfo("/bin/mkfifo", fifo) { UseShellExecute = false })!.WaitForExit(5000);
        // exec keeps the fd open across the exec, so the same pid holds each end for its whole life:
        // read end (fd 3, O_RDONLY) and write end (fd 4, O_WRONLY) of one FIFO (same pipe inode).
        var readEnd = StartBash($"exec 3< {fifo}; exec sleep 120");
        var writeEnd = StartBash($"exec 4> {fifo}; exec sleep 120");
        // The FIFO open blocks until both ends are present; poll through the production seam.
        var sw = Stopwatch.StartNew();
        string? readTarget = null, writeTarget = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            readTarget = FdTarget(readEnd.Id, 3);
            writeTarget = FdTarget(writeEnd.Id, 4);
            if (readTarget is not null && writeTarget is not null) break;
            Thread.Sleep(50);
        }
        try
        {
            // The two fds are the same pipe inode, observed through the same seam PipeHolders uses.
            Assert.True(readTarget is not null && writeTarget is not null, $"topology: both fds open (read={readTarget} write={writeTarget})");
            Assert.True(readTarget == writeTarget, $"same pipe inode, different access modes (read={readTarget} write={writeTarget})");
            // The access mode is the ownership proof: the foreign read-end fd never shows write
            // access, the write-end fd does, and a vanished fd is not counted either.
            var readFd = $"/proc/{readEnd.Id}/fdinfo/3";
            var writeFd = $"/proc/{writeEnd.Id}/fdinfo/4";
            Assert.False(BashTool.FdHasWriteAccess(readFd), "the foreign read-end fd must not show write access");
            Assert.True(BashTool.FdHasWriteAccess(writeFd), "the descendant write-end fd must show write access");
            Assert.False(BashTool.FdHasWriteAccess($"/proc/1/fdinfo/99999"), "a vanished fd is never counted");
        }
        finally
        {
            foreach (var p in new[] { readEnd, writeEnd })
            {
                try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { /* already gone */ } finally { p.Dispose(); }
            }
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    // pi-sharp-job-cancel-nested-process-groups (return): the empty-group / reused-ID refusal. A dead
    // process group's signal is refused by the kernel itself (kill(2) ESRCH: no such process in the
    // group), so the group signal can never reach a reused id — a live process that later gets the
    // number of a dead group is not a member of that group. Controlled end-to-end with real groups:
    // a group is created (python os.setpgrp: no fork, group number == pid) and observed through the
    // production seam, its only member is killed, and the group signal at that same number then
    // fails ESRCH while a different live group is untouched.
    [Fact]
    public void EmptyGroupSignalIsRefused_AReusedNumberNeverReachesAForeignProcess()
    {
        if (!Prereqs()) return;
        var group = StartOwnGroup();
        var control = StartOwnGroup();
        try
        {
            // The child made itself the leader of a fresh group (number == its pid) without forking,
            // observed with the production enumeration, so the pgrp the kill loop would signal comes
            // from this walk. Python starts up a few ms before its setpgid, so poll the seam until
            // the leader owns the number.
            var info = WaitForPgrp(group.Id, group.Id);
            Assert.True(info is not null, $"topology: the group leader must own group {group.Id}");
            var controlInfo = WaitForPgrp(control.Id, control.Id);
            Assert.True(controlInfo is not null, $"topology: the control leader must own group {control.Id}");
            Assert.True(info!.Value.pgrp != controlInfo!.Value.pgrp, "topology: the control is in a different group");

            // Kill the group's only member, then signal the group number again: the kernel refuses
            // (ESRCH) because the group is empty — the same number, if later reused, holds no member.
            group.Kill();
            group.WaitForExit(5000);
            var result = KillGroup(info.Value.pgrp);
            Assert.True(result != 0, "the group signal must not silently succeed: the group is empty");
            Assert.True(IsAlive(control.Id), "the ESRCH refusal: a foreign live group (control) was never signalled");
        }
        finally
        {
            foreach (var p in new[] { group, control })
            {
                try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { /* already gone */ } finally { p.Dispose(); }
            }
        }
    }

    // pi-sharp-job-cancel-nested-process-groups (return): the group lifecycle guard against a
    // reused group number. When our last observed member dies, the number it held is freed and may
    // later be a live group again; kill(-pgid) has no membership check, so an unguarded group signal
    // at that number kills whoever holds it. The production guard (GroupStillOwned) refuses the
    // group signal unless one of our observed (pid, starttime) members is alive in that exact group
    // at signal time. What the kernel allows decides the states a number can be in at signal time:
    // setpgid into a non-existent number is refused (EPERM: "the target process group does not
    // exist" — verified on this host), so a freed number comes back only as a new group led by a
    // process whose pid equals the number (pid reuse), i.e. a foreign-owned number led by a member
    // we never observed — or it stays empty. Both are controlled end-to-end with real groups: the
    // freed number is refused, the foreign-owned number is refused while the raw signal at it is
    // shown to kill the unrelated member, and a group we really still lead is signalled (the guard
    // does not over-refuse).
    [Fact]
    public void ForeignOwnedNumberIsRefused_TheGuardClosesTheReuseHazard()
    {
        if (!Prereqs()) return;
        var ours = StartOwnGroup();
        var foreign = StartOwnGroup();
        var control = StartOwnGroup();
        try
        {
            // Observe our group through the production seam: group leader, number == its pid.
            var info = WaitForPgrp(ours.Id, ours.Id);
            Assert.True(info is not null, $"topology: the group leader must own group {ours.Id}");
            var foreignInfo = WaitForPgrp(foreign.Id, foreign.Id);
            var controlInfo = WaitForPgrp(control.Id, control.Id);
            Assert.True(foreignInfo is not null && controlInfo is not null, "topology: the foreign and control leaders own their numbers");
            Assert.True(new[] { info!.Value.pgrp, foreignInfo.Value.pgrp, controlInfo.Value.pgrp }.Distinct().Count() == 3,
                "topology: three distinct group numbers");

            var observed = new Dictionary<int, long>
            {
                [ours.Id] = info.Value.starttime,
                [control.Id] = controlInfo.Value.starttime,
            };
            Assert.True(BashTool.GroupStillOwned(info.Value.pgrp, observed), "topology: the observed member is alive in the group");

            // Free our number: the group's only member dies and the kernel frees it. The guard
            // refuses the freed number, so the guarded kill loop never signals it.
            ours.Kill();
            ours.WaitForExit(5000);
            Assert.False(BashTool.GroupStillOwned(info.Value.pgrp, observed), "freed number: the guard refuses");

            // The foreign-owned number is exactly the state a reused number is in: an unrelated
            // live group under a member we never observed. The guard checks (pid, starttime)
            // membership, not the number — it refuses that number too.
            Assert.False(BashTool.GroupStillOwned(foreignInfo.Value.pgrp, observed), "the guard must refuse the foreign-owned number");

            // The hazard the guard closes: the raw group signal at the foreign number reaches the
            // unrelated member — an unguarded kill loop would have killed it.
            Assert.True(IsAlive(foreign.Id), "topology: the foreign member is alive");
            var hit = KillGroup(foreignInfo.Value.pgrp);
            Assert.True(hit == 0, "the raw signal must reach the foreign group: the unguarded hazard is real");
            Assert.True(DiesWithin(foreign.Id, 2000), "the raw signal at the foreign number killed the unrelated member");

            // The guard does not over-refuse: a group our observed member really still leads
            // passes, and the guarded signal at it reaches that member.
            Assert.True(BashTool.GroupStillOwned(controlInfo.Value.pgrp, observed), "our still-led group passes the guard");
            var controlHit = KillGroup(controlInfo.Value.pgrp);
            Assert.True(controlHit == 0 && DiesWithin(control.Id, 2000), "the guarded path still signals a group we really lead");
        }
        finally
        {
            foreach (var p in new[] { ours, foreign, control })
            {
                try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { /* already gone */ } finally { p.Dispose(); }
            }
        }
    }

    // ArgumentList keeps the script verbatim (the Argument string mangles shell quotes).
    private static Process StartBash(string script)
    {
        var psi = new ProcessStartInfo("/bin/bash") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        return Process.Start(psi)!;
    }

    // Poll the production seam until the process is alive and leads the expected group (or give up
    // after 5 s). Python needs a few ms to start before it issues its setpgid, so a single immediate
    // read would race the leader setup.
    private static (int pid, int ppid, long starttime, int pgrp)? WaitForPgrp(int pid, int expected)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            try
            {
                var info = BashTool.ReadStartAndPgrp(pid);
                if (info is { } i && i.pgrp == expected) return info;
            }
            catch { /* gone for a moment — keep polling */ }
            Thread.Sleep(50);
        }
        return null;
    }

    // os.setpgrp() == setpgid(0,0): the child becomes the leader of a fresh group (pgrp == its pid)
    // without forking, and stays in the test host's own session (setsid would not — a group number
    // created in another session cannot be reclaimed here: setpgid across sessions is EPERM). The
    // same number can later be reclaimed with os.setpgid(0, N) in this session, which StartInGroup
    // proves. exec keeps the same pid as the group leader for the whole life.
    private static Process StartOwnGroup()
    {
        var psi = new ProcessStartInfo("/usr/bin/python3") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("import os; os.setpgrp(); os.execv('/bin/sleep', ['sleep', '120'])");
        return Process.Start(psi)!;
    }

    // Reclaim an existing group number: os.setpgid(0, n) moves this process (not a group leader)
    // into group n, then exec keeps the same pid in that group for its whole life.
    private static Process StartInGroup(int pgid)
    {
        var psi = new ProcessStartInfo("/usr/bin/python3") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"import os; os.setpgid(0, {pgid}); os.execv('/bin/sleep', ['sleep', '120'])");
        return Process.Start(psi)!;
    }
    private static int KillGroup(int pgid)
    {
        // The production kill loop's raw group signal (BashTool's Kill is private), surfaced here so
        // the ESRCH refusal is observable: libc kill(-pgid, 9) == 0 on hit, non-zero on ESRCH.
        var kill = typeof(BashTool).GetMethod("Kill", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return (int)kill.Invoke(null, new object[] { -pgid, 9 })!;
    }

    private static string? FdTarget(int pid, int fd)
    {
        var readLink = typeof(BashTool).GetMethod("FdTarget", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return (string?)readLink.Invoke(null, new object[] { $"/proc/{pid}/fd/{fd}" });
    }

    private static async Task<JsonObject> Run(JobTool tool, JsonObject args)
    {
        var result = await tool.ExecuteAsync("t", args, null, CancellationToken.None);
        Assert.False(result.IsError, result.Text);
        return JsonNode.Parse(result.Text)!.AsObject();
    }

    private static async Task<string> Start(JobTool tool, string command)
    {
        var started = await Run(tool, new JsonObject { ["op"] = "start", ["command"] = command });
        return started["id"]!.GetValue<string>();
    }
}
