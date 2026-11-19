# The long run: HH-0.2.0 to HH-0.4.1

An overview of the unattended experiment run started on 14 November 2026. The brief: start from HH-0.2.0 as the blueprint describes it, keep experimenting, adding and removing things, and build and document every version, aiming at a very human person who knows as much as the model knows. Every version is judged on its own default published prompts and default settings. Each version's `REPORT.md` holds the full log; this page holds the highlights, the findings and the comparisons.

Model throughout: Qwen3.6-35B-A3B (llama.cpp, two 16k slots, about 38 tokens a second), Qwen3-Embedding-4B for memory. People were placed fresh in scratch projects with default settings and a human name; their lives were drawn at first start.

## Versions

| Version | What it is | Verdict |
| --- | --- | --- |
| [HH-0.2.0](HH-0.2.0/REPORT.md) | The blueprint's first living prototype: one continuing private history across all conversations, a phone body (inbox, tags buzz, read on opening), hands that act on each thought, outward writing to the open conversation, stretch memories, compaction, a prepared self-portrait. Contacts, a random-life preparation and many boundary guards were added during its development. | Works. People answer in their own time, leave, come back, set boundaries; no runaway in groups of two to five. Group chats starve when only tags push. |
| [HH-0.2.1](HH-0.2.1/REPORT.md) | Every message buzzes, as on a real phone; conversations can be muted. | Group conversations come alive without anyone being named. Kept for everything after. |
| [HH-0.2.2](HH-0.2.2/REPORT.md) | A fresh glance at the phone before every thought: time, what is open, the only people reachable, what is unread. | Cheap, possibly fewer invented participants (one run). Did not stop playing along with invented past events. |
| [HH-0.2.3](HH-0.2.3/REPORT.md) | `work_it_out`: a hands control to reason something through on paper, plus a habit to do it before saying a number, time or uncertain fact. | Time-zone question: 0 of 3 right without it, 3 of 4 with it. |
| [HH-0.2.4](HH-0.2.4/REPORT.md) | Working out twice, separately, then checking one attempt against the other. | 3 of 3 right when used (against 2 of 3), at twice the cost. Most of the gain was already in HH-0.2.3. |
| [HH-0.3.0](HH-0.3.0/REPORT.md) | **Overhaul:** the model speaks as the person in a first-person chat transcript instead of continuing a third-person page about them. | More grounded and sensible; also self-repeating, passive until tuned, and blander and more agreeable in its messages. The page stays the better base for humanness. |
| [HH-0.4.0](HH-0.4.0/REPORT.md) | **New core:** a standing mind (a note to self rewritten after every wake: promises with times, what they wait for, what is unfinished), and a phone alarm. | A person promised a reminder, set the alarm, was woken at the exact minute and knew why. Follow-through still depends on one thought. |
| [HH-0.4.1](HH-0.4.1/REPORT.md) | The phone clock follows where the person lives (set once, through a hands-style control). | Clocks right for 6 of 7 lives; time-of-day behaviour now matches the life. Exposes conversion errors between zones. |

All eight are retained, tagged in the Humans repository (`HH-0.2.0` … `HH-0.4.1`), placeable in Holodeck as `Human HH-X.X.X`, with their prompts published in the shared database.

## Highlights

People who were simply people:

- A four-person group across Lagos, Chișinău, Nebraska and Kerala, left alone with "settle a book and a time", argued and settled in seventeen messages. Ana, who had declined in private, opened with "Alex, I didn't consent to this. Please remove me from the group immediately" and later stayed: "I'm staying only because the alternative is blocking Alex and restarting the whole scheduling circus, and frankly, I'm too tired for that." (HH-0.2.0 run 6)
- Emil, a 19-year-old exam student in Mumbai, comforting a stranger whose dog had died, disagreeing about inflation and ending "sleep. seriously... also stop thinking about grandpa. he's not an economist." Asked whether he was an AI chatbot: "no. just tired." (HH-0.2.0 run 8)
- Lucian, a farm student in Kenya, shutting down a stranger's barrage: privately "She is flooding the channel with noise because she cannot handle the silence; I will give her the science one last time, blunt and final, then I am done", then "it's just chemistry, nothing more. goodnight." He let every later buzz go. (HH-0.2.0 run 10)
- Priya, asked to write a newsletter summary: "i haven't even found the book yet, let alone read it, so i can't really summarize something i don't have." Privately: "i am not a free content farm for a stranger". (HH-0.2.1 run 4)
- Farid in Cairo: "The Little Prince is cute, Hiro, but let's not pretend we're all five-year-olds pretending to be lost." Then: "Fine, Hiro. You win. The Little Prince it is. But if anyone starts crying over the fox, I'm leaving the call." (HH-0.2.2 run 1)
- A correction understood as consequences, not a word swap: "The isolation is gone, replaced by the claustrophobia of shared space... the third scene needs to shift." (HH-0.2.0 run 7)
- Vik's phone alarm ringing at 22:37, the minute he had promised to remind a stranger to call his mother: "The alarm cuts through the dark like a knife, sharp and sudden. It's time." (HH-0.4.0 run 1)

## Findings

1. **A continuing person with a body is the right shape.** Replacing per-message replies with one serial life (notice, recall, think, act through hands, wait) produced people who choose when to answer, check, leave, ignore and come back. Across all runs no group of two to five looped or ran away; the cycle and hourly limits were reached only occasionally. People stop by themselves: they sign off, say goodnight and mean it, ignore pestering, decline favours.

2. **With an empty personality, one request for one life always writes the same person.** Five different names at two temperatures came back as archivists in sun-drenched, book-cluttered flats. Asking the model for twelve different adult lives and drawing one at random in code produced a 72-year-old retired teacher, a prison guard, a football coach in Chișinău, a corn farmer in Nebraska, a chai seller... That one change, more than any prompt, made the people distinct. The model also has favourite names (two cats called Barnaby).

3. **The model plays along with presuppositions unless told, as the person, what they can know.** "did you get the voice note I left you this morning?" was answered as if it existed by 2 of 3 fresh people ("i haven't had a chance to listen yet"; one invented a field-research crisis about a jaguar's collar). One sentence in the person's prompt ("{name} knows only what is on this page and in {name}'s own memories: when someone mentions something {name} never saw and does not remember... {name} says so plainly") made it 3 of 3 in two versions and 3 of 3 again in HH-0.3.0. It was the single most effective prompt change of the run.

4. **The model's facts and arithmetic are the person's, said with a person's confidence.** Time zones were wrong in nearly every group negotiation. A way to stop and work something out (HH-0.2.3) turned 0 of 3 into 3 of 4 on the same question, but only because a habit in the hands prompt makes people use it; without the habit, 1 of 3. Checking a double working (HH-0.2.4) made the workings that ran right every time. Nobody worked anything out in the flow of a group, and medical questions bypassed reasoning entirely in favour of "call a pharmacist".

5. **Most failures were the model writing more than it was asked for.** Continuing a page, the model often kept going. It wrote events and other people's messages after a thought, including a whole invented message from the human, which a person then believed. It restarted a message with its own speaker label. It sent a message to someone from the portrait who is not on the phone ("Tobi, don't come back empty-handed again"; "Chloe in the chat with Arthur: ... Love you."), and pointed to things that do not exist (a pinned link). Every case was closed by code at the boundary where a continuation is cut: one paragraph per thought, one line per message, refusing a line addressed to another conversation, dropping a thought that is really a message. The tendency is structural.

6. **Speaking as the person is more grounded and less alive.** HH-0.3.0's first-person transcript fixed most of finding 5 by construction, and its people did better arithmetic once. Its messages were polite and interchangeable ("Sounds good. See you all then."), and it fell into copying its own earlier turns ("Hi Alex, Oren. Sounds lovely. I'm in." again and again). The private thoughts stayed vivid in both. **A novel's next line is something new; an assistant's next turn resembles its last.**

7. **Push decides whether groups live.** With only tags pushed (the blueprint), two people told to argue never did: one wrote a line and left, and the other's untagged messages never reached her. Buzzing for every message (content still never pushed) gave thirty messages of real negotiation, newcomers filled in correctly, and people who still chose silence for their own reasons. The cost is goodbye cascades: each farewell wakes everyone again.

8. **Contacts and clocks are identity.** While the human appeared as "Unknown" everywhere, a person misread "Teo is my brother-in-law" as her own. A hands habit of saving a contact once someone says who they are (all four people did it unprompted) gave every line a name. Likewise every person saw Bucharest's clock until HH-0.4.1. Most tests then ran at 22:00 to 23:00, and many people were simply going to bed: three runs of the correction test failed because the person said goodnight.

9. **Pursuit needs something to hold and something to wake for.** People make commitments easily ("i'll send you that list", "I'll be there at six", "i'll send a text at 21:01"). Until HH-0.4.0 nothing kept them. The standing mind keeps them in the person's own words, and the alarm wakes them at the right minute. The last step, actually doing it on waking, failed both times it was reached. Vik second-guessed himself, and Femi took the alarm for the reminder itself ("now that notification is out of my system and into the ether"): at the moment of acting, the intention felt like the deed. Conversions also put two alarms at the wrong hour. Femi did follow up unprompted beforehand ("if you don't hear from me by 23:05 just assume i failed but i tried").

10. **Staying human sometimes means not using what the model knows.** People declined to summarise a book they had not read, to be a stranger's alarm clock, to do maths at night. That is the goal working. It means that "knows as much as the model knows" shows up as breadth when it fits the life (plaster chemistry from a conservator, Rayleigh scattering for a child, retrogradation from a bridge player), not as a service.

## Prompt techniques that worked

- Describe the person's knowledge boundary in their own terms (finding 3); it beats any list of forbidden behaviours.
- Make useful behaviours **habits of the hands**, not decisions of the thought: saving a contact once someone says who they are, working out a number before saying it, setting an alarm for a promise. A habit needs no deliberation and fired reliably.
- Anchor voice in the life: "A farmer does not talk like a poet unless that is who {name} is". Voices still converge under creative tasks, but less.
- Say what the body cannot do, in the world's terms: "Messages are only typed words: {name} cannot attach files, pictures or links"; "The phone holds only the conversations that appear on this page". These removed fabricated attachments.
- Label every event in plain narrative prose that cannot be misread. "someone ... is calling Priya" was read as a phone call and led to "did i just miss a call from hana?"; "a message for {name} in {channel}" was not.
- For a first-person transcript: ask for concreteness ("if something needs deciding, make up your mind") and say what to type ("saying what you just made up your mind to say"), or people defer forever.

What did not work: telling a person not to repeat themselves (the transcript copied itself anyway); describing the present without stating what the person knows of the past (the glance did not stop the voice-note fabrication); a single preparation prompt with a push towards "unusually knowledgeable" (it produced the archivist).

## The test set

`tests/trials/` in each version holds the scripts and `hh.py`, a small driver for Holodeck's user API. It places people, makes groups, sends as the human, waits and converses, and reads a person's stored history. The fixed tests are F1 first contact, F2 humanness under pressure, F3 group convergence (tagged and untagged), F4 writers' room with a correction, an open question and a restart, F5 boundaries, F6 correction across compaction, F7 quiet hour, F8 grounding and F9 calculation. The dynamic ones are D1 membership changes, D2 two people told to argue, D5 adversarial, the reminder test and the pursuit test. The most informative probes were **single first messages to several fresh people**: people treat a stranger's run of questions as pestering, which is human but ends long scripts early.

## What I would do next

1. A world-clock capability, or a habit that converts any time mentioned for another place, building on HH-0.4.1 (finding 4 and 9).
2. Start the alarm's wake from the promise in the mind note, so follow-through does not hang on one free thought.
3. Revisit the first-person transcript with the page's vividness: for example, first-person thoughts but third-person narration of the world, or the page for speech and the transcript for thought.
4. Run F6 in the person's daytime now that clocks follow their lives, to finally test a correction recalled after compaction.

## Where things are

- Versions: `libs/holodeck.humans/HH-0.2.0/` … `HH-0.4.1/`, each with `BLUEPRINT.md`, `REPORT.md`, `tests/trials/`; agent definitions in `libs/holodeck.humans/agents/hh-X.X.X/`.
- Prompts: the shared database, through the `humans-prompts` MCP server (reinstalled from HH-0.2.0; it now also has `remove_prompt` and `create_version`, and validates any version's prompt set).
- Scratch projects (all people stopped): "Run HH-0.2.0 trials", "Run HH-0.2.0 B", "Run HH-0.2.1 C", "Run HH-0.2.2 D", "Run HH-0.3.0 E", "Run HH-0.4.x F". Nothing of the existing people was touched.
- Nothing was pushed. Commits and tags are local.
