# Pack del Duelo - design notes

Grief made visible. Three features share one assembly (`Duelo.dll`), one settings
page and one test harness (`mods/DueloTest`). Every hook below was checked in
`decomp/1.6.4871`.

## Settings (DueloSettings)

| toggle | default | effect |
|---|---|---|
| Last Light | on | light flies from a dead colonist to whoever loved them most |
| Eulogy | on | funerals/burials read a eulogy (embers + floating lines) |
| Eulogy letter | on | the full eulogy also arrives as a letter |
| Eulogy at burial even when a funeral is pending | off | with Ideology, burial normally waits for the funeral |
| Grave moss | on | love-driven glowing moss on graves |
| Moss brightness | 1.0 | 0.25 - 2.0 multiplier on the glow |

## 1. The Last Light (done)

`Pawn.Kill` prefix measures the strongest bond while the victim is still alive
(opinion + relation weight, `LastLightUtility.FindStrongestBond`); the postfix
releases `Mote_LastLight` only if the pawn really died. The light rises, flies to
the target, circles once, gives `Duelo_LastLightFarewell` (+3, 3 days) and fades.
No target: rises and fades. Not saved (motes are not saveable).

## 2. Embers of the Eulogy (Brasas del elogio)

**What you see.** At a funeral (Ideology) or a burial with mourners (any game),
4-6 short lines about the deceased's real life rise one by one from the grave in
warm amber text, each with a puff of embers, and fade. The last line always says
how they died. A life with almost nothing to tell gets one sad line. Afterwards a
letter carries the whole eulogy, and the grave keeps it: its inspect pane shows
the first line and a "Read eulogy" button opens the full text.

**When the text is written.** At death, not at the funeral. `Pawn.Kill`
postfix (existing patch, `Pawn_Kill_Patch`) composes the eulogy for any
player-faction humanlike and stores it in `GameComponent_DueloMemory`
(keyed by `ThingID`, saved, capped at 300 entries). Reasons: volatile tales
(`TaleType.Volatile`: BecameLover, Recruited, KilledMajorThreat...) are culled by
the TaleManager and may be gone by the funeral; the cause of death (`dinfo`,
`exactCulprit`) is only known inside `Kill`. If a pawn reaches a grave without a
stored eulogy (mod added mid-game), it is composed on demand at that moment,
with an unknown cause of death.

**Facts used** (`EulogyComposer`, every number is real):
- opening: `RecordDefOf.TimeAsColonistOrColonyAnimal` as a period; variant if the
  pawn has a `LandedInPod` tale or was the JOINER of a `Recruited` tale.
- records: kills (`KillsHumanlikes + KillsMechanoids + KillsEntities`),
  `OperationsPerformed`, `TimesTendedOther`, `ThingsCrafted`, `ThingsConstructed`,
  `MealsCooked`, `PlantsHarvested`, `AnimalsTamed`, `PrisonersRecruited`,
  `CellsMined`, `ResearchPointsResearched` - each above a threshold.
- best skill (level >= 8), one non-suppressed trait.
- relations: spouse/fiance/lover (alive or dead), number of children, and the
  living colonist with the highest `OpinionOf(deceased)` (>= 20).
- tales from `Find.TaleManager` that `Concerns(pawn)`: Marriage, BecameLover,
  KilledMajorThreat, BondedWithAnimal, GaveBirth, BuriedCorpse, SocialFight,
  Captured, Recruited (as recruiter), CraftedArt (art title),
  GainedMasterSkillWith/WithoutPassion.
- death: humanlike/animal/mech instigator, or the culprit hediff, or the damage
  def, or unknown.

Lines come from `RulePackDef Duelo_RulePack_Eulogy` with `GrammarResolver`
(2-3 variants per fact, pawn symbols from `GrammarUtility.RulesForPawn` /
`TaleData_Pawn.GetRules`). Up to 4 middle facts are picked by weight with a
little randomness. Fewer than 2 facts -> the single `eulogy_sad` line.

**Triggers.**
- Funeral: `MapComponent_Duelo` polls `map.lordManager.lords` every 250 ticks
  for a `LordJob_Ritual` whose `Ritual.def == PreceptDefOf.Funeral` (or whose
  `behavior` is a `RitualBehaviorWorker_Funeral`) and whose `selectedTarget` is a
  `Building_Grave` holding a qualifying corpse. Lines are released as
  `LordJob_Ritual.Progress` passes evenly spaced thresholds (the funeral is
  5000 ticks). When the lord is gone: if `Ritual.lastFinishedTick` is at or after
  the reading start the funeral completed (set in `ApplyOutcome`) -> remaining
  lines + letter; otherwise it was called off -> the reading stops quietly.
  Ideology types are in Assembly-CSharp, so no DLC reference breaks without it;
  `PreceptDefOf.Funeral` is `[MayRequireIdeology]` and is null-checked.
- Burial: Harmony postfix on `Building_Grave.Notify_HauledTo` (called from
  `Toils_Haul` when a hauler deposits into an `INotifyHauledTo`; Sarcophagus
  calls base). Needs at least one mourner: a free colonist, spawned, awake, not
  downed, within 10 cells of the grave, with `OpinionOf(deceased) >= 10` or a
  family/love relation. The hauler counts if they cared. If Ideology is active
  and the colony owes this pawn a funeral (an active `RitualObligation` of a
  Funeral precept whose `targetA` is the pawn or its corpse) the burial does not
  speak (the funeral will), unless the setting says otherwise. One line every
  200 ticks.
- Each grave speaks a given eulogy once (`CompDueloGrave.SpokenFor`), so a
  burial reading followed by a funeral does not repeat. A burial stores the text
  on the grave when the reading starts (it cannot be called off); a funeral
  stores it only when it completes, so a funeral called off by a raid can be
  held, and read, again.
- Pacing: at least 3.2 real seconds between lines
  (`MapComponent_Duelo.MinRealSecondsBetweenLines`; the test harness sets 0),
  because the text motes live in real time like vanilla's.

**Visuals.** `Mote_EulogyLine` (MoteThrown subclass, realTime like vanilla text
motes so it stays readable at 3x speed) draws its text in `DrawGUIOverlay` with
`GameFont.Medium`, amber with a 1 px dark outline on a soft dark plate (it has to
read over the funeral's own light ray), wrapped at 520 px, rising straight up at
0.32 cells/s. Embers: `Duelo_Fleck_Ember` (FleckBase_Thrown, MoteGlow) - a burst
of 16 per line and 1-3 every 30 ticks while the reading lasts.

**Saved:** stored eulogies (GameComponent), per-grave eulogy + spoken flag
(comp). **Not saved:** a reading in progress (transient; after a load the grave
still has its text and the letter is simply not sent).

**Cost:** composition once per colonist death; a lord scan every 250 ticks
(a handful of lords); readings tick every 30 ticks only while one is active.

## 3. Moss of the Beloved (Musgo de los queridos)

**What you see.** A grave holding someone the living loved slowly grows soft
blue-green moss. At night it glows; the glow's strength follows how much the
living still love them. When those who loved them die or forget (opinion
memories expire), the moss fades back over a day or two.

**Rule.** `love = sum over living free colonists (all maps, caravans) of
max(0, OpinionOf(deceased))` for a humanlike corpse. Only people the colony knew count: the deceased
was of the player faction, or the living colonist has a relation to them (kin,
partner, bond). A stranger or raider never grows moss from looks alone. Relation offsets (spouse
+30, parent +30...) persist after death; social memories decay; dead colonists
stop counting. `target = 1 - exp(-love / 90)`, 0 when love < 5.
`level` moves toward `target` by at most 0.04 per recompute (about one day from
bare to full).

**Hooks.** `CompDueloGrave` is added in a `[StaticConstructorOnStartup]` to every
ThingDef whose `thingClass` is `Building_Grave` or a subclass (Grave,
Sarcophagus, modded graves). Graves tick Rare (`tickerType Rare`), so the comp
recomputes in `CompTickRare` once every 10 rare ticks (~2500), staggered by
`thingIDNumber`. Drawing: `MapComponent_Duelo.MapComponentUpdate` draws
registered graves inside the camera view with `Graphics.DrawMesh`: a moss
texture (Transparent shader, darkened at night like the grave) and a glow
(TransparentPostLight shader, drawn after the lighting overlay like fire, so it
reads at night) whose alpha is `level * (0.3 + 0.7 * night) * brightness`, night
= `1 - map.skyManager.CurSkyGlow`. No light is emitted (no gameplay effect on
darkness or mood).

**Saved:** `level` (comp). The target is recomputed after load; the first
recompute after spawn/load only sets the target, so save/load never adds growth.
Graves register with `MapComponent_Duelo` on spawn (and lazily from
`CompTickRare` if the map component was missing at spawn).

**Cost:** per grave, `OpinionOf` for each living colonist every ~2500 ticks;
drawing only graves in view with level > 0.01.

## Tests (DueloTest, armed by `--ticks`)

- LastLightHarness (unchanged rules).
- EulogyHarness: composer from real records/tales (PASS per fact type it seeds),
  stored at death, burial with mourner speaks (lines spawned, comp text, letter),
  burial of a stranger does not (negative), burial while a funeral is owed defers
  (negative), funeral via `RitualBehaviorWorker.TryExecuteOn` speaks and letters.
- MossHarness: beloved grave target > 0.5, stranger and empty graves 0
  (negative), growth one step through the real `CompTickRare`, killing the
  lovers lowers love and target and the moss steps down, `CompDueloGrave`
  fields through a Scribe write/read (`DebugOutputFor` + `LoadingVars`).
- Showcases (`showcase.flag` content picks: lastlight, eulogy, moss).
