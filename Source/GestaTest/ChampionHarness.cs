using System.Collections.Generic;
using System.Linq;
using Gesta;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace GestaTest
{
    /// <summary>
    /// The Champion's Challenge with real raids, driven through the real roll, duties, damage and
    /// down paths. Tick 2600 (after the other Gesta harnesses), second stage at 2780 (run: --ticks 2900).
    /// </summary>
    public class ChampionHarness : GameComponent
    {
        const int StageA = 2600, StageB = 2780;
        bool active, doneA, doneB;
        int ticks;
        float savedChance;
        bool savedOn;

        // carried from stage A to stage B
        Lord liveLord;
        Pawn liveChampion;
        float liveStartDist;
        readonly Dictionary<Pawn, IntVec3> holdersAt = new Dictionary<Pawn, IntVec3>();
        Pawn shooter;

        public ChampionHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || doneB) return;
            ticks++;
            if (ticks == StageA && !doneA)
            {
                doneA = true;
                savedChance = GestaMod.Settings.championChance;
                savedOn = GestaMod.Settings.championsChallenge;
                GestaMod.Settings.championChance = 1f;
                GestaMod.Settings.championsChallenge = true;
                try { RunA(); }
                catch (System.Exception ex) { Kit.Check(false, "champion harness (A) threw: " + ex); }
            }
            else if (ticks == StageB)
            {
                doneB = true;
                try { RunB(); }
                catch (System.Exception ex) { Kit.Check(false, "champion harness (B) threw: " + ex); }
                GestaMod.Settings.championChance = savedChance;
                GestaMod.Settings.championsChallenge = savedOn;
                Kit.Summary("Gesta/Champion");
            }
        }

        // ------------------------------------------------------------------ helpers

        static Faction Enemy() =>
            Faction.OfPirates is Faction f && f.HostileTo(Faction.OfPlayer) ? f : Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);

        static Lord Raid(Map map, float points)
        {
            var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = Enemy();
            parms.points = points;
            parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            var before = new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned);
            TimeSpeed speed = Find.TickManager.CurTimeSpeed;
            IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            KeepRunning(speed);
            Pawn any = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => !before.Contains(p) && p.GetLord() != null && p.HostileTo(Faction.OfPlayer));
            return any?.GetLord();
        }

        /// <summary>Raid and challenge letters auto-pause (AnyThreat): keep the run going.</summary>
        static void KeepRunning(TimeSpeed speed)
        {
            if (Find.TickManager.Paused) Find.TickManager.CurTimeSpeed = speed == TimeSpeed.Paused ? TimeSpeed.Normal : speed;
        }

        static Lord LordOf(Map map, List<Pawn> pawns, Faction f) =>
            LordMaker.MakeNewLord(f, new LordJob_AssaultColony(f, canKidnap: false, canTimeoutOrFlee: false, sappers: false, useAvoidGridSmart: false, canSteal: false), map, pawns);

        /// <summary>Forces "has been assaulting for a while" and rolls (chance is 1 during the harness).</summary>
        static Challenge Roll(Lord lord)
        {
            var cc = ChampionsChallenge.Instance;
            lord.ticksInToil = ChampionsChallenge.MinAssaultTicks + 100;
            TimeSpeed speed = Find.TickManager.CurTimeSpeed;
            cc.RollNow();
            KeepRunning(speed);
            return cc.For(lord);
        }

        static void KillHostiles(Map map)
        {
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer)).ToList()) p.Kill(null);
        }

        static bool AllDuty(Lord lord, DutyDef def, Pawn except = null) =>
            lord.ownedPawns.Where(p => p != except).All(p => p.mindState?.duty?.def == def);

        static bool NoChallengeDuties(Lord lord) =>
            lord == null || lord.ownedPawns.All(p => !ChampionsChallenge.IsChallengeDuty(p.mindState?.duty?.def));

        static bool HasMemory(Pawn p, ThoughtDef def) =>
            p.needs?.mood?.thoughts?.memories?.Memories.Any(m => m.def == def) == true;

        /// <summary>A six-pirate assault lord at the edge, rolled straight into a challenge.</summary>
        static Challenge Fresh(Map map)
        {
            Faction f = Enemy();
            IntVec3 edge = CellFinder.RandomEdgeCell(map);
            var pawns = Enumerable.Range(0, 6).Select(_ => Kit.SpawnHostile(map, edge, null, f)).ToList();
            return Roll(LordOf(map, pawns, f));
        }

        /// <summary>Past the grace, so any further act counts.</summary>
        static void PastGrace(Challenge c) => c.startTick = Find.TickManager.TicksGame - ChampionsChallenge.GraceTicks - 1;

        static void Forget(Map map, ThoughtDef def)
        {
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned) p.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDef(def);
        }

        static Pawn NewFighter(Map map, IntVec3 near)
        {
            Pawn p = null;
            for (int i = 0; i < 20; i++)
            {
                p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (!p.WorkTagIsDisabled(WorkTags.Violent)) break;
                p.Destroy();
                p = null;
            }
            if (p == null) return null;
            GenSpawn.Spawn(p, CellFinder.StandableCellNear(near, map, 5f), map);
            return p;
        }

        /// <summary>A free cell 3-6 cells from the turret, in its line of sight.</summary>
        static IntVec3 InSight(Map map, IntVec3 turret) =>
            CellFinder.StandableCellNear(turret, map, 6f, x => x.DistanceTo(turret) >= 3f && x.GetFirstPawn(map) == null
                                                              && x.GetFirstBuilding(map) == null && GenSight.LineOfSight(turret, x, map));

        /// <summary>Puts the champion at his spot and a drafted colonist inside the circle.</summary>
        static void Step(Map map, Challenge c, Pawn taker)
        {
            Kit.Teleport(c.champion, c.standCell);
            IntVec3 cell = CellFinder.StandableCellNear(c.banner.Position, map, 2.5f,
                x => x != c.standCell && x.InHorDistOf(c.banner.Position, ChampionsChallenge.CircleRadius - 1f) && x.GetFirstPawn(map) == null);
            if (!cell.IsValid) cell = c.banner.Position;
            taker.drafter.Drafted = true;
            Kit.Teleport(taker, cell);
            ChampionsChallenge.Instance.ProcessNow();
        }

        // ------------------------------------------------------------------ stage A

        void RunA()
        {
            Map map = Find.CurrentMap;
            var cc = ChampionsChallenge.Instance;
            var eng = EngagementTracker.Instance;
            KillHostiles(map);
            IntVec3 home = map.mapPawns.FreeColonistsSpawned.First().Position;
            Pawn duelA = NewFighter(map, home + new IntVec3(3, 0, 0));
            Pawn duelB = NewFighter(map, home + new IntVec3(-3, 0, 0));
            shooter = NewFighter(map, home + new IntVec3(0, 0, 3));
            Kit.Check(duelA != null && duelB != null && shooter != null && cc != null, "setup: three fresh fighters");
            if (duelA == null || duelB == null || shooter == null || cc == null) return;
            Faction pirates = Enemy();
            IntVec3 edge = CellFinder.RandomEdgeCell(map);

            // --- negatives: small, mechanoid and animal assaults
            var two = new List<Pawn> { Kit.SpawnHostile(map, edge, null, pirates), Kit.SpawnHostile(map, edge, null, pirates) };
            Lord small = LordOf(map, two, pirates);
            Kit.Check(Roll(small) == null && cc.WasRolled(small) && cc.Active.Count == 0, "a two-raider assault gets no challenge (and is never rolled again)");
            KillHostiles(map);

            if (Faction.OfMechanoids != null)
            {
                var mechs = Enumerable.Range(0, 5).Select(_ => Kit.SpawnHostile(map, edge, PawnKindDef.Named("Mech_Scyther"), Faction.OfMechanoids)).ToList();
                Lord mechLord = LordOf(map, mechs, Faction.OfMechanoids);
                Kit.Check(Roll(mechLord) == null && cc.Active.Count == 0, "a mechanoid assault gets no challenge");
                KillHostiles(map);
            }

            var wargs = Enumerable.Range(0, 5).Select(_ => Kit.SpawnHostile(map, edge, PawnKindDef.Named("Warg"), pirates)).ToList();
            Lord beasts = LordOf(map, wargs, pirates);
            Kit.Check(Roll(beasts) == null && cc.Active.Count == 0, "an all-animal assault gets no challenge");
            KillHostiles(map);

            // --- a real raid: not before it has assaulted for a while
            Lord l1 = Raid(map, 1500f);
            Kit.Check(l1 != null && l1.CurLordToil is LordToil_AssaultColony && l1.ownedPawns.Count >= ChampionsChallenge.MinRaiders,
                $"setup: humanlike raid assaulting ({l1?.ownedPawns.Count} raiders)");
            if (l1 == null) return;
            l1.ticksInToil = 0;
            cc.RollNow();
            Kit.Check(cc.For(l1) == null && !cc.WasRolled(l1), "no challenge in the raid's first seconds");

            Challenge c1 = Roll(l1);
            Kit.Check(c1 != null && c1.phase == ChallengePhase.Challenge, "after assaulting a while, the raid issues a challenge");
            if (c1 == null) { KillHostiles(map); return; }
            Pawn champ1 = c1.champion;
            Kit.Check(c1.banner != null && c1.banner.Spawned && c1.banner.def == GestaDefOf.Gesta_ChallengeBanner, $"a banner is planted at {c1.banner?.Position}");
            Kit.Check(ChampionsChallenge.PickChampion(l1) == champ1 || champ1 == l1.faction.leader, $"the champion is the raid's best melee fighter or leader ({champ1.LabelShort})");
            Kit.Check(champ1.mindState.duty?.def == GestaDefOf.Gesta_ChampChallenge
                      && (champ1.CurJobDef == JobDefOf.Goto || champ1.CurJobDef == GestaDefOf.Gesta_ChampStand),
                $"the champion heads for the banner (duty {champ1.mindState.duty?.def?.defName}, job {champ1.CurJobDef?.defName})");
            List<Pawn> holders1 = l1.ownedPawns.Where(p => p != champ1).ToList();
            Kit.Check(AllDuty(l1, GestaDefOf.Gesta_ChampHold, champ1) && holders1.All(p => p.CurJobDef == GestaDefOf.Gesta_ChampStand),
                $"the other {holders1.Count} raiders hold and stand still");
            Kit.Check(l1.ownedPawns.All(p => p.ThreatDisabled(null)) && CombatUtil.ActiveHostilePawns(map) == 0,
                "under the truce no raider counts as an active threat");
            eng.PollNow();
            Kit.Check(eng.IsActive(map), "the engagement stays on during the truce");

            // a player turret next to a holder does not pick it
            Pawn nearTurret = holders1.First();
            IntVec3 turretCell = CellFinder.StandableCellNear(home + new IntVec3(10, 0, 10), map, 6f, x => x.GetFirstBuilding(map) == null && x.GetFirstPawn(map) == null);
            ThingDef turretDef = ThingDef.Named("Turret_MiniTurret");
            var turret = (Building_TurretGun)ThingMaker.MakeThing(turretDef, GenStuff.DefaultStuffFor(turretDef));
            turret.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(turret, turretCell, map);
            Kit.Teleport(nearTurret, InSight(map, turretCell));
            LocalTargetInfo tgt = turret.TryFindNewTarget();
            Kit.Check(!(tgt.Thing is Pawn tp && tp.GetLord() == l1), $"a player turret ignores a holding raider ({tgt.Thing?.LabelShort ?? "no target"})");

            l1.CurLordToil.UpdateAllDuties();
            Kit.Check(champ1.mindState.duty?.def == GestaDefOf.Gesta_ChampChallenge && AllDuty(l1, GestaDefOf.Gesta_ChampHold, champ1),
                "duties survive vanilla's UpdateAllDuties");

            string xml = Scribe.saver.DebugOutputFor(cc);
            Kit.Check(xml != null && xml.Contains("challenges") && xml.Contains("Lord_" + l1.loadID) && xml.Contains(champ1.GetUniqueLoadID()),
                "the challenge is written to the save by reference");

            // --- accept, duel, win
            Step(map, c1, duelA);
            Kit.Check(c1.phase == ChallengePhase.Duel && c1.challenger == duelA && champ1.mindState.duty?.def == GestaDefOf.Gesta_ChampDuel,
                $"a drafted colonist in the circle accepts ({c1.phase}, champion duty {champ1.mindState.duty?.def?.defName})");
            Kit.Check(duelA.CurJobDef == JobDefOf.AttackMelee && duelA.CurJob.targetA.Thing == champ1, $"the challenger is ordered to fight the champion ({duelA.CurJobDef?.defName})");
            Kit.Check(AllDuty(l1, GestaDefOf.Gesta_ChampHold, champ1), "the rest of the raid still holds during the duel");
            Kit.DownBy(champ1, duelA);
            Kit.Check(cc.For(l1) != null, "the challenger's blows do not break the truce");
            cc.ProcessNow();
            bool lordAlive = map.lordManager.lords.Contains(l1);
            Kit.Check(cc.LastOutcome == ChallengeOutcome.ColonistWon && cc.Active.Count == 0, $"downing the champion wins the duel ({cc.LastOutcome})");
            Kit.Check(lordAlive && (l1.CurLordToil is LordToil_PanicFlee || l1.CurLordToil is LordToil_ExitMap),
                $"the raid breaks and flees ({(lordAlive ? l1.CurLordToil.GetType().Name : "lord gone")})");
            Kit.Check(HasMemory(duelA, GestaDefOf.Gesta_ChampionVictory), "the winner remembers the victory");
            Kit.Check(Find.TaleManager.AllTalesListForReading.Any(t => t.def == GestaDefOf.Gesta_ChampionDuelTale && (t as Tale_DoublePawn)?.firstPawnData?.pawn == duelA),
                "the duel is a tale");
            Kit.Check(FeatTracker.Instance.Has(duelA, GestaDefOf.Gesta_Feat_Champion), $"the winner earns the champion feat (title {duelA.story?.Title})");
            Kit.Check(c1.banner.Destroyed && NoChallengeDuties(lordAlive ? l1 : null), "the banner is gone and no challenge duty is left");
            duelA.drafter.Drafted = false;
            KillHostiles(map);

            // --- the champion wins: the raid resumes
            Lord l2 = Raid(map, 1500f);
            Challenge c2 = l2 == null ? null : Roll(l2);
            Kit.Check(c2 != null, "setup: second raid challenges");
            if (c2 != null)
            {
                Pawn champ2 = c2.champion;
                Step(map, c2, duelB);
                Kit.Check(c2.phase == ChallengePhase.Duel, "setup: second duel on");
                Kit.DownBy(duelB, champ2);
                Kit.Check(cc.For(l2) != null, "the champion's blows on the challenger do not break the truce");
                cc.ProcessNow();
                Kit.Check(cc.LastOutcome == ChallengeOutcome.ChampionWon && l2.CurLordToil is LordToil_AssaultColony
                          && AllDuty(l2, DutyDefOf.AssaultColony), $"the champion wins and the raid resumes ({cc.LastOutcome})");
                Kit.Check(!HasMemory(duelB, GestaDefOf.Gesta_ChampionVictory) && c2.banner.Destroyed, "no reward for losing, banner gone");
                // control for the turret check: an assaulting raider is a target again
                Pawn raider = l2.ownedPawns.FirstOrDefault(p => p != champ2);
                if (raider != null)
                {
                    Kit.Teleport(raider, InSight(map, turretCell));
                    LocalTargetInfo t2 = turret.TryFindNewTarget();
                    Kit.Check(t2.Thing is Pawn rp && rp.GetLord() == l2, $"control: the turret targets a resumed raider ({t2.Thing?.LabelShort ?? "none"})");
                }
            }
            KillHostiles(map);
            turret.Destroy();

            // --- nobody answers within the hour
            Lord l3 = Raid(map, 1500f);
            Challenge c3 = l3 == null ? null : Roll(l3);
            Kit.Check(c3 != null, "setup: third raid challenges");
            if (c3 != null)
            {
                c3.startTick -= ChampionsChallenge.AcceptTicks + 1;
                cc.ProcessNow();
                Kit.Check(cc.LastOutcome == ChallengeOutcome.Refused && AllDuty(l3, DutyDefOf.AssaultColony) && c3.banner.Destroyed,
                    $"an hour without an answer: the raid resumes ({cc.LastOutcome})");
                Kit.Check(Kit.Colonists(map).Where(p => p.needs?.mood != null).All(p => HasMemory(p, GestaDefOf.Gesta_ChallengeRefused)),
                    "the colony remembers refusing");
            }
            KillHostiles(map);

            // --- the champion dies of something else before the duel: clean abort
            Lord l4 = Raid(map, 1500f);
            Challenge c4 = l4 == null ? null : Roll(l4);
            Kit.Check(c4 != null, "setup: fourth raid challenges");
            if (c4 != null)
            {
                c4.champion.Kill(null);
                cc.ProcessNow();
                Kit.Check(cc.LastOutcome == ChallengeOutcome.Aborted && cc.Active.Count == 0 && AllDuty(l4, DutyDefOf.AssaultColony) && c4.banner.Destroyed,
                    $"a dead champion aborts cleanly and the raid resumes ({cc.LastOutcome})");
                Kit.Check(!Kit.Colonists(map).Any(p => HasMemory(p, GestaDefOf.Gesta_ChallengeDishonor)), "no dishonor for an abort");
            }
            KillHostiles(map);

            // --- a raider breaks the truce
            Lord l5 = Raid(map, 1500f);
            Challenge c5 = l5 == null ? null : Roll(l5);
            Kit.Check(c5 != null, "setup: fifth raid challenges");
            if (c5 != null)
            {
                Pawn holder = l5.ownedPawns.First(p => p != c5.champion);
                c5.startTick -= ChampionsChallenge.GraceTicks + 1;
                shooter.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 6f, 1f, -1f, holder));   // full penetration: armour must not turn it to 0
                cc.ProcessNow();
                Kit.Check(cc.LastOutcome == ChallengeOutcome.BrokenByRaiders && AllDuty(l5, DutyDefOf.AssaultColony),
                    $"a raider hurting a colonist ends the truce ({cc.LastOutcome})");
                Kit.Check(!Kit.Colonists(map).Any(p => HasMemory(p, GestaDefOf.Gesta_ChallengeDishonor)), "the colony is not shamed for the raiders' breach");
            }
            KillHostiles(map);

            // --- the state is lost (a broken load): the sweep gives the raid its duties back
            Lord l6 = Raid(map, 1500f);
            Challenge c6 = l6 == null ? null : Roll(l6);
            Kit.Check(c6 != null, "setup: sixth raid challenges");
            if (c6 != null)
            {
                cc.DebugForgetAll();
                Kit.Check(!NoChallengeDuties(l6), "setup: orphaned challenge duties");
                int swept = cc.SweepNow();
                Kit.Check(swept >= 1 && AllDuty(l6, DutyDefOf.AssaultColony) && c6.banner.Destroyed,
                    $"the sweep restores an orphaned raid and removes its banner ({swept})");
            }
            KillHostiles(map);

            // --- the whole raid dies mid-challenge: the lord ends, the challenge goes quietly
            Lord l7 = Raid(map, 1500f);
            Challenge c7 = l7 == null ? null : Roll(l7);
            Kit.Check(c7 != null, "setup: seventh raid challenges");
            if (c7 != null)
            {
                KillHostiles(map);
                cc.ProcessNow();
                Kit.Check(cc.LastOutcome == ChallengeOutcome.Aborted && cc.Active.Count == 0 && c7.banner.Destroyed,
                    $"the lord ending aborts the challenge ({cc.LastOutcome})");
            }

            // --- exploits (review): each must count against the colony, not end the truce for free
            Pawn duelC = NewFighter(map, home + new IntVec3(0, 0, -3));

            Challenge f1 = Fresh(map);
            Kit.Check(f1 != null, "setup: fire challenge");
            if (f1 != null)
            {
                Pawn h = f1.lord.ownedPawns.First(p => p != f1.champion);
                PastGrace(f1);
                h.TakeDamage(new DamageInfo(DamageDefOf.Burn, 2f, 0f, -1f, shooter));
                Kit.Check(f1.pending == ChallengeOutcome.None, "fire that spread on its own (no weapon) does not break the truce");
                h.TakeDamage(new DamageInfo(DamageDefOf.Burn, 2f, 0f, -1f, shooter, null, ThingDef.Named("Weapon_GrenadeMolotov")));
                cc.ProcessNow();
                Kit.Check(cc.LastOutcome == ChallengeOutcome.BrokenByColony, $"a direct molotov hit breaks the truce ({cc.LastOutcome})");
                Forget(map, GestaDefOf.Gesta_ChallengeDishonor);
            }
            KillHostiles(map);

            Challenge f2 = Fresh(map);
            Kit.Check(f2 != null, "setup: shock-lance challenge");
            if (f2 != null)
            {
                Step(map, f2, duelC);
                Kit.Check(f2.phase == ChallengePhase.Duel, "setup: duel on");
                PastGrace(f2);
                f2.champion.health.AddHediff(HediffDefOf.PsychicShock, f2.champion.health.hediffSet.GetBrain());
                Kit.Check(f2.champion.Downed, "setup: the psychic shock downs the champion");
                cc.ProcessNow();
                Kit.Check(cc.LastOutcome == ChallengeOutcome.BrokenByColony && !HasMemory(duelC, GestaDefOf.Gesta_ChampionVictory),
                    $"a champion felled without the challenger's blow is no win: dishonor ({cc.LastOutcome})");
                Forget(map, GestaDefOf.Gesta_ChallengeDishonor);
                duelC.drafter.Drafted = false;
            }
            KillHostiles(map);

            Challenge f3 = Fresh(map);
            Kit.Check(f3 != null, "setup: banner challenge");
            if (f3 != null)
            {
                PastGrace(f3);
                f3.banner.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 5f, 0f, -1f, shooter));
                cc.ProcessNow();
                Kit.Check(cc.LastOutcome == ChallengeOutcome.BrokenByColony && f3.banner.Destroyed, $"shooting the banner breaks the truce ({cc.LastOutcome})");
                Kit.Check(f3.banner.def.GetStatValueAbstract(StatDefOf.Flammability) == 0f, "the banner cannot be burned down");
                Forget(map, GestaDefOf.Gesta_ChallengeDishonor);
            }
            KillHostiles(map);

            Challenge f4 = Fresh(map);
            Kit.Check(f4 != null, "setup: provocation challenge");
            if (f4 != null)
            {
                Pawn h = f4.lord.ownedPawns.First(p => p != f4.champion);
                PastGrace(f4);
                h.mindState.meleeThreat = shooter;
                shooter.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 6f, 1f, -1f, h));
                cc.ProcessNow();
                Kit.Check(cc.LastOutcome == ChallengeOutcome.BrokenByColony && cc.LastEnded?.breaker == shooter,
                    $"a raider hitting back at a colonist who swung first: the colony broke it ({cc.LastOutcome})");
                Forget(map, GestaDefOf.Gesta_ChallengeDishonor);
            }
            KillHostiles(map);

            Challenge f5 = Fresh(map);
            Kit.Check(f5 != null, "setup: swing challenge");
            if (f5 != null)
            {
                Pawn h = f5.lord.ownedPawns.First(p => p != f5.champion);
                PastGrace(f5);
                IntVec3 next = CellFinder.StandableCellNear(h.Position, map, 1.9f, x => x != h.Position && x.GetFirstPawn(map) == null);
                Kit.Teleport(shooter, next);
                Verb melee = shooter.meleeVerbs.TryGetMeleeVerb(h);
                Traverse.Create(melee).Field("currentTarget").SetValue(new LocalTargetInfo(h));
                Traverse.Create(melee).Method("TryCastShot").GetValue<bool>();
                Kit.Check(f5.pending == ChallengeOutcome.BrokenByColony && f5.breaker == shooter, $"a colonist's swing at a holder, hit or miss, breaks the truce ({f5.pending})");
                cc.ProcessNow();
                Forget(map, GestaDefOf.Gesta_ChallengeDishonor);
            }
            KillHostiles(map);

            Challenge f6 = Fresh(map);
            Kit.Check(f6 != null, "setup: kiting challenge");
            if (f6 != null)
            {
                Step(map, f6, duelC);
                Kit.Check(f6.phase == ChallengePhase.Duel, "setup: duel on");
                Kit.Teleport(duelC, CellFinder.StandableCellNear(f6.champion.Position + new IntVec3(9, 0, 0), map, 4f, x => x.GetFirstPawn(map) == null));
                f6.duelStartTick -= ChampionsChallenge.DuelTicks + 1;
                cc.ProcessNow();
                Kit.Check(cc.LastOutcome == ChallengeOutcome.Refused && AllDuty(f6.lord, DutyDefOf.AssaultColony),
                    $"running out the clock out of reach counts as refusing ({cc.LastOutcome})");
                duelC.drafter.Drafted = false;
            }
            KillHostiles(map);

            // --- left running for stage B
            liveLord = Raid(map, 1500f);
            Challenge live = liveLord == null ? null : Roll(liveLord);
            Kit.Check(live != null, "setup: eighth raid challenges and is left running");
            if (live != null)
            {
                liveChampion = live.champion;
                liveStartDist = liveChampion.Position.DistanceTo(live.standCell);
                holdersAt.Clear();
                foreach (Pawn p in liveLord.ownedPawns) if (p != liveChampion) holdersAt[p] = p.Position;
            }
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned) if (p.drafter != null) p.drafter.Drafted = false;
        }

        // ------------------------------------------------------------------ stage B

        void RunB()
        {
            Map map = Find.CurrentMap;
            var cc = ChampionsChallenge.Instance;
            Challenge c = liveLord == null ? null : cc.For(liveLord);
            if (c == null && liveLord != null && map.lordManager.lords.Contains(liveLord) && !(liveLord.CurLordToil is LordToil_AssaultColony))
            {
                // vanilla may switch an assault to kidnapping (a downed colonist within 8 cells) or stealing
                Log.Warning($"[Test] SKIP stage B: the raid switched to {liveLord.CurLordToil.GetType().Name} on its own");
                Kit.Check(cc.LastOutcome == ChallengeOutcome.Aborted && NoChallengeDuties(liveLord),
                    "a raid that changes plans mid-challenge drops it cleanly");
                KillHostiles(map);
                return;
            }
            Kit.Check(c != null && c.phase == ChallengePhase.Challenge, "after 180 ticks of real play the challenge still stands");
            if (c == null) { KillHostiles(map); return; }

            int moved = holdersAt.Count(kv => kv.Key.Spawned && !kv.Key.Dead && kv.Key.Position.DistanceTo(kv.Value) > 1.5f);
            Kit.Check(moved == 0 && AllDuty(liveLord, GestaDefOf.Gesta_ChampHold, liveChampion), $"the holders stood their ground ({moved} moved)");
            float dist = liveChampion.Position.DistanceTo(c.standCell);
            Kit.Check(dist <= 2f || dist < liveStartDist - 1.5f, $"the champion walked to the banner ({liveStartDist:0.0} -> {dist:0.0} cells)");

            Pawn holder = holdersAt.Keys.First(p => p.Spawned && !p.Dead && !p.Downed);
            c.startTick = Find.TickManager.TicksGame - 10;   // as if the truce had just begun
            holder.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 3f, 0f, -1f, shooter));
            Kit.Check(c.pending == ChallengeOutcome.None, "a shot inside the grace period does not break the truce");

            c.startTick -= ChampionsChallenge.GraceTicks;
            holder.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 3f, 0f, -1f, shooter));
            cc.ProcessNow();
            Kit.Check(cc.LastOutcome == ChallengeOutcome.BrokenByColony && cc.Active.Count == 0 && c.banner.Destroyed,
                $"a colonist shooting a holder breaks the truce ({cc.LastOutcome})");
            Kit.Check(Kit.Colonists(map).Where(p => p.needs?.mood != null).All(p => HasMemory(p, GestaDefOf.Gesta_ChallengeDishonor)),
                "the whole colony carries the dishonor");
            Kit.Check(liveLord.CurLordToil is LordToil_AssaultColony && AllDuty(liveLord, DutyDefOf.AssaultColony), "and the raid resumes");
            KillHostiles(map);
        }
    }
}
