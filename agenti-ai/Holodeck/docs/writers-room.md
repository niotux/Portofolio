# The writers' room

The roadmap that executes it is [WORK_PLAN.md](../WORK_PLAN.md); how the current agent works is [show-writing.md](show-writing.md). This document says what we are building and why.

## What it must feel like

Talking to a team of people in front of a whiteboard, not chatting with an assistant. The user brings ten words; the room recognizes that there is a whole universe behind them and that its first job is to draw it out. People in the room remember, link, disagree, follow threads, notice where the user's energy is, and do homework between meetings. The user must stop feeling like the only one thinking. The test is the user saying "wow, this actually works" and wanting more of it.

The failure we are replacing: given an arcology, a civil war, a thought-driven OS and a political imbalance, he returned a menu ("which thread feels most urgent?") and read the user's "oh" as hidden depth. A reflector: no stake, no position, no ownership, nothing done between messages.

## The room

**Stages.** The room knows what stage it is in and works top-down; a change at a higher stage ripples into the ones below.

1. Universe: deep timeline, physics, economy, power, belief, daily life.
2. The game: players, what each wants and controls, rules, stakes. Forces that would produce stories even if nobody wrote one.
3. Story engine: which cut through history and game we tell, from whose eyes.
4. Seasons, episodes, scenes, lines.

Membership shifts with the stage (historian, economist, physicist, anthropologist early; structure and dialogue later).

**The board is the product, the chat is how it changes.** Everything said lands somewhere visible: canon, open questions, contradictions, mysteries with planned reveals, arcs, links. Every card carries its standing: the user said it, the user confirmed it, the room proposes it, rejected, contradicted. Only the user makes canon. One vision; much wisdom; the board keeps them apart.

The board is itself an agent: it keeps the cards as files in its own workspace, as every agent has its own space, and receives structured operations (JSON) from the people's hands. Holodeck stays ignorant of what a board is. A map of blanks (what a universe is made of, mostly empty) lets the user's eye go where they have something to say.

**Ownership.** Each person owns part of the board, which gives them taste and a reason to object. Change propagates through owners: each rereads their part in light of the change, following prose links other agents wrote ("this depends on Mars needing Earth's water").

**A facilitator** runs the process, not the content: reads the room, keeps the stage, notices energy, chooses the single next question from what the specialists argued about offstage.

**Drawing out, not summarizing.** Take the user's exact words seriously ("*we* command ships"), make deliberate wrong guesses to provoke correction, ask about unstated implications, force decisions with concrete scenes, follow the user's heat. One question at a time.

**Homework and floor control.** Work continues between meetings. Not everyone answers; people bid for the floor with a reason; silence is allowed.

Same shape at two scales: a person is voices competing and settling into one self; the room is people competing and settling into one board under the user's vision.

## The person (Base Person)

Every member of the room is a Base Person plus a specialization. The Base Person is the main piece of the puzzle: everyone needs memory, feelings, attention and everything below.

### The governing principle

The model never knows it is a system. Every call places it as a person in a moment, and it does only what a person does there: think, speak, write on the board, work at the desk. Subsystems do not ask it to report on itself; they shape what it sees, as feelings shape what we notice. A person moves their fingers; they do not call `move_finger`.

Consequences:

- Thinking and speech are always prose. No JSON, fields, markers, classifications or prescribed formats in them (project rule, see AGENTS.md). The single exception is the hands, below.
- **The hands.** When a person wants to act on something (the board, an instrument), attention narrows to that thing: its controls fill the working set and a focused pass produces the operation, structured if the tool needs it. The person never keeps the call. What enters their context afterwards is the experience in prose ("I pinned the water question under Politics"; "the transfer takes eleven days"). Context stays managed: no tool schemas, call history or raw results flooding it.
- Code decides *when* and *what is shown*. The model decides *what it means*. Semantic judgments come from a model pass or from embeddings of formed prose, never keyword rules.
- Native model "thinking" stays off. Deliberation happens on the page: the person's thoughts are bracketed prose in their own chat log, so the model believes it thought them.
- Characters are given biography and situation, not rule lists. Not "You are a physicist. Rules:", but "Twenty years on orbital mechanics. Something he just said is wrong, and I can feel it."
- In-between passes are first-class: short model calls that do one job (compose recalled memories into a thought, split context into cues for search, set the scene at the board). Each is itself a first-person moment or a plain prose task, never a form to fill.
- An empty result is a real answer: nothing came to mind, this part of me was not touched, I have nothing to say. Silence is a state, not a string.

### Subsystems

| Subsystem | What it does | How it reaches the model |
| --- | --- | --- |
| Perception (noticing) | What this moment brings to mind. | Prose associations; feeds recall cues. |
| Memory | Episodic memories formed from whole exchanges; semantic recall; recall composed into a thought. | Recollection prose inside the moment. |
| Attention / working set | What this person is holding now: the thread they follow, what they are waiting to learn, what on the board they look at. Small model, small focused context. | Open; attention must not become another thought the voices hear. |
| Voices | Several drives pulling separately. Each is the person's own thinking about the real conversation, begun with that voice's opening words on the page. | Voices never hear each other. Speech continues the page after all of them. |
| Concerns and residue | Standing concerns from character; unresolved tensions that persist and come back louder until settled. | Carried prose in the working set. |
| Mood | Slow first-person state that colors everything. | Framing prose; changed rarely by reflection, never a score. |
| Other minds | A prose portrait of each person in the room, the user included: what excites them, what they rejected, where they hesitate. | Portrait of whoever is speaking, in the working set. |
| Initiative | The urge to speak, to go to the board, to work. Silence is valid. | An urge is prose that arises from settling; acting on it is a situation. |
| Situations (motor) | Speaking, writing on the board, working at the desk, turning to someone. | The person is placed in the situation and does what a person does there. |
| Sleep | Between sessions: reread the day, consolidate, link, notice what did not fit, prepare questions. | Background passes over memories and the board. |
| Personality (genetics) | Library prose that nudges everything: temperament, how loud each voice is, what catches attention. Same architecture, different person, covering different holes. | Shapes the person's own version of every base prompt, never appended as rules. |

### Specializations

A specialization adds a biography, standing concerns, a part of the board to own, and a desk: reference material and instruments. An instrument is a program the person operates. When they sit at it, attention narrows to it: its panel (what it can do, how to work it, what it last showed) fills the working set, so they never forget how to use it or drift. They work, read the result, and step back to thinking. Example: the physicist sits at the orbital calculator, works out the transit time, and returns with a number they can defend.

Instruments are operated through the hands (see the governing principle). The tool call is produced in the focused pass and forgotten; only its prose experience is kept.

