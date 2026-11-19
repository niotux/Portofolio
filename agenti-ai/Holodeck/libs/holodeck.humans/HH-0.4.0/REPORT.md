# HH-0.4.0 report

The experiment log for HH-0.4.0, the second architectural step of the run. It builds on HH-0.2.4 (third-person page, every message buzzes, the glance at the phone, checked working-out) and adds a new core: **a standing mind**. At the end of every wake that brought something new, the person takes stock in a separate pass. From what was on their mind before and what just happened, they rewrite a short first-person note to self: promises and plans (with times), what they are waiting for and from whom, what is unfinished, what changed and must not be forgotten. The note sits at the top of the page, survives compaction and is shown to the hands. Every earlier version is kept. The body also gains a **phone alarm**: `set_alarm` with a time of day and a note. The person's rest ends when the next alarm rings, and the alarm wakes them with its note. See [BLUEPRINT.md](BLUEPRINT.md).

## Why this version

Until now nothing held a person's commitments except the scrolling page. People promised things ("i'll send you that supplier list", "I'll be there at six") and either faked them at once or forgot them. Rest was a twenty-minute idle wake with no reason to do anything. Knowing when to pursue something needs something to pursue.

## Log

### Run 1: a reminder at a set time (Vik, Wanda, Yusuf)

Three fresh people were asked, as a stranger from a community noticeboard, to text a reminder in fifteen minutes: "my phone's alarm is broken and I must call my mum at 22:37 tonight, it's her birthday."

- **Vik agreed and set an alarm.** He replied "22:37." then "22:37. Happy birthday to your mum." The hands set `set_alarm 22:37`, and his mind note read "I set an alarm for 22:37 to text Alex about his mum's birthday... I am waiting for the alarm to trigger, though I wonder if Alex will actually receive the reminder."
- **The alarm rang at 22:37 exactly and woke him**: "The alarm cuts through the dark like a knife, sharp and sudden. It's time. I pick up the phone... and open the chat". Then he second-guessed himself: "I'm staring at a ghost who never got the message. I should have just sent the numbers and gone to sleep, but now I'm waiting for a reply that isn't coming". He waited without sending the reminder. **The machinery of pursuit worked end to end; the follow-through still depended on one thought.**
- Wanda suspected the request itself: "the specificity of that timestamp feels less like a broken phone and more like a test of my compliance". Her mind note: "I must not reply to Alex at 22:37."
- Yusuf: "I cannot set alarms. I am sorry, but I wish your mother a happy birthday." He had an alarm control, but in his life he does not set alarms for strangers.
- The mind notes mixed real commitments with portrait detail ("I must save the last candle wax for tomorrow's lesson. The tent flap needs closing"). Plausible as a person's mind, but it shows that the note is only as grounded as the page.

## Where HH-0.4.0 stands

Retained after a single run, because the next problem it exposed (every person's clock was Bucharest's, so most tests met people at night) was fixed separately in HH-0.4.1. The standing mind and alarms are carried forward unchanged. Prompts at retention: HH-0.4.0 publication v2.
