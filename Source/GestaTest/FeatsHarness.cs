using System.Collections.Generic;
using System.Linq;
using Gesta;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace GestaTest
{
    /// <summary>Feats of Arms through real kills/carries. Runs after the last stand harness (tick 300+).</summary>
    public class FeatsHarness : GameComponent
    {
        bool active, done;
        int ticks, carryStartTick = -1;
        Map map;
        Pawn a, b, c, d, e, carryHostile;
        readonly List<Pawn> spawned = new List<Pawn>();

        public FeatsHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            try { Step(); }
            catch (System.Exception ex) { Kit.Check(false, "feats harness threw: " + ex); Finish(); }
        }

        void Step()
        {
            var feats = FeatTracker.Instance;
            if (ticks == 300)
            {
                map = Find.CurrentMap;
                IntVec3 root = map.mapPawns.FreeColonistsSpawned.First().Position;
                a = NewColonist(root); b = NewColonist(root); c = NewColonist(root); d = NewColonist(root); e = NewColonist(root);
                c.story.Title = "Potato Baron";

                KillHostiles(a, 2);
                Kit.Check(!feats.Has(a, GestaDefOf.Gesta_Feat_TripleKill), "two quick kills are not a feat");
                KillHostiles(a, 1);
                Kit.Check(feats.Has(a, GestaDefOf.Gesta_Feat_TripleKill), "third kill within 10 s earns Triple kill");
                Kit.Check(GestaDefOf.Gesta_Feat_TripleKill.epithets.Contains(a.story.Title), $"title became the epithet ({a.story.Title})");
                Kit.Check(a.LabelNoCount.Contains(a.story.Title), "the epithet shows next to the name");
                Kit.Check(Find.TaleManager.AllTalesListForReading.Any(t => t.def == GestaDefOf.Gesta_Feat_TripleKill.tale), "triple kill leaves a tale");
                Kit.Check(a.needs.mood.thoughts.memories.GetFirstMemoryOfDef(GestaDefOf.Gesta_DidAFeat) != null, "feat gives a memory");

                string titleBefore = a.story.Title;
                KillHostiles(a, 3);
                Kit.Check(a.story.Title == titleBefore && a.needs.mood.thoughts.memories.NumMemoriesOfDef(GestaDefOf.Gesta_DidAFeat) == 1,
                    "a second triple kill gives nothing new");

                for (int i = 0; i < 3; i++)
                {
                    Pawn muffalo = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"), null);
                    GenSpawn.Spawn(muffalo, CellFinder.StandableCellNear(root, map, 8f), map);
                    Kit.KillBy(muffalo, b);
                }
                Kit.Check(!feats.Has(b, GestaDefOf.Gesta_Feat_TripleKill), "killing non-hostile animals is not a feat");

                Pawn thrumbo1 = SpawnManhunter("Thrumbo", root);
                Kit.Check(thrumbo1.HostileTo(Faction.OfPlayer), "setup: manhunter thrumbo is hostile");
                Kit.KillBy(thrumbo1, b, DamageDefOf.Bullet);
                Kit.Check(!feats.Has(b, GestaDefOf.Gesta_Feat_BeastBreaker), "shooting a thrumbo is not Beast breaker");
                Pawn thrumbo2 = SpawnManhunter("Thrumbo", root);
                Kit.KillBy(thrumbo2, b, DamageDefOf.Cut);
                Kit.Check(!feats.Has(b, GestaDefOf.Gesta_Feat_BeastBreaker), "a 'cut' from 15 cells away is not melee");
                Pawn thrumbo3 = SpawnManhunter("Thrumbo", root);
                Kit.Teleport(b, thrumbo3.Position + IntVec3.West);
                Kit.KillBy(thrumbo3, b, DamageDefOf.Cut);
                Kit.Check(feats.Has(b, GestaDefOf.Gesta_Feat_BeastBreaker), "killing a thrumbo in melee is Beast breaker");

                Pawn boss = Kit.SpawnHostile(map, root + new IntVec3(20, 0, 0), PawnKindDefOf.PirateBoss);
                spawned.Add(boss);
                Kit.KillBy(boss, d);
                Kit.Check(!feats.Has(d, GestaDefOf.Gesta_Feat_Kingslayer), "an ordinary raid boss is not a king");
                Pawn king = Kit.SpawnHostile(map, root + new IntVec3(20, 0, 0));
                spawned.Add(king);
                Faction oldLeaderFaction = king.Faction; Pawn oldLeader = oldLeaderFaction.leader;
                oldLeaderFaction.leader = king;
                Kit.KillBy(king, d);
                if (oldLeaderFaction.leader == king || oldLeaderFaction.leader == null) oldLeaderFaction.leader = oldLeader;
                Kit.Check(feats.Has(d, GestaDefOf.Gesta_Feat_Kingslayer), "killing the faction's leader is Kingslayer");

                // fire, downed victims and berserk colonists are not feats
                Pawn f = NewColonist(root);
                for (int i = 0; i < 3; i++)
                {
                    Pawn h = Kit.SpawnHostile(map, f.Position + new IntVec3(20, 0, 0));
                    spawned.Add(h);
                    Kit.KillBy(h, f, DamageDefOf.Flame);
                }
                Kit.Check(!feats.Has(f, GestaDefOf.Gesta_Feat_TripleKill), "three kills by fire are not a triple kill");
                for (int i = 0; i < 3; i++)
                {
                    Pawn h = Kit.SpawnHostile(map, f.Position + new IntVec3(20, 0, 0));
                    spawned.Add(h);
                    Kit.Down(h);
                    Kit.KillBy(h, f);
                }
                Kit.Check(!feats.Has(f, GestaDefOf.Gesta_Feat_TripleKill), "finishing off three downed raiders is not a triple kill");

                // two feats from one kill: one title, named by the later feat
                Pawn g = NewColonist(root);
                KillHostiles(g, 2);
                Pawn king2 = Kit.SpawnHostile(map, root + new IntVec3(20, 0, 0));
                spawned.Add(king2);
                Pawn prevLeader = king2.Faction.leader;
                king2.Faction.leader = king2;
                Kit.KillBy(king2, g);
                if (king2.Faction.leader == king2 || king2.Faction.leader == null) king2.Faction.leader = prevLeader;
                Kit.Check(feats.Has(g, GestaDefOf.Gesta_Feat_TripleKill) && feats.Has(g, GestaDefOf.Gesta_Feat_Kingslayer), "one kill can earn two feats");
                Kit.Check(GestaDefOf.Gesta_Feat_Kingslayer.epithets.Contains(g.story.Title) && g.needs.mood.thoughts.memories.NumMemoriesOfDef(GestaDefOf.Gesta_DidAFeat) == 1,
                    $"...but gives one title ({g.story.Title}) and one memory");

                KillHostiles(c, 3);
                Kit.Check(feats.Has(c, GestaDefOf.Gesta_Feat_TripleKill), "custom-titled colonist still earns the feat");
                Kit.Check(c.story.Title == "Potato Baron", "a title the player typed is never replaced");

                // Lifeline: d carries the downed e while a hostile is close
                Kit.Down(e);
                Kit.Teleport(d, e.Position + IntVec3.East);
                d.drafter.Drafted = true;
                d.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.CarryDownedPawnDrafted, e));
                carryHostile = Kit.SpawnHostile(map, e.Position + new IntVec3(7, 0, 0));
                carryHostile.equipment?.DestroyAllEquipment();
                spawned.Add(carryHostile);
            }
            else if (ticks > 300 && ticks % 10 == 0 && carryHostile != null)
            {
                if (carryStartTick < 0 && d.carryTracker.CarriedThing == e) carryStartTick = ticks;
                // the drafted carry job sometimes fails to start on odd terrain; the feat is about carrying, so pick up directly
                if (carryStartTick < 0 && ticks == 420 && e.Spawned && !e.Dead)
                {
                    Kit.Teleport(d, e.Position + IntVec3.East);
                    d.carryTracker.TryStartCarry(e);
                }
                if (carryStartTick >= 0 && ticks - carryStartTick >= 300)
                {
                    bool los = carryHostile.Spawned && GenSight.LineOfSight(carryHostile.Position, d.Position, map);
                    Kit.Check(feats.Has(d, GestaDefOf.Gesta_Feat_Lifeline),
                        $"carrying a downed friend near the enemy is Lifeline (carrying {d.carryTracker.CarriedThing?.LabelShort ?? "nothing"}, hostile {(carryHostile.Dead ? "dead" : carryHostile.Downed ? "downed" : "up")} at {carryHostile.Position.DistanceTo(d.Position):0} cells, sight {los})");
                    Finish();
                }
                else if (ticks > 1450)
                {
                    Kit.Check(false, $"Lifeline setup: carrier never picked up the downed colonist (carrying {d.carryTracker.CarriedThing?.LabelShort ?? "nothing"}, job {d.CurJobDef?.defName})");
                    Finish();
                }
            }
        }

        void Finish()
        {
            foreach (Pawn p in spawned) if (!p.Dead && !p.Destroyed) p.Kill(null);
            if (d != null && d.drafter != null) d.drafter.Drafted = false;
            Kit.Summary("Gesta/Feats");
            done = true;
        }

        Pawn NewColonist(IntVec3 near)
        {
            for (int i = 0; i < 20; i++)
            {
                Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (p.WorkTagIsDisabled(WorkTags.Violent)) { Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard); continue; }
                GenSpawn.Spawn(p, CellFinder.StandableCellNear(near, map, 6f), map);
                return p;
            }
            throw new System.Exception("could not generate a violent-capable colonist");
        }

        void KillHostiles(Pawn killer, int n)
        {
            for (int i = 0; i < n; i++)
            {
                Pawn h = Kit.SpawnHostile(map, killer.Position + new IntVec3(20, 0, 0));
                spawned.Add(h);
                Kit.KillBy(h, killer);
            }
        }

        Pawn SpawnManhunter(string kind, IntVec3 near)
        {
            Pawn p = PawnGenerator.GeneratePawn(PawnKindDef.Named(kind), null);
            GenSpawn.Spawn(p, CellFinder.StandableCellNear(near + new IntVec3(15, 0, 0), map, 8f), map);
            p.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter, forced: true);
            spawned.Add(p);
            return p;
        }
    }
}
