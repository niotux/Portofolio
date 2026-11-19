> Historical component discussion. Superseded by the current [Humans blueprint](../../../BLUEPRINT.md), [roadmap](../../../WORK_PLAN.md), and [experiments](../../../EXPERIMENTS.md). This archive does not gate prototype implementation.

# Senses and body loop

Design agreed at the behaviour level, including notification queues, visibility and channel focus. Retry handling remains a proposal and detailed presentation and delivery mechanics remain implementation work. This is a design contract; the current runtime has not been replaced. Related representation trials belong in the [experiment register](experiments.md#senses-and-body-loop).

## Agreed behaviour

A tag reports only that the person was tagged in a channel. It does not include the tagged message, sender or a conversation excerpt. The person has not read that material merely because a notification arrived. A tag interrupts the opaque sleep/break and resumes the person's internal process. Ordinary channel notifications accumulate but are never automatically pushed into the person's context, including on waking or in internal mode. Only tags are pushed. Ordinary notifications are requestable through the hands in either mode, and checking them is part of the person's routine. They do not themselves interrupt the break.

The person can remember what the channel concerned, consider its importance and choose whether to visit and read it. Being tagged does not force reading or replying. Memory, Logic and the other faculties may participate; their internal mechanisms are not settled here.

Notifications arriving during work also accumulate until the person can focus on them. Tags have priority over ordinary notifications and can be seen even in external mode while another channel is active. Seeing a tag does not automatically switch channels or force an immediate reply. An incoming event does not discard the current operation or start a second person loop.

In either mode, ordinary notifications become visible when the person requests them through the hands. The routine must include checking them; its timing and prompt mechanism belong to the continuing design, not to an automatic notification push.

The person has internal and external modes. These are perspectives of the same person with the same personality and continuity. Prompts are separated where a loop stage requires its own prompt. A prompt that works unchanged in both modes can be shared, including Memory or Logic prompts where appropriate. There is no requirement to duplicate every prompt per mode.

The hands are the same in both modes, and the same capabilities are available subject to the agent's actual access. Mode is not a tool-permission gate. The person knows the available capabilities; the hands know their precise calling mechanics.

The agreed interaction is:

1. During the internal loop, a tag wakes the person with only its channel notification.
2. Internal consideration can lead to an intention to visit that channel, or to leaving it alone.
3. The hands perform the channel-opening operation. Opening the channel establishes external mode and that channel as the active conversation.
4. The person receives the conversation view actually read, including the tagged message, and can think about it or investigate further.
5. The writing stage's output is the outward message on the active conversation. The model does not call a send-message tool.
6. When the person considers the visit finished and intends to leave, the hands perform the exit, returning them to internal mode.

Private thought, Memory and Logic outputs are not publicly posted. External mode changes the conversational setting; it does not make every Core's prose public. Only the stage producing outward writing supplies the channel message. In internal mode, ordinary internal output remains private.

## Boundary

The body receives channel notifications, tags and operation results and introduces textual experience into the ongoing person. It resumes execution on a tag and maintains the current internal/external mode and conversation focus in application state. Notifications queue while the person is occupied. The body pushes tags and exposes the ordinary queue only on request in either mode. Tags have priority. The routine for checking ordinary notifications belongs to the person's prompt-controlled behaviour. Technical routing identifiers remain in code and storage rather than becoming ordinary speech.

The hands interpret an intention and execute the available operation. They remain callable in either mode. They return the actual experience and result; tool-operation syntax is not retained as thought. A wish to open a channel is not itself evidence that its contents were read.

The body routes outward writing to the intended active conversation. There is no extra model-authored send operation. Conversation focus is explicit application state. It changes through the hands' channel-switch or exit operation, never merely because another notification arrived. Outward writing uses that state. Precise handling of concurrent operations and delivery remains implementation work.

This discussion establishes interfaces with Hands and Response but does not finish their designs. Choosing what deserves consideration remains a responsibility of thinking for now; a separate Attention Core is deferred. Memory supplies recalled understanding; Logic/Thought develop it. Their mechanisms remain separate discussions. Sleep remains an opaque break.

## Proposed mechanism

Use application state to represent internal/external mode and the active channel. Opening a conversation through the hands changes focus and the corresponding context presentation; exiting returns to internal mode. Tags can be presented during external work without changing this state. Select the already prepared stage prompt needed for the next operation. A perspective change does not regenerate prompts or change the person.

Present internal notifications as limited experiences. Present external conversation as actual read material with enough distinction to know its source and extent. How much internal context accompanies the person into the conversation is undecided and relates to the [context-voice experiment](experiments.md#c1-preserve-the-generated-context-voice).

The writing operation produces ordinary prose. The harness handles its delivery to the active conversation as the consequence of outward writing; the model does not emit a send-tool operation. The process still needs an explicit distinction in code between internal stage output and outward writing. No prescribed model-produced routing record is introduced.

The queue and visibility rules do not require interrupting an in-flight model generation to insert text into it. How pending tags enter the next suitable context update remains an implementation detail; arrival alone does not change channel focus or discard current work.

Retry handling is provisional. A temporary model, transport or tool failure can be retried while preserving context, pending work and focus. Some failures cannot be repaired by waiting, such as a deleted channel or revoked access; these should return an actual failed experience to the person rather than retry indefinitely. A failed operation must not be treated as a successful focus change or delivery. The precise policy is still open, including avoiding duplicate outward writing when a delivery result is uncertain.

These mechanisms do not impose a complete Core execution order or settle how intentions are recognised by the hands.

## Open questions

The behavioural discussion is complete. Remaining details belong to implementation, experiments or later faculties:

- Notification wording, how requested ordinary notifications fit within available context, and how pending tags enter context during ongoing work. Only tags are pushed; ordinary notifications are requestable in either mode. The routine for requesting them remains to be designed.
- Which capabilities and current-location information remain present or discoverable in context, and how they are presented.
- What initial conversation material opening a channel reveals and how the person requests more.
- How internal continuity accompanies a conversation visit and is resumed afterward; the [context-voice experiment](experiments.md#c1-preserve-the-generated-context-voice) spans this question.
- The provisional retry policy: temporary versus non-recoverable failures, model stalls and uncertain delivery results. No infinite retry policy has been agreed.
- Detailed focus-transition and delivery mechanics, including operations already in flight. Focus itself is explicit state and notifications never switch it automatically.
- Distinguishing outward writing from private thinking in streaming and incomplete generations, to be handled with Response and delivery.
