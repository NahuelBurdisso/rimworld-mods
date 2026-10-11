using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Gesta
{
    public enum ChallengePhase { Challenge, Duel }

    public enum ChallengeOutcome { None, ColonistWon, ChampionWon, BrokenByColony, BrokenByRaiders, Refused, DuelTooLong, Aborted }

    /// <summary>One champion's challenge in progress. The phases live in the pawns' duties.</summary>
    public class Challenge : IExposable
    {
        public Lord lord;
        public Map map;
        public Pawn champion;
        public Pawn challenger;
        public Thing banner;
        public IntVec3 standCell = IntVec3.Invalid;
        public ChallengePhase phase;
        public int startTick;
        public int duelStartTick = -1;

        // not saved: set from damage hooks, acted on in the next tick
        public ChallengeOutcome pending;
        public Thing breaker;
        public Pawn pendingChallenger;
        /// <summary>What downed or killed the champion (null instigator: no damage, e.g. a shock lance).</summary>
        public bool fellRecorded;
        public Thing fellTo;
        public int lastTakerHitTick = -1;

        // saved: how much of the duel the challenger spent within reach of the champion
        public int duelChecks, duelChecksInReach;

        public void ExposeData()
        {
            Scribe_References.Look(ref lord, "lord");
            Scribe_References.Look(ref map, "map");
            Scribe_References.Look(ref champion, "champion");
            Scribe_References.Look(ref challenger, "challenger");
            Scribe_References.Look(ref banner, "banner");
            Scribe_Values.Look(ref standCell, "standCell", IntVec3.Invalid);
            Scribe_Values.Look(ref phase, "phase", ChallengePhase.Challenge);
            Scribe_Values.Look(ref startTick, "startTick", 0);
            Scribe_Values.Look(ref duelStartTick, "duelStartTick", -1);
            Scribe_Values.Look(ref duelChecks, "duelChecks", 0);
            Scribe_Values.Look(ref duelChecksInReach, "duelChecksInReach", 0);
        }
    }

    /// <summary>
    /// The Champion's Challenge: a raid's champion plants a banner and calls for single combat; the
    /// rest of the raid holds under a truce. See DESIGN.md "Feature 11".
    /// </summary>
    public class ChampionsChallenge : GameComponent
    {
        public const int MinAssaultTicks = 600, MinRaiders = 4, AcceptTicks = 2500, DuelTicks = 1200, GraceTicks = 90, TakerWoundTicks = 600;
        public const int RollInterval = 250, ProcessInterval = 20;
        public const float CircleRadius = 4f, BannerSearchRadius = 6f;
        static readonly Color RingColor = new Color(1f, 0.82f, 0.35f);

        List<Challenge> active = new List<Challenge>();
        List<int> rolledLords = new List<int>();
        List<Challenge> saveList;

        public ChallengeOutcome LastOutcome { get; private set; }
        public Challenge LastEnded { get; private set; }

        static ChampionsChallenge instance;
        public static ChampionsChallenge Instance => instance;
        public ChampionsChallenge(Game game) { instance = this; }

        public IReadOnlyList<Challenge> Active => active;

        public static bool IsChallengeDuty(DutyDef d) =>
            d != null && (d == GestaDefOf.Gesta_ChampHold || d == GestaDefOf.Gesta_ChampChallenge || d == GestaDefOf.Gesta_ChampDuel);

        public static bool ActiveOn(Map map)
        {
            if (instance == null || map == null) return false;
            List<Challenge> a = instance.active;
            for (int i = 0; i < a.Count; i++) if (a[i].map == map) return true;
            return false;
        }

        public Challenge For(Lord lord)
        {
            if (lord == null) return null;
            for (int i = 0; i < active.Count; i++) if (active[i].lord == lord) return active[i];
            return null;
        }

        public Challenge ForBanner(Thing banner)
        {
            for (int i = 0; i < active.Count; i++) if (active[i].banner == banner) return active[i];
            return null;
        }

        public bool WasRolled(Lord lord) => lord != null && rolledLords.Contains(lord.loadID);

        // ---------------------------------------------------------------- ticking

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (!GestaMod.Settings.championsChallenge)
            {
                if (active.Count > 0) AbortAll("feature turned off");
                if (now % RollInterval == 17) SweepOrphans();   // never leave a raid frozen, on or off
                return;
            }
            bool process = now % ProcessInterval == 0;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (i >= active.Count) continue;
                Challenge c = active[i];
                if (process || c.pending != ChallengeOutcome.None || c.pendingChallenger != null) Process(c, now);
            }
            if (now % RollInterval == 17)
            {
                RollAll(now);
                SweepOrphans();
            }
        }

        /// <summary>For tests: roll every eligible lord right now.</summary>
        public void RollNow() => RollAll(Find.TickManager.TicksGame);

        /// <summary>For tests: process every challenge right now.</summary>
        public void ProcessNow()
        {
            int now = Find.TickManager.TicksGame;
            for (int i = active.Count - 1; i >= 0; i--) if (i < active.Count) Process(active[i], now);
        }

        /// <summary>For tests: forget every challenge without restoring anything, as a broken load would.</summary>
        public void DebugForgetAll() => active.Clear();

        /// <summary>For tests: run the orphan sweep now.</summary>
        public int SweepNow() => SweepOrphans();

        void Process(Challenge c, int now)
        {
            if (c.map == null || c.map.Disposed || !Find.Maps.Contains(c.map))
            {
                Drop(c, "map gone");
                return;
            }
            if (c.pending != ChallengeOutcome.None)
            {
                End(c, c.pending);
                return;
            }
            // the champion is down or dead: who felled him decides it
            if (c.champion != null && (c.champion.Dead || c.champion.Downed))
            {
                Pawn taker = c.phase == ChallengePhase.Duel ? c.challenger : c.pendingChallenger;
                Thing by = FelledBy(c, taker, now, out bool shocked);
                if (shocked && now - c.startTick >= GraceTicks)
                {
                    // a psychic shock with no damage behind it: a shock lance or psycast, the colony's doing
                    c.breaker ??= PlayerStandIn(c, taker);
                    End(c, ChallengeOutcome.BrokenByColony);
                }
                else if (taker != null && by == taker)
                {
                    // in the duel, or stepping into the circle and felling him with the first blows
                    c.challenger = taker;
                    c.phase = ChallengePhase.Duel;
                    End(c, ChallengeOutcome.ColonistWon);
                }
                else if (by != null && by.Faction == Faction.OfPlayer && now - c.startTick >= GraceTicks)
                {
                    c.breaker = by;
                    End(c, ChallengeOutcome.BrokenByColony);
                }
                else End(c, ChallengeOutcome.Aborted, "the champion fell to something else");
                return;
            }
            if (c.phase == ChallengePhase.Duel && (c.challenger == null || c.challenger.Dead || c.challenger.Downed))
            {
                End(c, ChallengeOutcome.ChampionWon);
                return;
            }
            if (!LordValid(c)) { End(c, ChallengeOutcome.Aborted, "the raid changed plans or ended"); return; }
            // vanilla's kidnap and steal transitions wait for 300 quiet ticks: a truce is not quiet
            c.lord.lastPawnHarmTick = now;
            if (c.banner == null || c.banner.Destroyed || !c.banner.Spawned) { End(c, ChallengeOutcome.Aborted, "banner gone"); return; }
            Pawn champ = c.champion;
            if (champ == null || champ.Dead || !champ.Spawned || champ.Map != c.map) { End(c, ChallengeOutcome.Aborted, "champion gone"); return; }

            if (c.phase == ChallengePhase.Challenge)
            {
                if (champ.InMentalState || champ.GetLord() != c.lord) { End(c, ChallengeOutcome.Aborted, "champion can no longer fight"); return; }
                Pawn taker = c.pendingChallenger;
                c.pendingChallenger = null;
                if (taker == null || !CanAccept(c, taker)) taker = FindChallenger(c);
                if (taker != null) { StartDuel(c, taker, now); return; }
                if (now - c.startTick > AcceptTicks) { End(c, ChallengeOutcome.Refused); return; }
            }
            else
            {
                c.pendingChallenger = null;
                if (!c.challenger.Spawned || c.challenger.Map != c.map) { End(c, ChallengeOutcome.Aborted, "challenger left"); return; }
                c.duelChecks++;
                if (c.challenger.Position.AdjacentTo8WayOrInside(champ.Position)) c.duelChecksInReach++;
                if (now - c.duelStartTick > DuelTicks)
                {
                    // kiting the champion to run out the clock is refusing the fight
                    End(c, c.duelChecksInReach * 2 < c.duelChecks ? ChallengeOutcome.Refused : ChallengeOutcome.DuelTooLong);
                    return;
                }
                KeepChallengerFighting(c);
            }
        }

        /// <summary>
        /// Who brought the champion down: the instigator of the downing or killing damage. With no
        /// damage behind it: a psychic shock is reported through <paramref name="shocked"/>; bleeding or
        /// pain counts for the challenger only if they wounded him recently.
        /// </summary>
        static Thing FelledBy(Challenge c, Pawn taker, int now, out bool shocked)
        {
            shocked = false;
            Thing by = c.fellRecorded ? c.fellTo : LastAttackers.Peek(c.champion);
            if (by != null) return by;
            if (HasPsychicShock(c.champion)) { shocked = true; return null; }
            if (taker != null && c.lastTakerHitTick >= 0 && now - c.lastTakerHitTick <= TakerWoundTicks) return taker;
            return null;
        }

        /// <summary>A colonist on the map, so a "broken by the colony" ending has someone to name.</summary>
        static Thing PlayerStandIn(Challenge c, Pawn notThis)
        {
            List<Pawn> cs = c.map?.mapPawns.FreeColonistsSpawned;
            if (cs == null) return null;
            for (int i = 0; i < cs.Count; i++) if (cs[i] != notThis) return cs[i];
            return notThis;
        }

        static bool HasPsychicShock(Pawn p)
        {
            HediffSet h = p?.health?.hediffSet;
            if (h == null) return false;
            return (HediffDefOf.PsychicShock != null && h.HasHediff(HediffDefOf.PsychicShock))
                   || (HediffDefOf.DarkPsychicShock != null && h.HasHediff(HediffDefOf.DarkPsychicShock));
        }

        static bool LordValid(Challenge c) =>
            c.lord != null && c.map != null && !c.map.Disposed && c.map.lordManager.lords.Contains(c.lord)
            && c.lord.CurLordToil is LordToil_AssaultColony;

        // ---------------------------------------------------------------- rolling

        void RollAll(int now)
        {
            if (!GestaMod.Settings.championsChallenge) return;
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (!CombatUtil.IsPlayerHome(map) || ActiveOn(map)) continue;
                List<Lord> lords = map.lordManager.lords;
                for (int i = 0; i < lords.Count; i++)
                {
                    Lord lord = lords[i];
                    if (rolledLords.Contains(lord.loadID)) continue;
                    if (!(lord.CurLordToil is LordToil_AssaultColony) || lord.ticksInToil < MinAssaultTicks) continue;
                    if (!PermanentlyEligible(lord))
                    {
                        MarkRolled(lord);
                        continue;
                    }
                    if (!EligibleNow(lord)) continue;
                    MarkRolled(lord);
                    if (!Rand.Chance(GestaMod.Settings.championChance)) continue;
                    if (TryStart(lord, now)) break;
                }
            }
        }

        void MarkRolled(Lord lord)
        {
            if (rolledLords.Count > 200) PruneRolled();
            rolledLords.Add(lord.loadID);
        }

        void PruneRolled()
        {
            var live = new HashSet<int>();
            foreach (Map map in Find.Maps)
                foreach (Lord l in map.lordManager.lords) live.Add(l.loadID);
            rolledLords.RemoveAll(id => !live.Contains(id));
        }

        /// <summary>Faction and size: a raid that fails these never qualifies.</summary>
        public static bool PermanentlyEligible(Lord lord)
        {
            Faction f = lord.faction;
            if (f == null || f.IsPlayer || !f.HostileTo(Faction.OfPlayer) || f.def == null || !f.def.humanlikeFaction) return false;
            int able = 0;
            for (int i = 0; i < lord.ownedPawns.Count; i++) if (CanChampion(lord.ownedPawns[i])) able++;
            return able >= MinRaiders;
        }

        /// <summary>Things that may change: a berserk raider, no colonist able to fight.</summary>
        static bool EligibleNow(Lord lord)
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
                if (lord.ownedPawns[i].InAggroMentalState) return false;
            return CombatUtil.CountFighters(lord.Map) > 0;
        }

        public static bool CanChampion(Pawn p) =>
            p != null && p.Spawned && !p.Dead && !p.Downed && p.RaceProps.Humanlike && p.mindState != null
            && !p.InMentalState && !p.WorkTagIsDisabled(WorkTags.Violent);

        public static Pawn PickChampion(Lord lord)
        {
            Pawn leader = lord.faction?.leader;
            if (leader != null && lord.ownedPawns.Contains(leader) && CanChampion(leader)) return leader;
            Pawn bestMelee = null, bestAny = null;
            float bestMeleeDps = -1f, bestAnyDps = -1f;
            foreach (Pawn p in lord.ownedPawns)
            {
                if (!CanChampion(p)) continue;
                float dps = p.GetStatValue(StatDefOf.MeleeDPS);
                ThingWithComps primary = p.equipment?.Primary;
                if ((primary == null || primary.def.IsMeleeWeapon) && dps > bestMeleeDps) { bestMelee = p; bestMeleeDps = dps; }
                if (dps > bestAnyDps) { bestAny = p; bestAnyDps = dps; }
            }
            return bestMelee ?? bestAny;
        }

        /// <summary>Plants the banner and declares the truce. Public for tests (skips the roll, not the checks).</summary>
        public bool TryStart(Lord lord, int now)
        {
            Map map = lord.Map;
            if (map == null || ActiveOn(map) || For(lord) != null || !(lord.CurLordToil is LordToil_AssaultColony)) return false;
            Pawn champ = PickChampion(lord);
            if (champ == null) return false;
            Pawn nearest = null;
            float bestD = float.MaxValue;
            foreach (Pawn p in CombatUtil.Fighters(map))
            {
                float d = (p.Position - champ.Position).LengthHorizontalSquared;
                if (d < bestD) { bestD = d; nearest = p; }
            }
            if (nearest == null) return false;
            // the midpoint first; if that ground is unusable (fogged, walled), a little nearer either side
            IntVec3 cell = IntVec3.Invalid;
            foreach (float f in BannerFractions)
            {
                IntVec3 at = new IntVec3(Mathf.RoundToInt(Mathf.Lerp(champ.Position.x, nearest.Position.x, f)), 0,
                                         Mathf.RoundToInt(Mathf.Lerp(champ.Position.z, nearest.Position.z, f)));
                if (CellFinder.TryFindRandomCellNear(at, map, (int)BannerSearchRadius, c => GoodBannerCell(c, map, champ), out cell, 100)) break;
                cell = IntVec3.Invalid;
            }
            if (!cell.IsValid) return false;

            Thing banner = GenSpawn.Spawn(ThingMaker.MakeThing(GestaDefOf.Gesta_ChallengeBanner), cell, map);
            var ch = new Challenge
            {
                lord = lord,
                map = map,
                champion = champ,
                banner = banner,
                standCell = StandCellNear(cell, map, champ),
                phase = ChallengePhase.Challenge,
                startTick = now,
            };
            active.Add(ch);
            lord.lastPawnHarmTick = now;
            if (!rolledLords.Contains(lord.loadID)) MarkRolled(lord);
            ApplyDuties(ch);
            StopColonistAttacks(map, lord);

            string factionName = lord.faction?.Name ?? "Gesta_Killcam_TheEnemy".Translate().ToString();
            Find.LetterStack.ReceiveLetter("Gesta_Champion_LetterLabel".Translate(),
                "Gesta_Champion_LetterText".Translate(champ.LabelShort, factionName),
                LetterDefOf.ThreatSmall, new LookTargets(banner), lord.faction);
            Log.Message($"[Gesta] champion's challenge: {champ.LabelShort} of {factionName} at {cell}");
            return true;
        }

        static readonly float[] BannerFractions = { 0.5f, 0.35f, 0.65f, 0.2f };

        static bool GoodBannerCell(IntVec3 c, Map map, Pawn champ)
        {
            if (!c.InBounds(map) || !c.Standable(map) || c.Fogged(map) || c.GetZone(map) != null) return false;
            List<Thing> things = c.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                // open ground only: no building, frame, blueprint or item under the banner
                Thing t = things[i];
                if (t is Building || t is Blueprint || t.def.category == ThingCategory.Item) return false;
            }
            return champ.CanReach(c, PathEndMode.OnCell, Danger.Deadly);
        }

        static IntVec3 StandCellNear(IntVec3 banner, Map map, Pawn champ)
        {
            IntVec3 best = IntVec3.Invalid;
            float bestD = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                IntVec3 c = banner + GenAdj.AdjacentCells[i];
                if (!c.InBounds(map) || !c.Standable(map) || c.GetEdifice(map) != null || !champ.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) continue;
                float d = (c - champ.Position).LengthHorizontalSquared;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best.IsValid ? best : banner;
        }

        static void StopColonistAttacks(Map map, Lord lord)
        {
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                Job j = p.CurJob;
                if (j == null || (j.def != JobDefOf.AttackMelee && j.def != JobDefOf.AttackStatic)) continue;
                if (j.targetA.Thing is Pawn t && t.GetLord() == lord) p.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        // ---------------------------------------------------------------- duties

        /// <summary>Gives every pawn of the raid its challenge duty (skips pawns that already have it).</summary>
        void ApplyDuties(Challenge c)
        {
            if (c.lord == null) return;
            List<Pawn> pawns = c.lord.ownedPawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.mindState == null) continue;
                PawnDuty want;
                if (p == c.champion)
                {
                    if (c.phase == ChallengePhase.Duel)
                    {
                        if (p.mindState.duty?.def == GestaDefOf.Gesta_ChampDuel && p.mindState.duty.focus.Thing == c.challenger) continue;
                        want = new PawnDuty(GestaDefOf.Gesta_ChampDuel, c.challenger);
                    }
                    else
                    {
                        if (p.mindState.duty?.def == GestaDefOf.Gesta_ChampChallenge) continue;
                        want = new PawnDuty(GestaDefOf.Gesta_ChampChallenge, c.banner.Position, c.standCell, 1.5f);
                    }
                }
                else
                {
                    if (p.mindState.duty?.def == GestaDefOf.Gesta_ChampHold) continue;
                    want = new PawnDuty(GestaDefOf.Gesta_ChampHold);
                }
                p.mindState.duty = want;
                // JobGiver_ReactToCloseMeleeThreat sits above the duty and remembers a threat for 400 ticks
                p.mindState.meleeThreat = null;
                if (p.Spawned && p.jobs != null) p.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        /// <summary>LordToil_AssaultColony.UpdateAllDuties postfix: vanilla just overwrote our duties.</summary>
        public void Notify_DutiesUpdated(Lord lord)
        {
            Challenge c = For(lord);
            if (c == null || c.banner == null || c.banner.Destroyed) return;
            ApplyDuties(c);
        }

        /// <summary>Gives the raid its normal duties back. Call after removing the challenge from the list.</summary>
        static void Restore(Challenge c)
        {
            if (LordValid(c))
            {
                c.lord.CurLordToil.UpdateAllDuties();
            }
            else if (c.lord != null && c.map != null && !c.map.Disposed && c.map.lordManager.lords.Contains(c.lord))
            {
                // another toil owns the raid now; it set its own duties on entry, this only catches leftovers
                bool leftover = false;
                foreach (Pawn p in c.lord.ownedPawns) if (IsChallengeDuty(p.mindState?.duty?.def)) leftover = true;
                if (leftover) c.lord.CurLordToil?.UpdateAllDuties();
                foreach (Pawn p in c.lord.ownedPawns.ToArray()) ClearDuty(p);   // ending a job must not trip the loop
            }
            ClearDuty(c.champion);
        }

        static void ClearDuty(Pawn p)
        {
            if (p?.mindState == null || !IsChallengeDuty(p.mindState.duty?.def)) return;
            p.mindState.duty = null;
            if (p.Spawned && p.jobs != null) p.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        /// <summary>Assaulting raids whose pawns still carry a challenge duty with no challenge behind it.</summary>
        int SweepOrphans()
        {
            int fixedLords = 0;
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                // a banner whose challenge did not survive a load
                List<Thing> banners = maps[m].listerThings.ThingsOfDef(GestaDefOf.Gesta_ChallengeBanner);
                for (int b = banners.Count - 1; b >= 0; b--)
                    if (b < banners.Count && ForBanner(banners[b]) == null) banners[b].Destroy(DestroyMode.Vanish);

                List<Lord> lords = maps[m].lordManager.lords;
                for (int i = 0; i < lords.Count; i++)
                {
                    Lord lord = lords[i];
                    if (!(lord.CurLordToil is LordToil_AssaultColony) || For(lord) != null) continue;
                    List<Pawn> pawns = lord.ownedPawns;
                    for (int j = 0; j < pawns.Count; j++)
                    {
                        if (!IsChallengeDuty(pawns[j].mindState?.duty?.def)) continue;
                        lord.CurLordToil.UpdateAllDuties();
                        fixedLords++;
                        Log.Message($"[Gesta] champion's challenge: restored the duties of an orphaned raid ({lord.faction?.Name})");
                        break;
                    }
                }
            }
            return fixedLords;
        }

        // ---------------------------------------------------------------- the duel

        public bool CanAccept(Challenge c, Pawn p) =>
            p != null && p.Spawned && p.Map == c.map && p.Drafted && p.IsFreeNonSlaveColonist && CombatUtil.IsFighter(p)
            && !p.InMentalState && c.banner != null && c.banner.Spawned && p.Position.InHorDistOf(c.banner.Position, CircleRadius);

        Pawn FindChallenger(Challenge c)
        {
            Pawn best = null;
            float bestD = float.MaxValue;
            foreach (Pawn p in c.map.mapPawns.FreeColonistsSpawned)
            {
                if (!CanAccept(c, p)) continue;
                float d = (p.Position - c.banner.Position).LengthHorizontalSquared;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        void StartDuel(Challenge c, Pawn taker, int now)
        {
            c.phase = ChallengePhase.Duel;
            c.challenger = taker;
            c.duelStartTick = now;
            ApplyDuties(c);
            OrderAttack(taker, c.champion);
            Messages.Message("Gesta_Champion_Accepted".Translate(taker.LabelShort, c.champion.LabelShort),
                new LookTargets(taker, c.champion), MessageTypeDefOf.NeutralEvent);
            Log.Message($"[Gesta] champion's challenge: {taker.LabelShort} accepts, duel against {c.champion.LabelShort}");
        }

        static void OrderAttack(Pawn taker, Pawn champ)
        {
            Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, champ);
            job.killIncappedTarget = false;
            taker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        /// <summary>The champion is threat-disabled, so an idle drafted challenger would not swing back on its own.</summary>
        static void KeepChallengerFighting(Challenge c)
        {
            Pawn p = c.challenger;
            if (!p.Drafted || p.InMentalState) return;
            Job j = p.CurJob;
            if (j != null && j.def != JobDefOf.Wait_Combat && j.def != JobDefOf.Wait) return;
            OrderAttack(p, c.champion);
        }

        // ---------------------------------------------------------------- truce

        /// <summary>Lord.Notify_PawnDamaged postfix: a pawn of a raid took damage (before it is applied).</summary>
        public void Notify_RaiderDamaged(Lord lord, Pawn victim, DamageInfo dinfo)
        {
            Challenge c = For(lord);
            if (c == null || c.pending != ChallengeOutcome.None) return;
            if (!CountsAsViolence(dinfo, victim)) return;
            Thing inst = dinfo.Instigator;
            if (inst == null || inst.Faction != Faction.OfPlayer) return;
            int now = Find.TickManager.TicksGame;
            if (victim == c.champion && inst is Pawn hitter)
            {
                if (c.phase == ChallengePhase.Duel && hitter == c.challenger) { c.lastTakerHitTick = now; return; }
                // stepping into the circle and striking first is accepting
                if (c.phase == ChallengePhase.Challenge && CanAccept(c, hitter)) { c.pendingChallenger = hitter; c.lastTakerHitTick = now; return; }
            }
            Break(c, inst);
        }

        /// <summary>
        /// Violence for the truce. Fire that spread on its own (Fire.DoFireDamage: no weapon) does not
        /// count; a direct incendiary hit (molotov, incendiary round: the weapon is set) does.
        /// </summary>
        static bool CountsAsViolence(DamageInfo dinfo, Thing victim)
        {
            DamageDef def = dinfo.Def;
            if (def == null || def.consideredHelpful || !def.ExternalViolenceFor(victim)) return false;
            if ((def == DamageDefOf.Flame || def == DamageDefOf.Burn) && dinfo.Weapon == null) return false;
            return true;
        }

        /// <summary>The colony broke the truce (after a short grace for shots already in the air).</summary>
        void Break(Challenge c, Thing by)
        {
            if (c.pending != ChallengeOutcome.None || Find.TickManager.TicksGame - c.startTick < GraceTicks) return;
            c.pending = ChallengeOutcome.BrokenByColony;
            c.breaker = by;
        }

        /// <summary>The challenge a raider belongs to (by lord, or as its champion).</summary>
        Challenge OfRaider(Pawn p)
        {
            if (p == null) return null;
            Challenge c = For(p.GetLord());
            if (c != null) return c;
            for (int i = 0; i < active.Count; i++) if (active[i].champion == p) return active[i];
            return null;
        }

        /// <summary>Verb_MeleeAttack.TryCastShot prefix: a swing, hit or miss (a miss provokes too).</summary>
        public void Notify_MeleeSwing(Pawn attacker, Pawn target)
        {
            if (attacker?.Faction != Faction.OfPlayer) return;
            Challenge c = OfRaider(target);
            if (c == null) return;
            if (target == c.champion)
            {
                if (c.phase == ChallengePhase.Duel && attacker == c.challenger) return;
                if (c.phase == ChallengePhase.Challenge && CanAccept(c, attacker)) { c.pendingChallenger = attacker; return; }
            }
            Break(c, attacker);
        }

        /// <summary>A hostile item or psycast used by a colonist on a raider under the truce.</summary>
        public void Notify_HostileAct(Pawn actor, Pawn target)
        {
            if (actor?.Faction != Faction.OfPlayer) return;
            Challenge c = OfRaider(target);
            if (c != null) Break(c, actor);
        }

        /// <summary>Hostile psycasts and abilities aimed at an area: every challenge with a raider inside it.</summary>
        public void Notify_HostileArea(Pawn actor, IntVec3 cell, float radius)
        {
            if (actor?.Faction != Faction.OfPlayer) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Challenge c = active[i];
                if (c.map != actor.Map || c.lord == null) continue;
                foreach (Pawn p in c.lord.ownedPawns)
                    if (p.Spawned && p.Position.InHorDistOf(cell, radius)) { Break(c, actor); break; }
            }
        }

        /// <summary>Building_ChallengeBanner.PreApplyDamage: the banner is part of the truce.</summary>
        public void Notify_BannerDamaged(Thing banner, DamageInfo dinfo)
        {
            Challenge c = ForBanner(banner);
            if (c == null || !CountsAsViolence(dinfo, banner)) return;
            Thing inst = dinfo.Instigator;
            if (inst != null && inst.Faction == Faction.OfPlayer) Break(c, inst);
        }

        /// <summary>Pawn_HealthTracker.MakeDowned postfix and Pawn.Kill prefix: remember what felled a champion.</summary>
        public void Notify_PawnFell(Pawn p, DamageInfo? dinfo)
        {
            for (int i = 0; i < active.Count; i++)
            {
                Challenge c = active[i];
                if (c.champion != p || c.fellRecorded) continue;
                c.fellRecorded = true;
                // fire that spread on its own names nobody, as for the truce itself
                c.fellTo = dinfo.HasValue && CountsAsViolence(dinfo.Value, p) ? dinfo.Value.Instigator : null;
            }
        }

        /// <summary>Pawn.PostApplyDamage postfix: a colonist was hurt by a pawn.</summary>
        public void Notify_ColonistHit(Pawn victim, Pawn attacker)
        {
            if (active.Count == 0 || victim.Faction != Faction.OfPlayer) return;
            Challenge c = attacker == null ? null : For(attacker.GetLord());
            if (c == null)
            {
                for (int i = 0; i < active.Count; i++) if (active[i].champion == attacker) c = active[i];
            }
            if (c == null || c.pending != ChallengeOutcome.None) return;
            if (attacker == c.champion)
            {
                if (c.phase == ChallengePhase.Duel && victim == c.challenger) return;
                if (c.phase == ChallengePhase.Challenge && CanAccept(c, victim)) { c.pendingChallenger = victim; return; }
            }
            if (Find.TickManager.TicksGame - c.startTick < GraceTicks) return;
            // a raider hitting back at a colonist who swung at him first: the colonist broke it
            Pawn provoker = attacker.mindState?.meleeThreat;
            if (provoker != null && provoker.Faction == Faction.OfPlayer && !(attacker == c.champion && provoker == c.challenger))
            {
                Break(c, provoker);
                return;
            }
            c.pending = ChallengeOutcome.BrokenByRaiders;
            c.breaker = attacker;
        }

        // ---------------------------------------------------------------- endings

        void AbortAll(string why)
        {
            for (int i = active.Count - 1; i >= 0; i--) if (i < active.Count) End(active[i], ChallengeOutcome.Aborted, why);
        }

        /// <summary>No map left to fix: forget it.</summary>
        void Drop(Challenge c, string why)
        {
            active.Remove(c);
            LastOutcome = ChallengeOutcome.Aborted;
            LastEnded = c;
            Log.Message($"[Gesta] champion's challenge dropped: {why}");
        }

        void End(Challenge c, ChallengeOutcome outcome, string why = null)
        {
            active.Remove(c);
            LastOutcome = outcome;
            LastEnded = c;
            Map map = c.map;
            string champ = c.champion?.LabelShort ?? "?";
            string taker = c.challenger?.LabelShort ?? "?";
            string factionName = c.lord?.faction?.Name ?? "Gesta_Killcam_TheEnemy".Translate().ToString();
            var look = new LookTargets(c.champion);

            switch (outcome)
            {
                case ChallengeOutcome.ColonistWon:
                    Reward(c);
                    bool fled = BreakRaid(c);
                    Messages.Message((fled ? "Gesta_Champion_Won" : "Gesta_Champion_WonNoFlee").Translate(taker, champ, factionName),
                        new LookTargets(c.challenger), MessageTypeDefOf.PositiveEvent);
                    break;
                case ChallengeOutcome.ChampionWon:
                    Restore(c);
                    Messages.Message("Gesta_Champion_Lost".Translate(taker, champ), look, MessageTypeDefOf.NegativeEvent);
                    break;
                case ChallengeOutcome.BrokenByColony:
                    Shame(map, GestaDefOf.Gesta_ChallengeDishonor);
                    Restore(c);
                    Find.LetterStack.ReceiveLetter("Gesta_Champion_DishonorLabel".Translate(),
                        "Gesta_Champion_Dishonor".Translate(c.breaker?.LabelShort ?? "?", champ), LetterDefOf.NegativeEvent, look);
                    break;
                case ChallengeOutcome.BrokenByRaiders:
                    Restore(c);
                    Messages.Message("Gesta_Champion_RaiderBroke".Translate(factionName), look, MessageTypeDefOf.ThreatSmall);
                    break;
                case ChallengeOutcome.Refused:
                    Shame(map, GestaDefOf.Gesta_ChallengeRefused);
                    Restore(c);
                    Messages.Message("Gesta_Champion_Refused".Translate(champ), look, MessageTypeDefOf.NegativeEvent);
                    break;
                case ChallengeOutcome.DuelTooLong:
                    Restore(c);
                    Messages.Message("Gesta_Champion_DuelTooLong".Translate(), look, MessageTypeDefOf.NeutralEvent);
                    break;
                default:
                    Restore(c);
                    break;
            }
            if (c.banner != null && !c.banner.Destroyed) c.banner.Destroy(DestroyMode.Vanish);
            Log.Message($"[Gesta] champion's challenge ended: {outcome}{(why != null ? " (" + why + ")" : "")}");
        }

        static void Reward(Challenge c)
        {
            Pawn winner = c.challenger;
            if (winner == null) return;
            if (!winner.Dead) winner.needs?.mood?.thoughts?.memories?.TryGainMemory(GestaDefOf.Gesta_ChampionVictory);
            if (c.champion != null) TaleRecorder.RecordTale(GestaDefOf.Gesta_ChampionDuelTale, winner, c.champion);
            bool feat = GestaMod.Settings.feats && FeatTracker.Instance != null && FeatTracker.CanEarn(winner)
                        && FeatTracker.Instance.TryAward(winner, GestaDefOf.Gesta_Feat_Champion);
            if (!feat && winner.Spawned) FeatEffects.Play(winner, GestaDefOf.Gesta_Feat_Champion.LabelCap);
        }

        /// <summary>
        /// Sends the raid into its graph's own PanicFlee toil (or ExitMap). Never makes new toils: the
        /// graph is rebuilt on load and only knows its own.
        /// </summary>
        static bool BreakRaid(Challenge c)
        {
            Lord lord = c.lord;
            bool exists = lord != null && c.map != null && !c.map.Disposed && c.map.lordManager.lords.Contains(lord);
            if (!exists)
            {
                ClearDuty(c.champion);
                return false;
            }
            if (!(lord.CurLordToil is LordToil_AssaultColony))
            {
                // losing the champion may already have tripped the raid's own flee transition
                Restore(c);
                return lord.CurLordToil is LordToil_PanicFlee || lord.CurLordToil is LordToil_ExitMap;
            }
            LordToil flee = lord.Graph.lordToils.Find(t => t is LordToil_PanicFlee) ?? lord.Graph.lordToils.Find(t => t is LordToil_ExitMap);
            if (flee == null)
            {
                Restore(c);
                return false;
            }
            lord.GotoToil(flee);
            ClearDuty(c.champion);
            return true;
        }

        static void Shame(Map map, ThoughtDef def)
        {
            if (map == null) return;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned.ToArray()) p.needs?.mood?.thoughts?.memories?.TryGainMemory(def);
        }

        // ---------------------------------------------------------------- drawing

        public override void GameComponentUpdate()
        {
            if (active.Count == 0 || WorldRendererUtility.WorldSelected) return;
            Map current = Find.CurrentMap;
            for (int i = 0; i < active.Count; i++)
            {
                Challenge c = active[i];
                if (c.map == current && c.banner != null && c.banner.Spawned)
                    GenDraw.DrawRadiusRing(c.banner.Position, CircleRadius, RingColor);
            }
        }

        // ---------------------------------------------------------------- saving

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                saveList = new List<Challenge>();
                foreach (Challenge c in active)
                {
                    // references to things that no longer exist would not resolve on load
                    bool ok = c.map != null && !c.map.Disposed && c.lord != null && c.map.lordManager.lords.Contains(c.lord)
                              && c.champion != null && !c.champion.Destroyed && c.banner != null && !c.banner.Destroyed
                              && (c.challenger == null || !c.challenger.Destroyed);
                    if (ok) saveList.Add(c);
                }
            }
            Scribe_Collections.Look(ref saveList, "challenges", LookMode.Deep);
            Scribe_Collections.Look(ref rolledLords, "rolledLords", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.Saving) saveList = null;
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                active = new List<Challenge>();
                if (saveList != null)
                    foreach (Challenge c in saveList)
                        if (c != null && c.lord != null && c.map != null && c.champion != null && c.banner != null
                            && (c.phase == ChallengePhase.Challenge || c.challenger != null))
                            active.Add(c);
                saveList = null;
                rolledLords ??= new List<int>();
            }
        }
    }
}
