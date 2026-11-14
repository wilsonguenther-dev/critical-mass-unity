# Online test kit

Everything here runs with no game window. Two peers that play the same match must log the same fingerprint at
every 30th tick; `compare-hashes.py` says MATCH, or DESYNC and the first tick that differs.

## Console peer (no Unity, .NET 8)

```
dotnet build tools/online/HeadlessPeer -c Release
P=tools/online/HeadlessPeer/bin/Release/net8.0/cm-online-peer
$P -host -relay $CM_RELAY_URL -humans 2 -ticks 1800 -hash-log host.txt -code-out code.txt
$P -join $(cat code.txt) -relay $CM_RELAY_URL -team crimson -class miner -hash-log join.txt
python3 tools/online/compare-hashes.py host.txt join.txt
```

## The built game as a hidden peer

Two copies of the game on one machine: each needs its own data folder or the second quits on the single-instance
lock ("another copy of Critical Mass is already running"). Pass `-dataDir /tmp/cm-a` and `-dataDir /tmp/cm-b` to a
normal windowed copy; the hidden modes below (`-onlineHost` / `-onlineJoin` with `-batchmode` or `-onlineHeadless`)
pick `TEMP/cm-online-PID` by themselves when no `-dataDir` is given.

The same flags work on the player with the `online` prefix, in `-batchmode -nographics` (or with `-onlineHeadless`):

```
CriticalMass.app/Contents/MacOS/CriticalMass -batchmode -nographics -logFile host.log \
  -onlineHost -onlineRelay $CM_RELAY_URL -onlineHumans 2 -onlineTicks 1800 -onlineHashLog host.txt -onlineCodeOut code.txt
CriticalMass.app/Contents/MacOS/CriticalMass -batchmode -nographics -logFile join.log \
  -onlineJoin $(cat code.txt) -onlineRelay $CM_RELAY_URL -onlineTeam crimson -onlineHashLog join.txt
```

All flags: `-onlineHost`, `-onlineJoin CODE`, `-onlineCode`, `-onlineKey`, `-onlineRelay URL`, `-onlineName`,
`-onlineTeam azure|crimson`, `-onlineClass NAME`, `-onlineHumans N` (host starts when N are in and ready),
`-onlineTicks N` (0 = whole match), `-onlineTeamSize`, `-onlineSeed`, `-onlinePace`, `-onlineHashLog FILE`,
`-onlineCodeOut FILE` (written once the host is connected, "ROOM READY"), `-onlineTimeout SECONDS`,
`-onlineLobby SECONDS` (host gives up if the room is not full and ready, default 180, exit 4), `-onlineJoinTries N`
(joiner asks again 1.5 s apart while the room is not there yet, default 8), `-onlineChatAt TICK` and
`-onlineChatText TEXT` (send a chat line; every received line is logged as ONLINE CHAT), `-onlineDropAt TICK` (drop off
with no goodbye; the others log ONLINE TOBOT with the seat and tick), `-onlineBuild`, `-onlineHeadless`.
The hash log starts with tick 0 (the world as built) and tick 1, then every 30th tick: a mismatch at 0 is a setup
difference, at 1 the first inputs, later a simulation drift. The summary line ends with every peer's round trip.
Exit codes: 0 ok, 2 desync, 3 room closed or host left, 4 timed out, 5 could not create or join.

## Two-machine plan

1. CI, every push to `feat/online-rooms-*` (`.github/workflows/online-smoke.yml`, job local-relay): a local relay
   on the runner and two console peers, 1800 ticks, hashes compared. No account needed.
2. Cross-OS, after the relay is deployed (repo variable `CM_RELAY_URL`, run the workflow by hand with cross_os):
   host on a Windows runner, joiner on a macOS runner, through the real relay over the internet.
3. The Air (only in a slot the brief gives): a built Mac player, two hidden instances
   (`-batchmode -nographics`) against the real relay, one hosting and one joining, then compare. Then one hidden
   Air instance against a GitHub runner console peer: Mono player against .NET 8, different networks.
4. People: owner hosts, Jeisil and Travis join by code from their homes; per-tick hashes are not needed, the
   in-game "MATCH STOPPED" panel reports any desync with its tick.
