# Old Scores — enemies (and beasts) remember the colony

Three features about memory between the colony and the ones it fought. Each has its own
toggle in the mod settings. Player-facing text in English. packageId `nahuel.oldscores`,
prefix `OldScores_`, C# namespace `OldScores`.

## Shared

- **`OldScoresTracker`** (`GameComponent`, saved): holds the records of all three features
  (`NicknameRecord`, `SparedRecord`, `BeastRecord`, each an `IExposable` saved with
  `Scribe_Collections ... LookMode.Deep`). Pawns and factions are saved with `Scribe_References`
  inside the records (never as dictionary keys); the colonist of a nickname is saved by
  `thingIDNumber`. Destroyed/discarded pawns save as null and records with a null pawn are
  dropped on load. Every 2500 ticks a cleanup pass drops records that can no longer happen
  (see each feature) and unpins their pawns from `WorldPawns.ForcefullyKeptPawns`.
- **`OldScoresMapComponent`** (`MapComponent`, not saved): every 30 ticks the spared-pawn check,
  every 250 ticks the dread check, `MapComponentOnGUI` draws the beast labels.
- **Tuning** (`OldScoresTuning`, static fields, not settings): the numbers below. The test
  harness overrides the chances to 1 / 0 to make rolls deterministic and restores them.
- **Keeping a pawn alive off-map:** `Find.WorldPawns.ForcefullyKeptPawns.Add(p)` (world pawn GC
  skips them) plus a Postfix on private static
  `PawnGenerator.IsValidCandidateToRedress(Pawn, PawnGenerationRequest)` that returns false for any
  pawn this mod is holding (spared pawn or escaped beast), so a new raid or trader can never
  reuse (redress) him. Spawning a world pawn removes him from the world (and from the pinned set);
  the mod pins him again whenever he leaves a map while still remembered.
- **Leaving a map** means `Pawn.ExitMap` with the pawn ending up unspawned, not teleporting
  (gravship/carried transfers) and not in a player caravan; those exits are ignored.

Harmony (all Postfix unless said): `Pawn.Kill(DamageInfo?, Hediff)` Prefix+Postfix,
`Pawn.PostApplyDamage(DamageInfo, float)`, `Pawn.ExitMap(bool, Rot4)`,
`Pawn_GuestTracker.CapturedBy(Faction, Pawn)`, `GenGuest.PrisonerRelease(Pawn)`,
`PawnGenerator.IsValidCandidateToRedress` (private), `IncidentWorker_Raid.PostProcessSpawnedPawns`
(protected virtual; `IncidentWorker_RaidEnemy` does not override it), `Pawn.GetInspectString()`.

## Feature 1 — Enemy Nickname (`nicknames`)

**Pitch:** kill enough of them and they give you a name — and start running when they see you.

**Counting** (Pawn.Kill Prefix, everything read while the victim is still alive):
- killer = `dinfo.Instigator as Pawn`; must be a free non-slave colonist (`IsFreeNonSlaveColonist`),
  not a quest lodger, not dead.
- victim: humanlike, `Faction != null`, not the player faction, `HostileTo(Faction.OfPlayer)`,
  not downed. Finishing off a downed raider never counts. Animals, mechs and insects never count
  (a nickname is something people give).
- the count is per (colonist, faction). At **5** kills the faction names her. Killing the
  faction's real leader (`victim.Faction.leader == victim`, read in the prefix) names her at once,
  whatever the count.
- One nickname per (colonist, faction), forever. Different factions can each name her.

**Naming:** `GrammarResolver.Resolve("r_name", request)` with RulePackDef
`OldScores_Namer_EnemyNickname` + `GrammarUtility.RulesForPawn("PAWN", colonist, constants)` +
constants `weapon` (`melee` / `ranged` / `none`), `trait` (the first of a short list of traits
she has: Tough, Brawler, Pyromaniac, Nimble, Wimp, Bloodlust, Psychopath, Kind, Cannibal,
Masochist, Gourmand, Ascetic — else `none`) and a `WEAPON_label` rule when armed. Result is lower-case
"the ..." with capitalised words, e.g. "the Unkillable Toaster", "the Revolver Ghost".
If the resolver returns nothing usable, fallback "the Nightmare".

**On naming:** letter (`LetterDefOf.NeutralEvent`, `relatedFaction` = that faction, look target the
colonist): "Pirates of X have started calling Ana "the Unkillable Toaster". Their raiders may break
and run when they see her." (the leader variant says she killed their leader). Her inspect pane
gets a line `Known as: the Unkillable Toaster (Pirates of X)` (Postfix `Pawn.GetInspectString`,
one line listing every nickname).

**Dread** (every 250 ticks, per map, only maps where a nicknamed colonist is spawned):
- colonist: spawned, not downed, player faction.
- raider: spawned, humanlike, `Faction` == the naming faction, `HostileTo(player)`, not downed,
  `MentalStateDef == null`, not a prisoner, within **15** cells and `GenSight.LineOfSight` from her.
- each such raider rolls **15%**: `TryStartMentalState(OldScores_Dread, forced: true,
  otherPawn: colonist)` and a pale-blue "!" text mote over him.
- `OldScores_Dread`: own MentalStateDef, `stateClass Verse.AI.MentalState_Terror`, 300-600 ticks,
  no letter, no message. XML patch prepends a `ThinkNode_ConditionalMentalState`
  (`state OldScores_Dread`) with `JobGiver_FleeTerror` (+ jogging wander fallback) to
  `ThinkTreeDef MentalStateNonCritical`, like Core wires Odyssey's `Terror`. `JobGiver_FleeTerror`
  flees from `MentalState.causedByPawn`, i.e. the nicknamed colonist, 50-75 cells.
- The vanilla `Terror` def is Odyssey-only, which is why the mod defines its own.

**Cleanup:** records whose faction no longer exists are dropped.

**Tests:** 4 kills → no nickname; 5th → nickname, letter with relatedFaction, inspect line;
5 kills of downed raiders → nothing; killing the leader → nickname at once; feature off → 5 kills
give nothing; dread (chance forced to 1): a raider 8 cells away in sight gets OldScores_Dread caused
by her, and within 120 ticks his job is `Flee`; a raider behind a wall, one 20 cells away, a downed
one and (if one exists) a raider of a different hostile faction do not.

## Feature 2 — The One You Spared (`spared`)

**Pitch:** let an enemy go, and one day he may come back with a raid — and lower his weapon
when he sees the one who carried him in.

- **Captured:** Postfix `Pawn_GuestTracker.CapturedBy(Faction by, Pawn byPawn)`: `by` is the player,
  `byPawn` is a humanlike player pawn, the captive is humanlike, his `Faction` is not the player's
  and `HostileTo(player)`, and he is now a prisoner → record (pawn, rescuer = byPawn, stage
  Captured). Captured again → rescuer updated.
- **Released:** Postfix `GenGuest.PrisonerRelease(p)`: record in stage Captured → stage Released.
- **Leaves the map:** Postfix `Pawn.ExitMap`: if the pawn is now an unspawned live world pawn and
  his record is Released (or Returned, see below) → stage Away, `leftTick` = now, pinned. A
  Captured record (escaped, never released) is dropped: he owes nothing.
- **Returns:** Postfix `IncidentWorker_Raid.PostProcessSpawnedPawns(parms, pawns)` (runs after
  the raiders are spawned and before the lord is made, so adding him to `pawns` puts him in the
  raid lord): for the first Away record whose pawn's faction is `parms.faction`, left at least
  **3 days** (180,000 ticks) ago, alive, unspawned world pawn, whose rescuer is spawned on the
  raided map (player home): roll **35%**. On success spawn him at
  `CellFinder.RandomClosewalkCellNear(parms.spawnCenter, map, 6)` (spawnCenter invalid → next to
  the first raider), give him a weapon if he has none (`PawnWeaponGenerator.TryGenerateWeaponFor`),
  add him to `pawns` and to `parms.pawnGroups` (group of the first raider, 0 if none) if that is
  not null; stage Returned. At most one per raid. No extra letter: the raid letter lists him.
- **Recognition** (every 30 ticks, per map): Returned pawn spawned there, not downed, not in an
  aggressive mental state (berserk etc.); rescuer spawned on the same map and alive; distance
  ≤ **12** cells and line of sight → text mote with one of several Keyed lines ("You carried me out
  of the snow."), `equipment.DropAllEquipment` (not forbidden), any other mental state ends,
  `InteractionWorker_RecruitAttempt.DoRecruit(rescuer, pawn, ..., sendLetter: false)` and our own
  positive letter telling the story; record removed, unpinned.
- He leaves the map again without meeting her (raid fled) → back to Away, `leftTick` = now.
- **Cleanup:** dead, recruited/enslaved by the player, or rescuer dead/gone → record removed,
  unpinned.
- Skipped by design: the "he leaves instead" branch; caravans; non-home maps.

**Tests:** capture by a colonist → record Captured; release → Released; ExitMap → Away, pinned,
redress check (reflection on the private method) false; raid of his faction with chance 1 before
3 days → he is not in it; after back-dating `leftTick` → he spawns, is in the raid lord with his
faction; rescuer far away (> 12) → still hostile after 60 ticks; rescuer teleported next to him →
joins the colony, has no weapon, record gone, unpinned; a captured prisoner who exits without
release → no record, not pinned; feature off → no record on capture.

## Feature 3 — The Beast That Came Back (`beasts`)

**Pitch:** the wolf that mauled your doctor got away. It remembers. It will come back, scarred.

- **Marked:** Postfix `Pawn.PostApplyDamage(dinfo, totalDamageDealt)` on the victim: victim is a
  colonist (`IsColonist`), `dinfo.Instigator` is an animal (`RaceProps.Animal`) with `Faction ==
  null` (wild or manhunter), alive; and `totalDamageDealt >= 10` or the victim is now downed or dead.
  → record (beast, victims' names and ids, first tick), stage Marked. More victims are appended.
- **Escapes:** Postfix `Pawn.ExitMap`: Marked (or Returned) beast, alive, now a world pawn →
  if it has no name, `Name = new NameSingle(generated)` from RulePackDef `OldScores_Namer_Beast`
  (rules for the beast "ANIMAL" and first victim "VICTIM": "Old Scar", "Kerr's Bane",
  "Three-Toes"...); every current injury gets `HediffComp_GetsPermanent.IsPermanent = true`
  (scars); pinned; `Find.Storyteller.incidentQueue.Add(OldScores_BeastReturns, fireTick, parms)`
  with fireTick = now + random 1-2 quadrums (900,000-1,800,000 ticks), parms target = the map it
  left (if still a player home, else any player home map), `forced = true` (the player earned
  this one), retry window 1 day. Stage Away, fireTick stored.
- **Returns:** `OldScores_BeastReturns` (`IncidentDef`, `IncidentWorker_BeastReturns`, baseChance 0
  so the storyteller never rolls it, category ThreatSmall, target Map_PlayerHome).
  `CanFireNowSub`: player home map, a due Away record (fireTick ≤ now) whose beast is an unspawned
  live world pawn, and an entry cell exists. `TryExecuteWorker`: earliest due record →
  `RCellFinder.TryFindRandomPawnEntryCell`, spawn, add hediff `OldScores_Veteran` (MeleeDamageFactor
  ×1.35, IncomingDamageFactor ×0.75, shown red) if it lacks it, `ManhunterPermanent` (forced),
  `mindState.exitMapAfterTick` = now + 1-2 days, threat letter naming whom it hurt. Stage Returned.
- **Red label:** `MapComponentOnGUI` (Repaint only): under every spawned, unfogged Returned beast,
  red text "Hurt Kerr, Ana" (`GenMapUI.LabelDrawPosFor(beast, -0.6)` + 14 px,
  `GenMapUI.DrawThingLabel`). Its inspect pane says the same.
- **Killed** (Pawn.Kill Postfix, position read in the Prefix): Returned beast → trophy
  `OldScores_BeastTrophy` (item; `CompBeastTrophy` saves beast name, kind, victims, first-blood
  tick, killer name and kill tick; label "trophy (Old Scar)", inspect "Old Scar, a timber wolf.
  Mauled Kerr, Ana on 5th of Aprimay 5501. Came back scarred and was killed by Ana on ...")
  placed near the death cell; killer (instigator pawn with mood) gets memory
  `OldScores_SettledOldScore` (+6, 10 days); record removed, unpinned. A Marked beast killed before
  it escapes just loses its record.
- It leaves again alive (timed out) → back to Away and re-queued.
- **Cleanup:** dead/destroyed beast, or beast now in a faction (tamed) → removed, unpinned. An Away
  beast more than 2 days overdue with nothing of ours in the incident queue is re-queued 1 day out.

**Tests:** a wild timber wolf deals 12 bite damage to a colonist → marked with her name; a second
wolf dealing 3 → not marked; a pirate-faction wolf dealing 12 → not marked; the marked wolf (with
an injury) exits → named, injuries permanent, pinned, incident queued 1-2 quadrums out; the unmarked
wolf exits → unnamed, not pinned; incident CanFireNow false while not due; forced due →
TryExecute spawns it with the veteran hediff, ManhunterPermanent, exitMapAfterTick set, letter;
label text lists the victim; killed by a colonist → trophy with the history (name + victim) on the
map, killer has the memory, record gone, unpinned; feature off → no mark.

## Harness (OldScoresTest)

`GameComponent`s armed by `GenCommandLine.TryGetCommandLineArg("rwtools-ticks", out _)`; they start
at harness tick 2700 (the Gesta harness uses 60-2200 in the same game) and must finish before
4500: nicknames 2700-3070, spared 3100-3260 (two real raids), beasts 3700-3701. Every test spawns its own colonists
and cleans up the hostiles it spawned. `Kit.Check` PASS/FAIL lines, `Kit.Summary("OldScores/...")`.

Harness robustness (after the first in-game run):
- Enemy faction: `TestUtil.HostileFaction` — a hostile humanlike faction that has Combat pawn groups,
  preferring one nobody has a nickname from yet (the Gesta harness kills faction leaders earlier in the
  same game), pirates first. `Faction.OfPirates` can be null on a quicktest world.
- Test strips: `TestUtil.PrepareStrip` near a colonist (bounded search, radius 40) then clears the strip
  (unfog, remove plants/rock/chunks/items, soil over water or impassable ground; pawns are moved, player
  things are never in a chosen strip), so it works on any map.
- A raid letter (ThreatBig) pauses the game under the default auto-pause preference (MajorThreat):
  `LetterStack.ReceiveLetter` -> `TickManager.Pause()`. While paused no GameComponentTick runs, so the
  harness waited forever. The raid helper restores the speed right after `TryExecute`, and a
  `HarnessWatchdog` GameComponent (GameComponentUpdate runs while paused) resumes the game while any
  harness is unfinished. Every wait has a timeout that logs a FAIL.
