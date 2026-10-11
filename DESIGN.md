# Aftermath — what a battle leaves behind the next day

Five small features about the day after a fight: the door you patched, the banner you took, the
enemy you buried with honour, the lamp by a dying friend's bed, the gifts piled next to a wounded
one. Each has its own toggle in the mod settings (scroll view). Player-facing text in English.
packageId `nahuel.aftermath`, prefix `Aftermath_`, C# namespace `Aftermath`. Requires Harmony.
Safe to add to an existing save (all comps are injected at startup; `ThingWithComps.ExposeData`
re-creates comps on load, `Verse/ThingWithComps.cs:236-243`).

All signatures re-checked in `decomp/1.6.4871/Assembly-CSharp` (paths below are relative to it).

## Shared

- **`AftermathMod` / `AftermathSettings`**: five bools (`bracedDoors`, `trophyStandards`,
  `honoredFoes`, `vigilLamps`, `bedsideOfferings`), all default true, scroll view like Gesta.
- **`AftermathTuning`** (static fields, not settings): every number below. The test harness may
  lower intervals and restores them.
- **`CompInjector`** (`[StaticConstructorOnStartup]`): adds `CompProperties { compClass = ... }` to
  - every ThingDef with `IsDoor` (`Verse/ThingDef.cs:912`) except `Building_VacBarrier` subclasses
    (Odyssey energy barrier) → `CompBracedDoor`;
  - every ThingDef whose `thingClass` is `Building_Grave` or a subclass (grave, sarcophagus, mod
    graves) → `CompHonoredFoe`.
- **Harmony** (all wrapped in try/catch so nothing cosmetic can break a death or a downing):
  - `Pawn.Kill(DamageInfo? dinfo, Hediff exactCulprit)` Prefix (`Verse/Pawn.cs:2096`). The prefix
    still sees the victim alive, in his lord, in his bed, with his equipment. Used by standards,
    honored foe, vigil lamp.
  - private `Pawn_HealthTracker.MakeDowned(DamageInfo? dinfo, Hediff hediff)` Prefix
    (`Verse/Pawn_HealthTracker.cs:837`, victim in `private readonly Pawn pawn` → `___pawn`). Runs
    before `Lord.Notify_PawnLost(Incapped)` (:867) removes a downed raider from his lord
    (`Verse.AI.Group/Lord.cs` Notify_PawnLost → RemovePawn). Used by standards and honored foe.
  - `Pawn.PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)` Postfix (`Verse/Pawn.cs:1935`):
    standard veterans.
  - `JobDriver.Cleanup(JobCondition condition)` Postfix (`Verse.AI/JobDriver.cs:274`), filtered to
    `JobDriver_VisitSickPawn`: bedside offerings. Runs exactly once when any job ends.
- Dates: `GenDate.DateFullStringAt(GenDate.TickGameToAbs(tick), longLat)` with
  `longLat = Find.WorldGrid.LongLatOf(map.Tile)` captured when the event happens (as
  `Building_Grave.GetInspectString`, `RimWorld/Building_Grave.cs:146-155`).

## Feature 1 — Braced Door (`bracedDoors`)

**Pitch:** the door they broke comes back patched — one crossed plank per breach, dated.

**What counts as a breach** (only doors owned by the player, `parent.Faction == Faction.OfPlayer`):
- **Broken** — the door is destroyed: `ThingComp.Notify_Killed(Map prevMap, DamageInfo? dinfo)`
  (`Verse/ThingComp.cs:202`, called by `ThingWithComps.Kill` after `Destroy(KillFinalize)`,
  `Verse/ThingWithComps.cs:308`). `parent.Position` is still valid.
- **Battered** — the door survives a hit that leaves it at ≤ **30%** of max hit points:
  `ThingComp.PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)` (`Verse/ThingComp.cs:72`).
- The attacker must be hostile: `dinfo.Instigator` is a Thing with `HostileTo(Faction.OfPlayer)`
  (`RimWorld/GenHostility.cs:118`; covers raiders, mechs, insects and manhunters). Label of the
  attacker: its faction name, else its `LabelShort` ("Pirates of Ash", "boar"). For a *broken*
  door with no instigator (fire spreading, collapse), it still counts when
  `GenHostility.AnyHostileActiveThreatToPlayer(map)` (`RimWorld/GenHostility.cs:227`) is true,
  attacker "raiders". A door the player or his colonists destroy never counts.
- **One breach per raid per door cell:** if the cell already has a record from the last **1 day**,
  a battered hit adds nothing and a broken door upgrades that record to *broken* (keeps its date).
- At most **6** records per cell (oldest dropped). Records belong to the **cell** (a
  `MapComponent`, `BreachTracker`), so the door rebuilt there — by the vanilla auto-rebuild
  blueprint or by hand, any stuff, any door def — inherits them.

**Planks:** a *broken* record shows a plank on any door standing on the cell (it was rebuilt by
definition). A *battered* record shows its plank once the door has been **repaired** back to full
hit points (latched in the record, saved). Up to **4** planks are drawn (crossing diagonals, slightly
staggered); the inspect pane lists every record. Drawn in `ThingComp.PostDraw()` (called from
`Building_Door.DrawAt` → `Comps_PostDraw`, `RimWorld/Building_Door.cs:526-535`; doors are
RealtimeOnly) with `Graphics.DrawMesh(MeshPool.plane10, TRS, mat, 0)` at
`AltitudeLayer.DoorMoveable.AltitudeFor()` + a small bias, only while the door is closed
(`Building_Door.Open`, :51). Material: own procedural plank texture, `MaterialPool` (cached once).
Multi-cell doors use `Position` and scale the planks by `def.size.x`.

**Inspect** (`CompInspectStringExtra`): `Braced 2×: broken 5 Aprimay 5501 (Pirates of Ash), battered
12 Jugust 5501 (boar)`; a battered record not yet repaired shows "(patch it up to brace it)".

**Clearing:** deconstructing the door (`PostDestroy(DestroyMode.Deconstruct, map)`) clears the
cell's records: the player chose to remove it. `KillFinalize` (another breach), `WillReplace`,
`Vanish` keep them. Feature off: no new records, no planks drawn, inspect line hidden; records kept.

**Data:** `BreachTracker : MapComponent`, `List<BreachRecord>` (`Scribe_Collections`, Deep;
record = cell, tick, longLat, attacker label, kind, repaired). The comp caches its cell's records and
refreshes when the tracker's `version` changes (no lookups per frame beyond an int compare).

**Tests:** hostile kill of a player door → 1 *broken* record with the faction's name; new door on the
cell → inspect says "Braced 1×", 1 plank visible; second kill a day later (record back-dated) → 2
records, 2 planks; second kill within the day → still 2 (upgrade, not add); hostile damage to ≤30% →
*battered* record, 0 planks until repaired, 1 plank after `HitPoints = MaxHitPoints`; a door killed
by a colonist → nothing (negative); deconstruct → records cleared; feature off → hostile kill records
nothing (negative); `BreachRecord` save/load roundtrip.

## Feature 2 — Trophy Standards (`trophyStandards`)

**Pitch:** bring down a raid's leader and his faction's standard falls with him. Hang it in your
hall; it remembers the day and who did it.

**Raid lords** (`StandardTracker : MapComponent`, every **250** ticks and lazily on a fall):
`map.lordManager.lords` (`Verse.AI.Group/LordManager.cs:13`) on a player home map
(`Map.IsPlayerHome`, `Verse/Map.cs:354`) with `lord.faction` non-null, not the player, hostile to
the player, `def.humanlikeFaction`, and at least **2** humanlike members when first seen.
Mechanoid and insect raids never carry a standard.

**Leader** (fixed once per lord, saved): the faction's real `leader` (`RimWorld/Faction.cs:26`) if he
is a member; else the humanlike member with the highest `kindDef.combatPower`
(`Verse/PawnKindDef.cs:162`), ties to the lowest thingIDNumber. If the lord was never seen (killed
within 250 ticks of arriving), the leader is chosen at the moment of the first fall, the falling
pawn included.

**The fall:** the leader is downed (`MakeDowned` prefix) or killed while still in the lord
(`Pawn.Kill` prefix; downed raiders already left the lord, and they already dropped it). Once per
lord. The standard is made with `ThingMaker.MakeThing(Aftermath_CapturedStandard)`
(`Verse/ThingMaker.cs:8`), filled in, minified (`MinifyUtility.MakeMinified`,
`RimWorld/MinifyUtility.cs:8`) and placed near his position (`GenPlace.TryPlaceThing(..., Near)`,
`Verse/GenPlace.cs:27`). A message names it. Who brought him down: `dinfo.Instigator` — a pawn's
short name, a turret's label, else "unknown hands". Leaders that flee, are kidnapped out, or are
never brought down leave nothing.

**The standard** (`Building_CapturedStandard : Building, IObservedThoughtGiver`): minifiable,
installable anywhere (it is furniture-like, passable, 1×1, not buildable from the menu). Saved:
faction (reference, may become null), faction name, faction def (for the icon), colour, leader name,
killer name, tick, longLat, veteran thingIDs. Graphic: a procedural pole with an off-white cloth
(that is what the minified crate and the menu show); when installed, `Print` overlays the procedural
cloth tinted with the faction colour (`Faction.Color`, `RimWorld/Faction.cs:108`) and the faction
icon (`FactionDef.FactionIcon`, `RimWorld/FactionDef.cs:363`, skipped if it is `BaseContent.BadTex`)
in a contrasting tone, via `Printer_Plane.PrintPlane` (`Verse/Printer_Plane.cs:50`) and cached
`MaterialPool.MatFrom(tex, shader, color)` (one material per faction colour).

**Pride:** `GiveObservedThought(observer)` (`RimWorld/IObservedThoughtGiver.cs`), read by vanilla
`PawnObserver` (≤5 cells, line of sight, awake, every ~4 need intervals,
`RimWorld/PawnObserver.cs`). Only free colonists, only while installed (spawned).
- `Aftermath_TrophyStandard` +3 ("We took that from them."), 1 day, stack limit 3.
- veterans of that battle get `Aftermath_TrophyStandardVeteran` +6 instead. A veteran is a colonist
  who damaged a member of that lord or was damaged by one (`Pawn.PostApplyDamage` postfix), plus
  whoever brought the leader down.
- Both are `Thought_MemoryObservation` (renewed per standard, never stacked on the same one).

**Inspect:** `Standard of Pirates of Ash.` / `Taken from Big Jim, their leader, 5 Aprimay 5501.` /
`Brought down by Ana.` / `3 veterans of that battle.`

**Data:** `StandardTracker` saves `List<LordRecord>` (lord loadID, leader thingID, dropped flag,
veteran IDs). Records of lords that no longer exist are pruned on the 250-tick pass.

**Tests:** a real raid (`IncidentDefOf.RaidEnemy.Worker.TryExecute`) of a hostile humanlike faction
→ within 300 ticks the tracker names a leader who is a member; killing another raider first → no
standard (negative); downing the leader with a colonist → exactly 1 minified standard whose inner
thing has the right faction name, colour, leader and killer; killing another member afterwards →
still 1; installed on a clear cell, a non-veteran colonist 2 cells away (real
`PawnObserver.ObserveSurroundingThings` via reflection) gets `Aftermath_TrophyStandard`, the killer
gets the veteran thought; inspect has faction, leader and killer; feature off → a second raid's
leader goes down and drops nothing (negative).

## Feature 3 — Honored Foe (`honoredFoes`)

**Pitch:** the raider who nearly broke your line is buried under his own rifle and helmet.

**Deeds** (`FoeTracker : GameComponent`, saved): a hostile humanlike pawn (`Faction` not the player,
`HostileTo(player)`) is credited when he
- downs a colonist (`MakeDowned` prefix, `dinfo.Instigator` is him) — one deed per distinct victim;
- kills a colonist (`Pawn.Kill` prefix, `dinfo.Instigator` is him) — the victim is marked killed.
Colonist = `IsColonist` (slaves included). Downs without an instigator (bleeding out) do not count.
**Worthy:** at least **2** distinct victims, or at least **1** killed.

**Death of a worthy foe** (`Pawn.Kill` prefix, any cause): snapshot into a `FoeRecord` keyed by his
thingIDNumber — name, faction name, `equipment.Primary` def + stuff, the worn apparel on the
`Overhead` layer (helmet) def + stuff, death tick + longLat, victims (killed flags), and who killed
him. The deeds entry is removed. Deeds of foes not seen for **15 days** are pruned; unburied records
older than **60 days** are pruned (buried ones are kept while their grave holds them).

**The grave:** `CompHonoredFoe` (injected into every `Building_Grave` def) looks up
`Building_CorpseCasket.Corpse.InnerPawn.thingIDNumber` (`RimWorld/Building_CorpseCasket.cs:17`).
Vanilla already re-prints the cell when a corpse goes in (`Building_Grave.TryAcceptThing` →
`MapMeshDirty`, `RimWorld/Building_Grave.cs`) or out (`EjectContents`). In `PostPrintOnto`:
- the weapon, stood vertically, muzzle into the ground: an unspawned
  `ThingMaker.MakeThing(def, stuff)` cached per (def, stuff), `Graphic.MatSingleFor(thing)`
  (`Verse/Graphic.cs:243`; `Graphic_StackCount` handled like `PawnRenderUtility.DrawEquipmentAiming`),
  rotation `90 + def.equippedAngleOffset` (the same angle vanilla uses to aim a weapon south);
- the helmet on top of it: `ApparelGraphicRecordGetter.TryGetGraphicApparel(apparel,
  BodyTypeDefOf.Male, false, out rec)` (`RimWorld/ApparelGraphicRecordGetter.cs:8`; Overhead paths
  ignore the body type) → `rec.graphic.MatAt(Rot4.South)`.
- Placed on the grave's `Position` cell (the head end), above the grave's altitude. No weapon →
  helmet only; neither → nothing drawn but the inspect text still shows. Nothing is drawn for
  ordinary dead.
- Inspect (`CompInspectStringExtra`): `Here lies Big Jim of Pirates of Ash, a worthy foe.` /
  `He brought down Ana and Bob, and killed Carl.` / `Fell to Dana on 5 Aprimay 5501.`
- Feature off: no new deeds/records, nothing drawn, inspect hidden.

**Tests:** a hostile with a rifle and a helmet downs two colonists (real blows, `Kit.DownBy`) → he is
worthy; downing only one with another hostile → not worthy (negative); killing the worthy one →
record with the right weapon/helmet defs and both names; buried in a spawned grave
(`TryAcceptThing`) → the comp reports a marker with a non-null weapon material and the inspect text
has his name and victims; the unworthy one buried in another grave → no marker, no text (negative);
a hostile who kills one colonist → worthy at once; `FoeRecord` roundtrip.

## Feature 4 — Vigil Lamp (`vigilLamps`)

**Pitch:** the bed of someone fighting for their life glows a faint amber. If they die, it goes out.

**Who:** every **250** ticks (`VigilTracker : MapComponent`, not saved) over
`map.mapPawns.AllHumanlikeSpawned` (`Verse/MapPawns.cs:187`): alive, `CurrentBed()`
(`RimWorld/RestUtility.cs:493`) non-null, bed owned by the player, `bed_humanlike`,
`bed_canBeMedical` (excludes deathrest caskets). Colonists, prisoners and guests alike.

**In danger** if any of:
- `HealthUtility.TicksUntilDeathDueToBloodLoss(p)` (`Verse/HealthUtility.cs:572`) < **6 hours**;
- a visible lethal hediff (`IsLethal`, `Verse/Hediff.cs:290`) at ≥ **60%** of its `lethalSeverity`,
  not `FullyImmune()` (`Verse/HediffUtility.cs:50`), and — if immunizable — immunity
  (`ImmunityHandler.GetImmunity`, `Verse/ImmunityHandler.cs:80`) below its severity (losing the race;
  blood loss itself counts here);
- a visible hediff whose current stage is life-threatening (`IsCurrentlyLifeThreatening`, :302).

**The light:** `Aftermath_VigilGlow`, an ethereal `ThingWithComps` with **no graphic**
(`EtherealThingBase`: category Ethereal, drawerType None, no hit points), `isSaveable false`
(`Verse/ThingDef.cs:302`, skipped by `Map.cs:652` when saving), not selectable, with a vanilla
`CompGlower` (`Verse/CompGlower.cs`): colour (64,38,10), radius 3. That is a candle-strength amber —
enough to read at night or in a dark room, the bed cell itself may count as lit. It is spawned on the
bed's `Position`. It is on whatever the hour; it is just only noticeable when it is dark.

**Fading:** when the danger passes (patient still in bed, alive), the light fades over **10** polls
(`CompGlower.GlowColor` setter re-registers, `Verse/CompGlower.cs:39,357`) and is removed. Danger
returning during the fade restores full colour. Patient leaves the bed alive, bed gone, setting off →
removed at the next poll.

**Death:** `Pawn.Kill` prefix reads `CurrentBed()` (vanilla reads it there too, `Verse/Pawn.cs:2117`)
and destroys that bed's light **in the same tick** — no fade.

**No saved data:** the light is not saved, the tracker is in memory; after a load the light comes
back within one poll if the patient is still in danger.

**Tests:** the glow def has no graphicData, isSaveable false, and loads (no config error in the
log); a colonist with 65% blood loss in a player bed → within one poll a glower stands on the bed
and is lit (`CompGlower.Glows`); a lightly hurt colonist in another bed → no glower (negative);
killing the first → the glower is destroyed in the same tick; a third patient in danger, then cured →
the glow colour drops on the next poll and the glower is gone after the fade; setting off → glower
removed for a patient in danger (negative).

## Feature 5 — Bedside Offerings (`bedsideOfferings`)

**Pitch:** every visit to a wounded colonist leaves a small gift by the bed. The loved one ends up
surrounded; the disliked one alone.

**A visit counts** when a `JobDriver_VisitSickPawn` (`RimWorld/JobDriver_VisitSickPawn.cs`, used by
the joy giver and the doctors' visit work giver) ends with `Succeeded`, or ends otherwise (not
`Errored`) while on its last toil (the visit itself, `OnLastToil`) after at least **600** ticks of it
(`job.def.joyDuration - ticksLeftThisToil`). Visitor: humanlike, spawned, alive. Patient:
`job.targetA.Thing` as Pawn, `IsColonist`, alive, `CurrentBed()` non-null, same map.

**Who leaves a gift:** the visitor's opinion of the patient (`Pawn_RelationsTracker.OpinionOf`,
`RimWorld/Pawn_RelationsTracker.cs:592`) is ≥ **0**, or they are lovers/fiancés/spouses, or blood family
(`PawnRelationDef.familyByBloodRelation`; exes are not family). Visitors who
dislike the patient (opinion < 0) leave nothing — vanilla already makes them visit less
(`SickPawnVisitUtility.VisitChanceScore`), and now their visits leave no trace.

**The gift** (one per visitor per patient): the kind depends on the bond —
lover/spouse/fiancé (`LovePartnerRelationUtility.LovePartnerRelationExists`) → posy of flowers;
blood family (`patient.GetMostImportantRelation(visitor)`, `familyByBloodRelation`) → folded letter; opinion ≥ **20** → carved figure;
otherwise → a candle. Four ThingDefs (`Aftermath_Offering_Flowers/_Letter/_Figure/_Candle`) sharing
`Aftermath.Offering : ThingWithComps`, category Item, `tickerType Rare`, Beauty 2, in a root
ThingCategoryDef of their own (`Aftermath_Offerings`) so default stockpiles
(`Verse/ThingFilter.cs:779-790`) never haul them away. Placed with `GenSpawn.Spawn` on a cell
adjacent to the bed (`GenAdj.CellsAdjacent8Way(Thing)`), in bounds, `Standable`, no item
(`GetFirstItem`), no door, not another occupied bed cell. No free cell → no gift (the visit is
lost). The same visitor again → his gift's visit count goes up (no new gift). At most 8 gifts per
bed by geometry.

**Inspect:** `Left for Mira by Tom (lover), 5 Aprimay 5501.` / `Visits: 3.`

**Afterwards** (each offering's `TickRare`):
- patient alive, not in bed and no longer needing medical rest
  (`HealthAIUtility.ShouldSeekMedicalRest`, `RimWorld/HealthAIUtility.cs:23`): the first offering to
  notice gives the patient `Aftermath_CameToSeeMe` by number of distinct gift-givers on the map
  (1 → +2, 2-3 → +4, 4+ → +7; 4 days), marks all of that patient's offerings *done*; done offerings
  disappear after **6 hours** (taken home).
- patient dead: offerings become a small *shrine* (inspect: "In memory of Mira.") and disappear after
  **3 days**.
- patient gone from the map or discarded: done, no thought.
- Feature off: no new gifts; existing ones still resolve.

**Tests (real visits)** with `JobDefOf.VisitSickPawn.joyDuration` shortened for the run and
restored: lover visits → a flowers offering adjacent to the bed naming him; same lover again → still
one offering, visits 2; a neutral colonist visits a second patient → candle; a colonist with a
strongly negative opinion (3× `HadAngeringFight`) visits → no offering (negative); a visit
interrupted after 100 ticks → no offering (negative); first patient cured and out of bed → after a
rare tick he has `Aftermath_CameToSeeMe` stage 0 and his offering is done; second patient killed →
his offering is a shrine.

## Edge cases decided

- Mods with custom doors/graves: covered by `IsDoor` / `Building_Grave` subclassing; doors drawn by
  custom code that skips `Comps_PostDraw` simply show no planks.
- Caravans / non-home maps: standards only on player home maps; doors only player-owned; offerings
  and vigil only where a player bed is.
- Animals: never leaders, never worthy foes, never get gifts or vigil lamps (`bed_humanlike`), but a
  manhunter breaking a door counts as a breach.
- Removing the mod: the standards and offerings are things of missing defs (standard RimWorld
  behaviour, dropped on load); comps and trackers vanish silently.
- Combat Extended: nothing here depends on damage numbers except the 30% battered threshold.
