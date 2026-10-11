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

**Recording** (Pawn.Kill postfix with the KillContext): the victim is humanlike, the killer is a
pawn (a combat death), the death cell is in bounds, unroofed, with fertility ≥ 0.5. Stored per map
(MapComponent `PoppyField`): cell, colonist?, victim name, victim faction name, killer name, weapon
label, death tick, and the living colonists who loved the victim (opinion ≥ 20 or any relation;
only for colonist victims). A new record is skipped if a poppy or record already exists within 1
cell (killboxes); at most 80 poppies + records per map.

**Blooming:** every 2500 ticks, records at least 3 days old bloom if it is growing season there.
Target cell: the death cell or the nearest valid cell within 2 (in bounds, unroofed, fertile, no
edifice, not in a growing zone, no tree). An existing small wild plant is cleared. A record that
cannot bloom within 60 days is dropped.

**The plant** (`Gesta.Plant_Poppy`, defs `Gesta_PoppyRed` / `Gesta_PoppyWhite`): decorative, not
sowable, not harvestable, not edible, not auto-cut, never blighted, never dies of age or light; it
can still burn or be cut by the player. Beauty +8. Label "red poppy (Mía)". Inspect: "Here fell
Mía, killed by Kerr with a bolt-action rifle, 12th of Aprimay 5502."
**Mourners:** every long tick, a mourner within 4 cells gets `Gesta_VisitedPoppy` (+3, 1 day).

**Tests:** a colonist killed by a hostile on fertile open ground creates a record with the right
names; a death under a roof does not; a second death in the adjacent cell is merged; forcing the
record's age blooms a red poppy at the cell with the victim's name in its label and inspect text;
an enemy's poppy is white; the poppy is not ingestible and does not die of age; a mourner standing
next to it gets the memory.
