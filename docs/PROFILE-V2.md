# Player profile, schema 2

The profile is one JSON file in the player's data folder:
`<persistentDataPath>/profile/profile.json` (on a Mac,
`~/Library/Application Support/Drivia/Critical Mass/profile/profile.json`).

Between-match progress is cosmetic and class mastery only. Nothing in the profile changes
the match: no stat bonus, no power, nothing for sale.

## Saving and recovery

Every save follows these steps:

1. Write `profile.json.tmp`.
2. Move the current main file to `profile.json.bak`.
3. Move the temp file into place.

Reading tries the main file first, then the `.bak`. When both are unreadable, the game starts
a fresh profile and keeps the bad file as `profile.json.corrupt`.

A file whose `Version` is higher than this build knows is refused, so an older build never
overwrites a newer profile. It loads the backup or starts fresh instead.

When an older schema is loaded, the original file is copied once, byte for byte, to
`profile.json.v<N>.bak` (for example `profile.json.v1.bak`). That copy is never overwritten.

## Migration 1 to 2

Every schema-1 field is kept as it was:

- name, skill points, coins
- matches, wins, draws, losses
- knock-outs, best match, relics
- loadouts (head, emblem, emote, name)

Migration adds the parts below. All of them start empty, except achievements, which are
imported. Schema 1 did not record per-class play, so mastery starts at level 1 for every
class, and no history is invented.

Achievements are imported from the old `PlayerPrefs` keys `cm.ach.<id>`. The keys are never
deleted, so an older build still sees them.

## New in schema 2

| Field | Type | Meaning |
|---|---|---|
| `Version` | int | 2 |
| `Mastery[]` | ClassMastery | one per class played |
| `Mastery[].Class` | string | `Warrior`, `Farmer`, `Miner`, `Demolitions`, `Builder`, `Healer` |
| `Mastery[].Xp` | int | 0 to 24,700 (level 20) |
| `Mastery[].BotDay`, `BotXpToday` | int | the bots-only daily allowance (yyyymmdd, XP used that day) |
| `Mastery[].Matches`, `Wins`, `Draws`, `Losses` | int | career per class (practice modes excluded) |
| `Mastery[].KnockOuts` … `Purified`, `Deaths`, `BestScore` | int | career role totals per class |
| `Loadouts[].Title`, `Frame`, `Trim`, `Badge` | int | the cosmetic look per class, cleared on load if above the class's mastery |
| `Achievements[]` | AchievementRecord | `Id`, `Unlocked`, `UnlockedUtc` (ISO 8601), `Best` (best in one match, for the progress bar) |
| `History[]` | MatchRecord | the newest 50 finished matches, newest first |
| `History[].DateUtc` | string | ISO 8601 from the system clock at save time |
| `History[].Map`, `Mode`, `ModeLabel`, `TeamSize`, `Seed` | | the match |
| `History[].Class`, `Result` (0 loss, 1 draw, 2 win), `Score`, `Azure` | | the player's side |
| `History[].AzureHill`, `CrimsonHill` | int | hill seconds per team |
| `History[].BotOnly`, `MasteryXp`, `Stats` | | anti-farm flag, mastery earned, role stats |
| `History[].TurningPoint` | string | one sentence from the match summary when it gives one, else empty |
| `History[].ReplayFile` | string | the replay's file name only (never a path); empty if none |

## Class mastery

Mastery has 20 levels. Going from level L to L + 1 costs 400 + 100 × (L − 1) XP, so these are
the totals needed:

| Level | Total XP |
|---|---|
| 2 | 400 |
| 5 | 2,200 |
| 10 | 7,200 |
| 15 | 14,700 |
| 20 | 24,700 |

XP comes from each class's role work, counted from the sim's events at the end of a match:

| Class | Earns from |
|---|---|
| Warrior | knock-outs and hill time |
| Farmer | deliveries |
| Miner | ore banked |
| Demolitions | explosive hits and structures destroyed |
| Builder | built pieces that teammates use, and pieces built |
| Healer | healing on teammates and purifying |

Every stat has its own cap per match, and a match pays at most 1,500 XP.

Anti-farm rules:

- Practice, training, spectating and unfinished matches earn nothing.
- A bots-only match earns half, up to 2,000 XP per class per day.
- Only 3 knock-outs on the same victim count.
- A self-heal does not count, and neither does using your own piece.
- A player who did nothing gets no finish or win bonus.

All the numbers live in `Runtime/Progression/Mastery.cs`. The reward table, in data, lives in
`Runtime/Progression/MasteryRewards.cs`, and the unlock levels are shared with the network
clamp in `Net/CosmeticLook.cs`.

Each level from 1 to 20 unlocks exactly one cosmetic (level 20 unlocks two), 21 per class:

| Reward | Unlocks at levels |
|---|---|
| 7 titles | 1, 3, 7, 11, 14, 18, 20 |
| 4 portrait frames (bronze, silver, gold, Basin Crown) | 5, 10, 15, 20 |
| 5 colour trims (moss, slate, bone, soot, lichen) | 4, 8, 12, 16, 19 |
| 5 emblems | 2, 6, 9, 13, 17 |

Trims tint only the fixed colours of the six existing models. The team colour regions are
never touched.

## The look online

Each player's look (title, frame, trim, emblem and claimed mastery, 5 bytes) travels in the
lobby messages `Look` (28) and `Looks` (29). It is outside the match setup, the inputs, the
config hash and the state hash.

Every look from a peer is clamped on arrival: ids that are out of range, or above the claimed
mastery, become 0, and mastery is held to 1–20. A look is shown only while that player keeps
the class they picked in the lobby. Bots show none.
