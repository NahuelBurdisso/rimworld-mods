using RimWorld;
using Verse;
using Verse.AI;

namespace OverTheBody
{
    /// <summary>
    /// Lives in ThinkTreeDef OverTheBody_Guard (insertTag Animal_PreMain). Returns null unless this animal is bonded
    /// to a downed colonist on its map who is in danger (or who it was already guarding and is being carried away).
    /// </summary>
    public class JobGiver_StandOverMaster : ThinkNode_JobGiver
    {
        public const int RushExpiry = 120;
        public const int GuardExpiry = 250;
        public const int BiteExpiry = 200;
        public const int FollowExpiry = 140;

        protected override Job TryGiveJob(Pawn pawn) => TryMakeJob(pawn, notify: true);

        /// <summary>
        /// The whole decision. With notify false it has no side effects (no record, no message): the component uses it
        /// to know whether waking / re-thinking the animal would actually give it one of our jobs.
        /// </summary>
        public static Job TryMakeJob(Pawn pawn, bool notify)
        {
            if (!OverTheBodyUtility.CanGuard(pawn)) return null;
            Pawn master = OverTheBodyUtility.FallenMasterFor(pawn);
            if (master == null) return null;
            GameComponent_OverTheBody comp = GameComponent_OverTheBody.Instance;
            if (comp == null) return null;

            Pawn carrier = master.CarriedBy;
            if (carrier != null)
            {
                // 1. kidnapped: go for the carrier, no leash
                if (carrier.HostileTo(Faction.OfPlayer))
                {
                    if (!pawn.kindDef.canMeleeAttack || !pawn.CanReach(carrier, PathEndMode.Touch, Danger.Deadly)) return null;
                    if (notify) comp.Notify_Guarding(pawn, master);
                    return BiteJob(carrier, master);
                }
                // 2. a friend carries them: follow, but only if we were already guarding them
                GuardRecord r = comp.RecordFor(pawn);
                if (r == null || r.master != master) return null;
                if (!pawn.CanReach(carrier, PathEndMode.Touch, Danger.Deadly)) return null;
                Job follow = JobMaker.MakeJob(OverTheBodyDefOf.OverTheBody_Follow, carrier, LocalTargetInfo.Invalid, master);
                follow.expiryInterval = FollowExpiry;
                follow.checkOverrideOnExpire = true;
                follow.followRadius = OverTheBodyUtility.FollowRadius;
                follow.locomotionUrgency = LocomotionUrgency.Jog;
                return follow;
            }

            if (!master.Spawned || master.Map != pawn.Map) return null;
            if (!OverTheBodyUtility.InDanger(master)) return null;

            // 3. a hostile standing over the body: bite it
            if (pawn.kindDef.canMeleeAttack)
            {
                Pawn biteTarget = OverTheBodyUtility.BiteTarget(pawn, master);
                if (biteTarget != null)
                {
                    if (notify) comp.Notify_Guarding(pawn, master);
                    return BiteJob(biteTarget, master);
                }
            }

            // 5. already beside the body: stand guard
            if (OverTheBodyUtility.AdjacentTo(pawn, master) && pawn.Map.pawnDestinationReservationManager.CanReserve(pawn.Position, pawn))
            {
                if (notify) comp.Notify_Guarding(pawn, master);
                Job guard = JobMaker.MakeJob(OverTheBodyDefOf.OverTheBody_Guard, master);
                guard.expiryInterval = GuardExpiry;
                guard.checkOverrideOnExpire = true;
                return guard;
            }

            // 4. run to the body, on the side of the threat
            IntVec3 cell = OverTheBodyUtility.GuardCell(pawn, master, OverTheBodyUtility.NearestThreat(master));
            if (!cell.IsValid) return null;
            if (notify) comp.Notify_Guarding(pawn, master);
            Job rush = JobMaker.MakeJob(OverTheBodyDefOf.OverTheBody_Rush, cell, LocalTargetInfo.Invalid, master);
            rush.locomotionUrgency = LocomotionUrgency.Sprint;
            rush.expiryInterval = RushExpiry;
            rush.checkOverrideOnExpire = true;
            return rush;
        }

        static Job BiteJob(Pawn target, Pawn master)
        {
            Job job = JobMaker.MakeJob(OverTheBodyDefOf.OverTheBody_Bite, target, LocalTargetInfo.Invalid, master);
            job.maxNumMeleeAttacks = 3;
            job.expiryInterval = BiteExpiry;
            job.killIncappedTarget = false;
            return job;
        }
    }
}
