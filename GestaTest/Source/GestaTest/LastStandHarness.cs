using System.Collections.Generic;
using System.Linq;
using Gesta;
using RimWorld;
using Verse;

namespace GestaTest
{
    /// <summary>
    /// The Last One Standing, driven through the real down/kill paths during `rw launch --ticks N`.
    /// Rules checked: DESIGN.md "Feature 1".
    /// </summary>
    public class LastStandHarness : GameComponent
    {
        bool active, done;
        int ticks;
        Map map;
        Pawn hero, c1, c2, c3;
        readonly List<Pawn> hostiles = new List<Pawn>();

        public LastStandHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            try { Step(); }
            catch (System.Exception ex) { Kit.Check(false, "last stand harness threw: " + ex); done = true; }
        }

        void Step()
        {
            var tracker = EngagementTracker.Instance;
            var ls = LastStandTracker.Instance;
            switch (ticks)
            {
                case 60:
                    map = Find.CurrentMap;
                    var fighters = EnsureFighters(4);
                    hero = fighters[0]; c1 = fighters[1]; c2 = fighters[2]; c3 = fighters[3];
                    hostiles.Add(SpawnUnarmedHostile());
                    hostiles.Add(SpawnUnarmedHostile());
                    tracker.PollNow();
                    Kit.Check(tracker.IsActive(map), "engagement starts when hostiles appear");
                    Kit.Check(tracker.Get(map).maxFighters >= 4, $"engagement counted {tracker.Get(map).maxFighters} fighters");
                    break;
                case 70:
                    Kit.Down(c1);
                    Kit.Down(c2);
                    break;
                case 80:
                    Kit.Check(ls.Hero == null, "no last stand while 2 fighters still stand");
                    Kit.Down(c3);
                    break;
                case 90:
                    Kit.Check(c1.Downed && c2.Downed && c3.Downed && !hero.Downed, "setup: three down, one standing");
                    Kit.Check(ls.Hero == null, "no last stand against only 2 hostiles");
                    hostiles.Add(SpawnUnarmedHostile());
                    tracker.PollNow();
                    break;
                case 95:
                    Kit.Check(ls.Hero == hero, $"last stand begins for the last one standing ({hero.LabelShort}) once a 3rd hostile arrives");
                    Kit.Check(hero.health.hediffSet.HasHediff(GestaDefOf.Gesta_LastStand), "hero gets the last stand hediff");
                    Kit.Check(!c1.health.hediffSet.HasHediff(GestaDefOf.Gesta_LastStand), "downed colonists do not get it");
                    Kit.Check(PawnNameColorUtility.PawnNameColorOf(hero) == LastStandTracker.Gold, "hero's map label is gold");
                    Kit.Check(PawnNameColorUtility.PawnNameColorOf(c1) != LastStandTracker.Gold, "others keep their label colour");
                    foreach (Pawn h in hostiles) if (!h.Dead) Kit.KillBy(h, hero);
                    tracker.PollNow();
                    break;
                case 105:
                    Kit.Check(!tracker.IsActive(map), "engagement ends when the hostiles are dead");
                    Kit.Check(ls.Hero == null, "last stand resolved");
                    Kit.Check(hero.needs.mood.thoughts.memories.GetFirstMemoryOfDef(GestaDefOf.Gesta_StoodAlone) != null, "hero remembers standing alone");
                    Kit.Check(Find.TaleManager.AllTalesListForReading.Any(t => t.def == GestaDefOf.Gesta_StoodAloneTale), "a 'stood alone' tale exists for art");
                    var saved = c1.needs.mood.thoughts.memories.Memories.FirstOrDefault(m => m.def == GestaDefOf.Gesta_SavedByLastStand);
                    Kit.Check(saved != null && saved.otherPawn == hero, "a colonist who was down remembers who stood over them");
                    Kit.Check(ls.HasCooldown(hero), "hero is on cooldown");

                    // a new fight where the hero is again the only fighter: cooldown must refuse it
                    for (int i = 0; i < 3; i++) hostiles.Add(SpawnUnarmedHostile());
                    tracker.PollNow();
                    tracker.Get(map).maxFighters = 4;
                    ls.Evaluate(map);
                    Kit.Check(ls.Hero == null, "cooldown blocks a second last stand for the same pawn");

                    // the crash when the hediff ends
                    hero.needs.rest.CurLevel = 0.9f;
                    Hediff h2 = hero.health.hediffSet.GetFirstHediffOfDef(GestaDefOf.Gesta_LastStand);
                    if (h2 != null) hero.health.RemoveHediff(h2);
                    Kit.Check(hero.needs.rest.CurLevel <= 0.151f, $"rest crashes when the last stand wears off ({hero.needs.rest.CurLevel:0.00})");
                    break;
                case 110:
                    foreach (Pawn h in hostiles) if (!h.Dead) h.Kill(null);
                    // slaves are not fighters (they cannot be the hero nor keep the colony from its last stand)
                    Pawn slave = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    GenSpawn.Spawn(slave, CellFinder.StandableCellNear(hero.Position, map, 5f), map);
                    slave.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);
                    Kit.Check(slave.IsSlave && !CombatUtil.IsFighter(slave), "slaves do not count as fighters");
                    slave.Destroy();
                    Kit.Summary("Gesta/LastStand");
                    done = true;
                    break;
            }
        }

        List<Pawn> EnsureFighters(int n)
        {
            var list = map.mapPawns.FreeColonistsSpawned.Where(CombatUtil.IsFighter).ToList();
            int guard = 0;
            while (list.Count < n && guard++ < 20)
            {
                Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (p.WorkTagIsDisabled(WorkTags.Violent)) { Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard); continue; }
                GenSpawn.Spawn(p, CellFinder.StandableCellNear(list[0].Position, map, 5f), map);
                list.Add(p);
            }
            return list;
        }

        Pawn SpawnUnarmedHostile()
        {
            Pawn p = Kit.SpawnHostile(map, hero.Position + new IntVec3(12, 0, 0));
            p.equipment?.DestroyAllEquipment();
            return p;
        }
    }
}
