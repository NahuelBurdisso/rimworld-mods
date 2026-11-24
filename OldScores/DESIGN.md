# Old Scores — enemies (and beasts) remember the colony

Four features about memory between the colony and the ones it fought. Each has its own
toggle in the mod settings. Player-facing text in English. packageId `nahuel.oldscores`,
prefix `OldScores_`, C# namespace `OldScores`.

## Shared

- **`OldScoresTracker`** (`GameComponent`, saved): holds the records of the first three features (and the overlay toggle of the fourth)
  (`NicknameRecord`, `SparedRecord`, `BeastRecord`, each an `IExposable` saved with
  `Scribe_Collections ... LookMode.Deep`). Pawns and factions are saved with `Scribe_References`
  inside the records (never as dictionary keys); the colonist of a nickname is saved by
  `thingIDNumber`. Destroyed/discarded pawns save as null and records with a null pawn are
  dropped on load. Every 2500 ticks a cleanup pass drops records that can no longer happen
  (see each feature) and unpins their pawns from `WorldPawns.ForcefullyKeptPawns`.
- **`OldScoresMapComponent`** (`MapComponent`; saves only the Raid Echoes data of its map, see Feature 4): every 30 ticks the spared-pawn check,
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
gets a line `Known as the Unkillable Toaster (Pirates of X)` (no colon: RimWorld's `Translate` runs
`GenText.CapitalizeSentences`, which capitalises the first letter after ':', '.', '!', '?', so the
nickname keeps its lower-case "the" everywhere except at a sentence start) (Postfix `Pawn.GetInspectString`,
one line listing every nickname).

**Dread** (every 250 ticks, per map, only maps where a nicknamed colonist is spawned):
- colonist: spawned, not downed, player faction.
- raider: spawned, humanlike, `Faction` == the naming faction, `HostileTo(player)`, not downed,
  `MentalStateDef == null`, not a prisoner, within **15** cells and `GenSight.LineOfSight` from her.
- each such raider rolls **15%**: `TryStartMentalState(OldScores_Dread, forced: true,
  otherPawn: colonist)` and a dread icon (FleckDef `OldScores_Fleck_Dread`, red-orange "!" in a
  dark-outlined white disc, `FleckMaker.ThrowMetaIcon`, twice) drifts up from him.
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
4500: nicknames 3000-~3380, spared 3400-~3670 (two real raids), beasts 3750-3757, echoes 3900-~6100 (three real raids; worst case ~7400, so the run uses `--ticks 8000`); each first clears all hostile pawns and buildings and waits 5 ticks (Gesta harnesses run raids up to ~2900). Every test spawns its own colonists
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

## Review fixes (after the in-game runs)

1. **Drop-pod raids** (Spared.cs `Notify_RaidSpawned`/`JoinRaid`): `PostProcessSpawnedPawns` runs after
   `Arrive`; on pod raids the raiders are still in incoming pods (`pawns[0]` not spawned). He then comes
   the same way, `DropPodUtility.DropThingsNear(parms.spawnCenter, ...)` (MakeDropPodAt removes him from
   the world pawns); with no valid spawnCenter he sits that raid out without a roll.
2. **Manhunter packs** (Beasts.cs): scaria animals are never marked; at most one Marked beast per map per
   day (a later attacker replaces it only if its tallied damage to colonists is higher; the tally is not
   saved and resets every 2500 ticks); a beast returns at most `BeastMaxReturns` = 2 times, after that
   leaving alive drops the record and unpins it; the queued incident is no longer `forced`, so the
   storyteller guards (difficulty, scenario, game conditions, game ending) apply; it retries for a day
   and the cleanup pass re-queues it a day later if it is overdue.
3. **Hidden / non-humanlike factions** never give nicknames (ancients, entities, shamblers).
4. **Spared records expire** (Away stage): his faction defeated, not hostile for 30 days in a row
   (`notHostileSinceTick`, saved), or 2 in-game years since the capture -> record dropped, unpinned.
5. **Friendly raids** (military aid, reinforcements) never bring him: `parms.faction.HostileTo(player)`.
6. **Load**: a spared record dropped on load because its captor is gone unpins the pawn first.

## Feature 4 — Raid Echoes (`echoes`)

**Pitch:** the map remembers how past raids came at you. Toggle the overlay and you see faint lines
of the roads each faction walked, where they broke in and where they fell; when they come by the same
road again, the colony notices.

**What counts as a raid.** Postfix on the public, non-virtual
`IncidentWorker_Raid.TryGenerateRaidInfo(IncidentParms parms, out List<Pawn> pawns, bool debugTest)`
(`RimWorld/IncidentWorker_Raid.cs:66`): the single path every raid worker takes (RaidEnemy, quest raids,
sieges, mechanoid raids, shambler assaults and psychic-ritual sieges — `IncidentWorker_ShamblerAssault`
overrides `PostProcessSpawnedPawns` without calling base, so that hook would miss it). Recorded when
`__result` is true, `debugTest` is false, `parms.target` is a `Map` with `IsPlayerHome`, `parms.faction`
is not null and `HostileTo(Faction.OfPlayer)`, and `pawns` is not empty. Never recorded: friendly raids
(military aid — the faction is not hostile), visitors, traders, manhunter packs, infestations, mech
clusters (none of them go through `IncidentWorker_Raid`), hostiles spawned any other way, raids on
non-home maps (quest sites, caravan ambushes), and anything while the feature is off.
Arrival kind: `parms.raidArrivalMode.walkIn` → walk-in (edge walk-in, groups, sieges), else **pods**
(edge/center drops: the pawns are still in drop pods when the hook runs; they are tracked once they
land).

**Recording (active raid, per map, in `OldScoresMapComponent`).** An `ActiveRaid` keeps the faction (by
`loadID` + name), start tick, arrival cell (`parms.spawnCenter` if valid), walk-in flag, one
`RaidTrack` per raider (pawn reference, sampled points, breach cell, recording and seen flags,
farthest reach and stall count) and
the death cells. Every **60** ticks (`MapComponentTick`, only while a raid is active on that map) each
track that is still recording samples its pawn:
- pawn dead/destroyed → stops recording, reference cleared.
- not spawned: seen before (left the map, carried, kidnapped) → stops, reference cleared; never seen
  (still in a drop pod) → waits.
- on another map, downed, no longer hostile (captured, recruited), in a mental state (panic flee,
  dread...) or with a leaving duty (`ExitMapBest`, `ExitMapRandom`, `ExitMapBestAndDefendSelf`,
  `Steal`, `Kidnap`) → stops recording (the echo is the approach, not the retreat).
- **Loitering:** a raider who has not got farther from the first point of his road (by more than half a
  cell) for **8** samples in a row is milling around (siege camp, staging point, firing line outside the
  base): no points are added until he gets farther than his farthest reach again (an assault after a
  siege resumes the road). Without this, a siege camp would scribble a knot of lines.
- else the cell is appended if it is ≥ **3** cells from the last point. A track holds at most **64**
  points; when full, every second point is dropped (the first and last are kept), so a path never
  grows unbounded.
- **Breach:** the first sampled cell inside the player's Home area (`map.areaManager.Home`) is the
  pawn's breach cell; it is appended and the track stops recording (inside the base it is a fight,
  not a road).
- Deaths: the existing `Pawn.Kill` prefix/postfix passes the death cell and map; a raider of an active
  raid on that map adds it (cap **30** per raid).

**When a raid ends.** At each sample, a raid is still on while any of its pawns is alive, spawned on
the map, not downed and hostile, or is a never-seen pawn and the raid is younger than **5,000** ticks
(pods). Else (or when the raid is **3 days** old: sieges, stragglers) it is finalized:
- lanes: tracks with ≥ 2 points, simplified with Ramer–Douglas–Peucker (ε = **1** cell), longest
  first; a track whose start *and* end are both within **15** cells of a kept lane's is the same road
  and is skipped; at most **3** lanes per raid (one per arrival group in practice).
- breaches: the tracks' breach cells, merged within **6** cells, at most **6**.
- an echo with no lane and no death is discarded. Otherwise `RaidEcho` {factionId, factionName,
  endTick, arrival, walkIn, raiders, lanes (flat `List<IntVec3>` + `List<int>` lane starts), breaches,
  deaths} is added; at most **16** echoes per map (the oldest goes).
- Old echoes are forgotten after **2 years** (cleanup every 2,500 ticks); their drawing fades from
  alpha **0.9** (new) to **0.3** at **1 year**.

**Story payoff ("the old road").** In the same raid postfix, before the new raid is registered: a
walk-in raid with a valid `spawnCenter` whose faction already has a walk-in echo on this map with its
arrival or any lane start within **20** cells → `Messages.Message("They come by the old road again:
{faction}, from the {north/east/south/west}.", first raider, MessageTypeDefOf.NegativeEvent)`. Pod
raids never get it. At most one per raid.

**Overlay toggle.** Postfix `PlaySettings.DoPlaySettingsGlobalControls(WidgetRow row, bool worldView)`
(`RimWorld/PlaySettings.cs:108`): on the map view only, and only while the feature is on, one more
`row.ToggleableIcon(ref tracker.showEchoes, icon, tooltip, SoundDefOf.Mouseover_ButtonToggle)`. The
state is saved per game in `OldScoresTracker` (`showEchoes`, default **off**), like vanilla's
overlay toggles in `PlaySettings`. Icon: `Textures/UI/OldScores/EchoesToggle.png` (64×64).

**Drawing** (only the current map, only while the feature and the toggle are on and the map has
echoes; nothing is computed otherwise):
- A per-map cache (not saved) is rebuilt when echoes change and every 2,500 ticks: world positions of
  every lane point, chevrons every ~12 cells and at each lane's end (direction of travel), the colour of
  each echo (a 5-colour pale palette, assigned to factions in order of first appearance on that map),
  and the labels.
- `MapComponentDraw` (called from `MapInterface.MapInterfaceUpdate` for the current map only): each lane
  segment whose bounding box touches `CameraDriver.CurrentViewRect` (+3) and whose ends are not both
  fogged is drawn with `GenDraw.DrawLineBetween(A, B, mat, width)` (0.4 cells) over a darker under-stroke
  (0.75), both widened by `CameraDriver.RootSize / 18` when zoomed out so they never shrink to a pixel, at
  `AltitudeLayer.MetaOverlays`; chevrons are two short lines; a death is a small X; a breach is a
  12-sided ring of radius 1.4. Materials: `MaterialPool.MatFrom(GenDraw.LineTexPath,
  ShaderDatabase.Transparent, colour)`, alpha quantised to 10 steps and cached (at most 5×11+11).
  Hard cap **800** line draws per frame.
- Labels (`MapComponentOnGUI`, Repaint only, same conditions, in view and unfogged):
  `GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(cell), text, colour)`. Per faction on the map,
  breach clusters (merged within 6 cells across all its echoes): the biggest one (ties: most recent)
  gets "Pirates of X - 3 raids, broke in here"; the others "Pirates of X - broke in here". A faction that
  never broke in gets "Pirates of X - 3 raids" just above the end of the first lane of its newest
  echo (where it stopped; above, so it does not sit on the corpses and their labels). The faction's current name, or the saved name if the faction is gone.

**Save/load.** `OldScoresMapComponent.ExposeData` (the component is already in every map's
`components` list, which `Map` saves with `LookMode.Deep`): `echoes` and `activeRaids`
(`Scribe_Collections ... LookMode.Deep`). Echoes hold only values (faction by `loadID`, never a
reference), so a removed faction never breaks a load. Raid tracks reference their pawn with
`Scribe_References` (destroyed pawns save as null; a null pawn is a finished track). Null lists are
replaced on load; malformed echoes (no lane and no death, lane starts out of range) are dropped. An old
save without the data loads with nothing recorded.

**Map removal.** All of it lives in the map's own component, so it goes with the map. No world state.

**Cost.** Per tick: one modulo and one list-count check per map. Every 60 ticks with a raid on: one pass
over its raiders (a few field reads each). Per frame with the overlay off: two bool checks. With it on:
at most 800 `DrawMesh` calls (typically well under 200) and a handful of labels, no allocation.

**Compatibility.** Only positions, factions and the Home area are read; nothing about combat, so Combat
Extended and race mods change nothing (non-humanlike raiders such as mechanoids are tracked the same).
Other mods' raid workers that derive from `IncidentWorker_Raid` are covered by the same hook.

**Tests (OldScoresTest, `EchoHarness`, from harness tick 3900, after the other three):** clears
hostiles, old echoes and active raids; a real walk-in raid (`IncidentDefOf.RaidEnemy`, ImmediateAttack,
EdgeWalkIn, fixed spawnCenter at a map edge) → an active raid with one track per raider; after ~600 ticks
of walking the tracks have points that start near the edge and move inward; Home area painted under the
lead raider → its breach is recorded; all raiders killed by a colonist → within 120 ticks the raid is
finalized: an echo of that faction with ≥ 1 lane of ≥ 2 points in bounds, a breach, as many death
cells as kills, walkIn; a second raid of the same faction from the same spawnCenter → the old-road message
(checked in `Messages`' live list) and, after it ends, the label "… - 2 raids, broke in here"; a drop-pod
raid (EdgeDrop) → tracked once landed, echo with walkIn false, no old-road message; negatives: hostiles
spawned without a raid incident, a visitor group, a trader caravan and a friendly raid create no active
raid; feature off → a raid is not tracked; overlay off → zero lines and labels drawn (draw counters over
several frames), overlay on with the camera on the echo → lines and labels drawn; the toggle postfix is
on `DoPlaySettingsGlobalControls`; save/load: the map's echoes written with Scribe to a temp file and
read back are equal (lanes, breaches, deaths, faction, flags); an echo older than 2 years is forgotten by
the cleanup.

**Showcase** (`EchoShowcase`, same `showcase.flag`, after the other showcase): two real walk-in raids of one
faction from the same edge at ultrafast speed (the quicktest colony's Home area is tiny, so after 900 ticks it
is painted under the lead raider, as if he had reached the walls), then the overlay is captured:
`4-old-road-message`, `5-echoes-overview`, `6-echoes-normal-zoom`, `7-echoes-overlay-off`.

Visual fixes after looking at the captures: lines 0.24 → 0.4 cells with a stronger under-stroke and widened
when zoomed out (at full zoom-out they were a pixel wide); labels lifted towards white (pale red on the grey
label background was hard to read); the no-breach label moved above the road's end (it sat on the corpses);
the toggle icon redrawn in the vanilla style (grey glyph, dark outline).

## Feature 5 — The Beast Remembers (`beastMemory`)

**Pitch:** your dog does not forget who hurt it. When that man is near again — as a prisoner, a recruit,
a visitor or a raider back for more — it growls and glares at him. And the raider your warg dragged down,
now in your cells, goes pale every time that beast walks past.

**Who remembers whom (a).** Postfix on `Pawn.PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)`
(already patched): the victim is an animal (`RaceProps.Animal`) of the player faction, alive after the
hit; `totalDamageDealt > 0`; `dinfo.Def.ExternalViolenceFor(victim)`; `dinfo.Instigator` is a living humanlike
pawn whose faction is not the player's (raiders, visitors, prisoners breaking out, wild men), not a mutant
(Anomaly shamblers). Never:
colonists and colony slaves (friendly fire, slaughter, training accidents), animals and mechanoids as
attackers (an animal remembers a face and a smell; the reverse direction is Feature 3), non-animal victims
(mechs, colonists), animals of other factions, dead animals. Bonded or not makes no difference: every
tame animal of the colony remembers, from a husky to a hen.
- `GrudgeRecord` {animalId, animalName, humanId, humanName, createdTick, lastGrowlTick, colonistSinceTick}
  by `thingIDNumber` (values only, no references: pawns that are discarded later never break a save).
  Hurt again by the same man → `createdTick` refreshed. At most **3** per animal (the oldest goes), **300**
  in all (the smallest `createdTick` goes).

**The growl.** Every **60** ticks per map (`OldScoresMapComponent`), only while any grudge exists: each
spawned animal of the player faction that holds grudges, not downed, awake, not in a mental state; each
remembered man spawned on the same map, alive, within **6** cells and in the same indoor room (not
`PsychologicallyOutdoors`: outdoors is one big room) or in line of sight (`GenSight.LineOfSight`); per pair at most once per **2,500** ticks → `animal.caller?.DoCall(forceAggressive:
true)` (the angry lifestage sound; at most one growl per animal per pass, the rest wait for the next pass) and `MoteMaker.MakeInteractionBubble(animal, man, ThingDefOf.Mote_Speech,
"Things/Mote/SpeechSymbols/Insult", red)` — a red bubble on the animal pointing at him. No message, no
letter: the player notices it on the map. The animal's inspect pane says "Remembers who hurt it: Tom, Ana".
Raiders in battle are growled at too (cooldown applies) — it is the same man.

**Fear of the beast (b).** Postfix on private `Pawn_HealthTracker.MakeDowned(DamageInfo? dinfo, Hediff
hediff)` (`Verse/Pawn_HealthTracker.cs:837`, `___pawn`): the downed pawn is humanlike, not of the player
faction and hostile to it, and `dinfo?.Instigator` is a living animal of the player faction → `FearRecord`
{prisonerId, prisonerName, animalId, animalName, lastFearDay}. A later downing by another animal replaces
it (one per man); at most **100** (the oldest goes). Downed by bleeding later (no `dinfo`) does not count.
A prefix records whether the pawn was standing: the postfix counts only a pawn that was up before and is
down after (another mod's skipping prefix, or vanilla's "already downed" error path, must not record).
In the same 60-tick pass: the man is spawned, `IsPrisonerOfColony`, alive, awake, has a mood; the animal is
spawned on that map, not downed; within **6** cells and same room or line of sight; not yet today
(`TicksGame / 60000`) → memory `OldScores_FearOfBeast` (−4 mood, 1 day, `stackLimit` 1, `showBubble`,
icon the mod's dread icon; label "Afraid of {0}", `otherPawn` = the animal) and
`guest.resistance = Max(0, resistance − 0.5)` (direct: lowering mood would *slow* recruitment, because of
the mood curve in `InteractionWorker_RecruitAttempt`). Once per in-game day per prisoner.

**Forgetting / cleanup.**
- `Pawn.Kill` (existing patch): the dead animal's grudges and fear records go; a dead man's grudges and
  fear record go.
- Every 2,500 ticks (tracker cleanup), over the pawns spawned on any map: a remembered man who is a free
  colonist (`IsFreeColonist`; a slave is not "one of us") for **30** days in a row (`colonistSinceTick`, saved) is forgiven; an animal found spawned
  with another faction (sold, released to the wild) loses its grudges and fear records; a man found as a
  colonist or no longer a prisoner and not hostile loses his fear record. Pawns not found (caravans, world
  pawns) are left alone.
- Feature off (`OldScoresSettings.beastMemory`): nothing is recorded, nothing fires and the inspect line is
  hidden; records are kept.

**Save/load.** In `OldScoresTracker`: `grudges` and `fears` (`Scribe_Collections`, `LookMode.Deep`, values
only). Null lists replaced on load.

**Cost.** Per tick: a modulo and a count check per map. Every 60 ticks with records: a per-pass id→pawn
lookup of the map's spawned humanlikes and animals (a few hundred dictionary inserts, reused dictionary),
then each grudge and fear record is one lookup. No allocation in the per-tick path beyond the pass's small
dictionary (only when records exist).

**Compatibility.** Reads only factions, positions, rooms, sight; no combat code. Combat Extended damage goes
through `PostApplyDamage` and `MakeDowned` the same way. Animals from mods count if `RaceProps.Animal`.
Every hook is wrapped in try/catch with `Log.ErrorOnce` (the damage postfix, the downed postfix, the 60-tick
pass); a pair's growl tick / a record's fear day is set before the side effects, so a failing modded pawn
never retries every pass. The growl texture is loaded at startup on the main thread.

**Tests (`BeastMemoryHarness`, from tick 6400, run with `--ticks 9000`):** a husky (player animal) hurt by a
raider's blow (real `TakeDamage`, instigator the raider) → grudge recorded; hurt by a colonist → none; by
a wild animal → none; a non-player animal hurt by a raider → none; a fourth attacker → still 3 (oldest
gone); feature off → none. The raider captured (prisoner) and teleported 4 cells from the husky in the open
→ within 120 ticks the husky's red bubble targets him (a `MoteBubble` attached to the husky with the
raider as the arrow target and red icon colour) and the pair's growl tick is set; 10 cells away → nothing;
behind a wall → nothing; cooldown → no second growl within 2,500 ticks. Downed by the husky (real
`TakeDamage` with the husky as instigator until downed) → fear record; captured; husky 3 cells away in the
same room → the fear memory with the husky as `otherPawn` and resistance −0.5; a second pass the same day →
nothing more; next day → again. Kill the husky → its records are gone. Scribe roundtrip of both lists.
Inspect line on the husky. (It starts once the Echoes harness has finished, at the latest tick 7600.)

Added after review (2026-10-16): a colony slave's blow → none (Ideology); a remembered man dies → the
grudges against him go; inspect line hidden with the feature off; a downed raider not yet our prisoner with
the husky 3 cells away → no fear; two remembered men next to one animal → one growl per pass. Review
findings and what was done with each: `docs/forge/reviews/oldscores-beast-memory.md`.

**Showcase** (`BeastMemoryShowcase`, after the Raid Echoes showcase): `logs/showcase/oldscores/8-growl-bubble.png`
(the husky Grimm, red bubble with the insult glyph pointing at the prisoner who hurt it; readable at normal
zoom) and `9-fear-of-the-beast.png` (the downed prisoner's thought bubble with the dread icon, husky 3 cells away).

**Compat probada con** (gametest `--ticks 9000`, 2026-10-16): Vanilla Expanded Framework + Alpha Animals +
Grudges! + Vanilla Social Interactions Expanded (VSIE also patches `MakeDowned`; Grudges! is the colonist-side
grudge system) → `logs/run-20261016-223240.log`: The Beast Remembers 35/35 PASS, 0 errors. One FAIL in the
Beasts harness, "...and unpinned": Grudges! itself keeps the wolf in `ForcefullyKeptPawns` (its DLL uses
`get_ForcefullyKeptPawns`); without Grudges! (VEF + Alpha Animals + VSIE, `logs/run-20261016-225007.log`) every
PASS of the 5 features holds (173), and the only error is vanilla's `ScatterRoadDebris` map-gen message. Old
Scores' own record is cleared in both runs ("record cleared" PASS); a pawn kept by another mod is that mod's
business. A first compat run (`run-20261016-221146.log`) had a one-off Spared "he came armed (nothing)" (vanilla
weapon roll; not seen again in 3 runs, now with a diagnostic message) and stalled after the harnesses finished;
the harness watchdog now runs until the probe quits and names force-pausing windows.

### Raid Echoes — review fixes (docs/forge/reviews/oldscores-raid-echoes.md)

1. A raider who is not spawned but still held on the map (`MapHeld == map`: mid-jump in a `PawnFlyer`, or
   carried while downed) keeps his track: it counts as still fighting if he is not downed and hostile, gets
   no point while held, and his death is still recorded (where he was held). Before, the track was stopped,
   the raid could end early and the death was lost.
2. Drawing goes newest echo first and checks the budget before every line/chevron/cross/ring, so the cap is
   hard and the oldest, faintest echoes are the ones left out.
3. `EchoTick` and the toggle postfix are wrapped in try/catch (`Log.ErrorOnce`); the under-stroke is drawn
   0.01 below the line. The old-road test is a public static predicate (`MatchesOldRoad`).
4. Harness: friendly raid asserted whenever it fires (visible `SKIP` otherwise); the real
   `DoPlaySettingsGlobalControls` is called from `GameComponentOnGUI` with fresh `WidgetRow`s (one more icon
   on the map view with the feature on, none in the world view); the whole map component is Scribe'd mid-raid
   (live pawn references resolved) and an empty component node loads with empty lists; old-road radius,
   other faction and old pod echo cases; a downed raider carried off keeps his track and his death; the test
   echoes are cleared when the harness finishes.
