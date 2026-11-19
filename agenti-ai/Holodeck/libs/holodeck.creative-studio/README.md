# Creative Studio

A conversational visual collaborator with a freeform prose notebook, selected image inspection and versioned prompts. The first executable workflow is Anima Turbo text-to-image. Realistic images and both video families remain planned.

## Use in Holodeck

Attach Creative Studio to a project in Library Explorer, place Studio in Team, open its channel and start it. The model, embedding and ComfyUI connections are instance settings; changing connections takes effect after Stop and Start. The external AI stack supplies these services. The library does not start another database or GPU service.

Share an image or text file and discuss what matters about it. The agent can open a selected file, keep descriptive notebook entries and search them later. Received and generated files stay in its workspace; the notebook and recent-file index store their paths. A small recent selection remains available for later inspection, without loading every file into context. Notebook retrieval is scoped to the current channel.

Ask it to make an anime image when you want to render. It prepares a complete prompt while the LLM is available, saves the request, then waits for ComfyUI without consuming further messages for model work. Completed images are copied into the agent's workspace and delivered through the channel.

The first workflow accepts a written prompt. An inspected image can inform that description, but the graph does not directly condition on an input image and does not guarantee exact face, pose or identity preservation.

## Prompts and revisions

All authored prompts are under `prompts/`, registered by purpose in `prompts/registry.json`. Tool control schemas are under `controls/`. Workflow graphs and artifact manifests are under `workflows/`.

Team's Settings tab exposes these library actions:

- **List prompt purposes** shows registered purposes and their saved revisions.
- **Read prompt** returns the current content and revision for a purpose.
- **Save prompt draft** creates an immutable draft without changing active behavior.
- **Publish prompt revision** selects that revision for future work. Select an earlier revision to roll back.
- **Inspect image requests** shows retained execution states and errors.
- **Retry uncertain generation** explicitly resumes the original request after checking ComfyUI for existing work.

Defaults are seeded from the library on startup. New file content creates a new revision. A purpose follows library updates until a revision is explicitly selected for that instance. Running image jobs keep the prompt and graph snapshots captured when prepared.

## Retained work

The agent uses its own normal PostgreSQL schema through Holodeck's database API. It retains input receipts, conversation turns, compact prose briefs, notebook entries and revisions, prompt revisions, generation requests and an outbox. Only formed notebook descriptions enter the semantic-search corpus.

Files live in the workspace. Selected image previews are generated on the CPU at a bounded size; originals remain untouched. Context includes a small recent window, a compact brief and selected notebook experiences. Model usage is printed in the agent log.

Generation submission has a caller-assigned identity. After a disconnect, the worker reconciles ComfyUI queue/history before collecting an existing result. An uncertain missing request is retained for explicit review rather than automatically submitted again. A render cancellation tool is outside the initial model tool set.

## Build and exercise

```bash
bash agents/CreativeStudio/publish.sh
node tests/creative-studio-check.cjs
```

The live check uses the running default-context stack and real inference services. It creates a temporary project, checks prompt revision operations, retains a notebook entry across Stop/Start, renders an image and removes its test project. Set `CREATIVE_STUDIO_REFERENCE` to an image path to exercise linked-file inspection. Set `CREATIVE_STUDIO_KEEP=1` to retain the resulting project as Creative Studio.

The workflow's `Workflow.json` records the required model artifacts, pinned source revisions and hashes. Install those artifacts in the AI stack's ComfyUI model directory. Model and node installation belongs to that stack; this library does not download or change them at agent startup.
