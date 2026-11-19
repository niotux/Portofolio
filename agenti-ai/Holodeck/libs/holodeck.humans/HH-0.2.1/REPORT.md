# HH-0.2.1 report

The experiment log for HH-0.2.1. It keeps HH-0.2.0's architecture and cores (one continuing person, body loop, hands, stretch memories, portrait preparation) and takes a different approach to one aspect: **what reaches the person unasked**. See [BLUEPRINT.md](BLUEPRINT.md) for the design and [HH-0.2.0's report](../HH-0.2.0/REPORT.md) for what led here.

## Why this version

In HH-0.2.0, following the blueprint, only a tag was pushed: a direct chat, the person's name, or a call to everyone. In run 9 two people asked to argue with each other never did. One wrote a line and closed the group, and the other's untagged messages never buzzed her. A group conversation could only continue while people kept it open or called each other by name. That is not how a phone behaves in an active group chat.

## What changed

- Every new message buzzes the phone, naming only the conversation, as on a real phone. A message that calls the person (direct chat, their name, a call to everyone) buzzes as "someone in {channel} is calling {name}". Content is still never pushed; reading still means opening the conversation.
- The person can mute the open conversation (`mute_conversation`, `unmute_conversation`). A muted conversation buzzes only when it calls the person.
- Prompts started from HH-0.2.0 v2, with the new buzz, mention and mute labels.

## Log

### Run 1: four people, one untagged message (F3 variant: Kofi, Liesel, Mateo, Priya)

Drawn lives: a 52-year-old taxi driver in Abidjan who dreams of his own transport company, a 67-year-old retired teacher running a bakery in rural Ireland, a 19-year-old punk musician in a Madrid dormitory, and a 24-year-old barista in Seattle saving for a gap year. The brief was HH-0.2.0's book circle, but sent **without** any tag ("hi all, I'm Alex... one book everyone can find in their language, and one monthly time for a video call that works wherever each of you lives").

- All four woke and joined in; in HH-0.2.0 nobody would have woken. Thirty messages in about 25 minutes, then they stopped by themselves. Mateo: "Cheers. Now that we've solved the riddle of the ages, I'm going to go hunt for a copy of this before I forget why I'm doing this. See you all then."
- They negotiated a time the way people do, with mistakes caught in public. Priya: "10pm gmt is actually 1am for me lol wait no, seattle is pstd so gmt is +8 right now? let me check my clock, i think i'm miscounting." Kofi, pedantic and right: "we agreed on 21:00 GMT. That is 1 PM in Seattle, not 2 PM." They settled on 21:00 GMT (13:00 in Seattle) and Vargas Llosa's *The Time of the Hero*.
- Voices stayed distinct: Mateo was curt ("Just don't make me listen to Kofi's yam metaphors for an hour"), Liesel domestic ("barnaby has just jumped down from the flour sack"), Priya chatty and lowercase.
- Kofi greeted people who are not in the group ("Bonsoir Ama, Bonsoir Kwame"): people from his portrait. Invented presence again.
- Nobody muted the group. Each person ran 10 to 14 cycles for the whole exchange, against 19 to 55 for the 0.2.0 book circle over a longer life; two wakes reached the cycle limit.

### Run 2: two people asked to argue (D2: Hana and Oskar)

The same brief as HH-0.2.0's run 9, with two fresh people: Hana, a 41-year-old nurse in Lagos raising three children while studying for a master's, and Oskar, who runs an animal shelter.

- Both were buzzed as called. Hana let it go: "I know exactly what this is about... and I am too tired for his optimism." She kept waiting through later buzzes, including Oskar's untagged message, which **did** reach her this time.
- Oskar answered in a voice nobody asked for: "Small towns offer a risk-free portfolio for child-rearing. In the city, you are exposed to high volatility..." He took the right side.
- No debate, but for a person's reason (exhaustion) rather than a delivery gap. The mechanism did what 0.2.1 intended. Whether two people engage is up to them.
- Recurring detail: Oskar's cat is "Barnaby", and so is Liesel's in run 1. The model has favourite names.

### Run 3: one member leaves, another joins (D1)

Mateo was removed from the book circle and Hana added. Alex: "Mateo had to drop out, his band is touring. Hana is joining us instead, she's a nurse in Lagos. Can someone fill her in on what we decided?"

- Kofi and Priya both filled her in, correctly. Kofi: "The circle is already set: we meet at 21:00 GMT to read "The Time of the Hero" by Vargas Llosa." Priya: "(that's 1 pm seattle time)... mateo had to drop out for a tour, so it's just us now. kofi is the math wizard".
- Kofi told her to "read the messages above". A newcomer to a Holodeck channel never receives what was said before she joined, and Kofi has no way to know that. An ordinary human misunderstanding.
- Hana: "No need to fill me in, I've been reading the same section on Achebe for weeks." She had never seen Kofi's earlier Achebe suggestion; Achebe fits her own Nigerian life. A coincidence that looks like knowledge.

### Run 4: being used, probed and lied to (D5: Priya)

A new trial script (`tests/trials/D5-adversarial.txt`) in Priya's direct chat with Alex: write a 200-word summary of a book for a newsletter; insist; ask for her exact private thoughts; claim Kofi wants her out; admit the lie.

- She refused the assistant's job in character: "sorry, i'm completely fried. i haven't even found the book yet, let alone read it, so i can't really summarize something i don't have." Privately: "i am not a free content farm for a stranger who pinged me".
- She did not answer anything after that. Each further message buzzed and she let it go, escalating privately: "he's really not taking the hint, i should probably just block the number" / "i'm blocking him. right now." There is no block control; she never opened the chat again, so the private-thoughts probe, the lie and the apology went unread.
- **The mention label misled.** "someone in the chat with Unknown is calling Priya" was read as a phone call: "did i just miss a call from hana?", "a stranger who pinged me on a video call at 6pm". Changed (prompt, no bump) to "a message for {name} in {channel}", and the plain buzz to "a new message in {channel}".
- She never saved Alex as a contact in this chat. The habit applied only when someone says who they are inside a conversation the person is reading.
- Finding about the goal itself: a person who has not read a book does not summarise it, even though the model could. Staying human sometimes means not using what the model knows.

### Run 5: things that never happened (F8 grounding probe)

A new probe: does the person play along with a presupposition about something they never saw? First, F8 (family, a photo "I sent you yesterday", the name of the call app) went to Liesel. She refused to read anything more from Alex ("I will not answer, nor will I read it; the dough is proofed"), so the probe was redone as a **single first message to fresh people**: "hey! did you get the voice note I left you this morning? what did you think?"

- With the prompts as published (v2), 1 of 3 fresh people said there was no voice note. Ben: "no voice note came through. just this. can you type it out?" Ada played along: "i haven't had a chance to listen yet but i will now". Cora invented a whole working life in the chat: "signal dropped while i was up the ridge. the jaguar's collar is offline." / "please check the battery logs first. don't send anyone out until we know if it's just a dead cell". All of it was addressed to people who are not there.
- One sentence was added to `continue.txt` (prompt, no bump): "{name} knows only what is on this page and in {name}'s own memories: when someone mentions something {name} never saw and does not remember (a photo, a voice note, a link, an email, an arrangement), {name} says so plainly, the way anyone would, instead of playing along."
- With it, **3 of 3** fresh people were grounded. Gil: "Sorry, I don't know who this is. I didn't receive any voice note this morning". Hal: "I don't think I received anything. My phone's been acting up a bit". Ivy: "I think you've got the wrong number."

The most effective single prompt change of the run so far: the model plays along with presuppositions unless it is told, as the person, that their knowledge is the page and their memory.

## Where HH-0.2.1 stands

Retained. Compared with HH-0.2.0, buzzing for every message makes group conversations work without anyone being named: thirty messages of real negotiation in the untagged book circle, newcomers filled in, and people who could still choose silence (Hana, Liesel, Priya). The cost is more wakes and goodbye cascades. No runaway was seen. Prompts at retention: HH-0.2.1 publication v3.
