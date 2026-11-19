> Historical component discussion. Superseded by the current [Humans blueprint](../../../BLUEPRINT.md), [roadmap](../../../WORK_PLAN.md), and [experiments](../../../EXPERIMENTS.md). This archive does not gate prototype implementation.

# Context and continuity

Design agreed: classic token budgeting and compaction, preserving as much context as possible. This records the accepted approach; the new component abstraction has not been implemented. Exact implementation details remain open. The [context-voice experiment](experiments.md#c1-preserve-the-generated-context-voice) is tracked separately from capacity management.

## Agreed behaviour

- Manage actual model capacity rather than an approximate character or message count.
- Prefer retaining as much context as possible. A fixed fraction of the window is not yet an agreed reduction trigger.
- Account for the complete model input, including persistent speaker grounding, any request-specific instruction, current context and the new material being considered.
- Allow room for the next generation. When an operation cannot fit, compact older context and retain recent context, keeping as much useful material as possible. Exact boundaries and prompt wording are not yet selected.

The immediate situation is an incoming tagged message while the person already has substantial context. Context establishes how much room the next operation requires. If it cannot fit, classic compaction makes room while preserving continuity. The precise amount compacted and the protected material remain implementation choices.

Technical constraint: model input and newly generated tokens occupy the same usable context window. The active server allocation for one request can differ from the model's advertised maximum and from the server's total allocation across concurrent requests. A token is a tokenizer unit, not a character, word or message. Physical admission within the window also does not guarantee that every detail will be used equally well.

## Boundary

The accepted responsibility is management of what fits in the real model capacity, including classic compaction when room is needed. The precise interface to the other Cores is open. Experiments in the voice or presentation of context do not expand this responsibility into a design for the other faculties.

For a capacity check, the proposed inputs are the exact request to be sent, the model/server capacity information and the generation allowance for that operation. The proposed contribution is a capacity assessment before generation, with room available to the request. The representation of that assessment belongs in application code, not in a prescribed model output.

Context management concerns the material included in an operation. Excluding something from a request does not by itself authorize deleting its durable record. Ownership of the ongoing prose record, summaries and links to material outside the active window remains to be settled with Memory and Thought/Logic.

## Proposed mechanism

### Discover usable capacity

Read llama.cpp's `GET /props`, using `default_generation_settings.n_ctx` as the reported capacity for a slot. When exposed, `GET /slots` also reports `n_ctx` for each slot. Read the structured field rather than accepting the first property called `n_ctx` anywhere in a response. Do not assume a permanent slot belongs to a particular person or compute capacity solely from launch arguments.

Capacity must be refreshed when the serving model or its configuration changes. How to establish freshness and how to behave when capacity cannot be queried are open decisions.

### Count the actual request

Use the loaded model's tokenizer. For plain text continuation, count the fully assembled prompt with the same special-token handling as the completion route. For chat requests, use `POST /v1/chat/completions/input_tokens` with the same request body and options as the eventual completion. This runs the server's actual chat processing and returns `input_tokens` without generation. `POST /apply-template` followed by `POST /tokenize` is an alternative when exact equivalence to the eventual request has been established. Tokenizing only message bodies omits template and special-token overhead. Exact equivalence for the chosen route and request options must be established before implementation.

Count the final assembled text. Counts of fragments cannot always be added exactly because tokenization can change at their boundaries. Every subsequent Core contribution changes the next request, so the check belongs at the boundary of every model operation, not only when an external message arrives. Cached tokens remain part of the logical prompt length even when the server can reuse their computation.

### Define fits for the next operation

Let `C` be the usable capacity for the request, `P` the token count of its entire input, `G` the generation allowance, and `H` any documented boundary allowance required by the backend:

```text
P + G + H <= C
```

This is the proposed admission contract for completing that generation without context eviction. Merely accepting the input is a weaker condition. A full input leaves no room for continued thought or speech. Generation allowance is tied to the next operation; it is not an agreed blanket reservation for every later Core. The installed release rejects an ordinary prompt whose token count reaches the slot capacity, and its generation boundary also requires room. Use an explicit boundary allowance and verify its exact value during implementation rather than treating equality to capacity as safe.

If the request does not fit, use classic compaction rather than silently relying on backend truncation or context shifting. Summarize older context while retaining recent material and as much useful context as practical. Keep speaker grounding present and durable source material available. The exact compaction boundary, summary instruction and handling of an exceptionally large new input remain implementation details to settle.

Capacity bookkeeping does not settle what the model can reason with reliably at a given context length. That behavioural question requires separate assessment and must not be presented as an API-reported number.

### Technical references

- [llama.cpp server API for the installed release](https://github.com/ggml-org/llama.cpp/blob/b11854/tools/server/README.md): properties, slots, tokenizer, chat template and completion interfaces.
- [llama.cpp server context](https://github.com/ggml-org/llama.cpp/blob/b11854/tools/server/server-context.cpp): effective slot capacity and request admission behaviour.
- [llama.cpp route registration](https://github.com/ggml-org/llama.cpp/blob/b11854/tools/server/server.cpp#L312-L317): chat input-token counting endpoint.
- [llama.cpp input-token count implementation](https://github.com/ggml-org/llama.cpp/blob/b11854/tools/server/server-context.cpp#L6997-L7047): chat parsing and tokenization before generation.
- [llama.cpp server routes](https://github.com/ggml-org/llama.cpp/blob/b11854/tools/server/server-routes.cpp): tokenizer and template endpoint behaviour.

## Open questions

The component discussion is complete at the classic-management level. These details remain for implementation or the relevant later component:

- Exact compaction boundary, summary wording and material that must remain verbatim.
- Generation allowances, interruption headroom and handling an operation that needs further continuation.
- Handling a new input that is too large even after ordinary compaction; fitting the compaction request itself.
- Capacity discovery freshness, endpoint failures and a server change during request preparation.
- The boundary with Memory's durable retention and Thought/Logic's current understanding.
- The [planned context-voice experiment](experiments.md#c1-preserve-the-generated-context-voice), which concerns representation and spans other faculties as well.
