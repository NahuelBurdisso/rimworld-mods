using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace OverTheBody
{
    /// <summary>
    /// Standing beside the fallen colonist (TargetA). Vanilla wait: auto-melee against hostiles adjacent to the animal.
    /// Faces the nearest threat; ends when the colonist gets up, is moved, or a hostile comes within bite range.
    /// </summary>
    public class JobDriver_StandOver : JobDriver_Wait
    {
        Pawn Master => job.targetA.Thing as Pawn;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddEndCondition(() =>
            {
                Pawn m = Master;
                if (m == null || m.Dead || !m.Downed || !OverTheBodyUtility.AdjacentTo(pawn, m)) return JobCondition.Incompletable;
                return JobCondition.Ongoing;
            });
            foreach (Toil t in base.MakeNewToils()) yield return t;
        }

        public override void DecorateWaitToil(Toil wait)
        {
            Thing facing = null;
            bool looked = false;
            wait.handlingFacing = true;
            wait.tickAction = (Action)Delegate.Combine(wait.tickAction, (Action)(() =>
            {
                Pawn m = Master;
                if (m == null) return;
                if (!looked || pawn.IsHashIntervalTick(30))
                {
                    looked = true;
                    facing = OverTheBodyUtility.NearestThreat(m);
                    if (pawn.kindDef.canMeleeAttack && OverTheBodyUtility.BiteTarget(pawn, m) != null)
                    {
                        EndJobWith(JobCondition.InterruptForced);   // re-think: the giver hands out the bite
                        return;
                    }
                }
                if (facing != null && facing.Spawned && facing.Map == pawn.Map) pawn.rotationTracker.FaceTarget(facing);
                else pawn.rotationTracker.FaceTarget(m);
            }));
        }
    }

    /// <summary>Sprinting to a cell beside the fallen colonist (TargetC).</summary>
    public class JobDriver_RushToFallen : JobDriver_Goto
    {
        /// <summary>A rush to another cell (the colonist crawled or was moved) replaces this one on expiry.</summary>
        public override bool IsContinuation(Job j) => j.targetA == job.targetA;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddEndCondition(() =>
            {
                Pawn m = job.targetC.Thing as Pawn;
                return m == null || m.Dead || !m.Downed ? JobCondition.Incompletable : JobCondition.Ongoing;
            });
            foreach (Toil t in base.MakeNewToils()) yield return t;
        }
    }

    /// <summary>
    /// Melee against a hostile near the fallen colonist (TargetC). Leashed: lets go when the target is more than
    /// 5 cells from the body, unless the target is the one carrying the colonist away.
    /// </summary>
    public class JobDriver_BiteForFallen : JobDriver_AttackMelee
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddEndCondition(() =>
            {
                Pawn m = job.targetC.Thing as Pawn;
                Thing target = job.targetA.Thing;
                if (m == null || m.Dead || !m.Downed || target == null) return JobCondition.Incompletable;
                if (m.CarriedBy != null && m.CarriedBy == target) return JobCondition.Ongoing;
                if (!m.Spawned || !target.Spawned || target.Map != m.Map) return JobCondition.Incompletable;
                if (!target.Position.InHorDistOf(m.Position, OverTheBodyUtility.LeashRadius)) return JobCondition.Incompletable;
                return JobCondition.Ongoing;
            });
            foreach (Toil t in base.MakeNewToils()) yield return t;
        }
    }

    /// <summary>
    /// Staying within followRadius of whoever carries the fallen colonist (TargetA = carrier, TargetC = colonist).
    /// Unlike vanilla FollowClose it does not end when already close, so it never churns jobs while the carrier stands
    /// still; it ends when the colonist is put down (or gets up).
    /// </summary>
    public class JobDriver_StayWithCarried : JobDriver
    {
        Pawn Carrier => job.targetA.Thing as Pawn;
        Pawn Master => job.targetC.Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override bool IsContinuation(Job j) => j.targetA == job.targetA;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            AddEndCondition(() =>
            {
                Pawn m = Master;
                return m != null && !m.Dead && m.Downed && m.CarriedBy == Carrier ? JobCondition.Ongoing : JobCondition.Succeeded;
            });
            Toil stay = ToilMaker.MakeToil("StayWithCarried");
            stay.tickIntervalAction = (int delta) =>
            {
                Pawn c = Carrier;
                if (c == null || (pawn.pather.Moving && !pawn.IsHashIntervalTick(30, delta))) return;
                if (pawn.Position.InHorDistOf(c.Position, job.followRadius))
                {
                    if (pawn.pather.Moving && !c.pather.Moving) pawn.pather.StopDead();
                    return;
                }
                if (pawn.pather.Moving && pawn.pather.Destination.Thing == c) return;
                if (!pawn.CanReach(c, PathEndMode.Touch, Danger.Deadly))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                pawn.pather.StartPath(c, PathEndMode.Touch);
            };
            stay.defaultCompleteMode = ToilCompleteMode.Never;
            yield return stay;
        }
    }
}
