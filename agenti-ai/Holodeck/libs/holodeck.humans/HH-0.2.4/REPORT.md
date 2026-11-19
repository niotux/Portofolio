# HH-0.2.4 report

The experiment log for HH-0.2.4. It keeps HH-0.2.3 (the third-person page, every message buzzes, the glance at the phone, working things out through the hands) and takes a different approach to **how** something is worked out. Instead of one careful pass, the person works it out twice, separately (at thought temperature), then checks one attempt against the other: where they agree it is kept, where they disagree the wrong step is found and redone. The checked working is what lands on the page. See [the HH-0.2.3 report](../HH-0.2.3/REPORT.md) for the single-pass version.

## Why this version

In HH-0.2.3 one person (Lux) chose to work out the Bucharest–Lisbon flight and still got it wrong inside her own working ("Bucharest is UTC+2; Lisbon is UTC+0... So Lisbon is one hour behind"). A careful person redoes a sum before trusting it.

## Log

### Run 1: the same time-zone calculation (F9, four fresh people)

- 3 of 4 chose to work it out (HH-0.2.3 with the habit: 3 of 4), and **all three were right**: Quil "01:30", Ren "01:30.", Sol "01:30. That is the time when you land." Their checks found agreement, for example: "Both attempts agree: the flight lands at 01:30 Lisbon time. The first method converted to UTC first, while the second added duration to local departure time then adjusted for the two-hour time difference."
- Pax did not work it out and was wrong as before: "Lisbon is one hour behind Bucharest, so you'll land at 02:30".
- Right answers: 3 of 4, as in HH-0.2.3. Right when worked out: 3 of 3, against 2 of 3. Suggestive, not conclusive at this size.
- The cost is two extra model calls per working-out. With the model shared between people, replies that involved working out took a minute or two longer.

## Where HH-0.2.4 stands

Retained. Double working with a check is more reliable when used, but most of the gain over HH-0.2.2 (0 of 3) came from HH-0.2.3's decision to work things out at all, and the habit that makes people do it. The remaining errors come from people who do not stop to think, which is human. Prompts at retention: HH-0.2.4 publication v2.
