# Show Writing

Writing and bench roles built on Person from **Holodeck Humans**. This package declares an exact local dependency on `holodeck.humans` and keeps the writing collaborator, Victor, Ana, Constantin and Dorin definitions with their independent settings.

Person’s executable, prompts and runtime allowances belong to Humans. This package owns role biographies and backstory memories under `roles/<role>/`. A new role instance launches the dependency’s Person runtime with its role name and the Humans asset root. Conversation and memory remain in each instance’s own schema of Holodeck’s PostgreSQL.

Explicitly refresh the development implementation and writing compatibility assets with `bash agents/ShowWriting/publish.sh` from Holodeck. Retained HH releases are published separately inside Humans and do not upgrade writing agents. That compatibility entry point forwards to the explicit legacy publisher in Humans. Writing agents launch the Humans dependency's `runtime/Person`; the publisher also generates this package's `runtime/ShowWriting`, `prompts/` and `runtime.json` copies. Edit the canonical assets in Humans instead. An explicit restart loads published program changes without resetting memories.

How Person works: [the design](../../docs/show-writing.md). Generic Person package: [Holodeck Humans](../holodeck.humans/README.md).
