# Tuning index

Every tuning field the sim reads from `GameConfig`, found by reflection (`Assets/_Game/Sim.Tests/Harness/TuningIndex.cs`). Values are the game's config (`GameConfig.CreateDefault()`, the Full rules) when this file was generated; the meaning is the field's doc comment. Nothing was moved or renamed: the dials stay in their own config classes.

Regenerate: `tools/balance/balance-report.sh tuning-index (or the balance-report workflow, mode tuning-index)`. `TuningIndexTests` fails when a field has no row here. Paths work as `--set Path=Value` switches for the balance report.

813 fields.

## (top level)

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Seed` | 1337 | int | `Assets/_Game/Sim/Config/GameConfig.cs` | Default RNG seed. |
| `SimHz` | 30 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Fixed simulation rate (Hz). |
| `TeamSize` | 16 | int | `Assets/_Game/Sim/Config/GameConfig.cs` | Players per team including the human (U-004 default 16; browser main 5). |
| `TeamSizeOptions` | [5, 16] | int[] | `Assets/_Game/Sim/Config/GameConfig.cs` | Team sizes the menu and debug overlay offer. |
| `TeamSizeMax` | 16 | int | `Assets/_Game/Sim/Config/GameConfig.cs` | Largest team size; sizes actor storage. |
| `DefaultSpeed` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Initial match pace (driver time scale, sim ticks per wall second / SimHz). |
| `MatchSeconds` | 480 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Match clock (sim seconds); hill time is the only win condition. |
| `HatchSeconds` | 240 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Egg hatches at this many seconds elapsed (4:00 of 8:00). |
| `DrawEpsilonSeconds` | 0.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Hill totals within this many seconds of each other at the final whistle are a draw. |

## Hill

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Hill.Radius` | 6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |

## Movement

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Movement.AccelSeconds` | 0.12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Movement.BrakeSeconds` | 0.08 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |

## Jump

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Jump.AirTicks` | 18 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Airborne ticks of a full jump. |
| `Jump.ApexUnits` | 0.8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Apex lift of a full jump in world units. |
| `Jump.MinAirTicks` | 6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Shortest jump the landing guard may shorten to (ticks). |
| `Jump.CoyoteSeconds` | 0.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-02 coyote time in sim seconds (not in the browser; Travis's "Jumping", gap list row DG-02): a grounded actor whose footing was removed may still take off for this long. Counted in ticks (Jump.CoyoteTicks, 3 at 30 Hz). |
| `Jump.BufferSeconds` | 0.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-02 jump buffer in sim seconds (not in the browser): a press this early (before landing, before the get-up ends, or refused by the landing guard) fires on the first tick it can. Counted in ticks (Jump.BufferTicks, 3). |

## Respawn

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Respawn.DelaySeconds` | 5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Respawn.ProtectionSeconds` | 3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Spawn protection ends after this many seconds or when the actor leaves the base pad. |
| `Respawn.StandUpSeconds` | 0.8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-20: sim seconds an actor takes to get up off the pad after a respawn. |
| `Respawn.ClassSwitchCooldownTicks` | 150 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-54: sim ticks after an accepted class switch before another is accepted. |

## Heal

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Heal.HpPerTick` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | HP restored per sim tick inside the own base fountain zone, up to maxHp. |
| `Heal.ZoneRadius` | 3.75 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Zone radius around the pad centre. |
| `Heal.SeekHpShare` | 0.3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Bot retreat threshold as a share of maxHp (read by BotFsm.LowHp). |

## KaijuFeed

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `KaijuFeed.BiomassAllegiancePerUnit` | 10 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Allegiance per plant biomass unit. |
| `KaijuFeed.MeatAllegiancePerUnit` | 6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-35: allegiance per meat biomass unit. |
| `KaijuFeed.CorruptionAllegiancePerUnit` | 4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `KaijuFeed.CorruptionInstabilityPerUnit` | 9 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-34: instability per corruption unit. |
| `KaijuFeed.BiomassInstabilityPerUnit` | 0.125 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-85: instability per biomass unit delivered (plant and meat alike). |
| `KaijuFeed.BerserkThreshold` | 100 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Instability at or above this before hatch locks berserk. |
| `KaijuFeed.RandomBerserk` | false | bool | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-41 (U-062, not in the browser): when true the line is random per match instead of BerserkThreshold: u^2 x BerserkCurveMax with u uniform in [0, 1) from the match seed (SimResources.BerserkLine), so the chance that ... |
| `KaijuFeed.BerserkCurveMax` | 800 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-41: the instability at which the random line has surely been crossed (the top of the sqrt curve). |
| `KaijuFeed.ShrineInteractRange` | 3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `KaijuFeed.DeliverWindupTicks` | 12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-64: sim ticks a carrier stands in the delivery zone holding interact before its load goes in; an enemy hit during the wind-up interrupts it and it restarts from zero. |
| `KaijuFeed.Cleanse.Units` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Corruption units one cleanse drains (x CorruptionInstabilityPerUnit = 18 instability at 9). |
| `KaijuFeed.Cleanse.Ticks` | 90 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Ticks the drain is spread over (3 s). |
| `KaijuFeed.Cleanse.CooldownTicks` | 2700 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Ticks the cleansing team waits from the start (90 s). |
| `KaijuFeed.Cleanse.HoldTicks` | 15 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Ticks of held interact that start it (0.5 s). |
| `KaijuFeed.Cleanse.BotNetUnits` | 8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | The human slot on autopilot cleanses once this many net corruption units sit in the egg; pure bots never cleanse. |

## Resources

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Resources.NodeInteractRange` | 1.8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Resources.BiomassNodeCharges` | 6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Resources.BiomassRegrowSeconds` | 20 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Resources.CorruptionNodeCharges` | 3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Resources.CorruptionRegrowSeconds` | 30 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Resources.MeatUnitsPerKill` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-35: biomass units in the meat orb a killed actor drops. |
| `Resources.MeatOrbLifetimeTicks` | 600 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-35: sim ticks a meat orb lies on the ground before it rots away. |
| `Resources.MeatPickupRange` | 1.2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-35: walk-over pickup reach from the orb centre, added to the actor's radius. |
| `Resources.MeatCarryCapacity` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-35: meat units one actor can carry, outside the class cargo. |

## Kaiju

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Kaiju.HpPerTeamMember` | 400 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Kaiju max HP = HpPerTeamMember x TeamSize, computed at world creation and reset. |
| `Kaiju.MoveSpeed` | 2.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Kaiju.Radius` | 1.64 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Hit radius against actors (also the cleave and slam radius). |
| `Kaiju.GridRadius` | 0.7 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Grid collision radius (walls and pathing). |
| `Kaiju.MeleeDamage` | 30 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Kaiju.MeleeRange` | 2.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Kaiju.MeleeCooldown` | 1.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Kaiju.MeleeWindupTicks` | 11 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-26: sim ticks a melee swing winds up before it lands. |
| `Kaiju.SlamDamage` | 45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Kaiju.SlamRadius` | 5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Kaiju.SlamTelegraphSeconds` | 1.2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Kaiju.SlamCooldown` | 8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Kaiju.NeutralThreatMemorySeconds` | 8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | A neutral Kaiju only retaliates against actors that damaged it in the last N seconds. |
| `Kaiju.KoSeconds` | 60 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | At 0 HP the Kaiju is knocked out for this long, then recovers to full HP. |

## Classes

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Classes[0].MaxHp` | 160 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[0].MoveSpeed` | 3.8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Classes[0].Radius` | 0.45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Classes[0].AttackDamage` | 22 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[0].AttackRange` | 1.7 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Classes[0].AttackArcDeg` | 120 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Classes[0].AttackCooldown` | 0.7 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[0].AbilityCooldown` | 10 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[0].GatherSeconds` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Classes[0].CarryCapacity` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Classes[0].AttackWindupSeconds` | 0.12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Classes[0].RecoverySeconds` | 0.08 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Classes[0].Mass` | 1.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Classes[0].Poise` | 25 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |
| `Classes[1].MaxHp` | 100 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[1].MoveSpeed` | 4.2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Classes[1].Radius` | 0.4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Classes[1].AttackDamage` | 12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[1].AttackRange` | 1.4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Classes[1].AttackArcDeg` | 90 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Classes[1].AttackCooldown` | 0.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[1].AbilityCooldown` | 9 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[1].GatherSeconds` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Classes[1].CarryCapacity` | 3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Classes[1].AttackWindupSeconds` | 0.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Classes[1].RecoverySeconds` | 0.08 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Classes[1].Mass` | 0.8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Classes[1].Poise` | 15 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |
| `Classes[2].MaxHp` | 120 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[2].MoveSpeed` | 4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Classes[2].Radius` | 0.45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Classes[2].AttackDamage` | 16 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[2].AttackRange` | 1.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Classes[2].AttackArcDeg` | 90 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Classes[2].AttackCooldown` | 0.65 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[2].AbilityCooldown` | 12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[2].GatherSeconds` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Classes[2].CarryCapacity` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Classes[2].AttackWindupSeconds` | 0.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Classes[2].RecoverySeconds` | 0.08 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Classes[2].Mass` | 1.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Classes[2].Poise` | 18 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |
| `Classes[3].MaxHp` | 120 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[3].MoveSpeed` | 4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Classes[3].Radius` | 0.45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Classes[3].AttackDamage` | 16 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[3].AttackRange` | 1.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Classes[3].AttackArcDeg` | 90 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Classes[3].AttackCooldown` | 0.65 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[3].AbilityCooldown` | 12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[3].GatherSeconds` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Classes[3].CarryCapacity` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Classes[3].AttackWindupSeconds` | 0.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Classes[3].RecoverySeconds` | 0.08 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Classes[3].Mass` | 1.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Classes[3].Poise` | 18 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |
| `Classes[4].MaxHp` | 120 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[4].MoveSpeed` | 4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Classes[4].Radius` | 0.45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Classes[4].AttackDamage` | 16 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[4].AttackRange` | 1.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Classes[4].AttackArcDeg` | 90 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Classes[4].AttackCooldown` | 0.65 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[4].AbilityCooldown` | 12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[4].GatherSeconds` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Classes[4].CarryCapacity` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Classes[4].AttackWindupSeconds` | 0.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Classes[4].RecoverySeconds` | 0.08 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Classes[4].Mass` | 1.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Classes[4].Poise` | 18 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |
| `Classes[5].MaxHp` | 110 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[5].MoveSpeed` | 4.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Classes[5].Radius` | 0.42 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Classes[5].AttackDamage` | 0 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[5].AttackRange` | 4.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Classes[5].AttackArcDeg` | 90 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Classes[5].AttackCooldown` | 0.8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[5].AbilityCooldown` | 9 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Classes[5].GatherSeconds` | 1.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Classes[5].CarryCapacity` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Classes[5].AttackWindupSeconds` | 0 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Classes[5].RecoverySeconds` | 0 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Classes[5].Mass` | 0.9 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Classes[5].Poise` | 15 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |

## Abilities

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Abilities.WarriorShield.DurationSeconds` | 2.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.WarriorShield.DamageReduction` | 0.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.WarriorShield.MoveSpeedMultiplier` | 0.7 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.FarmerRoots.CastRange` | 6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.FarmerRoots.Radius` | 2.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.FarmerRoots.SlowMultiplier` | 0.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.FarmerRoots.SlowSeconds` | 3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerWallBreak.HitsPerState` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerWallBreak.Reach` | 1.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerBarricade.MaxActivePerMiner` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerBarricade.LifetimeSeconds` | 20 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerBarricade.MaxHp` | 120 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerBarricade.MinDistanceFromActor` | 0.9 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerBarricade.PlaceRange` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerTerraform.Reach` | 1.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerTerraform.TeamCooldownTicks` | 30 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerTerraform.MinGapTicks` | 15 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerTerraform.ActorMargin` | 0.05 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.MinerTerraform.CarryMax` | 3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-09 (U-053, not in the browser): blocks a miner can carry. The browser carries exactly one (CarryMax 1 replays it exactly: dig, place, dig...). |
| `Abilities.MinerTerraform.FallSearchRings` | 6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-09 fall rule (U-053, not in the browser): square cell rings searched around a fallen actor for ground to climb out onto [guess: 6, wider than any hole the rate cap lets one team dig in a few seconds]; none = back t... |
| `Abilities.Topple.HitsToTopple` | 3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.Topple.LengthCells` | 4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.Topple.WidthCells` | 3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.Topple.FallTicks` | 18 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.Topple.Reach` | 0.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsMine.PlaceRange` | 1.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsMine.MaxActivePerOwner` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsMine.ArmTicks` | 15 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsMine.Damage` | 70 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsMine.BlastRadius` | 1.2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsMine.LifetimeTicks` | 1800 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsTnt.ThrowRange` | 5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsTnt.FuseTicks` | 45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsTnt.BlastRadius` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsTnt.Damage` | 55 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsTnt.CarveRadius` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsTnt.CooldownTicks` | 150 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.DemolitionsTnt.FlightTicks` | 12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-22 (not in the browser, U-059): ticks from the throw until the bundle touches down (the TntLanded event; the fuse still counts from the throw) [guess: 0.4 s]. |
| `Abilities.DemolitionsTnt.ThrowOnAttack` | true | bool | `Assets/_Game/Sim/Config/GameConfig.cs` | b12-jz RULE (Full rules only, off while Depth.Classic): the ATTACK press throws TNT whenever it is ready (her default attack: aim point, or the nearest enemy within ThrowRange + BlastRadius when there is no aim, else ... |
| `Abilities.DemolitionsTnt.MinThrowRange` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | b12-jz: an attack throw lands at least this far from her (the crater never opens at her feet; her blast never hurts her or her team either way, Demolitions.BlastActors skips her team). |
| `Abilities.BuilderPieces.LadderCost` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Biomass a ladder costs (it spans a wall / cliff cell run up to LadderMaxCells long). |
| `Abilities.BuilderPieces.BridgeCost` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Biomass a rope bridge costs (it spans a void / water run up to BridgeMaxCells long). |
| `Abilities.BuilderPieces.StartBiomass` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Spendable biomass each team starts a match with (so a Builder can act before the first delivery). |
| `Abilities.BuilderPieces.LadderMaxCells` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.BuilderPieces.BridgeMaxCells` | 12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | b12-jz RULE (was 6): longest void / water run a rope bridge spans. |
| `Abilities.BuilderPieces.WidthCells` | 3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Lanes across the span, centred on the line the builder looks along (Topple.WidthCells: wide enough for bots). |
| `Abilities.BuilderPieces.ReachCells` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | The gap or wall must start within this many cells ahead of the builder's cell. |
| `Abilities.BuilderPieces.LifetimeTicks` | 1800 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | A piece lasts this long (60 s; Travis: "temporary ladders/bridges"), carried time included. |
| `Abilities.BuilderPieces.WarnTicks` | 90 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | The PieceCrumbling warning goes out this long before the end (3 s). |
| `Abilities.BuilderPieces.MaxActivePerBuilder` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Pieces one Builder may own at once (on the ground or carried). |
| `Abilities.BuilderPieces.PickupRange` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | An actor picks a piece up (Interact) within this distance of one of its cells (cell edge, plus its radius). |
| `Abilities.BuilderPieces.RefuseLockSeconds` | 0.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | A refused build locks Space this long (so a held key never floods the event ring). |
| `Abilities.Healer.MendHeal` | 6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | MEND (the attack press, class AttackCooldown): HP to the hurt ally nearest the aim within MendRange of her (not herself). |
| `Abilities.Healer.MendRange` | 5.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.Healer.PulseCastRange` | 6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | HEALING PULSE (Space, class AbilityCooldown): at the aim pulled back to PulseCastRange, PulseHeal to every ally within PulseRadius, her included. |
| `Abilities.Healer.PulseRadius` | 3.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.Healer.PulseHeal` | 30 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.Healer.PurifyUnits` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | PURIFY (DigPlace): within PurifyRange of the shrine while a cleanse could run, removes PurifyUnits corruption units at once. |
| `Abilities.Healer.PurifyRange` | 4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Abilities.Healer.PurifyCooldownTicks` | 900 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |

## Bots

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Bots.ThinkHz` | 4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Bots.DirectorPeriodSeconds` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Bots.InertiaBonus` | 0.25 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Utility bonus for staying in the current state. |
| `Bots.MinStateSeconds` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Minimum time in a state before a non-urgent switch. |
| `Bots.HillMinFraction` | 0.3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Director keeps at least this fraction of living bots on hill duty. |
| `Bots.KaijuGroupFraction` | 0.25 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Director sends this fraction (rounded, min 1) to biomass / Kaiju duty as the base quota. |
| `Bots.CorruptWhenBehindBy` | 10 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | A team behind in allegiance by at least this many points may send corruption runs. |
| `Bots.ClassMix` | [0.4, 0.3, 0.3, 0, 0, 0] | double[] | `Assets/_Game/Sim/Config/GameConfig.cs` | Fraction of bots per team per class when filling the roster, indexed by (int)ClassId. |
| `Bots.StuckSeconds` | 1.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Bots.TurnRateDegPerSec` | 300 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-21: a moving bot's move direction turns toward its flow direction at most this fast (deg / s). |
| `Bots.InterceptShrineRadius` | 14 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-64: an enemy carrier inside this radius of the shrine centre is contestable by Kaiju-duty bots. |
| `Bots.PassiveSeconds` | 0 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-16 (the guided first match): for the first PassiveSeconds of match time bots neither target nor damage the human slot (actor 0). 0 = off (the default; every bot decision is the browser's). |
| `Bots.Easy.ThinkPeriodScale` | 1.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Reaction delay: the think period is round(Normal period x this) ticks (Normal 8 at 30 Hz: Easy 12, Hard 4). |
| `Bots.Easy.AimErrorUnits` | 1.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Aim error: thrown or cast ability points (roots, mines, TNT) land up to this many units off, uniform over a disc. |
| `Bots.Easy.TntLeadFraction` | 0 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Aim: TNT aimed at a moving enemy leads it by velocity x fuse time x this (0 = aim where it stands, as Normal). |
| `Bots.Easy.SwingHesitation` | 0.4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Hesitation: share of 1/3-second windows in which the bot holds its melee swing. |
| `Bots.Easy.AbilityChance` | 0.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Ability use: chance per think that a decided ability press (or TNT throw) is sent; else it waits for the next think. |
| `Bots.Easy.ShieldCrowd` | 3 | int | `Assets/_Game/Sim/Config/GameConfig.cs` | Ability use: a Warrior raises its shield with this many enemies within 3 (Normal 2). |
| `Bots.Easy.FocusCarriers` | false | bool | `Assets/_Game/Sim/Config/GameConfig.cs` | Target choice: weight enemies by carried cargo or meat (x CarrierWeight on distance squared) and by missing HP. |
| `Bots.Easy.CarrierWeight` | 0.25 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Target choice: distance-squared multiplier for a carrier when FocusCarriers is on. |
| `Bots.Easy.HealerWeight` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | U-078 (depth rules only, with FocusCarriers): distance-squared multiplier for an enemy Healer (Hard 0.25: it cuts the healer down first). |
| `Bots.Easy.HillSquadFraction` | -1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | U-078 (depth rules only): share of the team's hill-duty bots in the hill squad (U-076); below 0 = Depth.Roles.HillSquadFraction. |
| `Bots.Easy.RolesCarrierWeight` | 0.25 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | U-078: CarrierWeight while the class-roles rule is on (every class carries food, so chasing carriers pulls a Hard bot off the hill all match: 1 = no carrier preference; measured Hard 16 -> 11 of 40 wins against it). |
| `Bots.Easy.OffHillChaseFactor` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Objective discipline: a hill holder's fight utility vs an enemy off the hill is multiplied by this (Normal 0.5). |
| `Bots.Easy.RetreatRange` | 6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Objective discipline: a hurt bot retreats when an enemy is within this range (Normal 6). |
| `Bots.Easy.DodgeKaiju` | false | bool | `Assets/_Game/Sim/Config/GameConfig.cs` | Sim-physics lane (appended): the bot steps out of a telegraphed Kaiju claw cone or slam ring (and jumps the slam). Hard only. |
| `Bots.Hard.ThinkPeriodScale` | 0.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Reaction delay: the think period is round(Normal period x this) ticks (Normal 8 at 30 Hz: Easy 12, Hard 4). |
| `Bots.Hard.AimErrorUnits` | 0 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Aim error: thrown or cast ability points (roots, mines, TNT) land up to this many units off, uniform over a disc. |
| `Bots.Hard.TntLeadFraction` | 0.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Aim: TNT aimed at a moving enemy leads it by velocity x fuse time x this (0 = aim where it stands, as Normal). |
| `Bots.Hard.SwingHesitation` | 0 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Hesitation: share of 1/3-second windows in which the bot holds its melee swing. |
| `Bots.Hard.AbilityChance` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Ability use: chance per think that a decided ability press (or TNT throw) is sent; else it waits for the next think. |
| `Bots.Hard.ShieldCrowd` | 1 | int | `Assets/_Game/Sim/Config/GameConfig.cs` | Ability use: a Warrior raises its shield with this many enemies within 3 (Normal 2). |
| `Bots.Hard.FocusCarriers` | true | bool | `Assets/_Game/Sim/Config/GameConfig.cs` | Target choice: weight enemies by carried cargo or meat (x CarrierWeight on distance squared) and by missing HP. |
| `Bots.Hard.CarrierWeight` | 0.25 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Target choice: distance-squared multiplier for a carrier when FocusCarriers is on. |
| `Bots.Hard.HealerWeight` | 0.25 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | U-078 (depth rules only, with FocusCarriers): distance-squared multiplier for an enemy Healer (Hard 0.25: it cuts the healer down first). |
| `Bots.Hard.HillSquadFraction` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | U-078 (depth rules only): share of the team's hill-duty bots in the hill squad (U-076); below 0 = Depth.Roles.HillSquadFraction. |
| `Bots.Hard.RolesCarrierWeight` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | U-078: CarrierWeight while the class-roles rule is on (every class carries food, so chasing carriers pulls a Hard bot off the hill all match: 1 = no carrier preference; measured Hard 16 -> 11 of 40 wins against it). |
| `Bots.Hard.OffHillChaseFactor` | 0.25 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Objective discipline: a hill holder's fight utility vs an enemy off the hill is multiplied by this (Normal 0.5). |
| `Bots.Hard.RetreatRange` | 3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Objective discipline: a hurt bot retreats when an enemy is within this range (Normal 6). |
| `Bots.Hard.DodgeKaiju` | true | bool | `Assets/_Game/Sim/Config/GameConfig.cs` | Sim-physics lane (appended): the bot steps out of a telegraphed Kaiju claw cone or slam ring (and jumps the slam). Hard only. |

## Render

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Render.CameraPitchDeg` | 38 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Render.CameraYawDeg` | 45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Render.CameraDistance` | 60 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Render.ViewHeightUnits` | 20 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Ortho framing: every screen shows this many world units vertically. |
| `Render.WheelFactorMin` | 0.8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Render.WheelFactorMax` | 1.25 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Render.WheelSensitivity` | 0.0015 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Render.CameraBoundsInset` | 6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Camera target is clamped to map bounds inset by this many units. |
| `Render.CharacterHeight` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | World height of a character model. |
| `Render.KaijuHeight` | 5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | World height of the Kaiju. |
| `Render.CameraFollowSharpness` | 8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Render.DprMin` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Render.DprMax` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Render.ShadowMapSize` | 2048 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Sun shadow map size (px, square). |
| `Render.AutoDropFps` | 50 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Below this fps the postprocessing chain degrades one step. |
| `Render.StandUpClipStart` | 1.45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | CM-20: Lie_StandUp is posed from this clip time (seconds) to its end. |

## Debug

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Debug.ClockSpeedOptions` | [1, 2, 4, 8] | double[] | `Assets/_Game/Sim/Config/GameConfig.cs` |  |

## Physics

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Physics.Classic` | false | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Browser parity master switch: all physics rules off, today's timings (the "classic" setting). |
| `Physics.FairTrades` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | All melee hits of a tick are collected first and applied together, so actor id order never decides a trade. |
| `Physics.KaijuClawMinRealSeconds` | 0.45 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | OD-7: the Kaiju claw wind-up lasts at least this long on screen. |
| `Physics.KaijuSlamMinRealSeconds` | 0.8 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | OD-7: the Kaiju slam telegraph lasts at least this long on screen. |
| `Physics.AttackWindup` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Melee swings wind up before they land (false = damage on the press tick, the browser). |
| `Physics.SwingMoveMultiplier` | 1 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Move speed multiplier while winding up and recovering. DG-69 asks 0.4 (no skating swings); shipped 1.0 because 0.4 cost bot hill presence 0.81 -> 0.68 and CM-64 intercepts 81 -> 77 of 100 (PR tuning table). |
| `Physics.SwingSlowsRecovery` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | The slowdown also covers the recovery after contact (false: the wind-up only). |
| `Physics.Knockback` | false | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Hits shove, heavy hits stagger. Default OFF: with it on, the D-030 passive check fell to 11 of 200 and the CM-64 berserk share to 0.06 (bands 30 and 0.10). Turn on per match; every rule test stages it. |
| `Physics.KnockDecayPer30Hz` | 0.82 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Knock velocity kept per 1/30 s (exponential decay, re-derived for other SimHz). |
| `Physics.KnockStopSpeed` | 0.1 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Knock velocity below this (u/s) is zeroed. |
| `Physics.MeleeKnockPerDamage` | 0.03 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Melee knock distance (u, for mass 1) per point of damage: the Warrior's 22 cleave = 0.66 u. |
| `Physics.KaijuClawKnock` | 2 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Kaiju claw knock distance (u, mass 1). |
| `Physics.SlamKnockCentre` | 2.4 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Slam knock distance at the centre and at the edge (u, mass 1), outward. |
| `Physics.SlamKnockEdge` | 0.6 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.BlastKnockCentre` | 1.8 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | TNT / mine knock distance at the centre and at the edge (u, mass 1), outward. |
| `Physics.BlastKnockEdge` | 0.5 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.CrushKnock` | 1 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | DG-99: a landing pillar shoves anyone under it this far (u, mass 1), out of its strip. |
| `Physics.ShieldFrontKnockShare` | 0.3 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | A Warrior's raised shield takes the push from the front: only this share of the knock reaches him. |
| `Physics.KnockSparesCarriers` | false | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | A fighter carrying biomass or corruption is neither shoved nor staggered (keeps deliveries landing). |
| `Physics.HeavyHitDamage` | 20 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Hit tiers (PR #30's table): light under HeavyHitDamage, heavy, critical at CriticalHitDamage or a finishing blow. |
| `Physics.CriticalHitDamage` | 40 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.HeavyStunSeconds` | 0.1 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Hit-stun (real seconds): a hit at or over the victim's Poise staggers; Kaiju hits stun; the slam knocks down. |
| `Physics.KaijuStunSeconds` | 0.3 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.SlamStunSeconds` | 0.5 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.BlastStunSeconds` | 0.2 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.CrushStunSeconds` | 0.3 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.WallsBlockHits` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Walls, barricades and intact breakables block melee, Kaiju hits, slam, blasts and wall breaks. |
| `Physics.KaijuClawCone` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | The claw hits the cone it showed at wind-up start (not the locked target), every hostile in it. |
| `Physics.KaijuClawConeDeg` | 90 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.SlamShockwave` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Slam damage: full inside SlamFullRadiusShare x R, falling linearly to SlamEdgeDamageShare at R. |
| `Physics.SlamFullRadiusShare` | 0.35 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | 1.75 u: anyone touching the solid body (1.3 + r) takes the full slam |
| `Physics.SlamEdgeDamageShare` | 0.38 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.AirDodgesSlam` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | An actor in the air on the resolve tick takes no slam damage and no push. |
| `Physics.KaijuKnockback` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | The Kaiju's claw and slam throw and stagger (KaijuClawKnock / KaijuStunSeconds, SlamKnock* / SlamStunSeconds) even while the fighters' Knockback is off (the owner: "it's not hitting, it's not fighting"). |
| `Physics.KaijuTopples` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | The Kaiju's claw arc and slam ring knock down structures (Topple: each strike takes one hit off every standing piece in the claw cone, or within KaijuSlamToppleShare x SlamRadius of the slam). |
| `Physics.KaijuSlamToppleShare` | 0.6 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.KaijuKnockScale` | 0.5 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Share of the Kaiju's designed throw distances (KaijuClawKnock, SlamKnock*) applied through KaijuKnockback while the fighters' Knockback is off; the stagger (stun) is always full. With Knockback on, throws are full. |
| `Physics.SolidKaiju` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | The Kaiju is an immovable disc fighters cannot enter; it shoves them as it walks; no trample damage. |
| `Physics.KaijuBodyRadius` | 1.3 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.KaijuGridRadius` | 1.1 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | The Kaiju's wall collider while SolidKaiju (KaijuConfig.GridRadius 0.7 otherwise): its body no longer sinks into walls. |
| `Physics.KaijuNavGuard` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | The Kaiju never walks at a point its body cannot reach (it holds at the last cell it fits in), faces the way it actually moved, claws and slams only what it can see, keeps its target unless a clearly better one appear... |
| `Physics.KaijuGiveUpSeconds` | 1.5 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Seconds of no progress (net move under KaijuProgressRadius, not attacking) before the Kaiju gives its target up. |
| `Physics.KaijuIgnoreSeconds` | 4 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Seconds a given-up target is skipped by target choice. |
| `Physics.KaijuProgressRadius` | 1 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.KaijuRetargetMargin` | 2 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | A new target must be this much closer (straight-line units) than the current one to take over. |
| `Physics.KaijuHiddenPenalty` | 6 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Target choice adds this to the distance of a target behind a wall (prefer what it can see). |
| `Physics.BotWideLine` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Bots walk straight at a fight target only when their whole body fits along the line (not just its centre), so the straight-line / route choice does not flip every tick at a wall end; and they do not strafe and swing a... |
| `Physics.KaijuBridgeSqueeze` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | The Kaiju squeezes across the narrow bridges: on a bridge cell, or within KaijuSqueezeHalo cells of one, a cell counts as walkable for it when its wall collider fits at KaijuSqueezeRadius (the stair-stepped diagonal b... |
| `Physics.KaijuSqueezeRadius` | 0.7 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.KaijuSqueezeHalo` | 2 | int | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.KaijuUnsqueezeRate` | 1 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Metres per second the wall collider grows back to KaijuGridRadius on wide ground. |
| `Physics.KaijuHillSiege` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | The Kaiju gets onto the hill. Miners holding the hill drop barricades in the ring's gaps, and one barricade closes a gap to its 1.1 m body (the east gap is two body cells wide) while fighters still slip past, so the h... |
| `Physics.KaijuBarricadeDamage` | 60 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.KaijuSiegeRadius` | 13 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Barricades this far (metres) from the hill centre count as "in the way" (the ring is at 10.5). |
| `Physics.KaijuHillFightRadius` | 13 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | fix/kaiju-hill-1 round 3 (art lead 2026-11-15: "Fighting on the hill"): an owned (not Berserk, not Neutral) Kaiju picks only hostile fighters within KaijuHillFightRadius of the hill centre (the hill disc and the ring ... |
| `Physics.KaijuHillLeash` | 15 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.KaijuBarricadeCrushPerSecond` | 240 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Its body crushes a barricade it presses against (barricade HP per second; 120 HP = half a second), and its path planning treats barricade cells as passable: a fort of crates round the hill holders no longer cuts the h... |
| `Physics.KaijuHitMemorySeconds` | 4 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.BotCalmSteer` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | fix/movement-shiver-2: bot steering with hysteresis. Straight line or route is a choice held at least BotModeHoldSeconds (straight needs the whole body to fit, it is left only when the centre line is blocked); the cir... |
| `Physics.BotModeHoldSeconds` | 0.5 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.BotReachSlack` | 0.4 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.BotPostArrive` | 0.5 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.BotPostArrivePerAlly` | 0.35 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.BotPostArriveMax` | 1.6 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.BotPostRelease` | 0.8 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.BotMoveDeadzone` | 0.15 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.RelaxedSeparation` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Order-free relaxed separation: pushes accumulated, Iterations passes at Relax, capped per tick (mass split off: it cost the passive check 9 of 200 seeds). |
| `Physics.SeparationIterations` | 3 | int | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.SeparationRelax` | 1 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.SeparationMaxStep` | 0.2 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.AllyPushShare` | 0.5 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Teammates push each other this much softer than enemies (a carrier squeezes past). |
| `Physics.AllyPassOpposing` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | U-071 (DG-22 Demolitions soak stall): a bot in Retreat and a teammate walking INTO it (input directions with a dot product under AllyPassDot) do not push at all, so the retreating bot and the allies coming the other w... |
| `Physics.AllyPassDot` | -0.3 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.SeparationByMass` | false | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Separation splits each push by ClassConfig.Mass (false: half each, as the classic pass). |
| `Physics.CornerRounding` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Bodies slide round convex wall corners instead of snagging (S10, every actor). |
| `Physics.CornerNudgeMax` | 0.1 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Largest overlap with a corner cell that is slid round (u); deeper overlaps still stop. |
| `Physics.BlastShockwave` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Blast damage falls from full at the centre to BlastEdgeDamageShare at the edge; push outward; air = push only. |
| `Physics.BlastEdgeDamageShare` | 0.35 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.ChainReaction` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | A blast arms every live TNT or armed mine in its radius to blow on the next tick. |
| `Physics.PillarFallsOnLanding` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | A toppling pillar changes the ground when it lands (Topple.FallTicks later), crushing and pushing anyone under it. |
| `Physics.PillarCrushDamage` | 25 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.ArcCoversBody` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | The cleave arc is widened per target by asin(r / d), so a swing at a big body's flank connects. |
| `Physics.MeatPop` | true | bool | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Meat orbs pop a short deterministic hop from the corpse onto walkable ground instead of stacking. |
| `Physics.MeatPopDistance` | 0.6 | double | `Assets/_Game/Sim/Config/PhysicsConfig.cs` |  |
| `Physics.CleaveMaxTargets` | 0 | int | `Assets/_Game/Sim/Config/PhysicsConfig.cs` | Most enemies one swing can hit (0 = no cap; OD-10 says no cap for now). |

## Depth

| Path | Value | Type | File | Meaning |
|---|---|---|---|---|
| `Depth.Classic` | false | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` | Master switch: true = none of the mechanics-depth rules run (the "classic" setting). |
| `Depth.Roles.Enabled` | true | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Roles.MayFeed` | [true, true, true, true, true, true] | bool[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | May this class hand biomass (plant, meat, wood, water) to the egg? U-070 (owner 2026-11-12, supersedes U-063's Farmers-only feeding): every class, at every team size, as in the browser. |
| `Depth.Roles.BestFeeder` | [false, true, false, false, false, false] | bool[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | U-070: the class that is BEST at feeding (the Farmer): its deliveries count BestFeederBonus extra, on top of its browser carry (3 a trip) and gather speed (1 s). Other classes hand their food to it for the PassBonus. |
| `Depth.Roles.BestFeederBonus` | 0.25 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | U-070: extra allegiance share of a best feeder's delivery (0.25 = a Farmer's unit counts 1.25). Chosen from the PR table. |
| `Depth.Roles.PassBonus` | 0.1 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | U-070: extra allegiance share of a unit that reached the egg through a hand-off to a Farmer or the team stock (the optional plays): paid once, on delivery, never on a deposit (no bank-and-withdraw loop). |
| `Depth.Roles.BestFeederGatherBias` | 1.25 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | U-070 bots: a Farmer bot's gather utility is scaled by this (it prioritises feeding). |
| `Depth.Roles.DeliverNearShrine` | 10 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | U-070 bots: any bot holding food within this many units of the shrine goes to deliver it. |
| `Depth.Roles.MayCorrupt` | [true, true, true, true, true, false] | bool[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | May this class gather, carry and deliver corruption? U-072 (2026-11-12): every class but the Healer (she purifies). |
| `Depth.Roles.CorruptionRunner` | [true, false, true, true, false, false] | bool[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | U-072 bots: the classes a team that is behind sends on a deliberate corruption run first (the raiders). |
| `Depth.Roles.HandoffRange` | 1.6 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | A carrier that may not feed hands its biomass to an allied feeder within this distance (edge to edge) by holding interact. |
| `Depth.Roles.RefuseLockTicks` | 45 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Sim ticks between two DeliverRefused events for one actor (a held key never floods the event ring). |
| `Depth.Roles.ClassMix` | [0.3125, 0.25, 0.125, 0.125, 0.0625, 0.125] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | The bot lineup when the rules are on (replaces Bots.ClassMix): every class on the field so every role exists. |
| `Depth.Roles.SmallTeamMax` | 8 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | The lineup for a team of SmallTeamMax or fewer: more fighters, no Demolitions or Builder (a five with one of each class had a single Warrior and two classes with no weapon). |
| `Depth.Roles.SmallTeamClassMix` | [0.4, 0.2, 0.2, 0, 0, 0.2] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Roles.EscortSeekRange` | 30 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bots: an escort (an egg-duty bot that may not feed) follows the nearest allied carrier within this many units. |
| `Depth.Roles.EscortKeepDistance` | 2.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bots: an escort stops this close to the carrier it guards. |
| `Depth.Roles.EscortUtility` | 0.75 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bots: utility of escorting (over the 0.6 x hp fight of an egg-duty bot, under an intercept at 1). |
| `Depth.Roles.HillSquadFraction` | 0.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | U-076 bots: the hill squad. Every director period, ceil(hill-duty bots x HillSquadFraction) of a team's hill-duty bots that do not run supply hold the hill and leave the side jobs (power-ups, supply drops, calls other... |
| `Depth.Roles.HillSquadStyleRank` | [1, 0, 3, 2] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | U-076: squad rank per play style, indexed Brawler, Guard, Harvester, Sapper (BotNames). |
| `Depth.Roles.ShortHandedMinShare` | 0.15 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | U-077 bots: the director's short-handed plan (CM-09: one egg-duty bot, everyone else on the hill) starts only when the team is down by at least this share of the other side's active players. |
| `Depth.Sources.Enabled` | true | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Sources.WoodAnchors` | [8, 29, 27, 14, 27, 59] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | Wood groves: (x, z) anchors on the west half, each mirrored to the east; placed on the nearest open floor. |
| `Depth.Sources.WaterAnchors` | [43, 45, 50, 6, 56, 64] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | Water springs: west-half anchors, mirrored; an anchor on the centre line (x = 56) is a single spring. |
| `Depth.Sources.WoodCharges` | 4 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Sources.WaterCharges` | 3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Sources.WoodRegrowTicks` | 600 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Ticks for one charge to grow back (wood 20 s, water 15 s). |
| `Depth.Sources.WaterRegrowTicks` | 450 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Sources.WoodGatherScale` | [1, 1.5, 0.5, 0.75, 0.5, 1] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | Gather time = class GatherSeconds x this. Builders and Miners chop fastest. |
| `Depth.Sources.WaterGatherScale` | [1, 1, 1, 1, 1, 0.67] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | Healers and Farmers draw water fastest. |
| `Depth.Sources.WoodAllegiancePerUnit` | 7 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Allegiance per unit fed to the egg (plant is 10, meat 6). |
| `Depth.Sources.WaterAllegiancePerUnit` | 4 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Sources.WaterCoolPerUnit` | 3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Instability one unit of water removes when fed to the egg (a corruption unit adds 9). |
| `Depth.Sources.BankRange` | 4.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | The base zone: a press of interact within this distance of the own pad centre banks the load. |
| `Depth.Sources.PlaceSearchRings` | 8 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Placement: rings searched around an anchor, and keep-outs from other landmarks. |
| `Depth.Sources.NodeKeepOut` | 4 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Sources.ShrineKeepOut` | 5.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Sources.HillKeepOut` | 3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Sources.PadKeepOut` | 2 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Sources.SupplyFraction` | 0.125 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bots: floor(living bots x this) per team run supply before the hatch (16: 2, 5: 0). |
| `Depth.Sources.SupplyClassRank` | [9, 9, 1, 2, 0, 9] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bots: which classes run supply, lowest first; 9 = never. |
| `Depth.Sources.SupplyUtility` | 0.8 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bots: utility of the supply run (under a hill bot's fight at full health, over the hill's 0.7). |
| `Depth.Stations.Enabled` | true | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Stations.Range` | 4.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | The station zone: within this distance of the own pad centre. |
| `Depth.Stations.UpgradeWood` | [3, 5] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | Wood to go from level L to L + 1 (index L - 1). |
| `Depth.Stations.UpgradeWater` | [1, 2] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | Water to go from level L to L + 1. |
| `Depth.Stations.MaxHpBonus` | [0.15, 0.3] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | Max HP bonus at level L + 1 (index L - 1), over the class row. |
| `Depth.Stations.SwitchAbilityLockSeconds` | 3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Seconds the ability stays locked after a live class switch. |
| `Depth.Stations.BotBuyPeriodTicks` | 30 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bots: a Builder bot in its station tries to buy once per this many ticks. |
| `Depth.Pickups.Enabled` | true | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.PickupRange` | 1.2 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Walk-over reach from the pickup centre, added to the actor's radius. |
| `Depth.Pickups.Capacity` | 96 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Slots of the fixed pickup pool. |
| `Depth.Pickups.CoinsPerKnockOut` | 3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Coins a knocked-out actor leaves where it fell, and how long they lie there (20 s). |
| `Depth.Pickups.CoinDropLifetimeTicks` | 600 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.CoinAnchors` | [31, 17, 31, 56, 46, 29, 46, 51, 52, 11] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | Coin piles on the map: west-half (x, z) anchors, each mirrored; value, first spawn and respawn after a pick-up (30 s). |
| `Depth.Pickups.CoinPileValue` | 5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.CoinFirstTicks` | 150 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.CoinRespawnTicks` | 900 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.PowerSpotsZ` | [30, 50, 14, 66] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | Power-up spots on the centre line (x = half the map width): their z. |
| `Depth.Pickups.FirstPowerUpTicks` | 600 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | First power-up at 20 s, then one every 45 s; announced 5 s before it can be taken; gone 30 s after that. |
| `Depth.Pickups.PowerUpPeriodTicks` | 1350 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.PowerUpArmTicks` | 150 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.PowerUpLifetimeTicks` | 900 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.HasteSpeed` | 1.3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.HasteTicks` | 300 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.MightDamage` | 1.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.MightTicks` | 300 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.WardHp` | 60 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.WardTicks` | 450 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.DropMinTicks` | 60 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | A knocked-out carrier drops its power-up when at least this much time was left; the drop lies 15 s. |
| `Depth.Pickups.DropLifetimeTicks` | 450 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pickups.BotGrabRange` | 9 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bots: a bot in transit or in a fight goes for a power-up within this range and in line of sight, at this utility. |
| `Depth.Pickups.BotGrabUtility` | 0.97 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.Enabled` | true | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.RateCount` | 3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | At most RateCount pings per actor in any RateWindowTicks (3 per 5 s, the HUD's limit). |
| `Depth.Pings.RateWindowTicks` | 150 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.PingLifeTicks` | 180 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Ticks the marker lives (6 s; the view fades it). An answer already taken runs its own timer. |
| `Depth.Pings.AnswerRange` | 40 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.MaxAttack` | 3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.MaxDefend` | 3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.MaxHelp` | 2 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.HillMinMates` | 2 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | A bot holding the hill answers only with at least this many teammates on the hill. |
| `Depth.Pings.AttackSeekRange` | 8 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.AttackTicks` | 360 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.ArriveRange` | 1.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.DefendHoldRange` | 5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.DefendReach` | 3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.DefendTicks` | 360 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.HelpTicks` | 240 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.HelpKeepDistance` | 2.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.OrderUtility` | 0.98 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Utility of following an order (over the hill's 0.7 + inertia, under a retreat's 0.9 only for the hurt, who drop the order first). |
| `Depth.Pings.RouteSlotsPerTeam` | 4 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Route slots per team (one per answered ping, round robin; a bot whose slot was reused walks straight). |
| `Depth.Pings.BotCalls` | true | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bots call too. |
| `Depth.Pings.BotHelpHpFraction` | 0.35 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.BotPingCooldownTicks` | 600 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.BotTeamPingGapTicks` | 240 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.BotPingResponders` | 1 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.WarriorGuardShare` | 0.4 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Play styles: share of Warriors that are Guards, how readily each style fights, and its distance multiplier when answering Attack, and Defend or Help. |
| `Depth.Pings.StyleFightScale` | [1.15, 0.95, 0.8, 1] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.StyleAttackBias` | [0.6, 1, 1.5, 1] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Pings.StyleDefendBias` | [1, 0.6, 1.5, 1] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.Enabled` | true | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.BridgeCollapse` | false | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bridge collapse in the event rotation (2026-11-13, off by default). |
| `Depth.Events.FirstEventTicks` | 1500 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | First event at 50 s, then one every 60 s; the three kinds rotate from a seed-fixed start. |
| `Depth.Events.PeriodTicks` | 1800 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.DropSpotsZ` | [34, 46, 22, 58] | double[] | `Assets/_Game/Sim/Config/DepthConfig.cs` | Supply drop: centre-line spots (their z), warning 8 s, claim by standing 3 s uncontested within range, gone after 30 s. |
| `Depth.Events.DropWarnTicks` | 240 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.DropClaimRange` | 2.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.DropClaimTicks` | 90 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.DropLifetimeTicks` | 900 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.DropWood` | 4 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.DropWater` | 2 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.DropCoins` | 10 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.DropPowerUpDelayTicks` | 30 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.BotDropRange` | 12 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bots: a bot within this range and in line of sight walks to the drop, at this utility (a healthy hill bot's fight still wins). |
| `Depth.Events.BotDropUtility` | 0.85 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.EggWindowTicks` | 600 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Egg exposed: 20 s in which every delivery counts this many times toward allegiance. |
| `Depth.Events.EggFeedScale` | 2 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.BridgeWarnTicks` | 240 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Bridge collapse: warning 8 s; the middle third within this half width falls; a span with fewer bridge cells there is skipped. |
| `Depth.Events.BridgeHalfWidth` | 3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.BridgeMinCells` | 2 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.BridgeRepairRange` | 3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | A Builder within this range of a broken end holds interact 3 s and pays this much wood; else it rebuilds itself after 45 s. |
| `Depth.Events.BridgeRepairTicks` | 90 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.BridgeRepairWood` | 2 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.Events.BridgeAutoRepairTicks` | 1350 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.LightWeapons.Enabled` | true | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.LightWeapons.Builder.Damage` | 16 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Damage per hit (before the Might pickup). |
| `Depth.LightWeapons.Builder.Range` | 1.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Reach from the wielder's centre (u); a target counts when its body edge is inside. |
| `Depth.LightWeapons.Builder.ArcDeg` | 90 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Full width of the swing arc (degrees). |
| `Depth.LightWeapons.Builder.CooldownSeconds` | 0.65 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Seconds between swings (the attack cooldown; the Healer shares it with her Mend). |
| `Depth.LightWeapons.Builder.WindupSeconds` | 0.1 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Wind-up before contact and follow-through after it (real seconds at the pace; 0 with sim physics off). |
| `Depth.LightWeapons.Builder.RecoverySeconds` | 0.08 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.LightWeapons.Builder.Shove` | 0.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Push on every hit that lands (u travelled at mass 1; heavier classes move less). With fighter knockback on, the normal damage knock counts toward it, so the total push is this, never more. |
| `Depth.LightWeapons.Demolitions.Damage` | 9 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Damage per hit (before the Might pickup). |
| `Depth.LightWeapons.Demolitions.Range` | 1.3 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Reach from the wielder's centre (u); a target counts when its body edge is inside. |
| `Depth.LightWeapons.Demolitions.ArcDeg` | 90 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Full width of the swing arc (degrees). |
| `Depth.LightWeapons.Demolitions.CooldownSeconds` | 0.75 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Seconds between swings (the attack cooldown; the Healer shares it with her Mend). |
| `Depth.LightWeapons.Demolitions.WindupSeconds` | 0.12 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Wind-up before contact and follow-through after it (real seconds at the pace; 0 with sim physics off). |
| `Depth.LightWeapons.Demolitions.RecoverySeconds` | 0.1 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.LightWeapons.Demolitions.Shove` | 0.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Push on every hit that lands (u travelled at mass 1; heavier classes move less). With fighter knockback on, the normal damage knock counts toward it, so the total push is this, never more. |
| `Depth.LightWeapons.Healer.Damage` | 8 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Damage per hit (before the Might pickup). |
| `Depth.LightWeapons.Healer.Range` | 1.2 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Reach from the wielder's centre (u); a target counts when its body edge is inside. |
| `Depth.LightWeapons.Healer.ArcDeg` | 90 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Full width of the swing arc (degrees). |
| `Depth.LightWeapons.Healer.CooldownSeconds` | 0.8 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Seconds between swings (the attack cooldown; the Healer shares it with her Mend). |
| `Depth.LightWeapons.Healer.WindupSeconds` | 0.12 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Wind-up before contact and follow-through after it (real seconds at the pace; 0 with sim physics off). |
| `Depth.LightWeapons.Healer.RecoverySeconds` | 0.1 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.LightWeapons.Healer.Shove` | 0.6 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | Push on every hit that lands (u travelled at mass 1; heavier classes move less). With fighter knockback on, the normal damage knock counts toward it, so the total push is this, never more. |
| `Depth.Hut.On` | true | bool | `Assets/_Game/Sim/Config/HutConfig.cs` | The rule's own switch (Full profile only: systems read it through DepthRules.On). |
| `Depth.Hut.LevelCost` | [6, 10, 14, 18, 24] | double[] | `Assets/_Game/Sim/Config/HutConfig.cs` | Ore each level costs, levels 1 to 5 (72 in all). The hut starts at level 0. |
| `Depth.Hut.StatPerLevel` | 0.05 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | +5% max HP, damage dealt and healing per level, every class of the team (move speed and cooldowns are not scaled). |
| `Depth.Hut.MineTicks` | 30 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | One channel per ore (30 ticks = 1.0 s); a hit restarts it. |
| `Depth.Hut.OreCarry` | 3 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | Ore a Miner carries (his class CarryCapacity row stays 1 for every other kind). |
| `Depth.Hut.HaulTicks` | 150 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | 150 ticks = 5 s of HaulSpeed and no carry slowdown. |
| `Depth.Hut.HaulSpeed` | 1.2 | double | `Assets/_Game/Sim/Config/HutConfig.cs` |  |
| `Depth.Hut.OreAnchors` | [31, 13, 31, 59, 42, 40] | double[] | `Assets/_Game/Sim/Config/HutConfig.cs` | (31,13) NW courtyard north, (31,59) SW courtyard south, (42,40) central island west rim (contested). |
| `Depth.Hut.OreCharges` | 6 | double | `Assets/_Game/Sim/Config/HutConfig.cs` |  |
| `Depth.Hut.OreRegrowTicks` | 360 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | One charge regrows per 360 ticks (12 s; was 450 = 15 s). b14-c item 1 (16v16 L5 in 4 of 16 matches, limit one third): the cause is the r3 HILL SHIFT, not dodge or walls. |
| `Depth.Hut.ExtraWoodAnchors` | [40, 27, 40, 53] | double[] | `Assets/_Game/Sim/Config/HutConfig.cs` | More trees for the Builder (Jeisil): added to the groves, mirrored, so 10 groves instead of 6. |
| `Depth.Hut.BuilderTerraform` | true | bool | `Assets/_Game/Sim/Config/HutConfig.cs` | While the hut rule is on, the dig / place terraform (F, Terraform.cs: same costs, reach, carry and rate cap) belongs to the BUILDER instead of the Miner (the Miner's F is MINE). |
| `Depth.Hut.BotMineUtility` | 0.82 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | Task MineOre utility (supply is 0.8, corrupt 0.85 x greed). |
| `Depth.Hut.BotCarryUtility` | 0.92 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | Task utility once the bot carries ore (mining on, or the hut run): above the shrine delivery utility (0.9) so a Miner bot never walks ore to the egg. |
| `Depth.Hut.BotHaulEnemyRange` | 8 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | HAUL when carrying ore and an enemy is this close (u). |
| `Depth.Hut.BotHaulHomeOre` | 2 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | HAUL on the way to the hut with this much ore. |
| `Depth.Hut.BotFightRange` | 8 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | An empty-handed Miner bot with an enemy this close fights (today's logic) instead of walking to a vein (u). |
| `Depth.Hut.BotFightWithOreRange` | 4 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | b13 integrator tuning pass: a Miner bot carrying ore still fights back when an enemy is this close (u). |
| `Depth.Hut.BotDefendUtility` | 1 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | b13 r3 (Miner bots fight): the Fight utility BotMine offers when an empty-handed Miner bot sees an enemy within BotFightRange (with ore: BotFightWithOreRange), and at the top level when he stands on the hill (radius p... |
| `Depth.Hut.BotTopFightHillMargin` | 2 | double | `Assets/_Game/Sim/Config/HutConfig.cs` |  |
| `Depth.Hut.BotHillShiftFromLevel` | 3 | int | `Assets/_Game/Sim/Config/HutConfig.cs` | b13 r3: the HILL SHIFT (BotMine): from this hut level a Miner bot that has just banked his ore spends BotHillShiftSeconds on the hill before mining again (0 s = off). |
| `Depth.Hut.BotHillShiftSeconds` | 15 | double | `Assets/_Game/Sim/Config/HutConfig.cs` |  |
| `Depth.Hut.BotHillShiftAheadSeconds` | 45 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | b13 r3 integrator (snowball guard, "higher-level team at 6:00 wins at most 75%"): the shift when his team's hut is AHEAD of the enemy's (longer, and no per-team cap: the leader's Miners mine less) and when it is BEHIN... |
| `Depth.Hut.BotHillShiftBehindSeconds` | 0 | double | `Assets/_Game/Sim/Config/HutConfig.cs` |  |
| `Depth.Hut.BotHillShiftMaxPerTeam` | 1 | int | `Assets/_Game/Sim/Config/HutConfig.cs` | At most this many of a team's Miners are on a hill shift at once (the others keep mining). |
| `Depth.Hut.BotEscapeUtility` | 2 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | A Miner bot that ended up in Deliver with ore (the shrine would refuse it) is pulled back to the hut at this utility. |
| `Depth.Hut.BotTaskSeconds` | 20 | double | `Assets/_Game/Sim/Config/HutConfig.cs` | A task lasts this long without a fresh think (s). |
| `Depth.Build.On` | true | bool | `Assets/_Game/Sim/Config/BuildConfig.cs` | The rule's own switch (Full profile only: systems read it through DepthRules.On). |
| `Depth.Build.Reach` | 3 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | The aim point is clamped to this distance from the Builder; the cell under it is the piece's centre. |
| `Depth.Build.AimAxisMin` | 0.3 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | Below this aim distance the wall's axis follows his facing instead of the aim direction. |
| `Depth.Build.WallCells` | 3 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | Cells in a wall (tier 0 and 1, and every short wall). |
| `Depth.Build.WallCellsTier2` | 5 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | Cells in a tall wall at tier 2 (hut level 4 to 5). |
| `Depth.Build.TierAtHutLevel` | [0, 0, 1, 1, 2, 2] | int[] | `Assets/_Game/Sim/Config/BuildConfig.cs` | Tier by the team's hut level 0..5 (Blacksmith.Level): 0 = short pieces only, 1 = tall pieces, 2 = 5-cell tall walls at TallHp2. |
| `Depth.Build.HillKeepout` | 3 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | No piece cell within this distance of the hill centre (the hill's inner circle). |
| `Depth.Build.ActorClearance` | 0.9 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | No piece cell where a living actor or the hatched Kaiju is within this distance + its radius of the cell centre (MinerBarricade.MinDistanceFromActor). |
| `Depth.Build.ShortHp` | 60 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | Short pieces (Cell.LowWall: a jump clears them): HP per cell and life. |
| `Depth.Build.ShortLifeTicks` | 1350 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` |  |
| `Depth.Build.TallHp` | 120 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | Tall pieces (Cell.Barricade: a jump does not clear them): HP per cell at tier 1, at tier 2, and life. |
| `Depth.Build.TallHp2` | 150 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` |  |
| `Depth.Build.TallLifeTicks` | 1800 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` |  |
| `Depth.Build.MaxCellsPerBuilder` | 15 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | Wall cells one Builder may have standing; building past it crumbles his oldest piece first. |
| `Depth.Build.LockTicks` | 18 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | Ticks between two builds of one Builder (0.6 s). |
| `Depth.Build.TntDamage` | 60 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | A TNT blast takes this much HP off every enemy piece cell whose centre is within BlastRadius. |
| `Depth.Build.PoolBuildersPerTeam` | 4 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | The barricade pool grows at world creation by MaxCellsPerBuilder x this x 2 teams slots (class switching can add Builders; past it a build is refused "full"). |
| `Depth.Build.CostShortWall` | 1 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` |  |
| `Depth.Build.CostTallWall` | 2 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` |  |
| `Depth.Build.CostBlock` | 1 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` |  |
| `Depth.Build.CostFill` | 1 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` |  |
| `Depth.Build.StartWood` | 4 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | Wood each team starts with for walls (its own credit, so BuilderPieces.StartBiomass is not counted twice). |
| `Depth.Build.BotHillEnemies` | 3 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | Rule 1: the team holds the hill and this many living enemies are within BotHillEnemyRange of its centre. |
| `Depth.Build.BotHillEnemyRange` | 14 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` |  |
| `Depth.Build.BotHillEdgeExtra` | 1.5 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | Rule 1: the wall goes at hill radius + this toward the enemy centroid, at most once per BotHillEveryTicks. |
| `Depth.Build.BotHillEveryTicks` | 90 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | b13 integrator: 180 -> 90 (walls per 16v16 match 1.5 after the first pass, need 2) |
| `Depth.Build.BotGuardHpShare` | 0.5 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | Rule 2: own HP under this share of max and an enemy within BotGuardRange: a short wall BotGuardDistance toward it. |
| `Depth.Build.BotGuardRange` | 5 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | b13 integrator: 4 -> 5 |
| `Depth.Build.BotGuardDistance` | 2 | double | `Assets/_Game/Sim/Config/BuildConfig.cs` | b13 integrator: 1.5 -> 2.0 (fewer refusals for overlapping the fight) |
| `Depth.Dodge.On` | true | bool | `Assets/_Game/Sim/Config/DodgeConfig.cs` | The rule's own switch (Full profile only: systems read it through DepthRules.On). |
| `Depth.Dodge.Distance` | 3 | double | `Assets/_Game/Sim/Config/DodgeConfig.cs` | Dash length (u) and its ticks (0.2 s); the per-tick shares are an ease-out (Dodge.Share). |
| `Depth.Dodge.DashTicks` | 6 | int | `Assets/_Game/Sim/Config/DodgeConfig.cs` |  |
| `Depth.Dodge.IFrameTicks` | 5 | int | `Assets/_Game/Sim/Config/DodgeConfig.cs` | Invulnerable on dash ticks 1 to IFrameTicks (0.17 s), every cause but Fall. |
| `Depth.Dodge.RecoverTicks` | 2 | int | `Assets/_Game/Sim/Config/DodgeConfig.cs` | Then this many ticks of walking at RecoverSpeedScale of the class speed. |
| `Depth.Dodge.RecoverSpeedScale` | 0.5 | double | `Assets/_Game/Sim/Config/DodgeConfig.cs` |  |
| `Depth.Dodge.CooldownTicks` | 90 | int | `Assets/_Game/Sim/Config/DodgeConfig.cs` | From the press to the next dodge (3.0 s). |
| `Depth.Dodge.MinMoveInput` | 0.2 | double | `Assets/_Game/Sim/Config/DodgeConfig.cs` | The move input decides the direction when its length is over this; else the facing. |
| `Depth.Dodge.MinClearTicks` | 2 | int | `Assets/_Game/Sim/Config/DodgeConfig.cs` | A planned path with fewer clear ticks than this is refused. |
| `Depth.Dodge.ReactionTicks` | [12, 8, 5] | int[] | `Assets/_Game/Sim/Config/DodgeConfig.cs` | A bot reacts to a threat only once it has existed this many ticks (the ORDER's numbers). |
| `Depth.Dodge.DodgeChance` | [0.15, 0.35, 0.6] | double[] | `Assets/_Game/Sim/Config/DodgeConfig.cs` | Chance a bot dodges a slam, claw or melee threat (one decision per threat per bot, a stateless hash of seed, tick and id). |
| `Depth.Dodge.TntChance` | [0.1, 0.25, 0.4] | double[] | `Assets/_Game/Sim/Config/DodgeConfig.cs` | b13 r3: chance a bot dodges an enemy TNT it stands next to (its own roll, lower than the others). |
| `Depth.Dodge.SlamTicksLeft` | 15 | int | `Assets/_Game/Sim/Config/DodgeConfig.cs` | Kaiju slam: the telegraph circle covers the bot with this many ticks or fewer left. |
| `Depth.Dodge.TntFuseTicksLeft` | 20 | int | `Assets/_Game/Sim/Config/DodgeConfig.cs` | Enemy TNT: landed, this many fuse ticks or fewer left, the bot within BlastRadius + TntSlack. |
| `Depth.Dodge.TntSlack` | 0.3 | double | `Assets/_Game/Sim/Config/DodgeConfig.cs` |  |
| `Depth.Dodge.MeleeSlack` | 0.3 | double | `Assets/_Game/Sim/Config/DodgeConfig.cs` | Enemy melee at the bot within the attacker's reach + MeleeSlack (+ the bot's body). |
| `Depth.Dodge.MeleeThreatIsTheCycle` | true | bool | `Assets/_Game/Sim/Config/DodgeConfig.cs` | b13 r3: a melee threat is the attacker's whole swing CYCLE aimed at the bot (wind-up, then its attack cooldown until the next swing), not only the wind-up. |
| `Depth.Dodge.HillAware` | true | bool | `Assets/_Game/Sim/Config/DodgeConfig.cs` | b13 r3 hill-aware dodges: a bot standing inside the hill ring only takes a dodge whose end stays at least HillMargin inside the ring (it tries the threat direction, then turned 45, 90, 135 and 180 degrees, never towar... |
| `Depth.Dodge.HillMargin` | 0.5 | double | `Assets/_Game/Sim/Config/DodgeConfig.cs` |  |
| `Depth.Dodge.JumpProbe` | 1 | double | `Assets/_Game/Sim/Config/DodgeConfig.cs` | Jump: a LowWall within JumpProbe u ahead of the steering and the cell past it walkable; at most once per JumpEveryTicks. |
| `Depth.Dodge.JumpEveryTicks` | 30 | int | `Assets/_Game/Sim/Config/DodgeConfig.cs` |  |
| `Depth.KaijuTypes.On` | true | bool | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | The rule's own switch (Full profile only: systems read it through DepthRules.On). |
| `Depth.KaijuTypes.RefShare` | [0.9, 0.1, 0.01, 0.02] | double[] | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | Reference share per kind (plant, meat, wood, water): a kind's share of the feed divided by this is its score. |
| `Depth.KaijuTypes.RefFloor` | 0.02 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | The floor applied when pinning RefShare to a kind the control never feeds (wood, water). |
| `Depth.KaijuTypes.TieEps` | 1E-09 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | Two best scores within this of each other = a tie = ELDER. |
| `Depth.KaijuTypes.ElderBelowScore` | 1.02 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | b13 r3 (ELDER reachable): a BALANCED feed hatches ELDER. When the best score is below this (no kind fed more than 3 % over its reference share) the mix is "today's mix" and today's Kaiju hatches. |
| `Depth.KaijuTypes.LevelUnits` | [8, 16, 24] | double[] | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | Units of the type's own kind fed (both teams) for power level 1, 2, 3 (below the first: level 0, trait only). |
| `Depth.KaijuTypes.GroveHpScale` | 1.05 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | b13 r3 tuning (first Kaiju knock-out +24 % against the control, limit 20 %): GROVE was the cause. |
| `Depth.KaijuTypes.GroveSpeedScale` | 0.98 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` |  |
| `Depth.KaijuTypes.RegrowthDelayTicks` | 240 | int | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | REGROWTH starts after this many Kaiju ticks without damage (240 = 8 s at 30 Hz; batch 13 shipped 150). |
| `Depth.KaijuTypes.RegrowthPctPerSecond` | [0.2, 0.35, 0.5] | double[] | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | REGROWTH per level 1..3: percent of max HP healed per second (never above max, not while knocked out; batch 13 shipped 0.4 / 0.7 / 1.0). |
| `Depth.KaijuTypes.BruteMeleeDamageScale` | 1.15 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` |  |
| `Depth.KaijuTypes.BruteSpeedScale` | 1.08 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` |  |
| `Depth.KaijuTypes.BruteHpScale` | 0.95 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` |  |
| `Depth.KaijuTypes.FrenzyBelowHpShare` | 0.5 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | FRENZY applies below this share of max HP. |
| `Depth.KaijuTypes.FrenzyMeleeCooldownScale` | [0.85, 0.75, 0.65] | double[] | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | FRENZY per level 1..3: melee (claw) cooldown multiplier. |
| `Depth.KaijuTypes.TimberHpScale` | 1.05 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` |  |
| `Depth.KaijuTypes.TimberSlamRadiusAdd` | 0.75 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` |  |
| `Depth.KaijuTypes.BarkskinExplosionScale` | [0.8, 0.7, 0.6] | double[] | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | BARKSKIN per level 1..3: multiplier on explosion damage to the Kaiju (DamageCause.Explosion: TNT). |
| `Depth.KaijuTypes.MireSlamCooldownScale` | 0.8 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` |  |
| `Depth.KaijuTypes.MireSpeedScale` | 0.92 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` |  |
| `Depth.KaijuTypes.ShroudTriggerCount` | 3 | int | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | SHROUD triggers when at least this many affected fighters are within ShroudTriggerRange of the Kaiju. |
| `Depth.KaijuTypes.ShroudTriggerRange` | 8 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` |  |
| `Depth.KaijuTypes.ShroudPuffTicks` | 15 | int | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | The puff telegraph (event KaijuPower) before the cloud, in ticks (15 = 0.5 s). |
| `Depth.KaijuTypes.ShroudRadius` | [5, 6, 7] | double[] | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | Cloud radius per level 1..3 (u), centred on the Kaiju and following it. |
| `Depth.KaijuTypes.ShroudTicks` | [120, 150, 180] | int[] | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | Cloud duration per level 1..3 in ticks (4 / 5 / 6 s). |
| `Depth.KaijuTypes.ShroudCooldownTicks` | 600 | int | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | Cooldown from the cloud's end, in ticks (20 s). |
| `Depth.KaijuTypes.SightInCloud` | 2.5 | double | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | An affected fighter inside the cloud sees (and bots target) only this far. |
| `Depth.KaijuTypes.BotsAvoidCloud` | true | bool | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | b13 r3: bots the SHROUD would blind leave the cloud and skirt its rim unless contesting the hill (BotKaijuFeed.AvoidCloud). |
| `Depth.KaijuTypes.FeedPlanGatherScale` | [1, 0.6, 0.8, 0.8] | double[] | `Assets/_Game/Sim/Config/KaijuTypesConfig.cs` | Per plan (0 plant, 1 meat, 2 wood, 3 water): multiplier on the bots' plant-gather utility (plan 0 = today). |
| `Depth.Corruption.On` | false | bool | `Assets/_Game/Sim/Config/CorruptionConfig.cs` | The rule's own switch (Full profile only: systems read it through DepthRules.On). |
| `Depth.TerrainHeight.Enabled` | true | bool | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` | The ONE switch of the rule (default on in Full rules). |
| `Depth.TerrainHeight.MaxStack` | 3 | int | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` | Highest stack (levels above the island) and deepest pit (levels below). |
| `Depth.TerrainHeight.MaxDepth` | 2 | int | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` |  |
| `Depth.TerrainHeight.StepU` | 0.45 | double | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` | One level in view units (the view only; the sim works in whole levels). |
| `Depth.TerrainHeight.WalkUp` | 1 | int | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` | Levels a grounded walker steps up without help; an airborne (jumping) actor rises this many. |
| `Depth.TerrainHeight.JumpUp` | 2 | int | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` |  |
| `Depth.TerrainHeight.ClimbTicks` | 15 | int | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` | CLIMB: walking into a ledge exactly JumpUp levels above for this many ticks climbs it. |
| `Depth.TerrainHeight.StepSlowTicks` | 6 | int | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` | Slows after a level change: a 1-level step, a drop of 2 or more, a climb of 2. |
| `Depth.TerrainHeight.StepSlowScale` | 0.7 | double | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` |  |
| `Depth.TerrainHeight.DropSlowTicks` | 12 | int | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` |  |
| `Depth.TerrainHeight.DropSlowScale` | 0.5 | double | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` |  |
| `Depth.TerrainHeight.ClimbSlowTicks` | 10 | int | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` |  |
| `Depth.TerrainHeight.ClimbSlowScale` | 0.6 | double | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` |  |
| `Depth.TerrainHeight.PathCostStep1` | 0.5 | double | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` | Bot fields: extra cost of an edge with a 1-level and a 2-level difference (3 or more: closed). |
| `Depth.TerrainHeight.PathCostStep2` | 3 | double | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` |  |
| `Depth.TerrainHeight.PathCostLadder` | 1 | double | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` | Extra path cost of climbing a height ladder (a Bridge cell 1+ levels above its neighbour): about 1 cell. |
| `Depth.TerrainHeight.TntLevels` | 2 | int | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` | TNT: levels a raised cell in CarveRadius loses (people on it drop). |
| `Depth.TerrainHeight.KaijuSmashFrom` | 2 | int | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` | Kaiju: a cell its body overlaps at this lift or more is smashed down to SmashTo at once. |
| `Depth.TerrainHeight.SmashTo` | 1 | int | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` |  |
| `Depth.TerrainHeight.KaijuSmashMargin` | 0.25 | double | `Assets/_Game/Sim/Config/TerrainHeightConfig.cs` | Kaiju body overlap test: its wall collider radius (PhysicsConfig.KaijuBodyRadius) plus this margin, u. |
| `Depth.CombatFeel.Enabled` | true | bool | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | The one switch (default ON in Full). False = the game before b15 combat feel, bit for bit. |
| `Depth.CombatFeel.Classes[0].MaxHp` | 115 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[0].MoveSpeed` | 3.8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Depth.CombatFeel.Classes[0].Radius` | 0.45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Depth.CombatFeel.Classes[0].AttackDamage` | 21 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[0].AttackRange` | 1.8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Depth.CombatFeel.Classes[0].AttackArcDeg` | 110 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Depth.CombatFeel.Classes[0].AttackCooldown` | 0.9 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[0].AbilityCooldown` | 10 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[0].GatherSeconds` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Depth.CombatFeel.Classes[0].CarryCapacity` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Depth.CombatFeel.Classes[0].AttackWindupSeconds` | 0.4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Depth.CombatFeel.Classes[0].RecoverySeconds` | 0.3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Depth.CombatFeel.Classes[0].Mass` | 1.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Depth.CombatFeel.Classes[0].Poise` | 30 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |
| `Depth.CombatFeel.Classes[1].MaxHp` | 100 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[1].MoveSpeed` | 4.2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Depth.CombatFeel.Classes[1].Radius` | 0.4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Depth.CombatFeel.Classes[1].AttackDamage` | 23 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[1].AttackRange` | 2.3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Depth.CombatFeel.Classes[1].AttackArcDeg` | 60 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Depth.CombatFeel.Classes[1].AttackCooldown` | 0.65 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[1].AbilityCooldown` | 9 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[1].GatherSeconds` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Depth.CombatFeel.Classes[1].CarryCapacity` | 3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Depth.CombatFeel.Classes[1].AttackWindupSeconds` | 0.27 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Depth.CombatFeel.Classes[1].RecoverySeconds` | 0.22 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Depth.CombatFeel.Classes[1].Mass` | 0.8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Depth.CombatFeel.Classes[1].Poise` | 20 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |
| `Depth.CombatFeel.Classes[2].MaxHp` | 100 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[2].MoveSpeed` | 4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Depth.CombatFeel.Classes[2].Radius` | 0.45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Depth.CombatFeel.Classes[2].AttackDamage` | 17 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[2].AttackRange` | 1.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Depth.CombatFeel.Classes[2].AttackArcDeg` | 90 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Depth.CombatFeel.Classes[2].AttackCooldown` | 0.65 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[2].AbilityCooldown` | 12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[2].GatherSeconds` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Depth.CombatFeel.Classes[2].CarryCapacity` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Depth.CombatFeel.Classes[2].AttackWindupSeconds` | 0.2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Depth.CombatFeel.Classes[2].RecoverySeconds` | 0.18 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Depth.CombatFeel.Classes[2].Mass` | 1.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Depth.CombatFeel.Classes[2].Poise` | 24 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |
| `Depth.CombatFeel.Classes[3].MaxHp` | 120 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[3].MoveSpeed` | 4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Depth.CombatFeel.Classes[3].Radius` | 0.45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Depth.CombatFeel.Classes[3].AttackDamage` | 12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[3].AttackRange` | 1.4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Depth.CombatFeel.Classes[3].AttackArcDeg` | 90 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Depth.CombatFeel.Classes[3].AttackCooldown` | 0.7 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[3].AbilityCooldown` | 12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[3].GatherSeconds` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Depth.CombatFeel.Classes[3].CarryCapacity` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Depth.CombatFeel.Classes[3].AttackWindupSeconds` | 0.23 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Depth.CombatFeel.Classes[3].RecoverySeconds` | 0.25 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Depth.CombatFeel.Classes[3].Mass` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Depth.CombatFeel.Classes[3].Poise` | 22 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |
| `Depth.CombatFeel.Classes[4].MaxHp` | 120 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[4].MoveSpeed` | 4 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Depth.CombatFeel.Classes[4].Radius` | 0.45 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Depth.CombatFeel.Classes[4].AttackDamage` | 26 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[4].AttackRange` | 1.6 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Depth.CombatFeel.Classes[4].AttackArcDeg` | 100 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Depth.CombatFeel.Classes[4].AttackCooldown` | 0.9 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[4].AbilityCooldown` | 12 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[4].GatherSeconds` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Depth.CombatFeel.Classes[4].CarryCapacity` | 1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Depth.CombatFeel.Classes[4].AttackWindupSeconds` | 0.33 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Depth.CombatFeel.Classes[4].RecoverySeconds` | 0.3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Depth.CombatFeel.Classes[4].Mass` | 1.3 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Depth.CombatFeel.Classes[4].Poise` | 26 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |
| `Depth.CombatFeel.Classes[5].MaxHp` | 110 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[5].MoveSpeed` | 4.1 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Units per second. |
| `Depth.CombatFeel.Classes[5].Radius` | 0.42 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Collision circle. |
| `Depth.CombatFeel.Classes[5].AttackDamage` | 15 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[5].AttackRange` | 1.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Melee reach from the actor centre. |
| `Depth.CombatFeel.Classes[5].AttackArcDeg` | 90 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Cleave arc, centred on facing. |
| `Depth.CombatFeel.Classes[5].AttackCooldown` | 0.8 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[5].AbilityCooldown` | 9 | double | `Assets/_Game/Sim/Config/GameConfig.cs` |  |
| `Depth.CombatFeel.Classes[5].GatherSeconds` | 1.5 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Time to pull one unit from a node. |
| `Depth.CombatFeel.Classes[5].CarryCapacity` | 2 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | Max units carried. |
| `Depth.CombatFeel.Classes[5].AttackWindupSeconds` | 0.23 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit). |
| `Depth.CombatFeel.Classes[5].RecoverySeconds` | 0.22 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed). |
| `Depth.CombatFeel.Classes[5].Mass` | 0.9 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70 / OD-3: knock distances are divided by this (Warrior heaviest, Farmer lightest). |
| `Depth.CombatFeel.Classes[5].Poise` | 20 | double | `Assets/_Game/Sim/Config/GameConfig.cs` | DG-70: a single hit of at least this much damage staggers this class (HeavyStunSeconds). |
| `Depth.CombatFeel.Strikes[0].Knock` | 0.9 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Knock of a landed hit (u travelled by a mass-1 body; Knock.Effective divides by the victim's Mass). |
| `Depth.CombatFeel.Strikes[0].ArmouredWindup` | true | bool | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Hits under this class's Poise do not stagger it (or cancel its swing) while it winds up. |
| `Depth.CombatFeel.Strikes[1].Knock` | 0.7 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Knock of a landed hit (u travelled by a mass-1 body; Knock.Effective divides by the victim's Mass). |
| `Depth.CombatFeel.Strikes[1].ArmouredWindup` | false | bool | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Hits under this class's Poise do not stagger it (or cancel its swing) while it winds up. |
| `Depth.CombatFeel.Strikes[2].Knock` | 0.3 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Knock of a landed hit (u travelled by a mass-1 body; Knock.Effective divides by the victim's Mass). |
| `Depth.CombatFeel.Strikes[2].ArmouredWindup` | false | bool | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Hits under this class's Poise do not stagger it (or cancel its swing) while it winds up. |
| `Depth.CombatFeel.Strikes[3].Knock` | 0.4 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Knock of a landed hit (u travelled by a mass-1 body; Knock.Effective divides by the victim's Mass). |
| `Depth.CombatFeel.Strikes[3].ArmouredWindup` | false | bool | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Hits under this class's Poise do not stagger it (or cancel its swing) while it winds up. |
| `Depth.CombatFeel.Strikes[4].Knock` | 1.3 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Knock of a landed hit (u travelled by a mass-1 body; Knock.Effective divides by the victim's Mass). |
| `Depth.CombatFeel.Strikes[4].ArmouredWindup` | false | bool | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Hits under this class's Poise do not stagger it (or cancel its swing) while it winds up. |
| `Depth.CombatFeel.Strikes[5].Knock` | 0.6 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Knock of a landed hit (u travelled by a mass-1 body; Knock.Effective divides by the victim's Mass). |
| `Depth.CombatFeel.Strikes[5].ArmouredWindup` | false | bool | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Hits under this class's Poise do not stagger it (or cancel its swing) while it winds up. |
| `Depth.CombatFeel.BufferSeconds` | 0.2 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | An attack press this early (real seconds) before it could be accepted fires on the first tick it can. |
| `Depth.CombatFeel.WindupMoveMultiplier` | 1 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Move speed while winding up and while recovering (no skating swings; the shipped physics value is 1.0). |
| `Depth.CombatFeel.RecoveryMoveMultiplier` | 1 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.WhiffRecoverySeconds` | 0.15 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Extra recovery (real seconds) after a swing that hit nothing: the whiff is punishable. |
| `Depth.CombatFeel.StaggerSeconds` | 0.1 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Every melee hit staggers this long (real seconds): input ignored, no attack; a non-armoured wind-up is cancelled. |
| `Depth.CombatFeel.HeavyStaggerSeconds` | 0.25 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | A hit at or over the victim's Poise staggers this long instead. |
| `Depth.CombatFeel.StaggerWindowSeconds` | 1.2 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Diminishing returns: each earlier stagger inside the window multiplies the next by StaggerDecay. |
| `Depth.CombatFeel.StaggerDecay` | 0.5 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.StaggerMaxInWindow` | 3 | int | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | After this many staggers inside one window the victim is immune to stagger for StaggerImmuneSeconds. |
| `Depth.CombatFeel.StaggerImmuneSeconds` | 0.8 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.StaggerMaxLockSeconds` | 0.4 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | No stun-lock: a run of melee staggers (each new one landing before the last ends) never lasts longer than this (real seconds); a hit that would extend it past the cap is clipped and gives StaggerImmuneSeconds at once. |
| `Depth.CombatFeel.StaggerChainCapSeconds` | 0.5 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Pass 3: no stun from any source (melee, Kaiju, blast) carries one unbroken stagger run past this; a stun that lands inside a run is shortened to fit (Knock.Push). StaggerMaxLockSeconds caps the melee share inside it. |
| `Depth.CombatFeel.BotUnjam` | true | bool | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Bots side-step when jammed (BotSteer.Unjam). False = the FSM's own unstick only. |
| `Depth.CombatFeel.BotUnjamKaijuClear` | 6 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.BotUnjamHillClear` | -1 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | No side-step within the hill disc grown by this many u (negative = no such rule). |
| `Depth.CombatFeel.BotUnjamHillZone` | 0 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Pass 4: no side-step within this many u of the hill centre while the Kaiju is hatched (the Kaiju's fight zone, KaijuHillFightRadius 13 plus its leash; 0 = no such rule). |
| `Depth.CombatFeel.BotUnjamBudget` | 3 | int | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Pass 4: the side-step budget: at most BotUnjamBudget side-steps per bot in any BotUnjamBudgetSeconds window (0 = unbounded). Past it the FSM's own slower unstick (StuckSeconds) takes over. |
| `Depth.CombatFeel.BotUnjamBudgetSeconds` | 6 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.StaggerSlowSeconds` | 0.3 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | After a stagger the victim moves at StaggerSlowMultiplier for StaggerSlowSeconds. |
| `Depth.CombatFeel.StaggerSlowMultiplier` | 1 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | OWNER FEEL-TEST: 0.6 slows after a stagger (see WindupMoveMultiplier) |
| `Depth.CombatFeel.FarmerCloseRange` | 1.2 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.FarmerCloseShare` | 0.6 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.SunderSeconds` | 3 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.SunderDamageTaken` | 0.15 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.MinerHeavyPierce` | 1.4 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.MinerHeavyMass` | 1.3 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.MinerStructureMultiplier` | 2 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.BuilderHomeRadius` | 3.5 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.BuilderHomeMultiplier` | 1.5 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.TntCooldownTicks` | 180 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.TntSelfShare` | 0.35 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Share of her own TNT's blast damage the thrower takes when she stands inside its radius. |
| `Depth.CombatFeel.TntDamage` | 46 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Thrown TNT blast damage while the rule is on (DemolitionsTntConfig.Damage is 55). |
| `Depth.CombatFeel.MendHeal` | 10 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | The Healer's mend while the rule is on (HealerConfig MendHeal 6 / MendRange 5.5). |
| `Depth.CombatFeel.MendRange` | 6 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.HealerBotStandOff` | 4 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Bots: a Healer keeps this far from the enemy she is near (she mends from there, swings only when caught). |
| `Depth.CombatFeel.LadderSpeedMultiplier` | 0.55 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Speed over a built ladder's cells (a ladder is at most LadderMaxCells = 2 long: about 0.9 s to climb at 0.55). |
| `Depth.CombatFeel.LadderAllyPass` | true | bool | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.LadderFallKnock` | 1.2 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` |  |
| `Depth.CombatFeel.BotNarrowRawStep` | false | bool | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | In narrow spots bots follow the raw next-cell step, not the blended flow. OFF by default: the shiver gate's match (seed 7) had 4 hard flags with it and 0 without (sim run 44233001957, CombatFeelTests shiver-by-part). |
| `Depth.CombatFeel.BotLeadSeconds` | 0.13 | double | `Assets/_Game/Sim/Config/CombatFeelConfig.cs` | Bots lead a moving target by at most this much of their wind-up (real seconds) when they chase and aim. |
| `Depth.BotBrain.Enabled` | true | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` | b15 integrator: ON by default in Full (TeamBrain.On keeps Classic and PhysicsOnly off) |
| `Depth.BotBrain.KiteSquad` | 2 | int | `Assets/_Game/Sim/Config/DepthConfig.cs` | Kite squad size at 10 living bots or fewer, and above that. |
| `Depth.BotBrain.KiteSquadLarge` | 3 | int | `Assets/_Game/Sim/Config/DepthConfig.cs` | Kite squad size at 10 living bots or fewer, and above that. |
| `Depth.BotBrain.KiteMinHpShare` | 0.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | A kiter needs this share of its HP when it is picked. |
| `Depth.BotBrain.KiteEngageRange` | 40 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | A kiter goes for the hostile Kaiju from this far (the plain FSM engages within 12 u). |
| `Depth.BotBrain.GuardCount` | 2 | int | `Assets/_Game/Sim/Config/DepthConfig.cs` | Hill-squad bots that never chase off the hill (the cocky rule skips them). |
| `Depth.BotBrain.StageRadius` | 15 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` | The staging ring: this far from the hill centre (outside the Kaiju's 13 u fight zone). |
| `Depth.BotBrain.GroupMin` | 3 | int | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.BotBrain.GroupShare` | 0.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.BotBrain.RallySeconds` | 12 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.BotBrain.RallyOutnumberedBy` | 2 | int | `Assets/_Game/Sim/Config/DepthConfig.cs` | A respawned bot stages only while the enemy has at least this many more bodies on the hill disc. |
| `Depth.BotBrain.TargetHealersFirst` | true | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.BotBrain.TargetStickScore` | 1.5 | double | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.BotBrain.CallToHillDeficit` | 3 | int | `Assets/_Game/Sim/Config/DepthConfig.cs` |  |
| `Depth.BotBrain.OwnKaijuEscort` | true | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` | KAIJU HOLDS NOTHING job (PR #219's rule: a team scores nothing while its own sided Kaiju stands in the hill ring). |
| `Depth.BotBrain.OwnKaijuRuleAssumed` | false | bool | `Assets/_Game/Sim/Config/DepthConfig.cs` | Test lever for a base without PR #219: behave as if the rule were on (it does not change the score). |
| `Depth.Comeback.Enabled` | true | bool | `Assets/_Game/Sim/Config/ComebackConfig.cs` | The rule's own switch (default on; Full profile only). |
| `Depth.Comeback.RallyOn` | true | bool | `Assets/_Game/Sim/Config/ComebackConfig.cs` | RALLY on / off (each lever has its own switch; the rule's Enabled turns them all off). |
| `Depth.Comeback.TrailThresholdSeconds` | 5 | double | `Assets/_Game/Sim/Config/ComebackConfig.cs` | Behind on hill seconds by more than this (s) = trailing (RALLY and FORWARD RETURN). |
| `Depth.Comeback.RallyPerMille` | 500 | int | `Assets/_Game/Sim/Config/ComebackConfig.cs` | Extra credit per owned tick while trailing, per mille of a tick (500 = the hill counts x1.5). |
| `Depth.Comeback.BonusCapSeconds` | 60 | double | `Assets/_Game/Sim/Config/ComebackConfig.cs` | Extra hill seconds a team can bank from RALLY in one match. |
| `Depth.Comeback.RespawnCurveOn` | true | bool | `Assets/_Game/Sim/Config/ComebackConfig.cs` |  |
| `Depth.Comeback.RespawnStartSeconds` | 2 | double | `Assets/_Game/Sim/Config/ComebackConfig.cs` |  |
| `Depth.Comeback.RespawnEndSeconds` | 5 | double | `Assets/_Game/Sim/Config/ComebackConfig.cs` |  |
| `Depth.Comeback.CurvePower` | 1 | int | `Assets/_Game/Sim/Config/ComebackConfig.cs` | 1 = linear, 2 = quadratic (slow start). Integers only. |
| `Depth.Comeback.WaveSeconds` | 0 | double | `Assets/_Game/Sim/Config/ComebackConfig.cs` | WAVES: a knocked-out fighter comes back at his team's next wave (every WaveSeconds of match time, both teams on the same beat), never sooner than his own wait: the dead regroup and walk back together. 0 = off. |
| `Depth.Comeback.KaijuTurncoat` | false | bool | `Assets/_Game/Sim/Config/ComebackConfig.cs` | TURNCOAT KAIJU: a sided Kaiju knocked out by the other team gets up on the side of the team that knocked it down ("it becomes what beat it"); a neutral or berserk Kaiju never turns. |
| `Depth.Comeback.FeedTrailingPerMille` | 0 | int | `Assets/_Game/Sim/Config/ComebackConfig.cs` | TRAILING FEED (arm C): while a team trails on hill seconds by more than TrailThresholdSeconds, its deliveries to the egg count this many per mille EXTRA toward allegiance (500 = x1.5). |
| `Depth.Comeback.KaijuHillPerMille` | 0 | int | `Assets/_Game/Sim/Config/ComebackConfig.cs` | KAIJU HOLDS NOTHING (arm D): while a sided Kaiju is up and standing in the hill ring (hill radius plus its own radius), its team's hill seconds count this many per mille (500 = half). |
| `Depth.Comeback.KaijuHillAnywhere` | false | bool | `Assets/_Game/Sim/Config/ComebackConfig.cs` | KAIJU HOLDS NOTHING applies wherever the sided Kaiju is while it is up (not only while it stands on the hill). |
| `Depth.Comeback.HoldFatigueOn` | true | bool | `Assets/_Game/Sim/Config/ComebackConfig.cs` | HOLD FATIGUE: after HoldGraceSeconds of unbroken ownership the owner's hill seconds count less and less, down to HoldMinPerMille / 1000 after HoldSpanSeconds more; a lost, contested or empty hill resets the streak. |
| `Depth.Comeback.HoldGraceSeconds` | 20 | double | `Assets/_Game/Sim/Config/ComebackConfig.cs` |  |
| `Depth.Comeback.HoldSpanSeconds` | 40 | double | `Assets/_Game/Sim/Config/ComebackConfig.cs` |  |
| `Depth.Comeback.HoldMinPerMille` | 250 | int | `Assets/_Game/Sim/Config/ComebackConfig.cs` |  |
| `Depth.Comeback.MajorityMargin` | 1 | int | `Assets/_Game/Sim/Config/ComebackConfig.cs` | MAJORITY: the hill is owned only with at least this many more bodies than the other team (1 = today's rule); fewer is contested and nobody scores. |
| `Depth.Comeback.ForwardReturnOn` | true | bool | `Assets/_Game/Sim/Config/ComebackConfig.cs` | FORWARD RETURN: a team trailing by more than TrailThresholdSeconds respawns at a rally point ForwardShare of the way from its pad to the hill (on open floor; the pad when the point is blocked). |
| `Depth.Comeback.ForwardShare` | 0.4 | double | `Assets/_Game/Sim/Config/ComebackConfig.cs` |  |
| `Depth.Comeback.ForwardWhenHillLost` | true | bool | `Assets/_Game/Sim/Config/ComebackConfig.cs` | FORWARD RETURN also for a team whose hill the other team holds (not only a trailing team). |
| `Depth.HutChoices.Enabled` | true | bool | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | The rule's own switch (default on; Full profile only, and only while the hut rule is on). |
| `Depth.HutChoices.VoteSeconds` | 8 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` |  |
| `Depth.HutChoices.BotThinkSeconds` | 1 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | A bot votes this many seconds after the vote opens, plus a seeded 0..BotThinkSpreadSeconds. |
| `Depth.HutChoices.BotThinkSpreadSeconds` | 3 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` |  |
| `Depth.HutChoices.BotContrarianShare` | 0.2 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | Share of bot votes that go against the team-need choice (seeded per bot and level): the picks vary and a human's vote can swing a split team. |
| `Depth.HutChoices.QuickReturnSeconds` | 1.5 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | QUICK RETURN: the team's knock-out wait is this much shorter (s), never under RespawnFloorSeconds. |
| `Depth.HutChoices.RespawnFloorSeconds` | 2 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` |  |
| `Depth.HutChoices.ToughWallScale` | 1.5 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | TOUGH WALLS: the team's Builder walls and blocks get this HP and life multiplier. |
| `Depth.HutChoices.RichVeinsMineScale` | 0.6 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | RICH VEINS: one ore takes this share of the mining channel (0.6 = 40% faster). |
| `Depth.HutChoices.ShieldWallScale` | 0.8 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | SHIELD WALL: a Warrior of the team with his shield up takes this share of every hit, from any side, before the shield's own frontal cut. |
| `Depth.HutChoices.ShortFuseScale` | 0.7 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | SHORT FUSE: the team's TNT blows after this share of the fuse (0.7 = 45 ticks to 32). |
| `Depth.HutChoices.BlastGuardScale` | 0.75 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | BLAST GUARD: the team takes this share of explosion damage (TNT and mines). |
| `Depth.HutChoices.WideMendScale` | 1.4 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | WIDE MEND: the team's Healers reach this much further with MEND (x MendRange: 5.5 u to 7.7 u). |
| `Depth.HutChoices.KaijuHunterScale` | 1.25 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | KAIJU HUNTERS: the team deals this much damage to the Kaiju. |
| `Depth.HutChoices.SwiftFeetScale` | 1.1 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` | SWIFT FEET: the team moves this much faster while off the hill (beyond the hill radius + SwiftHillMargin). |
| `Depth.HutChoices.SwiftHillMargin` | 2 | double | `Assets/_Game/Sim/Config/HutChoicesConfig.cs` |  |
