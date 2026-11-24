using System.Linq;
using Gesta;
using RimWorld;
using Verse;

namespace GestaTest
{
    /// <summary>Brothers in Arms through three real fights. Runs at tick 2350, after the blood harness.</summary>
    public class BrothersHarness : GameComponent
    {
        bool active, done;
        int ticks;

        public BrothersHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done || ++ticks != 2350) return;
            done = true;
            try { Run(); }
            catch (System.Exception ex) { Kit.Check(false, "brothers harness threw: " + ex); }
            Kit.Summary("Gesta/Brothers");
        }

        static bool Has(Pawn p, HediffDef d) => p.health.hediffSet.HasHediff(d);

        void Run()
        {
            Map map = Find.CurrentMap;
            var tracker = BrothersTracker.Instance;
            var eng = EngagementTracker.Instance;
            IntVec3 root = map.mapPawns.FreeColonistsSpawned.First().Position + new IntVec3(0, 0, -10);
            Pawn a = NewColonist(map, root), b = NewColonist(map, root + IntVec3.East * 2), c = NewColonist(map, root + IntVec3.West * 2);

            for (int fight = 1; fight <= 3; fight++)
            {
                eng.PollNow();   // close anything open from earlier harnesses
                Pawn h = Kit.SpawnHostile(map, root + new IntVec3(6, 0, 3));
                Kit.Teleport(h, root + new IntVec3(6, 0, 3));
                h.equipment?.DestroyAllEquipment();
                eng.PollNow();
                if (!eng.IsActive(map)) { Kit.Check(false, $"setup: fight {fight} started"); return; }
                h.TakeDamage(new DamageInfo(DamageDefOf.Cut, 3f, 2f, -1f, a));
                h.TakeDamage(new DamageInfo(DamageDefOf.Cut, 3f, 2f, -1f, b));
                if (fight < 3) h.TakeDamage(new DamageInfo(DamageDefOf.Cut, 3f, 2f, -1f, c));
                h.Kill(null);
                eng.PollNow();
                if (fight == 2) Kit.Check(!BrothersTracker.AreBrothers(a, b) && tracker.SharedFights(a, b) == 2, "two shared fights are not enough");
            }

            Kit.Check(BrothersTracker.AreBrothers(a, b) && BrothersTracker.AreBrothers(b, a), "three shared fights make brothers in arms (both ways)");
            Kit.Check(Has(a, GestaDefOf.Gesta_BrotherSash) && Has(b, GestaDefOf.Gesta_BrotherSash), "both wear the sash");
            Kit.Check(!BrothersTracker.AreBrothers(a, c) && tracker.SharedFights(a, c) == 2, "a colonist who missed one fight is not a brother yet");
            Kit.Check(Find.LetterStack.LettersListForReading.Any(l => l.Label.ToString().Contains("Bonded in arms")), "a letter tells the story");
            Kit.Check(a.health.hediffSet.GetFirstHediffOfDef(GestaDefOf.Gesta_BrotherSash).def.HasDefinedGraphicProperties, "the sash has a render node");

            Kit.Down(b);
            Kit.Check(a.needs.mood.thoughts.memories.Memories.Any(m => m.def == GestaDefOf.Gesta_BrotherDowned && m.otherPawn == b),
                "seeing a brother go down hurts");

            b.Kill(null);
            Kit.Check(Has(a, GestaDefOf.Gesta_BrotherSashMourning) && !Has(a, GestaDefOf.Gesta_BrotherSash), "the survivor's sash turns black");
            Kit.Check(a.needs.mood.thoughts.memories.Memories.Count(m => (m.def == GestaDefOf.Gesta_BrotherInArmsDied || m.def == GestaDefOf.Gesta_SisterInArmsDied) && m.otherPawn == b) == 1,
                "the survivor grieves a brother in arms");
        }

        static Pawn NewColonist(Map map, IntVec3 at)
        {
            Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(p, at, map);
            return p;
        }
    }
}
