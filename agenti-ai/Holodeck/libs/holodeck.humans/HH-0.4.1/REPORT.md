# HH-0.4.1 report

The experiment log for HH-0.4.1. It keeps HH-0.4.0 (standing mind, alarms, checked working-out, the third-person page) and changes one aspect, **the person's clock**. Until now every person saw the configured time zone (Europe/Bucharest), whether their drawn life was in Kerala, Tokyo or Saskatchewan. In HH-0.4.1, after the portrait is prepared, a hands-style pass sets the phone clock (`set_clock`, an IANA zone) to where the person lives. Code checks that the zone exists; otherwise the configured one stays. The zone is stored with the portrait and reused. The glance at the phone, time marks, alarms and the person's sense of day and night all follow it.

## Why this version

Three F6 runs and the reminder test met people who were going to bed: it was 22:00 to 23:00 in Bucharest, and every person's page said so. Priya in Seattle and Dev in Kerala saw Bucharest's night. The tests were meeting a fact of the implementation, not of the people.

## Log

### Run 1: clocks (seven fresh people)

The clock was set by the pass for 6 of 7: an 85-year-old retired teacher in rural Scotland got Europe/London; a ballet dancer in New York City America/New_York; a long-haul truck driver in Astana Asia/Almaty; a simit seller in Istanbul Europe/Istanbul; an engineering student in Ibadan Africa/Lagos; a medical resident in Toronto America/Toronto. For a man in "a remote village in the Andes" no valid zone came back, and the configured one stayed. He then described himself as "in GMT-5".

### Run 2: the untagged book circle across four time zones (Amara, Bastian, Carmen, Dmitri)

- Each now reasoned from their own clock ("i'm in eastern standard time. i'm usually free between 7 and 9 pm est on saturdays... my social battery is basically non-existent by then"). They settled on a Saturday slot and an English translation. Carmen: "cool, thanks everyone. i'm going to go wrap these feet now before they turn into bricks. see you saturday."
- **The arithmetic between zones was still poor, and nobody stopped to work it out in the group.** Dmitri: "8pm my time (astana), which is 3pm in berlin and 11am in nairobi" (it is 4pm and 6pm). Carmen, in New York: "saturday 10am est is my 7am". Bastian: "8pm EST is 11pm here" for a place he called GMT-5. The work-it-out habit fired for a direct question in earlier versions, but not in the flow of a group negotiation.

### Run 3: the reminder, now across zones (Elif, Femi, Greta)

The request was for a reminder at "23:01 Bucharest time", so each person had to convert to their own clock.

- Elif refused: "I can't be the alarm clock for a man who can't fix his own phone". Her mind note: "I won't text Alex at 23:01 Bucharest time."
- Femi agreed ("i set a reminder for 23:01 your time"), saying so before the hands actually did it. He set the alarm for 23:01 on his own Lagos clock, an hour late. His mind note: "I must ensure the phone's battery survives the night so the 23:01 reminder for Alex's mum's birthday fires."
- Greta agreed ("i'll send a text at 21:01 to give you plenty of time to dial") and set an alarm for 21:01 Toronto time, six hours after the real moment (16:01 in Toronto).
- Both commitments were real: kept in the mind note, with an alarm set. Both were mistimed because nobody worked out the conversion.
- Femi followed it up by himself on an idle wake, before the alarm: "the generator is dead so i might be late but the phone clock is still ticking. if you don't hear from me by 23:05 just assume i failed but i tried." When his alarm rang at 23:01 Lagos time he took the alarm for the reminder itself: "The ping cuts through the dark like a struck match... now that notification is out of my system and into the ether, waiting for a network that might not come back until morning." He waited and never wrote. With Vik in HH-0.4.0 this is the second time: **at the moment of acting, the person experiences the intention as already done.**

## Where HH-0.4.1 stands

Retained. A clock that follows the person's life is cheap, works for most lives, and makes time-of-day behaviour (sleepiness, "my 7am") coherent with who they are. It also exposes the model's weakness with conversions more often, because people now live in different zones. The next step would be a habit, or a body capability like a phone's world clock, for converting times, rather than more reasoning. Prompts at retention: HH-0.4.1 publication v2.
