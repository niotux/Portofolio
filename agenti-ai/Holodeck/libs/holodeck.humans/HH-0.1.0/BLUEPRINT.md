# HH-0.1.0 blueprint

This is the retained page-based conversation-and-memory baseline. Its application source and executable are unchanged by the flat source layout correction.

## Implemented behaviour

An incoming conversation message can trigger recognition, semantic memory searches, prose recollection, private first-person thinking and an outward reply. A durable background queue forms prose memories and embeds them. Conversation contexts are per channel. Older dialogue can be compacted while recent turns stay verbatim. Exchanges and pending deliveries are retained through the existing storage/transport boundaries.

Generation uses OpenAI-compatible Chat Completions with native model reasoning disabled. Thoughts, speech and memories are ordinary prose. Application code owns labels, routing and storage metadata. File-based prompts are used by this baseline; personality is substituted into its page rather than prepared through the proposed startup process.

## Retained boundaries

Existing people, settings, memories, conversations and containers must survive development. No mind taxonomy or prescribed model-authored data format is introduced. Only formed prose memories are embedded. A build does not establish intelligence; judge actual thoughts and conversation.

## Limitations and next work

A single continuing private person across channels, the explicit internal/external body loop, shared hands and tag-only notification routing are not implemented here. Startup personality preparation and before-operation token budgeting remain future work. Database-backed prompt drafts/publications belong to the HH-0.1.1 development project.

This project is a direct sibling checkout under the outer Humans library. It contains no other HH version. Its application source remains recoverable through the original HH-0.1.0 tag; the current layout-only commit removes the former nested packaging without changing that source.
