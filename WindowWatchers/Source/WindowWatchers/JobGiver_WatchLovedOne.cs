using RimWorld;
using Verse;
using Verse.AI;

namespace WindowWatchers
{
    /// <summary>
    /// Lives in ThinkTreeDef WW_WatchLovedOne (insertTag Humanlike_PreMain). During a threat: the plan the map scan made
    /// for this pawn, re-validated cheaply. After it: the pending embrace. Otherwise null (costs a dictionary lookup).
    /// </summary>
    public class JobGiver_WatchLovedOne : ThinkNode_JobGiver
    {
        public const int WatchExpiry = 250;
        public const int ReuniteExpiry = 1200;

        protected override Job TryGiveJob(Pawn pawn) => TryMakeJob(pawn);

        public static Job TryMakeJob(Pawn pawn)
        {
            if (pawn?.Map == null || !pawn.IsColonist) return null;
            MapComponent_WindowWatchers comp = MapComponent_WindowWatchers.For(pawn.Map);
            if (comp == null) return null;
            return comp.ThreatActive ? WatchJob(pawn, comp) : ReuniteJob(pawn, comp);
        }

        static Job WatchJob(Pawn pawn, MapComponent_WindowWatchers comp)
        {
            Job cur = pawn.CurJob;
            bool alreadyWatching = cur != null && cur.def == WWDefOf.WW_Watch;
            WatchPlan plan;
            if (alreadyWatching)
            {
                // keep the current spot even before the first scan after a load (plans are not saved)
                plan = new WatchPlan { target = cur.targetA.Pawn, spot = new WatchSpot { cell = cur.targetB.Cell, door = cur.targetC.Thing as Building_Door } };
            }
            else if (!comp.TryGetPlan(pawn, out plan)) return null;
            Pawn target = plan.target;
            Map map = pawn.Map;
            if (!WatcherUtility.CanBeWatcher(pawn) || !WatcherUtility.NeedsAllowWatching(pawn, comp.PatientsWaiting)) return null;
            if (!WatcherUtility.IsFighter(target, map)) return null;
            IntVec3 cell = plan.spot.cell;
            if (!cell.Standable(map)) return null;
            if (!map.pawnDestinationReservationManager.CanReserve(cell, pawn)) return null;
            if (!WatchSight.SeesTarget(cell, target, map, plan.spot.door, pawn)) return null;
            if (WatcherUtility.ThreatTooClose(map, cell, plan.spot.door)) return null;

            Job job = JobMaker.MakeJob(WWDefOf.WW_Watch, target, cell, plan.spot.door != null ? (LocalTargetInfo)plan.spot.door : LocalTargetInfo.Invalid);
            job.expiryInterval = WatchExpiry;
            job.checkOverrideOnExpire = true;
            job.locomotionUrgency = LocomotionUrgency.Jog;
            return job;
        }

        static Job ReuniteJob(Pawn pawn, MapComponent_WindowWatchers comp)
        {
            if (!comp.TryGetReunion(pawn, out Pawn target)) return null;
            if (!WatcherUtility.CanBeWatcher(pawn) || !WatcherUtility.NeedsAllowWatching(pawn, comp.PatientsWaiting)) return null;
            if (target == null || target.Dead || target.Downed || !target.Spawned || target.Map != pawn.Map) return null;
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking)) { comp.ConsumeReunion(pawn, target); return null; }
            if (!pawn.CanReach(target, PathEndMode.Touch, Danger.Some)) return null;
            Job job = JobMaker.MakeJob(WWDefOf.WW_Reunite, target);
            job.expiryInterval = ReuniteExpiry;
            job.locomotionUrgency = LocomotionUrgency.Jog;
            return job;
        }
    }
}
