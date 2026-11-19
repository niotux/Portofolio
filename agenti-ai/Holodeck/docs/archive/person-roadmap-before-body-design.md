# Superseded Person roadmap

Historical roadmap before the component-by-component body design. It is not active implementation guidance. Current direction: [Person component design](../person-design/README.md). Platform and Creative Studio work remain in the active [work plan](../../WORK_PLAN.md).

# Work plan

Direction: [ARCHITECTURE.md](../../ARCHITECTURE.md) for the platform, [docs/writers-room.md](../../docs/writers-room.md) for the writing room, and [docs/creative-agent.md](../../docs/creative-agent.md) for the proposed image and video agent. How the current writing agent works: [docs/show-writing.md](../../docs/show-writing.md).

**Working mode: prototype fast.** Build and publish, then let the user play and judge through the Thoughts cards. Preserve the existing people and memories; use `node tests/bench/reset-people.cjs` only when a fresh bench is explicitly wanted. Add a test only where data, transport or persistence could silently break. Platform (Holodeck) changes run the C# suite and the room browser check.

## You are here

The reusable Person now belongs to Holodeck Humans. Show Writing keeps its writing and bench roles and declares an exact local dependency on Humans. Existing agent containers, settings and memories remain in place.

| Task | Dependency | Completion check | State |
| --- | --- | --- | --- |
| Extract Person source and authored assets | Existing Person implementation | Humans exposes Person with the same prose and memory behavior | Verified |
| Make dependency files available | Exact local library resolution | Only declared dependencies mount read-only and resolve launch paths | Verified |
| Publish and retain existing agents | Extraction and dependency mounts | New Person and writing instances start; existing container IDs and settings remain | Verified |
| Align guidance and verify | Working packages | C# suite and focused live checks pass; roadmap reflects ownership | Verified |

The library extraction is complete. The next Person behavior increment belongs in Humans and must remain usable through the writing roles; E1 remains the active behavior work below.

Creative Studio remains available as the Creative Studio project. One conversational agent will own a flexible prose notebook with linked files and selective context, versioned prompts and four ComfyUI workflow families: realistic images, realistic video, anime images and anime video. The proposed ownership, GPU phases and milestones are in [docs/creative-agent.md](../../docs/creative-agent.md). The separate library and runtime are implemented and verified through the first live anime image path. Realistic images, direct reference conditioning and both video families remain planned.

E1b remains in play. The Base Person continues a page (chat log with bracketed private thoughts); memory lives in the agent's own schema of Holodeck's PostgreSQL with pgvector recall. The user plays project Bench with Victor, Ana, Constantin and Dorin and says what feels wrong.

Pinned (the user will review it before any change): how memory is prompted, in particular the "What to remember" pass, which asks for a capped list and parses it.

## Epics

| Epic | Pokeable result | Tasks | Status |
| --- | --- | --- | --- |
| E1 Base Person: a mind, not a conveyor | Thoughts cards show the person remembering, thinking and speaking, with what is carried into the next turn | Residue and standing concerns carried and resurfacing; persona and prompts as biography plus situation; mood as slow prose changed by occasional reflection | In progress |
| E1b Bench | The user plays the four people and reports what feels wrong | Roles under `libs/holodeck.show-writing/roles/`, personalities in settings, project Bench, replay and reset scripts in `tests/bench/` | In play |
| E2 Personality as genetics | Two instances of the same Base Person with different personality files behave visibly differently on the same message | Personality shapes biography and how the mind is framed; library layout for base vs. personality | Not started |
| E3 Other minds | The person stops re-asking what was answered and follows the user's energy | Prose portrait per participant, formed in the background like memories | Not started |
| E4 Initiative and sleep | The person comes back with "I've been thinking about..." after idle time | Idle time in the agent loop: consolidation, linking, preparing questions; urges to speak arise from residue | Not started |
| E5 The hands and the board | A person pins something, the board agent's files change, and only "I pinned..." stays in the person's context | Hands: focused pass with the tool's controls in attention, operation out, prose experience back, call forgotten. Board agent with cards as files, JSON operations in, standing and links. A way for the user to see the board and confirm canon | Not started |
| E6 Specialization and the desk | The physicist sits at an instrument, works a number, returns to thinking with it | Specialization package (biography, concerns, owned board area, reference material, instruments) operated through the hands | Not started |
| E7 The room | Universe stage with a facilitator and specialists drawing the user out, one question at a time | Facilitator; floor bidding and silence; stages; change propagation through owners; homework between sessions | Not started |

## Decisions

- **D1 Board.** The board is another agent with its own files; it receives JSON operations.
- **D2 Tools.** Tool use goes through the hands: attention focuses on the tool, a focused pass emits the call, the person keeps only the prose experience. Thinking and speech stay prose-only.
- **D3 Cost (open).** Several people times several passes per message on one local llama. Shallow-first (cheap noticing for everyone, deep thinking only when roused) is the plan; the llama server's parallel slots need checking before E7.
- **D4 Storage.** Agents keep all their data in Holodeck's one pgvector PostgreSQL, each in its own schema through the agent database API. No local databases.

## Platform lifecycle

An agent owns one container from placement until deletion. Start, Stop and exit retain it; AStudio reconnects after its own restart and leaves agents running during shutdown. Stable container credentials are authorized only while running; clearing the delivery session on stop preserves unfinished-packet replay.

The persistent lifecycle is implemented. Writing-agent transport and background memory formation retry through AStudio outages without exiting or abandoning claimed work. The placement API accepts optional computer grants before container creation. The next milestone is exposing that choice in the placement UI or supporting grants that change without replacement. Completion means a user can choose folder and port access when placing an agent, then Start and Stop preserve the same container and its files. This depends on the persistent lifecycle; Docker currently fixes mounts, ports and launch commands when creating a container, so changes to existing grants and definitions return a conflict.

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
