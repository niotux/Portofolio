# Work plan

Direction: [ARCHITECTURE.md](ARCHITECTURE.md) for Holodeck, [Humans blueprint](libs/holodeck.humans/HH-0.1.1/BLUEPRINT.md) for the person, [writers' room](docs/writers-room.md) for its intended writing use, and [Creative Studio](docs/creative-agent.md) for the image and video agent. [The current Person implementation](docs/show-writing.md) remains a working baseline.

## Humans prototype

Humans owns its source and development cycle in the independent repository at `libs/holodeck.humans/`. Its [blueprint](libs/holodeck.humans/HH-0.1.1/BLUEPRINT.md) defines the current system and its [roadmap](libs/holodeck.humans/HH-0.1.1/WORK_PLAN.md) lays out the next live prototype. Challenge, test, record observations, improve; unresolved nonessential mechanisms belong to the [experiment backlog](libs/holodeck.humans/HH-0.1.1/EXPERIMENTS.md).

Retain every HH-X.X.X version as a separately runnable agent with its own code blueprint, frozen bootstrap assets and complete source worktree. A shared database catalog holds that HH version's prompt drafts and immutable publications. Existing instances remain on their current code version; prompt selection is shared across its instances and changes only at operation boundaries. Holodeck only loads the library contract; it does not own the person's cognition. The library package version remains independent of the HH release identity so exact Show Writing dependencies stay valid.

Platform verification remains the C# suite and room browser check when platform behaviour changes. Humans runs its own solution and tests. Preserve existing instances, conversations, memories and environments.

## Library environments and applets

Status: implemented. Every library with agents declares `Runtime`; Humans, Show Writing and Creative Studio run their agents in it, and `holodeck.utilities` adds the first applet. Per-agent containers and container-based `Services` were removed and the existing agents migrated. The built-in applet implementations remain unchanged.

### Execution and ownership

One persistent container belongs to each project and exact runtime library version. Services and agents run as separate non-root users with private homes, working directories, credentials and process control. Ordinary child processes inherit their owner's user. The trusted supervisor alone retains the narrow privileges needed to assign users and stop their process trees; workload capability sets are empty and privilege escalation is disabled, except for a process the user granted sudo. Holodeck keeps those grants per environment and sends them to the supervisor, which writes one sudoers entry per granted uid, launches only granted processes without no-new-privileges, and stops their sudo commands with them. Environments created before sudo are recreated once: when stopped, or when a grant first needs them. Library code and direct dependency packages are read-only.

`Runtime.Processes` declares services independently of agents. Project activation starts them even with no agents or open applets. Activation and reference changes are serialized per project, environment operations per library; repeated opens reuse the environment. One library's startup failure is reported without preventing the project from opening. Explicit execution intent persists across Holodeck restarts; browser disconnection and project navigation never own process lifetime. Explicit project stop ends its environments. Host reboot restarts services when Holodeck reactivates the project; registered agents remain stopped. Services that exit are reported for inspection rather than blindly restarted.

Agents retain their own settings, private credentials, logs, file transport, stopped/running state and reset semantics. Their canonical home/table paths point into the shared environment's private storage, with access granted to the platform group. Start, Stop, exit and Delete affect only that agent's process tree. Deleting the last agent never removes its environment. Reference removal stops the affected library and unavailable dependents without erasing data. Project deletion removes attached and detached environments while retaining disk files.

The runtime uses the installation's trusted prepared image; workloads can install tools only into their own user environments. A container-based `Services` declaration is refused. An agent's registration fixes its library and launch; existing people, memories and files are never recreated automatically.

### Applet contract

Libraries declare their HTML entry points, window metadata and allowed services. Holodeck discovers them from project references and mounts sandboxed content inside native windows. Frontend-only applets need no runtime. The bridge supplies theme values, saved state, stable window identity, bounded HTTP requests (including binary content) and text WebSockets. Applets cannot access the host DOM or management credentials. Cross-origin browser mutation requests are refused by the user API. Services authenticate proxy requests using a separate private bearer credential for each service.

An applet may register a resource for cleanup on explicit window Close. That ownership is saved before resource creation, and Close waits for outstanding creation requests before cleanup. Browser teardown only disconnects the view. These are optional application capabilities, not a required session model.

`holodeck.utilities` supplies the Bash example. Its manager starts with the project and creates one shell per applet window. A new window creates a new shell; explicit window Close terminates that shell and its terminal jobs. Reloading, closing the browser, minimizing or navigating away preserves the shell for the saved window. Shell creation and cleanup use an idempotent window identity. Bash runs inside Utilities' environment, not on the host. Environment shutdown ends live shells; persistent files remain.

Bash renders with WebGL when available and falls back to DOM rendering if initialization fails or the GPU context cannot recover. Consecutive output packets are batched without crossing size changes; cursor following runs with rendering. Its renderer check is `tests/bash-terminal-browser-check.cjs`.

### Epics and remaining work

| Task | Dependency | Completion check | State |
| --- | --- | --- | --- |
| Define environment/process packaging | Library manifest | Trusted image, named processes, distinct ports, readiness and private credentials are explicit | Implemented |
| Establish user isolation and supervisor | Runtime packaging | Distinct users cannot read peer credentials/private files, signal peers or gain privileges; stopping one leaves others running | Verified in a disposable Docker environment |
| Add activation, recovery and cleanup | Supervisor | Applet-only libraries start, concurrent opens reuse execution, shutdown reconnects, stopped intent persists, and project deletion includes detached environments | Implemented and checked |
| Run all agents in shared environments | Supervisor and file access | Agents have private users, stable identities, live settings, file exchange, independent stop/start, logs including final output, and home reset | Verified through the live platform |
| Discover and host library applets | Manifest and scoped service access | Library windows restore and fit desktop/phone without adding built-in imports; opaque content cannot access the shell directly | Verified through the Utilities applet |
| Exercise window-owned Bash shells | Applet bridge and manager | Two windows have independent shells; reload preserves them and Close kills only its owned shell/jobs | Verified in the browser |
| Retire per-agent containers | All agent libraries declare Runtime | Existing agents keep ids, settings, tables and memories in their environments; container runtime and `Services` code are gone | Done |
| Add per-agent external grants | Private storage and endpoint ownership | Folder/port grants reach only the intended process without exposing other users' grants | Planned; currently refused explicitly |

Next milestone: user review of the Utilities Bash applet and the migrated agents, then per-agent external grants.

Aggregate CPU, memory and process budgets apply to the library container; workload users also have process/open-file limits. Independent per-agent memory/CPU cgroups remain future work. Users inside a container share a network namespace, so service endpoints require authentication and distinct ports. No blanket folder-grant union is permitted. A conventional desktop GUI program still needs a remote-display frontend.

Platform checks remain the C# suite and `tests/room-browser-check.cjs`. `runtime/library/check.py` covers adversarial user/launcher isolation and recovery. `tests/computer-check.cjs` covers what an agent can see and reach, crash output and Docker reconnection. `tests/library-environment-check.cjs` covers real shared-agent execution, private access, transport and lifecycle. `tests/library-applet-browser-check.cjs` covers discovery, terminal input, saved ownership, browser disconnect, explicit cleanup, independent projects and phone behavior. Bash's focused backend checks live beside its service source.

## Creative agent tasks

| Task | Dependency | Completion check | State |
| --- | --- | --- | --- |
| Establish ownership and GPU phases | Current platform and gateway contracts | Design separates existing infrastructure from library behavior and generic platform additions | Implemented for the first image path |
| Inventory candidate artifacts and nodes | Model shortlist | Exact compatible weights, encoders, nodes and licenses are identified before trial installation | Anima workflow installed; other families pending |
| Compare still-image candidates | Artifact inventory | User can compare realistic and anime results with saved prompts, input references and measured loading/generation costs | First anime workflow renders; broader comparison pending |
| Verify GPU handoff and job recovery | Existing Holodeck inbox and gateway coordination | Agent consumes messages in LLM phases, waits through ComfyUI work, and recovers a job without duplicate submission | Verified for the first image path, including agent restart during rendering |
| Build notebook and first image workflow | Selected image candidate and durable execution | Agent infers useful freeform notes, retrieves descriptive matches, opens linked workspace files selectively and reuses a reference after restart | Verified through linked-file inspection, retained notes and generation after restart |
| Add prompt tuning and the remaining families | First complete reference-to-result loop | Revisions can be compared and rolled back; both image and both video families have locally viable workflows | Prompt draft/publish/rollback implemented; remaining workflows planned |

The next milestone is a realistic image workflow and comparison against the anime path, followed by direct reference conditioning. Video remains dependent on local model feasibility.

Gateway status UI, richer Holodeck rendering and application-operated cancellation are pinned. The initial model tool set excludes render cancellation. All prompts, including notebook and context assembly instructions, remain editable and versioned.

## Other platform work

- A Work applet that shows the selected agent's table, joined to linked selection.
- Remote agents: file upload/download over HTTP so an agent on another machine can send and receive files.
- A Claude Code agent.
- Export/import of a project with its room.
- Repair empty-workspace restoration after closing all desktop windows.

## Deferred llama reload experiment

Status: deferred until the user returns to it. The aim is to shorten LLM reloads after ComfyUI releases the GPU. This experiment belongs to the standalone AI stack; it does not require changes to Holodeck or the creative agent.

Compare four cases while holding the model artifacts, server build, context, GPU offload and test request constant:

1. Establish the existing hard-drive loading baseline.
2. Enable memory mapping with the same files on the same drive, isolating the loading-mode change.
3. Load an identical copy of the weights and multimodal projector from an SSD with the original loading mode, isolating storage speed.
4. Combine SSD storage and memory mapping.

For each case, distinguish first loads from repeated loads with potentially cached file pages. Measure server initialization to readiness, readiness to first token, and the complete ComfyUI-to-LLM handoff separately. Observe disk reads, host RAM, swap activity and GPU memory so a faster startup is not mistaken for merely postponing the cost until the first request. Compare repeated samples and retain normal inference behavior as a completion check.

Keeping file pages resident through the OS cache is a candidate benefit of memory mapping, not a guarantee. Consider explicit RAM locking only as a separate follow-up after confirming enough headroom for the LLM, ComfyUI and other processes. The prompt/context cache is separate from model-weight residency.

Before testing, preserve the AI stack configuration and keep a reversible baseline. Retain the original model files, avoid disruptive global cache clearing, and change one factor at a time. Record measurements in experiment artifacts rather than this roadmap. No configuration changes, model copies or restarts are authorized by this deferred note.
