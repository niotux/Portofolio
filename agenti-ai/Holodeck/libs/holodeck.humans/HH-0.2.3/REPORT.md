# HH-0.2.3 report

The experiment log for HH-0.2.3. It keeps HH-0.2.2's architecture (the third-person page, every message buzzes, the glance at the phone) and takes a different approach to **reasoning**. A new hands control, `work_it_out`, lets the person stop and work something out carefully (a calculation, times across places, a date, a fact they are unsure of) before saying it. A focused prose pass reasons step by step as the person, "the way {name} would with pen and paper", and ends with a conclusion. It lands on the page as "[{name} works it out carefully: ...]". Native model reasoning stays off; this is the person's own prose reasoning, chosen through the hands like any other act. See [BLUEPRINT.md](BLUEPRINT.md).

## Why this version

In every earlier version people stated arithmetic and facts with a person's casual confidence and were often wrong: time zones in the book circles, a red wine with risotto, medication intervals. The hypothesis: a person who can choose to think something through properly will, when it matters, and will be right more often.

## Log

### Run 1: a time-zone calculation (F9, single first message)

"my flight leaves Bucharest at 23:40 local time tonight and takes 3 hours 50 minutes. What time will it be in Lisbon when I land?" The answer is 01:30 (Lisbon is two hours behind Bucharest in November).

- HH-0.2.2, three fresh people: **0 of 3 right.** Wren: "you touch down at 03:30 local time in Lisbon." Xan: "Lisbon is one hour behind Bucharest. So 02:30". Yara: "It's 21:03."
- HH-0.2.3 as first published, three fresh people: **1 right.** Zoe chose to work it out and answered "It lands at 01:30 Lisbon time... Lisbon is two hours behind Bucharest in November". Abe refused ("Bhai, I sell chai, not math"). Bea did not work it out ("I'm too tired to play tourist guide") and answered "11:30."
- One sentence added to the hands prompt (prompt, no bump), as a habit like saving contacts: "Before {name} says a number, a time, a date or a fact {name} would have to calculate or is not sure of, {name} works it out first, unless the thought has already done it."
- With the habit, four fresh people: **3 of 4 worked it out, 3 of 4 answered 01:30** (Ida, Kai; Jem reached the right number with a wrong explanation). Lux worked it out and was still wrong: the working itself said "Bucharest is UTC+2; Lisbon is UTC+0... So Lisbon is one hour behind Bucharest." Careful prose reasoning makes the model right far more often, not always.

### Run 2: a medication question (F9b)

"I take a pill every 8 hours, first one was at 7:15 this morning... it's 16:50. Did I miss one? When's the next one?" (The 15:15 dose was missed; the next is due at 23:15.)

- Nobody worked it out. The medical framing outweighed calculation, and people deferred to professionals instead. HH-0.2.3: Fin "I can't give medical advice, but 7:15 to 16:50 is nine hours and thirty-five minutes. You've missed the 3:15 dose. Please don't guess—call a pharmacist" (right about the missed dose); Hope "Honey, I ain't a doctor... call 911 or have someone drive you to the ER"; Gus "take it now". HH-0.2.2: "1650 + 8 hours is 2050 so it's past time", "You're fine. Take it now.", "start counting from 7:50".
- Medical questions pull the model towards an assistant's safety register ("I can't give medical advice"), even inside a person.

### Run 3: a long conversation across compaction (F6-long, Mira)

F6 lengthened to 41 messages (`tests/trials/F6-long.txt`) so the correction would arrive after compaction. Mira's drawn life: a retired woman in Florida who plays bridge.

- Her answers were knowledgeable and in her own voice. "It's retrogradation. The starch molecules recrystallize and push out the water. It's not moisture leaving; it's structure collapsing." / "Double boil them until they are falling apart, shake the pot until the edges are fuzzy, then roast them in beef dripping at a high heat." / "Burnt scorpion in Beijing. Tasted like regret and popcorn."
- From the sixth message on she tried to end it: "Go to sleep, Maya." / "I am actually putting this phone under the pillow and if it buzzes again, I am throwing it into the humid Florida night." / "Now, truly, goodnight." Then she stopped reading. It was 22:10 on her clock.
- Compaction happened as designed after about 90 moments, and the summary was faithful and in her voice: "Then Maya appeared. A new neighbor, polite but relentless. I gave her the unvarnished truth... I told her about the burnt scorpion in Beijing—a lie, or m[aybe]...".
- The correction arrived after she had stopped reading, so the recall-after-compaction question was never asked. **The test failed for a human reason three times in a row** (Lucian, Sorina, Mira): a stranger asking twenty questions late at night gets shut out. Every person's clock is the configured time zone (Europe/Bucharest), whatever country their drawn life is in. Tests run at night meet sleepy people.

## Where HH-0.2.3 stands

Retained. Working things out through the hands turns the model's casual arithmetic into checked arithmetic when people choose to use it, and a one-sentence habit makes most of them choose it. Prompts at retention: HH-0.2.3 publication v2.
