// CodeIntel service options. The KB stays the single source of identity: the service asks it for
// /whoami with the caller's bearer token, exactly like AiFactory.Chat's KbIdentityResolver (60s
// valid / 10s negative cache).
namespace PiSharp.CodeIntel;

public sealed class CodeIntelOptions
{
    /// <summary>Base URL of the knowledge base, e.g. http://192.168.8.81:8081.</summary>
    public string KbUrl { get; set; } = "http://192.168.8.81:8081";

    /// <summary>Root folder where shadow workspaces are cloned, e.g. /var/lib/codeintel/workspaces.</summary>
    public string WorkspaceRoot { get; set; } = "/var/lib/codeintel/workspaces";

    /// <summary>Default language server: "csharp-ls" or "roslyn". Slice 1 ships csharp-ls; the command is injected for tests.</summary>
    public string LsKind { get; set; } = "csharp-ls";

    /// <summary>How long to wait for the LS to become ready (first $/progress end), before giving up.</summary>
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Per-request navigation cap, carried over from the design research.</summary>
    public TimeSpan NavigationTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>git worktree restore cap.</summary>
    public TimeSpan RestoreTimeout { get; set; } = TimeSpan.FromSeconds(180);
}

/// <summary>Service-level knobs the deployment or the test suite overrides.</summary>
public sealed class CodeIntelServiceOptions
{
    /// <summary>Max live language-server processes; full table -> 429 with retry_after.</summary>
    public int MaxLive { get; set; } = 3;

    /// <summary>Idle TTL before a slot is evicted.</summary>
    public TimeSpan IdleTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How the LS process is started. null = built-in (dotnet tool run csharp-language-server
    /// or the CODEINTEL_LS_*_COMMAND env override). Tests inject a fake in-process LS here.</summary>
    public System.Func<string, System.Diagnostics.Process>? StartProcess { get; set; }
}
