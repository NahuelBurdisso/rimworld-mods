using System.Collections.Generic;
using System.Linq;
using MachineVigil;
using RimWorld;
using Verse;
using Verse.AI;

namespace MachineVigilTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N`. Makes a mechanitor with 3 pikemen, kills them, and checks
    /// the mechs form a ring facing the corpse, a nearby colonist gets the memory and a far one does
    /// not, the ring follows a moved corpse, and a new overseer wakes exactly one mech.
    /// Negatives: a mech that never had this mechanitor does not join, a non-mechanitor death makes no vigil.
    /// </summary>
    public class VigilHarness : GameComponent
    {
        enum Phase { Idle, Gather, Moved, Woken, Done }

        bool active;
        int ticks;
        Phase phase = Phase.Idle;
        int phaseStart;

        Map map;
        Pawn mechanitor, witness, farColonist;
        List<Pawn> mechs = new List<Pawn>();
        Pawn stray;
        IntVec3 movedCenter;
        bool strayEverVigil;

        public VigilHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || phase == Phase.Done) return;
            ticks++;
            if (ticks == 60) { Start(); return; }
            if (ticks < 60 || ticks % 30 != 0) return;
            if (stray != null && GameComponent_MachineVigil.IsVigilJob(stray.CurJobDef)) strayEverVigil = true;
            switch (phase)
            {
                case Phase.Gather: PollGather(); break;
                case Phase.Moved: PollMoved(); break;
                case Phase.Woken: PollWoken(); break;
            }
        }

        GameComponent_MachineVigil Comp => GameComponent_MachineVigil.Instance;

        static bool OpenArea(IntVec3 c, Map m) =>
            GenRadial.RadialCellsAround(c, 5f, true).All(x => x.InBounds(m) && x.Standable(m) && !x.Fogged(m) && x.GetEdifice(m) == null);

        void Start()
        {
            map = Find.CurrentMap;
            List<Pawn> colonists = Kit.Colonists(map);
            if (colonists.Count < 3) { Kit.Check(false, "need 3 colonists, found " + colonists.Count); Finish(); return; }
            mechanitor = colonists[0];
            witness = colonists[1];
            farColonist = colonists[2];

            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 40, c => OpenArea(c, map), out IntVec3 center))
            { Kit.Check(false, "found an open area for the test"); Finish(); return; }
            if (!CellFinder.TryFindRandomCellNear(center, map, 25, c => OpenArea(c, map) && c.DistanceTo(center) >= 9f && c.DistanceTo(center) <= 14f, out movedCenter))
            { Kit.Check(false, "found a second open area to move the corpse"); Finish(); return; }

            // 1. a mechanitor
            mechanitor.health.AddHediff(HediffDefOf.MechlinkImplant, mechanitor.health.hediffSet.GetBrain());
            PawnComponentsUtility.AddAndRemoveDynamicComponents(mechanitor);
            Kit.Check(mechanitor.mechanitor != null, $"{mechanitor.LabelShort} is a mechanitor");
            Kit.Teleport(mechanitor, center);

            // 2. three pikemen under their command, spawned ~10 cells away
            for (int i = 0; i < 3; i++)
            {
                Pawn mech = PawnGenerator.GeneratePawn(PawnKindDefOf.Mech_Pikeman, Faction.OfPlayer);
                IntVec3 cell = CellFinder.StandableCellNear(center + new IntVec3(10, 0, i * 2 - 2), map, 4f);
                GenSpawn.Spawn(mech, cell.IsValid ? cell : center, map);
                mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
                mechs.Add(mech);
            }
            Kit.Check(mechs.All(m => m.IsColonyMechPlayerControlled), "3 pikemen controlled by the mechanitor");

            // a 4th player mech that never belonged to this mechanitor (negative)
            stray = PawnGenerator.GeneratePawn(PawnKindDefOf.Mech_Pikeman, Faction.OfPlayer);
            GenSpawn.Spawn(stray, CellFinder.StandableCellNear(center + new IntVec3(-8, 0, 0), map, 4f), map);

            // witness 4 cells from the body (drafted to stay put); the other colonist far away
            Kit.Teleport(witness, CellFinder.StandableCellNear(center + new IntVec3(0, 0, 4), map, 1f));
            witness.drafter.Drafted = true;
            Kit.Teleport(farColonist, CellFinder.StandableCellNear(center + new IntVec3(-20, 0, -20), map, 5f));
            farColonist.drafter.Drafted = true;

            // negative: a plain colonist-like death creates no vigil
            int before = Comp.Vigils.Count;
            Pawn stranger = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, null);
            GenSpawn.Spawn(stranger, CellFinder.RandomClosewalkCellNear(center + new IntVec3(0, 0, -15), map, 4), map);
            stranger.Kill(null, null);
            Kit.Check(Comp.Vigils.Count == before, "a non-mechanitor death creates no vigil");

            // 3. kill the mechanitor
            mechanitor.Kill(null, null);
            Kit.Check(mechanitor.Dead, "mechanitor died");
            Vigil v = Comp.VigilOfMechanitor(mechanitor);
            Kit.Check(v != null && v.mechs.Count == 3 && mechs.All(m => v.mechs.Contains(m)),
                $"one vigil with the 3 mechs (got {v?.mechs.Count ?? -1})");
            Kit.Check(mechs.All(m => m.IsColonyMechRequiringMechanitor()), "all 3 mechs now require a mechanitor");
            Kit.Check(Comp.VigilFor(stray) == null, "the stray mech is not in the vigil");
            Kit.Check(v != null && v.TryGetCenter(out IntVec3 c0, out Map m0) && m0 == map && c0 == mechanitor.Corpse?.PositionHeld,
                "vigil center is the corpse");

            phase = Phase.Gather;
            phaseStart = ticks;
        }

        bool AllKeeping(IntVec3 center, IEnumerable<Pawn> who) =>
            who.All(m => m.Spawned && m.CurJobDef == MachineVigilDefOf.MachineVigil_Keep && JobGiver_KeepVigil.InRing(m.Position, center));

        void PollGather()
        {
            Corpse corpse = mechanitor.Corpse;
            IntVec3 center = corpse.PositionHeld;
            if (AllKeeping(center, mechs) || ticks - phaseStart > 2400)
            {
                Kit.Check(AllKeeping(center, mechs),
                    "all 3 mechs keep vigil in the ring: " + string.Join(", ", mechs.Select(m => $"{m.CurJobDef?.defName} d={m.Position.DistanceTo(center):F1}")));
                Kit.Check(mechs.All(m => m.Rotation == JobGiver_KeepVigil.FacingTowards(m.Position, center)),
                    "every mech faces the body: " + string.Join(", ", mechs.Select(m => $"{m.Rotation}/{JobGiver_KeepVigil.FacingTowards(m.Position, center)}")));
                Kit.Check(mechs.Select(m => m.Position).Distinct().Count() == 3, "the mechs stand on 3 different cells");
                string report = mechs[0].jobs.curDriver?.GetReport() ?? "";
                Kit.Check(report.Contains(mechanitor.LabelShort), $"inspect report names the mechanitor: \"{report}\"");
                Vigil v = Comp.VigilOfMechanitor(mechanitor);
                Kit.Check(v != null && v.announced, "the vigil was announced");
                Kit.Check(HasMemory(witness), $"{witness.LabelShort} (4 cells away, LOS) got the vigil memory about {mechanitor.LabelShort}");
                Kit.Check(!HasMemory(farColonist), $"{farColonist.LabelShort} (far away) did not get the memory");

                // 4. move the corpse: the ring follows
                corpse.DeSpawn();
                GenSpawn.Spawn(corpse, movedCenter, map);
                phase = Phase.Moved;
                phaseStart = ticks;
            }
        }

        void PollMoved()
        {
            IntVec3 center = mechanitor.Corpse.PositionHeld;
            if (AllKeeping(center, mechs) || ticks - phaseStart > 2400)
            {
                Kit.Check(center == movedCenter && AllKeeping(center, mechs),
                    $"after moving the corpse {(ticks - phaseStart) / 60f:F0}s ago, the ring re-formed around it: " +
                    string.Join(", ", mechs.Select(m => $"{m.CurJobDef?.defName} d={m.Position.DistanceTo(center):F1}")));

                // 5. a new mechanitor takes mech 0
                farColonist.health.AddHediff(HediffDefOf.MechlinkImplant, farColonist.health.hediffSet.GetBrain());
                PawnComponentsUtility.AddAndRemoveDynamicComponents(farColonist);
                farColonist.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mechs[0]);
                Kit.Check(Comp.VigilFor(mechs[0]) == null, "the reclaimed mech left the vigil at once");
                Kit.Check(!GameComponent_MachineVigil.IsVigilJob(mechs[0].CurJobDef), $"the reclaimed mech dropped its vigil job (now {mechs[0].CurJobDef?.defName})");
                Kit.Check(mechs[0].IsColonyMechPlayerControlled, "the reclaimed mech is controlled again");
                Kit.Check(Comp.VigilFor(mechs[1]) != null && Comp.VigilFor(mechs[2]) != null, "the other two stay in the vigil");
                phase = Phase.Woken;
                phaseStart = ticks;
            }
        }

        void PollWoken()
        {
            if (ticks - phaseStart < 600) return;
            IntVec3 center = mechanitor.Corpse.PositionHeld;
            Kit.Check(AllKeeping(center, new[] { mechs[1], mechs[2] }), "10s later the two unclaimed mechs still keep vigil");
            Kit.Check(!GameComponent_MachineVigil.IsVigilJob(mechs[0].CurJobDef), $"the reclaimed mech did not go back (job {mechs[0].CurJobDef?.defName})");
            Kit.Check(!strayEverVigil && Comp.VigilFor(stray) == null, "the stray mech never kept vigil");
            Finish();
        }

        bool HasMemory(Pawn p) =>
            p.needs?.mood?.thoughts?.memories?.Memories.Any(m => m.def == MachineVigilDefOf.MachineVigil_WitnessedVigil && m.otherPawn == mechanitor) == true;

        void Finish()
        {
            phase = Phase.Done;
            Kit.Summary("Machine Vigil");
        }
    }
}
