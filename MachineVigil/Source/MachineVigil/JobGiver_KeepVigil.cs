using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MachineVigil
{
    /// <summary>
    /// Inserted into the Mechanoid think tree right before the top-level Idle tagger (see
    /// Patches/MachineVigil_ThinkTree.xml). Gives a mech of a dead mechanitor a spot in a ring
    /// around the body and keeps it standing there facing the center.
    /// </summary>
    public class JobGiver_KeepVigil : ThinkNode_JobGiver
    {
        public const float MinRadius = 2f;
        public const float MaxRadius = 3.5f;
        public const int KeepExpiry = 250;
        public const int ApproachExpiry = 300;

        static readonly List<IntVec3> candidates = new List<IntVec3>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!pawn.IsColonyMech) return null;
            Vigil v = GameComponent_MachineVigil.Instance?.VigilFor(pawn);
            if (v == null || !pawn.IsColonyMechRequiringMechanitor()) return null;
            if (!v.TryGetCenter(out IntVec3 center, out Map map) || map != pawn.Map) return null;

            IntVec3 pos = pawn.Position;
            if (InRing(pos, center) && pos.StandableBy(map, pawn) && map.pawnDestinationReservationManager.CanReserve(pos, pawn))
                return KeepJob(pos, center);

            IntVec3 cell = FindRingCell(pawn, v, center, map);
            if (!cell.IsValid) return null;
            Job job = JobMaker.MakeJob(MachineVigilDefOf.MachineVigil_Approach, cell);
            job.locomotionUrgency = LocomotionUrgency.Walk;
            job.expiryInterval = ApproachExpiry;          // re-think while walking: enemies in range win
            job.checkOverrideOnExpire = true;
            return job;
        }

        public static bool InRing(IntVec3 cell, IntVec3 center)
        {
            float d = cell.DistanceTo(center);
            return d >= MinRadius - 0.01f && d <= MaxRadius + 0.01f;
        }

        public static Rot4 FacingTowards(IntVec3 from, IntVec3 center) => Rot4.FromAngleFlat((center - from).AngleFlat);

        static Job KeepJob(IntVec3 pos, IntVec3 center)
        {
            Job job = JobMaker.MakeJob(MachineVigilDefOf.MachineVigil_Keep);
            job.expiryInterval = KeepExpiry;
            job.overrideFacing = FacingTowards(pos, center);
            return job;
        }

        /// <summary>Free, standable, reachable ring cell closest to this mech's slot angle (360*i/n).</summary>
        public static IntVec3 FindRingCell(Pawn pawn, Vigil v, IntVec3 center, Map map)
        {
            int n = Mathf.Max(1, v.mechs.Count);
            int slot = Mathf.Max(0, v.SlotOf(pawn));
            float slotAngle = 360f * slot / n;

            candidates.Clear();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(center, MinRadius, MaxRadius))
            {
                if (!c.InBounds(map) || !c.StandableBy(map, pawn)) continue;
                if (!map.pawnDestinationReservationManager.CanReserve(c, pawn)) continue;
                candidates.Add(c);
            }
            // prefer cells that can see the body (not the far side of a wall), then the slot angle
            candidates.SortBy(c => (GenSight.LineOfSight(c, center, map) ? 0f : 1000f)
                + Mathf.Abs(Mathf.DeltaAngle((c - center).AngleFlat, slotAngle)) + c.DistanceTo(center) * 0.5f);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (pawn.CanReach(candidates[i], PathEndMode.OnCell, Danger.Some))
                {
                    IntVec3 found = candidates[i];
                    candidates.Clear();
                    return found;
                }
            }
            candidates.Clear();
            return IntVec3.Invalid;
        }
    }
}
