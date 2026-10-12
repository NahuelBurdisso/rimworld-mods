# Gesta — combat that becomes legend

Pack of combat-narrative features sharing one combat detector. Each feature has a toggle
in the mod settings. Player-facing text in English. packageId `nahuel.gesta`, prefix `Gesta_`.

## Core (shared)

- **EngagementTracker** (`GameComponent`, not saved): every 250 ticks, for each player home map,
  `GenHostility.AnyHostileActiveThreatToPlayer(map)`. Transition false→true = engagement start,
  true→false = engagement end. While active it records `maxFighters` (most violence-capable,
  non-downed free colonists seen standing at once). An engagement older than 1 day force-ends.
  Features subscribe to `EngagementStarted(map)`, `EngagementEnded(map)` and per-tick checks.
- **CombatEvents** (Harmony): Postfix on private `Pawn_HealthTracker.MakeDowned(DamageInfo?, Hediff)`
  and Prefix on `Pawn.Kill(DamageInfo?, Hediff)` raise `ColonistDowned`/`PawnDied` (prefix captures
  map, position, faction, lord before death).
- "Fighter" = free colonist, spawned, not downed, not dead, `!WorkTagIsDisabled(WorkTags.Violent)`.
- "Active hostile" = spawned, non-downed `Pawn` in `map.attackTargetsCache.TargetsHostileToColony`
  that passes `GenHostility.IsActiveThreatToPlayer`.

## Feature 1 — The Last One Standing (`last-stand`)

**Pitch:** when only one of your fighters is still on their feet against a real force, the game
stops to watch them.

**Trigger** (checked on every colonist down/death and every 250 ticks during an engagement):
- engagement active on a player home map;
- exactly 1 fighter standing, and the engagement had `maxFighters >= 2`;
- at least 3 active hostile pawns;
- that pawn has not had a last stand in the past in-game year (3,600,000 ticks);
- no other last stand active on that map.

**On trigger:**
- Hediff `Gesta_LastStand` for 2500 ticks (1 in-game hour): PainShockThreshold +0.5,
  ShootingAccuracyPawn +5, MeleeHitChance +0.2, blocks mental breaks. When it ends: rest need
  drops to at most 0.15 (the crash).
- Cinematic (each step has its own setting; all only if that map is the current map):
  camera pans to the pawn (`PanToMapLocAndSize`, size 14, ~1.5 s); speed drops to Normal if faster;
  music fades to silence for 25 s (`ForceFadeoutAndSilenceFor(25, 2, preventDangerTransition)`);
  screen vignette (dark red, pulsing like a heartbeat, stronger as the pawn's health drops) while
  the hediff lasts; a caption "<Name> stands alone." fades in at the top for ~4 s.
- Map label of the pawn turns gold while active (Postfix `PawnNameColorUtility.PawnNameColorOf`,
  reference check, O(1)).

**Resolution:**
- Engagement ends with the pawn alive → memory `Gesta_StoodAlone` (+8 mood, 20 days) and
  tale `Gesta_StoodAlone` (PermanentHistorical, art can depict it). Other colonists who were downed
  during it get `Gesta_SavedByLastStand` (+4, 10 days, social memory about the pawn).
- The pawn dies during it → tale `Gesta_FellAlone`.
- The pawn goes down during it → last stand ends, no reward (it already failed).

**Not triggering (by design):** a colony with one fighter from the start; pacifists; caravans and
non-home maps; animals, mechs and slaves (not free colonists); 1-2 hostiles.

**Tests (GestaTest harness):** ensure 4 violent-capable colonists; spawn 2 unarmed hostiles 25
cells away; engagement starts (PASS); down 2 colonists → no trigger (2 standing); down the 3rd →
no trigger (only 2 hostiles); spawn a 3rd hostile → periodic check triggers on the last one
(hediff, active state, gold label); downed colonists do not get the hediff; kill all hostiles →
engagement ends → memory + tale + saved-by memory, active cleared, cooldown recorded; a second
trigger attempt on the same pawn is refused by the cooldown.

## Feature 2 — Feats of Arms (`feats-epithets`)

**Pitch:** do something legendary in a fight and the colony gives you a name for it.

**Feats** (each at most once per pawn; only free non-slave colonists who are not quest lodgers;
only against pawns that were hostile, on their feet and not of the player's faction at the moment
of death — finishing off downed enemies or a berserk colonist is not a feat; never by fire, whose
instigator lingers for minutes; on any map, caravans included):
| Feat | Condition | Epithets (one picked at random) |
|---|---|---|
| Triple kill | 3 hostile kills within 600 ticks (10 s) | Reaper, Scythe, Thricefold |
| Beast breaker | melee kill (not ranged, explosive or fire, killer adjacent to the victim) of a hostile with BodySize ≥ 2.5 or combatPower ≥ 300 | Giantkiller, Beastbreaker, Thrumbo-bane |
| Kingslayer | kills a hostile who is its faction's real leader (`Faction.leader`) or whose kind `isBoss` (raid-group bosses like PirateBoss do not count: almost every raid has one) | Kingslayer, Crownbreaker, Headsman |
| Lifeline | carries a downed humanlike colonist who is not a baby, with an active hostile in line of sight within 15 cells, for 4 consecutive checks (60 ticks apart; any gap restarts the count). The job is not checked: a drafted carry ends its job once the pawn is picked up | Lifeline, the Shield, Unflinching |

A fifth feat, **Champion** (epithets Champion, Duelist, Bannerbreaker), is awarded by Feature 11 to a
colonist who wins the champion's duel; it has no tale of its own (the duel records one).

**On a feat** (several feats from the same kill are awarded together: one tale each, but one beam,
one title — named by the last feat — one memory and one message):
- A golden column of light rises at the pawn (fleck `Gesta_Fleck_FeatBeam`, tall vertical glow,
  ~2.5 s) with a bloom at the feet, and gold floating text with the feat name.
- The epithet becomes the pawn's title (`story.Title`, vanilla, saved, shown as "Ana, Kingslayer"
  in grey next to the name) **only if** the current title is the backstory default or an epithet
  this mod gave. A title the player typed is never replaced (the feat is still recorded).
- A tale per feat (PermanentHistorical) so art can depict it.
- Memory `Gesta_DidAFeat` (+5, 6 days).
- Message (positive) "Ana earned a name: Kingslayer."

**Saved:** per pawn id, the feats earned and the last epithet given (GameComponent `FeatTracker`).
Kill timestamps for the triple kill are not saved.

**Tests:** a colonist kills 2 hostiles → no feat; a 3rd within 10 s → triple kill, title set,
tale, memory; kills of non-hostiles don't count; a 2nd triple kill gives nothing new; a melee
kill of a thrumbo gives Beast breaker but a ranged kill of one does not; killing a pawn whose
kind is a faction leader gives Kingslayer; a player-set custom title is not overwritten;
a colonist carrying a downed colonist near a hostile for 2 checks gets Lifeline.

## Feature 3 — Battlefield Poppies (`battlefield-poppies`)

**Pitch:** where someone fell in battle, a poppy grows the next season: red for your people,
white for the enemy, and it remembers who.

**Recording** (Pawn.Kill postfix with the KillContext): the victim is humanlike and not a mutant
(no shambler fields), the killer is a pawn — or, for a death without a DamageInfo (bleeding out),
whoever last hurt the victim within a day (`LastAttackers`, from a Pawn.PostApplyDamage postfix) —
and it is not an execution or sacrifice (`ExecutionCut`), the death cell is in bounds, unroofed, with fertility ≥ 0.5. Stored per map
(MapComponent `PoppyField`): cell, colonist?, victim name, victim faction name, killer name, weapon
label, death tick, and the living colonists who loved the victim (opinion ≥ 20 or any relation;
only for colonist victims). A new record is skipped if a poppy or record already exists within 1
cell (killboxes); at most 80 poppies + records per map.

**Blooming:** every 2500 ticks, records at least 3 days old bloom if it is growing season there.
Target cell: the nearest empty valid cell within 2 (in bounds, unroofed, fertile, no edifice, not
in a growing zone); failing that, one whose plant is worthless wild growth (not a tree, not sown,
auto-cuttable, nothing to harvest — anima grass, healroot, ambrosia and berries are never cleared). A record that
cannot bloom within 60 days is dropped.

**The plant** (`Gesta.Plant_Poppy`, defs `Gesta_PoppyRed` / `Gesta_PoppyWhite`): decorative, not
sowable, not harvestable, not edible, not auto-cut, never blighted, never dies of age or light; it
can still burn or be cut by the player. Beauty +8. Label "red poppy (Mía)". Inspect: "Here fell
Mía, killed by Kerr with a bolt-action rifle, 12th of Aprimay 5502."
**Mourners:** every long tick, an awake mourner within 4 cells and in line of sight gets
`Gesta_VisitedPoppy` (+3, 1 day). Turning the feature off stops blooming and mourning too.
**Known:** a growing zone or a blueprint placed over a poppy removes it, like any wild plant.

**Tests:** a colonist killed by a hostile on fertile open ground creates a record with the right
names; a death under a roof does not; a second death in the adjacent cell is merged; forcing the
record's age blooms a red poppy at the cell with the victim's name in its label and inspect text;
an enemy's poppy is white; the poppy is not ingestible and does not die of age; a mourner standing
next to it gets the memory.

## Feature 4 — Ash Silhouettes (`ash-silhouettes`)

**Pitch:** someone killed by an explosion or fire leaves their shadow burned into the ground,
like the shadows of Hiroshima.

**Trigger** (Pawn.Kill prefix, while the pawn is still drawn standing; wrapped in try/catch so a
cosmetic failure can never stop a death): humanlike, spawned, and the killing damage is explosive
(`isExplosive`, so modded explosives count), Flame, Burn or Vaporize, or the pawn is on fire. At
most 3 per tick; at 40 per map the oldest mark fades to make room.

**Snapshot:** the pawn's own render (body, head, hair, apparel; portrait pose, facing south) is
drawn by vanilla's `PawnCacheRenderer` into a 128×128 render texture covering 2.6 × 2.6 cells,
read back to a texture, turned into a soot-black silhouette (colour (22,18,16), alpha × 0.78) and
stored as PNG bytes. This uses the real renderer, so alien races and modded apparel work.

**The mark** (`Gesta.AshSilhouette`, def `Gesta_AshSilhouette`): drawn flat on the floor layer at the
death cell (map mesh, built lazily on the main thread because saves load asynchronously). Not
filth: it cannot be cleaned. It fades after one in-game year, or at once if the floor under it
changes or a wall is built on it. Its texture and material belong to the mark (not MaterialPool,
which never forgets) and are destroyed with it. `neverMultiSelect`, so drag-selection skips it. Inspect: "Here Ruiz was caught in the blast, 12th of Aprimay
5502." (fire: "burned"). Saved: PNG bytes (deflated by the scribe), name, cause, tick, terrain.

**Tests:** a hostile killed by Bomb leaves one silhouette at its cell whose texture has a visible
shape (non-transparent pixel count in a sane range); a bullet death leaves none; a burning pawn
killed by a cut leaves one; the inspect text names the pawn; changing the terrain removes it.

## Feature 5 — Turning Tide (`turning-tide`)

**Pitch:** the sky turns cold and grey while you are losing a fight, and opens gold when you turn
it around.

**Strength** (every 250 ticks during an engagement, home maps only): hostile = Σ combatPower ×
health of active hostile pawns; colony = Σ (40 + 4 × best of Shooting/Melee) × health of fighters.
- ratio ≥ 2 for 2 checks → **losing**: the cold sky eases in over ~5 s.
- after losing, ratio ≤ 0.8, or no active hostile left with a fighter standing (checked every 60
  ticks while losing, so it does not wait for the engagement poll) → **gold** for 2500
  ticks (eases in 3 s, fades the last 600 ticks); the cold drops at once.

**Sky:** a `WeatherEvent` (vanilla blends weather events with plain Lerp, while game conditions can
only darken). Cold: steel blue, saturation 0.25, up to 80 %. Gold: warm amber, saturation 1.4, up
to 70 % (screenshots: centre of the screen went from (92,59,31) to (62,58,55) losing and
(103,67,31) after winning). The event's glow is the sky's current glow, so light levels (and gameplay) do not change.
Not during eclipse, aurora or on maps without sky lighting. Nothing saved: after a load the sky is
simply normal.

**Tests:** a quiet map has no hostile strength; 8 centipedes against the colony count as losing;
the cold eases in and a live sky event pulls saturation down; killing them opens the gold sky with
saturation above 1 and the cold gone.

## Feature 6 — Blood-Stained (`blood-stained`)

**Pitch:** after a fight you can tell who was in the thick of it: they come back covered in blood.

**Accumulation** (Pawn.PostApplyDamage postfix): damage whose def `makesBlood`, to a red-blooded
(`Filth_Blood`) pawn that can bleed, adds 0.012 × damage to the victim (if humanlike) and 0.009 ×
damage to a humanlike attacker who struck from the adjacent cell with a non-ranged, non-explosive
blow. Insects, mechanoids and races with other blood never stain anyone (v1: red only).
**Hediff** `Gesta_Bloodstained` (visible, not bad, 0..1): speckled → spattered (0.35) → drenched
(0.7). Fades 0.6/day awake, 1.8/day asleep.
**Drawing:** the hediff's render nodes (body + head) use a copy of vanilla's firefoam overlay: a
tiled splatter texture per stage, masked to the body/head graphic, drawn over apparel with the
vanilla FirefoamOverlay shader. Living, fresh pawns only (corpses do not tick, so a stain would
never fade). The drawer lives on a custom node (`PawnRenderNode_Blood`), rebuilt with the render
tree, so a new body/head never keeps a stale mask. The killing blow counts too (the victim is
already despawned in PostApplyDamage, so positions use PositionHeld). A new stain starts at 0.1.

**Tests:** an adjacent cut stains attacker and victim; a distant shot stains only the target;
cutting a mechanoid stains nobody; the hediff is visible and has render nodes; stages rise with
more blood and cap at 1; it fades. Screenshot: splatter visible on body, clothes and face.

## Feature 7 — Brothers in Arms (`brothers-in-arms`)

**Pitch:** fight side by side often enough and two colonists become brothers (or sisters) in
arms, and wear it.

**Counting:** during an engagement, every free non-slave colonist who deals damage to a hostile
(non-player-faction) pawn is a participant (Pawn.PostApplyDamage postfix). When the engagement ends
normally (not by the 1-day timeout), every pair of living participants gets +1 shared fight
(saved by pawn ids). At 3, the pair is bonded.

**Bond:** reflexive `Gesta_BrotherInArms` relation (label "brother/sister in arms", opinion +25,
importance 55 — below family, so a brother by blood still takes the death thought; generated 0).
Both get the `Gesta_BrotherSash` hediff, which draws a red sash over the clothes (render node,
Graphic_Multi south/north/east, layer 49: above body apparel, under the head). Positive letter.
- A brother going down near the other: social memory "saw X go down" (−4, 1 day).
- A brother dying: vanilla gives `Gesta_BrotherInArmsDied` (−12, family death memory). If the
  survivor has no other living brother in arms, the sash turns black (`Gesta_BrotherSashMourning`).

**Tests:** two shared fights are not enough; three bond both ways with sashes and a letter; a
colonist who missed a fight is not bonded; seeing a brother go down gives the memory; the death
turns the survivor's sash black and gives the grief memory. Screenshot: matching sashes.

## Feature 8 — The Chronicler (`battle-narrator`)

**Pitch:** an offline, Darkest-Dungeon-style narrator: one line across the bottom of the screen at
the decisive moments of a fight, written from the colonist's real history.

**Beats:** first colonist downed in an engagement (FirstDown) · a colonist killed in a fight or by
a hostile (Death) · a colonist kills the enemy's real leader or a boss (Kingslayer) · a colonist
gets back up during a fight (Recovered, `MakeUndowned` postfix) · the engagement ends normally with
at least 2 enemy kills and a fighter standing; the subject is the colonist with most kills
(Victory). Ordinary beats share a cooldown of 5000 ticks; deaths always speak (600 ticks apart).

**Text:** RulePackDefs `Gesta_Chronicle_<Beat>` resolved with GrammarResolver (root `r_line`):
PAWN (and OTHER: the killer / the leader) symbols from `GrammarUtility.RulesForPawn`; constants for
conditional lines — storyteller (Cassandra/Phoebe/Randy tone), kills, timesDowned (counted by the
mod, saved), count, hasLover, veteran (10+ kills), hasOther; number rules [kills] [timesDowned]
[count]. Up to 6 attempts avoid repeating one of the last 10 lines.
**Display:** letterbox band at the bottom (real time, also while paused), italic, ~8 s.

## Feature 9 — The Breaking Blow (`killcam-finale`)

**Pitch:** when the last raider of a big raid falls, the camera finds the shooter, time slows for a
breath, black bars close in and the moment is named.

**Tracking:** `RaidStrategyWorker.MakeLords` postfix stores each raid lord's points. `Lord.Notify_PawnLost`
postfix: when that lord's last pawn is killed or downed by a free colonist, the raid had at least
the configured points (default 1000), the map is a home and no other hostile is still active, and
no finale played in the last half day → finale.
**Finale:** golden tracer from shooter to victim (`FleckMaker.ConnectingLine`, real-time fleck),
positive message "X breaks the attack of Y", and only if that map is on screen: camera pan to the
midpoint (size 12), speed to Normal, 2.2 real seconds at 0.3× (`TickManager.TickRateMultiplier`
postfix), letterbox bars with the caption for 4.5 s. Settings: on/off, camera, slow motion, points.
**Known:** the slow-motion patch is not multiplayer-safe.

## Feature 10 — War Stories (`war-stories`)

**Pitch:** after a battle, veterans retell it in social time, a little bigger every time.

**Story:** colonists who killed hostiles during an engagement that ended normally get a story
(real kills, end tick, the ids of every colonist who fought in it). A new fight replaces it.
**Interaction** `Gesta_RecountsBattle` (weight 0.12 — deep talk is 0.075 — only while the teller has
a fresh story: ≤ 15 days old, told < 6 times, not mid-fight). Each telling adds 1-3 raiders per
previous telling (3… 6… 9…). The vanilla log entry is built by vanilla, so the number is stored by
(teller id, tick) and swapped into the text (token GESTAKILLS) by a postfix on
`PlayLogEntry_Interaction.ToGameStringFromPOV_Worker`. Listener who fought in it: social memory
"that's not how it happened" (−3 opinion, 3 days). Listener who did not: "X's war story" (+3 mood,
1 day) and 2% chance of a random inspiration. Three crossed-swords icons rise over the teller.
Forced interactions (other mods) with no fresh story do nothing.

## Feature 11 — The Champion's Challenge (`champions-challenge`)

**Pitch:** in the middle of a raid, the enemy's champion plants a banner between the lines and
calls for single combat. Send someone into the circle and the battle stops to watch.

**Roll** (every 250 ticks, one roll per lord, ever — `rolledLords` saved by `Lord.loadID`):
the lord is on a player home map, its current toil is exactly `LordToil_AssaultColony` and it has
been in it for ≥ 600 ticks (`Lord.ticksInToil`); its faction is hostile, not the player's and
`humanlikeFaction`; it has ≥ 4 able humanlike raiders (spawned, not downed, not in a mental state,
violence-capable); the colony has a fighter; no challenge is active on that map. Raids that fail
the permanent checks (animals, mechs, insects, fewer than 4 raiders) are marked rolled. A raid
with a raider in an aggro mental state waits. Then `Rand.Chance(championChance)` (setting, default
0.25).
**Champion:** the faction's real leader if it is in the lord and able; otherwise the best
`MeleeDPS` among able raiders holding a melee weapon or nothing; otherwise the best of all.
**Banner:** `Gesta_ChallengeBanner` (passable, unowned building; it can be damaged but not burned: flammability 0) at a random
cell within 6 of the midpoint between the champion and the nearest colonist fighter (then 0.35,
0.65 and 0.2 of the way if that ground is unusable): reachable by the champion, standable,
unfogged, outside zones, with no building, frame, blueprint or item. The champion stands on a
reachable cell next to it. Ring of radius 4 drawn around it while the
challenge lasts. Letter (ThreatSmall).

**Phases** are pawn duties, never new lord toils (`pawn.mindState.duty`, saved with the pawn):
- CHALLENGE: champion `Gesta_ChampChallenge` (jogs to a cell next to the banner, then stands facing
  it); every other pawn of the lord `Gesta_ChampHold` (stands still). Standing is a job of its own,
  `Gesta_ChampStand`, not vanilla `Wait`, whose driver melees any adjacent hostile (colonists are not
  threat-disabled, so every passer-by would be hit). Remembered melee threats are cleared too
  (`JobGiver_ReactToCloseMeleeThreat` sits above the duty and keeps one for 400 ticks). Both duties have
  `threatDisabled`: turrets, drafted colonists and the "active threat" checks ignore them.
  Colonists' attack jobs on that raid are ended when the truce starts.
- DUEL: a drafted, violence-capable free colonist within 4 cells of the banner accepts (checked
  every 20 ticks; also when the champion strikes such a colonist, or such a colonist strikes the
  champion — felling him with those first blows is a win). The champion gets `Gesta_ChampDuel` (melee-attacks the challenger, still
  `threatDisabled` so nothing else shoots him); the challenger gets an ordered melee attack on the
  champion, re-issued only when they stand idle while drafted.
- `LordToil_AssaultColony.UpdateAllDuties` (called by vanilla on any down, undown or new pawn)
  overwrites duties: a postfix re-applies the challenge duties while it is active.
- The assault graph's kidnap and steal transitions fire after 300 ticks without harm to the raid
  (`Lord.lastPawnHarmTick`), which a truce would hand them at once: while the challenge lasts that
  tick is kept current (every 20 ticks), so the raid does not start looting or kidnapping mid-truce.

**Outcomes** (each destroys the banner and gives the raid back its duties via
`CurLordToil.UpdateAllDuties()`, unless said otherwise):
| Outcome | When | Effect |
|---|---|---|
| Colonist wins | the challenger felled the champion: the downing or killing damage was theirs, or (no damage: bleeding, pain) they wounded him in the last 600 ticks. Also when a colonist steps into the circle and fells him with the first blows | raid goes to the lord graph's existing `LordToil_PanicFlee` (else its `LordToil_ExitMap`); winner gets `Gesta_ChampionVictory` (+8, 15 days), tale `Gesta_ChampionDuelTale` (winner, champion) and the feat `Gesta_Feat_Champion` (title, golden beam, message: see Feature 2) — if feats are off or already earned, just the golden beam |
| Champion wins | challenger downed or dead | raid resumes |
| Dishonor | after a 90-tick grace (shots already in the air): any violent damage to a pawn of that lord or to the banner by the player's faction — fire counts when it is a direct incendiary hit (the damage has a weapon: molotov, incendiary round), not fire that spread on its own; any melee swing by a colonist at a raider, hit or miss; a raider hitting back at a colonist who provoked it (its `meleeThreat` is that colonist); a psychic shock / insanity / brain-damage lance or a hostile psycast or ability on a raider (or an area one covering a raider); the champion downed by a psychic shock with no damage behind it, or by another colonist's damage. The challenger's own blows on the champion are the duel | every colonist on the map gets `Gesta_ChallengeDishonor` (−5, 8 days); raid resumes |
| Refused | nobody accepts within 2500 ticks (1 in-game hour), or the duel runs out its 1200 ticks with the challenger out of the champion's reach for most of it (kiting) | every colonist on the map gets `Gesta_ChallengeRefused` (−3, 4 days); raid resumes |
| Raiders break it | after the grace, a raider other than the dueling champion hurts a colonist who did not provoke it | raid resumes, no memory |
| Too long | the duel lasts 1200 ticks with the two mostly within reach | raid resumes |
| Aborted | the lord ends or leaves the assault toil (kidnap, steal, give up), the map goes, the champion dies, downs or despawns to anything not listed above, or breaks before the duel, the challenger leaves the map, the banner is destroyed by something other than the colony, or the feature is turned off | raid resumes (if the lord still exists), no memory |

**Engagement:** an active challenge counts as an ongoing fight for the EngagementTracker (held
raiders are not "active threats", which would otherwise end the engagement mid-truce), and the
Turning Tide freezes while it lasts.

**Saved:** the active challenges (lord, champion, challenger, banner, map by reference; phase, start
ticks, stand cell) and the rolled lord ids. Entries whose references are gone are not saved. After
a load, entries with missing references are dropped, and a sweep every 250 ticks gives any
assaulting lord with no challenge whose pawns still carry a challenge duty its normal duties back,
so nothing can stay frozen.

**Not done (cut):** no letterbox or choreographed walk; no buff or penalty for a lost duel; no
dishonor for a raider breaking the truce; combat AI mods (CAI-5000, CE) may ignore the duty think
nodes (unverified).

**Tests (GestaTest ChampionHarness, tick 2600 and 2780):** a 2-raider humanlike assault, a
mechanoid assault and an all-animal assault lord get no challenge (and are marked rolled); a real
1500-point raid gets none in its first seconds, then one: banner, the champion's walk/stand job, the
others holding (`Gesta_ChampStand`) and `ThreatDisabled`, no active threats, ignored by a player
turret, the engagement still active, duties survive a vanilla `UpdateAllDuties`, the state is written
to the save by reference; a drafted colonist in the circle starts the duel with an ordered melee
attack; the challenger's blows do not break the truce; downing the champion sends the raid to
PanicFlee with memory, tale and feat. Raid 2: the champion's blows do not break the truce, the
challenger goes down → raid resumes, and (control) the turret targets its raiders again. Raid 3: an
hour passes → refused memory, raid resumes. Raid 4: the champion dies of other causes → clean abort,
no dishonor. Raid 5: a raider hurts a colonist → raid resumes, no dishonor. Raid 6: the challenge
state is wiped (as after a broken load) → the sweep restores duties and removes the banner (the
sweep runs even with the feature off). Raid 7: the whole raid dies → quiet abort. Then six-pirate
assault lords for the exploits: fire that spread on its own is ignored but a molotov hit is dishonor;
a champion downed by a psychic shock mid-duel is dishonor, not a win; shooting the banner is
dishonor (and it cannot burn); a raider hitting back at a colonist who swung first is dishonor; a
real melee swing at a holder is dishonor; a duel run out of reach is a refusal. Raid 8 stays up
for 180 ticks of real play (holders do not move, the champion walks toward the banner); a shot inside
the grace (the truce restarted for the check) is ignored, then a colonist shooting a holder → dishonor for the whole colony, raid
resumes. If vanilla switches raid 8 to kidnapping or stealing on its own, stage B checks the clean
abort instead.

## Feature 12 — Trash Talk (`trash-talk`)

**Pitch:** before the shooting starts, someone on the wall shouts across the field at the raiders,
and the raiders shout back. A good insult rattles them; one too many and the weakest of them
breaks and runs. A bad one gets the whole raid laughing.

**Scan** (GameComponent `TrashTalk`, every 250 ticks at offset 131; at most one exchange per map
per scan and none within 600 ticks of the last one on that map), for each lord on a player home map:
- the lord's faction is not the player's, is hostile to the player and `humanlikeFaction` (so
  mechanoid, insect, entity and manhunter lords never qualify);
- its current toil is `LordToil_Stage`, `LordToil_Siege`, `LordToil_AssaultColony`,
  `LordToil_AssaultColonyBreaching` or `LordToil_AssaultColonySappers` (a raid that is coming, not
  one that is fleeing, kidnapping or leaving);
- no Champion's Challenge is active for that lord (the truce has its own drama);
- its trash talk is not over (fewer than 3 exchanges, nobody broke yet) and its last exchange was
  ≥ 1200 ticks ago;
- then `Rand.Chance(0.4)`.
A lord that passes picks the pair: candidate taunters sorted by taunt power (highest first); the
first one with a valid target speaks. The target is the nearest valid raider of that lord.

**Taunter** (colonist): free non-slave colonist, not a quest lodger, humanlike adult, spawned on
that map, not downed, not in a mental state, awake, can talk (`Talking`), not a mutant, Social not
disabled, and **qualifies**: Social ≥ 8, or the trait Abrasive, Bloodlust or Brawler. Drafted or
not does not matter. Each colonist speaks at most once every 2500 ticks (1 in-game hour).
**Target** (raider): an owned pawn of the lord, humanlike adult, spawned on the map, not fogged,
not downed, not in a mental state, awake, can hear (`Hearing`), not a mutant, not a prisoner, not
threat-disabled. It replies only if it can talk.
**Pair:** distance 8..40 cells (vanilla's insult needs ≤ 6: this one is shouted across the field),
`GenSight.LineOfSight` between them, and neither is in melee: no standing hostile pawn on an
adjacent cell and not running an `AttackMelee` job.

**Taunt power** P = Social level + 4 (Abrasive) + 2 (Bloodlust or Brawler; at most once).
**Outcome** (rolled at the shout):
- backfire chance = clamp(0.45 − 0.035·P, 0.05, 0.45) (P 0 → 45 %, 4 → 31 %, 8 → 17 %, 12+ → 5 %);
- **hit:** the lord's morale (starts at 100) drops by 15 + 2.5·P (P 8 → 35, 12 → 45, 20 → 65);
- **backfire:** morale +15 (max 100), the taunter gets `Gesta_TauntBackfired` (−3 mood, 1 day,
  "they laughed at me"), and the raider's reply mocks them;
- **break:** a hit that takes morale to 0 or below makes a raider flee: the target if he is "weak"
  (his kind's combatPower ≤ the median of the lord's able raiders), else the weakest able raider;
  never the faction leader or a boss kind. It is vanilla's `PanicFlee` mental state (not forced: a
  hediff that blocks mental breaks protects him), so he runs for the map edge and stops counting as a
  threat. Message (positive) "X breaks under Y's taunts and runs." The lord's trash talk ends there.
  If nobody can be made to flee, it counts as a plain hit and the talk ends anyway.
With 3 exchanges per raid, a Social 20 abrasive colonist breaks someone on the 2nd taunt, Social 12
on the 3rd, an ordinary Social 8 colonist almost never alone.
The taunter gains 10 Social XP per exchange.

**What the player sees:**
- the vanilla interaction bubble (insult icon, arrow pointing at the target) over the speaker;
- the words themselves in a readable speech box above the speaker's head (dark box, Small font,
  gold border for colonists, red for raiders; 5 real seconds, frozen while paused; current map
  only; setting "show the shouted words"). Lines come from RulePackDefs
  `Gesta_TrashTalk_Shouts` / `_Retorts` / `_Mockery` (root `r_shout`, SPEAKER and TARGET pawn
  symbols, constants `tribal`, `targetLeader`);
- 120 ticks later the raider's reply (bubble + words), unless he broke, went down or can't talk;
- play log entries (`PlayLogEntry_Interaction`, shown in both pawns' log tabs): the taunt
  (`Gesta_TrashTalk`, colonist → raider, with the outcome as an extra sentence pack
  `Gesta_TrashTalk_Hit` / `_Backfire` / `_Break`) and the reply (`Gesta_TrashTalkReply` or
  `Gesta_TrashTalkMock`, raider → colonist). They use the real names, kind and faction.
The InteractionDefs use the base `InteractionWorker` (weight 0), so they never happen in normal
social time.

**Saved:** per lord (`Lord.loadID`): morale, exchanges, last exchange tick, broke; per colonist
(`thingIDNumber`): last taunt tick. States of lords that no longer exist are pruned every 2500 ticks.
Not saved: pending replies and the speech boxes (a reply due at save time is simply skipped).
**Turning it off** stops the scan and drops pending replies and boxes.

**Not done (cut):** no raider-initiated taunts (they only answer); no effect on mood of the raid
beyond the one fleeing raider; no taunts on caravan/non-home maps; Combat Extended irrelevant.

**Tests (GestaTest TrashTalkHarness, ticks 3000–3700):** a real raid lord (ImmediateAttack) with its
raiders teleported 20 cells from a Social 20 abrasive colonist in the open: the feature off gives
no exchange; the real scan makes one: play log entry with both real names, a speech box and a
vanilla bubble on the speaker, morale 100 → 25; a second scan right away does nothing (lord
cooldown); 120 ticks later the reply entry and box exist; the second taunt breaks the weakest raider
(PanicFlee, not the leader), message, talk over, a further attempt refused. Pair rules: too close
(5), too far (50), a wall in between (no LOS), an adjacent hostile (melee) are all refused; the
same colonist again is refused (cooldown); a pawn with neither Social 8 nor a trait does not qualify.
Backfire: a Social 0 brawler with a forced low roll → memory, morale back up, a mocking reply. Max 3
exchanges per lord. Negative: a mechanoid raid lord, a manhunter animal, and a prisoner are never
part of an exchange. Live: a StageThenAttack raid staging with a colonist in sight gets an exchange
from the normal ticking scan.
