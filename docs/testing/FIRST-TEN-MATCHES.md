# The first ten human matches: how we run them

Purpose: learn whether a new player has fun, understands the game and can play it smoothly, before more people see it.
These are the first ten matches played by people outside the build team. Each one is run the same way so the answers
can be compared.

## Before each match

| Check | Why |
| --- | --- |
| The tester has a build number and it is the current release candidate | answers from an old build mislead |
| It is the tester's first launch, or we write down that it is not | the warning, the film and the first minutes only happen once |
| The observer does not explain the game beforehand | we are testing whether the game explains itself |
| The tester knows the feedback file stays on their computer and they choose what to send | consent and trust |

## During the match

- One observer watches (in the room or on a shared screen) and writes down, with the minute: the first moment the tester
  looks lost, the first hit they land, the first time they score for their team, and any time they ask a question.
- The observer answers a question only when the tester is stuck for more than a minute, and writes down the question.
- Nobody helps with controls unless the tester asks.

## After the match

1. The tester answers the in-game card (fun 1 to 5, what felt bad, an optional note) without the observer reading over
   their shoulder.
2. The observer asks three questions, in this order, and writes the answers word for word:
   - "What were you trying to do?"
   - "What was the most fun moment?"
   - "What would you change first?"
3. The tester sends the feedback file (and a problem report if something broke). The observer files the notes with the
   build number and the match's line from the file (matched by time).

## Matches 1 to 10

| Match | Who | Mode | What we look at most |
| --- | --- | --- | --- |
| 1 to 3 | people who have never seen the game | PLAY NOW, bots | the first five minutes: do they find a fight and a goal without help? |
| 4 to 6 | the same people, second match | PLAY NOW, a class they did not play | does the second match feel better than the first? |
| 7 and 8 | one person on Windows, one on Mac | PLAY ONLINE with the host | joining with a code, and lag or desync |
| 9 and 10 | anyone | a full match to the end | does the match end on a high point? results and card |

## What we read from the file

| Number | Where in the file | What we hope to see (draft targets, not yet measured) |
| --- | --- | --- |
| Seconds from launch to the first match | `first_fun.to_first_match_s` | under 120 on a first launch (warning and film included) |
| Seconds from launch to the first hit landed | `first_fun.to_first_hit_s` | under 60 after the match starts |
| Seconds from launch to the first objective credit | `first_fun.to_first_objective_s` | within the first match |
| Fun | `match.fun` | 4 or 5 from most testers |
| What felt bad | `match.felt_bad` | no chip picked by more than 3 of 10 |
| Smoothness | `match.frames.p95_ms`, `over_33ms` | p95 at or under 17.5 ms (the game's own budget), few frames over 33 ms |

## Stop rules

- If a tester cannot start the game, cannot join a room, or the game closes by itself: stop, collect the problem report,
  and fix that before the next tester.
- If three testers in a row pick the same chip, stop and look at that before running more matches.
- Never record a tester's screen, voice or face without asking them first.
