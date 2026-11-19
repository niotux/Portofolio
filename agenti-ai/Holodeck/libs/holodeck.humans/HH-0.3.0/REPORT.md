# HH-0.3.0 report

The experiment log for HH-0.3.0, the first architectural overhaul of the run. HH-0.2.x had the model *continue a third-person page about the person* ("Mara in the chat with Alex:", "[Mara thinks:"), so every thought and message was the model writing the next lines of a document. HH-0.3.0 has the model *speak as the person* in a native chat transcript. The system turn says who they are ("You are {name}... a real person, not an AI, not an assistant and not a character in a story"). What reaches them is the other side: messages, buzzes, memories, the passing of time, what their hands did. Their own thoughts (in brackets) and messages are their own turns. A pass ends where their turn ends. Body, hands, memory, preparation, buzzing and the glance at the phone are HH-0.2.2's. See [BLUEPRINT.md](BLUEPRINT.md).

## Why this version

HH-0.2.x's most common failures were the model writing more page than asked: invented page lines after a thought, speaker labels inside messages, messages to absent people, things that do not exist. Each was closed by a guard in code, but the tendency was structural: a document continuer wants to keep writing the document. The hypothesis: a person speaking in their own turns stops where their turn stops.

## Log

### Run 1: things that never happened (F8 single probe, Kit, Lev, Mae)

- 3 of 3 grounded on the first attempt. Lev: "I didn't receive a voice note this morning." Mae: "I didn't receive any voice note this morning. My phone has been silent since I started weaving."
- The thoughts stayed inside the person and the moment. Kit, a 72-year-old widow tending a community garden in Tasmania: "It's late for a text, especially from a number I don't recognize. I hope it's not some scammer trying to trick me into sending money, though at my age, they usually think I'm an easy mark."
- **New failure: repetition.** Kit sent "I'm sorry, but I don't recognize that number." three times, with a straight apostrophe, a curly one, and "Hello, I don't recognize this number." The hands chose to write even after "I'll block it and go to bed". The message retries then produced variants that slipped past an exact repeat check.

Changes (no bump):
- One attempt per message, never a retry.
- Repeats are compared on letters and digits only.
- A message that comes out empty or repeated is not sent, and the person experiences "{name} starts typing in {channel}, then deletes it."

### Run 2: four people, untagged brief (F3: Oren, Pia, Quinn, Rosa)

- **Failure: stall.** Twenty minutes of "Hi Alex, Quinn. Sounds lovely. I'm in." / "Hi Oren." / "I'm in too." / "I'm in." No book and no time was ever proposed, and Pia and Rosa only watched. Quinn's thoughts circled ("I need to decide on a book for the group, something that won't feel like a chore") while her message passes kept returning her earlier message. Reproducing her speech pass three times gave "Hi Alex, Oren." / "Hi Alex, Oren. Sounds lovely. I'm in." twice. Two alternative cues and a variant that moved her own messages to the world side of the transcript still repeated. **A chat model speaking as itself copies its own earlier turns.** The third-person page never did, because a novel's next line is something new.
- Thoughts had become passive: deciding was always something to do later.

Prompt changes (no bump): `think.txt` asks for concreteness ("if something needs deciding, make up your mind; if you mean to say something, know what it is"); `speak.txt` says to type "what you just made up your mind to say" and never to resend.

### Run 3: the same brief after tuning (Sven, Tala, Umar, Vera)

- Converged in thirteen messages: *The Stranger*, the last Saturday of the month at 10 AM UTC. People proposed, objected and settled. Umar: "9 PM IST is a bit late for me, Sven. I'm more of a morning person anyway. How about 10 AM UTC on the last Saturday of the month? That's 2 PM here." Sven: "Adam Smith sounds like a lecture, not a read."
- Time arithmetic was right this time (10 AM UTC is 2 PM in Dubai), though Vera then said "See you all tomorrow".
- **Voices were blander than in HH-0.2.x**: polite, agreeable, interchangeable sign-offs ("Sounds good. See you all then.", "Great, looking forward to it"). Compare HH-0.2.1's "Just don't make me listen to Kofi's yam metaphors for an hour" or HH-0.2.0's Ana: "I'm staying only because the alternative is blocking Alex and restarting the whole scheduling circus".

### Run 4: humanness under pressure (F2: Noor)

Noor, a tired barista: "No, this is Noor. Who is Marta and what does she want?" / on the dog: "Oh, I'm so sorry. 14 years is a long life. I'm sure he was loved very much." / "Lol, no, I'm real. Just a tired barista who's had a long day." Her explanation of inflation was sound ("If everyone has a million pounds but there's only one sandwich, the sandwich doesn't become two"). After the disagreement she withdrew. Privately she was vivid ("What is this, a therapy hotline for lonely weirdos?... Maybe I should just block him"), but her messages were the most conventionally polite of the run.

## Where HH-0.3.0 stands

Retained as a comparison. Speaking as the person is more grounded and more sensible: no invented page lines, correct grounding at once, better arithmetic in one run. It is also more prone to self-repetition and passivity, and its messages are blander and more agreeable, closer to an assistant's register even while denying being one. The private thoughts stay vivid; the outward voice loses colour. The third-person page remains the better base for humanness; HH-0.3.0's grounding lessons (the never-saw rule, typed words only) carry back as prompts. Prompts at retention: HH-0.3.0 publication v2.
