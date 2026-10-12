# Omens — design

Package `nahuel.omens`, prefix `Omens_`, namespace `Omens`. RimWorld 1.6. No Harmony patches (the
Harmony dependency comes from the scaffold and stays for future features; `PatchAll()` finds nothing).

**Pitch.** The world warns you before the battle and remembers after it. Subtle in daily play,
readable when it matters. No AI changes: nothing here alters what any pawn decides to do.

Three features, one toggle each in the mod settings (all on by default):

| Feature | Setting | Save data |
|---|---|---|
| Rain Reveals the Unseen | `rainReveals` | none |
| The Crows Know | `crows` | carrion-omen window (3 ints) |
| The Cold Camp | `coldCamp` | camp entries in the building; raid follow-up in `StagingWatcher` |

Shared: **`StagingWatcher`** (MapComponent) polls `map.lordManager.lords` every 60 ticks and keeps one
`StageRecord` per hostile raid that staged on the map. Crows and cold camp read it.

---

## Shared: StagingWatcher

- Poll: `MapComponentTick`, every 60 ticks (`(TicksGame + map.uniqueID) % 60 == 0`). Cost: a loop over
  a handful of lords and their pawns; nothing allocates unless a record changes.
- **Qualifies as staging**: `lord.CurLordToil is LordToil_Stage` (`RimWorld/LordToil_Stage.cs:7`; used by
  `LordJob_StageThenAttack` and Biotech's `LordJob_BossgroupAssaultColony`) and
  `lord.faction != null && lord.faction.HostileTo(Faction.OfPlayer)`.
  - Sieges (`LordToil_Siege`) do **not** qualify: they build their own visible camp and are their own warning.
  - Immediate-attack raids never enter `LordToil_Stage`: no record, no crows, no camp.
  - Drop-pod raids: `StageThenAttack` only allows `EdgeDrop`/`EdgeDropGroups` among the drop modes; once the
    pods land the pawns join the lord and the lord stages like a walk-in raid. Pawns that land later are
    added to the record on the next poll (counted in "came").
  - Mechanoids (and Biotech bossgroups) stage like humans: they get crows and a camp. The camp text says
    "The <mechanoid faction> waited here…". It is a little odd for machines to leave ash, but the
    staging point is still the place they waited; kept on purpose (cheap, consistent).
  - Anomaly entities never use `LordToil_Stage`: nothing happens.
- **Record** (`StageRecord`, saved with Scribe): `lordId` (Lord.loadID), `faction` (reference) and
  `factionName` (string, survives a removed faction), `cell` (`LordToil.FlagLoc`), `startTick`
  (= now − `lord.ticksInToil` at first sight, so it is right after a load too), `endTick`, `seenIds`
  (thingIDNumbers of every pawn ever in the lord while followed → **came**), `pending` (alive raiders
  not yet resolved, references), `returned`, `died`, `captured`, `camp` (reference) + `campEntryId`, `ended`.
- **Events**:
  - *StageStarted*: first poll that sees a qualifying lord with no record.
  - *StageEnded(assault)*: the record's lord still exists, its faction is still hostile and its toil is now an
    assault toil (`LordToil_AssaultColony`, `…Breaching`, `…Prisoners`, `…Sappers`, `…Bossgroup`, `LordToil_MoveInBossgroup`).
  - *StageEnded(other)*: the lord moved to any other toil (e.g. it became non-hostile and leaves): crows scatter, no flock.
  - *StageEnded(gone)*: the lord is no longer in `lordManager.lords` while the record was staging
    (all killed or fled during staging, or the lord was removed).
- **Following raiders after the stage** (for "M went home"), every poll, for each pending pawn:
  1. `Dead` → `died`; 2. `Destroyed` (not dead) → dropped, no count;
  3. not spawned and `MapHeld == null` (exited to the world, a caravan, a pod in flight) → `returned`;
  4. `Faction == OfPlayer`, `IsPrisonerOfColony` or `IsSlaveOfColony` → `captured`;
  5. otherwise keep (still on the map, carried, downed).
  The record is final when nothing is pending, or 3 days after `endTick` (whatever is still pending is
  simply not counted as returned). Final records are removed. Before saving, the pending list is resolved
  first, so only living pawns are ever written as references.
- Temporary maps (caravan encounters, quest sites): the component lives and dies with its map, no cleanup needed.
  Pocket maps and vacuum maps: staging there is still recorded, but crows never fly there (see crows).

## 1. Rain Reveals the Unseen

**What the player sees.** In rain, a ring of small splashes flickers in the air around something
that is not there — a Revenant, a Sightstealer, any hostile hidden from the player — while it stands
under open sky. In snow, puffs of white dust do the same. The pawn stays unselectable and untargetable.

**Rules**
- Component `RainWatcher` (MapComponent), no save data, no Harmony.
- Runs only if the setting is on, `ModsConfig.AnomalyActive` (without Anomaly no vanilla pawn is hidden from
  the player, so the watcher sleeps), `map == Find.CurrentMap` (flecks on other maps are never seen), and
  `RainRate ≥ 0.1` or `SnowRate ≥ 0.1` (`WeatherManager.cs:45,47`, the lerped values, so it fades in and out
  with the weather transition).
- Every 30 ticks it pulses; every 240 ticks (only while it is raining/snowing) it rescans
  `map.mapPawns.AllPawnsSpawned` for **qualifying pawns**: `IsHiddenFromPlayer()` (`InvisibilityUtility.cs:22`;
  false for the player's own faction and for psychologically visible pawns) **and** `HostileTo(Faction.OfPlayer)`.
  A friendly or neutral hidden pawn never splashes.
- A qualifying pawn splashes in a pulse only if its cell is **not roofed** and **not fogged**.
- Rain pulse: 5 splashes (each kept with probability `min(1, RainRate)`) on an ellipse around `DrawPos`
  (radii 0.42 × 0.55 cells scaled by √bodySize, clamped 0.7–1.5), fleck `Omens_RainSplash` (the vanilla
  ground-splash textures, brighter and less transparent than `GroundWaterSplash`), scale 0.35–0.5.
  Lifetime 0.65 s, so a 30-tick pulse keeps an outline flickering.
- Snow pulse: 3 white puffs (`DustPuffThick`, `instanceColor` white α 0.85, slow drift), same ellipse,
  probability `min(1, SnowRate)`. Snow wins over rain: vanilla snow weathers also carry `rainRate 1` (they put out fires).
- Nothing is revealed to targeting: flecks are not things; no hediff, no visibility flag is touched.
- Cost: nothing at all unless it is raining/snowing on the current map; then one 240-tick scan and, per
  hidden hostile on screen, ≤ 5 flecks every 30 ticks.

## 2. The Crows Know

**What the player sees.** While a raid waits at its staging point, two or three black crows circle
slowly high over it. When the assault starts, the circling crows and a few more — a flock of 6 to 8 —
fly off toward the colony, and the sky over the camp is empty. If you see crows, you have time.
After a battle that left at least three dead, crows circle over the unburied bodies for up to two days.

**Crow**: `Omens.CrowMote : MoteThrown` (ThingDef `Omens_Crow`, a Mote: never saved). Texture
`Things/Omens/Crow` (64×64, drawn by `Source/Art/make_crow.py`), drawn 1.2 cells wide at `MoteOverhead`.
Two modes:
- *orbit*: centre, radius 2.5–5 cells, angular speed 0.25–0.45 rad/s (random sign), moves toward its point
  on the circle at a bounded speed (so a moved centre or the first frame never jumps), faces its heading
  (`rotateTowardsMoveDirection`), wings "flap" (x-scale squeeze) in short bursts every few seconds;
- *depart*: straight line at 3–4.5 cells/s, fades out after 6–9 s or when it leaves the map.
It fades in over 1.5 s. Orbiting crows live until they are told to depart (hard cap 3 days of game time).

**Rules** (`CrowWatcher`, MapComponent, every 60 ticks; carrion check every 250 ticks)
- Only with the setting on, `map == Find.CurrentMap`, map not a pocket map and biome not in vacuum.
- **Staging crows**: for each `StageRecord` still staging whose `cell` is in bounds, **not roofed** and
  **not fogged**: keep 2 + (lordId mod 2) crows orbiting `cell`; at most one new crow per 90 ticks.
- **StageEnded(assault)**: the orbiting crows of that raid depart toward the colony (angle from the
  stage point to the mean position of the free colonists, map centre if none, ±25° each); extra crows
  are spawned around the stage point until 6 + (lordId mod 3) are departing. If the stage point fails
  the roof/fog test, no flock either. **StageEnded(gone)**: the crows scatter (random headings), no flock.
- **Carrion**: a *battle* opens when `GenHostility.AnyHostileActiveThreatToPlayer(map)` turns true and
  closes when it turns false. At close, *battle corpses* = spawned corpses of flesh pawns with
  `timeOfDeath ≥ battle start`, on unroofed, unfogged cells. If there are ≥ 3, a carrion omen opens
  for 2 days: 2 crows (3 with ≥ 8 corpses) orbit the centroid of the battle corpses that still lie
  there, recounted every 250 ticks (the centre follows hauling). It ends when none remain (buried,
  hauled under a roof, destroyed) or after 2 days; its crows scatter.
  - Battles on any map with a colony presence count (home, caravan ambush). A new battle while an omen
    is open replaces it (new window).
  - The battle window (start tick, omen start/end/expiry) is saved; the crows are respawned after a load.
- Cost: ≤ 3 + 8 motes per raid; a lord loop every 60 ticks; one corpse-list pass every 250 ticks only
  while closing a battle or while an omen is open.

## 3. The Cold Camp

**What the player sees.** Where a raid waited, a dead campfire with ash around it remains. Select it:
"The Red Fangs waited here 5 hours, 12th of Aprimay 5501. 9 came; 4 went home." If another raid
stages within 5 cells, a new line is appended: the map learns the hill where they always camp.

**Rules**
- On *StageEnded* (assault or gone), if the setting is on and the raid waited ≥ 1000 ticks (0.4 h):
  - an existing `Omens_ColdCamp` within 5 cells of the stage point gets a new entry; otherwise
  - a new one is spawned at the stage point or the nearest cell within 4 (`CellFinder.TryFindRandomCellNear`,
    in bounds, standable, no edifice, no building, not water), with 4 `Filth_Ash` around it. No cell → no camp.
- `Building_ColdCamp : Building` (ThingDef `Omens_ColdCamp`): Campfire texture tinted grey-brown, no fire,
  no light, no heat, passable (`Standable`), not claimable, `alwaysDeconstructible`, no `costList` (so
  deconstruction gives nothing), WorkToBuild 120, Flammability 0, MaxHitPoints 60. Owned by no faction.
- **Entry** (`CampEntry`, saved inside the building): `id`, `factionName`, `startTick`, `waitTicks`, `came`,
  `returned`. Text per entry:
  `The <faction> waited here <ToStringTicksToPeriod(waitTicks)>, <DateFullStringAt(start)>. <came> came; <returned> went home.`
  ("The " is not doubled if the name already starts with "the"). Counts update live while the raid is followed.
  At most the 6 newest entries are shown, then "(and N earlier camps)".
- Save/load: the building saves its entries (Scribe_Collections Deep); the raid's `StageRecord` holds a
  reference to the camp. If the camp is deconstructed, the record just stops updating it.

## Edge cases

- **Save/load** mid-staging: records are saved; after load `startTick` is still right; crows respawn on the
  next poll (motes are not saved). Mid-follow: pending pawns are saved as references (alive only).
- **Lord without faction / non-hostile lord** (traders, visitors, allies staging to help): ignored.
- **Faction removed** later: the camp keeps the stored name.
- **Several raids at once**: one record each; camps merge only within 5 cells.
- **Psycast invisibility (Royalty)** is visible to the player → not "hidden" → no splashes.
- **Settings off**: tracking continues (cheap), nothing is shown or spawned; turning crows back on during a
  staging brings the crows back on the next poll.
- **No Anomaly**: rain watcher sleeps; crows/camp unaffected. No Anomaly-only def is referenced by the mod.

## Test plan (OmensTest, armed by `rw launch --ticks N`)

Rain (pawn = a real `Revenant` of `Faction.OfEntities`; friendly case = an allied-faction pawn given
`HoraxianInvisibility`; checks call `RainWatcher.Pulse()` directly after teleporting, plus one passive check):
1. PASS revenant `IsHiddenFromPlayer` and hostile (precondition).
2. Clear weather → 0 splashes in a pulse.
3. Rain, open cell → ≥ 3 splashes in a pulse; passive ticking for 120 ticks adds splashes on its own.
4. Rain, roofed cell → 0.  5. Rain, fogged cell → 0.
6. Snow → ≥ 1 dust puffs, 0 rain splashes.
7. Friendly hidden pawn in rain, open cell → 0.
8. Setting off → 0.  9. Revenant still hidden afterwards (nothing revealed).

Crows + camp (a real raid: `IncidentDefOf.RaidEnemy`, `raidStrategy = StageThenAttack`
(`DefDatabase<RaidStrategyDef>.GetNamed`), `raidArrivalMode = EdgeWalkIn`):
10. Watcher has a staging record for the lord within 120 ticks; `came` = raiders spawned.
11. After 600 ticks, 2–3 orbiting crows exist (live `CrowMote`s in orbit near the stage cell).
12. Force the assault (`lord.GotoToil(transition.target)`, as `DebugActionsMisc.ForceEnemyAssault`) →
    within 120 ticks: a flock of 6–8 departing crows, 0 orbiting for that raid.
13. A cold camp exists within 4 cells of the stage point; its inspect string names the faction, "came".
14. Two raiders exit the map (`ExitMap`) → entry `returned == 2`; inspect says "2 went home".
15. Second raid staged within 5 cells → same camp, 2 entries. (A real StageThenAttack raid whose
    `LordToilData_Stage.stagingPoint` is moved next to the camp right after it spawns, then
    `UpdateAllDuties()`; `MinWaitTicks` lowered to 200 for this raid only, the 1000-tick rule is checked by raid 1.)
16. Scribe roundtrip of the camp building's entries and of a `StageRecord`.
17. `DeconstructibleBy(Faction.OfPlayer).Accepted` and no cost list.
18. Negative: an ImmediateAttack raid → no staging record, no crows in 600 ticks, no new camp.
19. Negative: crows setting off during a staging → 0 crows; on again → crows return.
20. Carrion: kill 4 raiders in the open and remove the rest → battle closes → carrion omen with crows
    near the corpse centroid; destroy the corpses → omen ends within 250 ticks.

Showcase (`showcase.flag`, `--showcase`): rain outline of a revenant (rain + snow), staging crows over a
raid, the flock leaving, the cold camp selected with its inspect pane.
