using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WindowWatchers
{
    /// <summary>
    /// Watching a fighting loved one (A) from a safe roofed cell (B), optionally through a door (C) kept ajar.
    /// Reacts to the loved one being hurt (bubble trembles red, small memory), going down or dying (bigger memory, ends).
    /// </summary>
    public class JobDriver_WatchLovedOne : JobDriver
    {
        public const int HurtShakeTicks = 150;
        public const int HurtThoughtCooldown = 600;
        public const int GriefShakeTicks = 200;

        float lastHealth = -1f;
        int lastHurtTick = -99999;
        MoteBubble_Worry mote;

        public Pawn Target => job.targetA.Thing as Pawn;
        public IntVec3 Spot => job.targetB.Cell;
        public Building_Door Door => job.targetC.Thing as Building_Door;
        public MoteBubble_Worry Mote => mote;
        public bool Watching => CurToil != null && CurToil.debugName == "WW_Watch";

        MapComponent_WindowWatchers Comp => MapComponent_WindowWatchers.For(pawn.Map);
        bool StillMine => pawn.CurJob == job && !ended;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastHealth, "lastHealth", -1f);
            Scribe_Values.Look(ref lastHurtTick, "lastHurtTick", -99999);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.Map.pawnDestinationReservationManager.Reserve(pawn, job, Spot);
            return true;
        }

        public override bool IsContinuation(Job j) => j.targetA == job.targetA && j.targetB == job.targetB;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil go = Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            go.AddEndCondition(() =>
            {
                Pawn t = Target;
                MapComponent_WindowWatchers comp = Comp;
                if (t == null || t.Dead || t.Downed || !t.Spawned || t.Map != pawn.Map) return JobCondition.Incompletable;
                if (comp == null || !comp.ThreatActive || !WatcherUtility.IsFighter(t, pawn.Map)) return JobCondition.Succeeded;
                return JobCondition.Ongoing;
            });
            go.tickIntervalAction = delta =>
            {
                if (pawn.IsHashIntervalTick(60, delta) && WatcherUtility.ThreatTooClose(pawn.Map, Spot, Door))
                    EndJobWith(JobCondition.Incompletable);
            };
            yield return go;

            Toil watch = ToilMaker.MakeToil("WW_Watch");
            watch.initAction = () =>
            {
                Pawn t = Target;
                if (t == null) return;
                Comp?.Notify_Watching(pawn, t);
                lastHealth = t.health.summaryHealth.SummaryHealthPercent;
                Building_Door door = Door;
                if (door != null && door.Spawned) door.StartManualOpenBy(pawn);
            };
            watch.tickAction = () =>
            {
                Pawn t = Target;
                if (t == null) return;
                if (t.Spawned) pawn.rotationTracker.FaceTarget(t);
                if (mote == null || mote.Destroyed) mote = MoteBubble_Worry.Make(WWDefOf.WW_Mote_Worry, pawn, t);
                mote.Maintain();
                Building_Door door = Door;
                if (door != null && door.Spawned && pawn.IsHashIntervalTick(60)) door.StartManualOpenBy(pawn);
            };
            watch.tickIntervalAction = delta =>
            {
                if (!pawn.IsHashIntervalTick(30, delta)) return;
                CheckTarget();
                if (StillMine) CheckSpot();
            };
            watch.defaultCompleteMode = ToilCompleteMode.Never;
            watch.handlingFacing = true;
            watch.socialMode = RandomSocialMode.Off;
            yield return watch;
        }

        /// <summary>Loved one dead / down / gone / safe / hurt. Public so the harness can force a check.</summary>
        public void CheckTarget()
        {
            Pawn t = Target;
            if (t == null) { EndJobWith(JobCondition.Incompletable); return; }
            if (t.Dead) { Fell(t, died: true); return; }
            if (t.Downed) { Fell(t, died: false); return; }
            if (!t.Spawned || t.Map != pawn.Map) { EndJobWith(JobCondition.Incompletable); return; }
            MapComponent_WindowWatchers comp = Comp;
            if (comp == null || !comp.ThreatActive || !WatcherUtility.IsFighter(t, pawn.Map)) { EndJobWith(JobCondition.Succeeded); return; }
            float h = t.health.summaryHealth.SummaryHealthPercent;
            if (lastHealth >= 0f && h < lastHealth - 0.0001f) Hurt(t);
            lastHealth = h;
        }

        /// <summary>Still sees the loved one (door still usable), no enemy close to the spot or the door. Every 30 ticks.</summary>
        public void CheckSpot()
        {
            Pawn t = Target;
            if (t == null || !t.Spawned) return;
            Map map = pawn.Map;
            if (!WatchSight.SeesTarget(Spot, t, map, Door, pawn)) { EndJobWith(JobCondition.Incompletable); return; }
            if (WatcherUtility.ThreatTooClose(map, pawn.Position, Door)) EndJobWith(JobCondition.Incompletable);
        }

        void Hurt(Pawn t)
        {
            mote?.Shake(HurtShakeTicks, MoteBubble_Worry.HurtTint);
            int now = Find.TickManager.TicksGame;
            if (now - lastHurtTick >= HurtThoughtCooldown)
            {
                lastHurtTick = now;
                WatcherUtility.GainMemory(pawn, WWDefOf.WW_WatchedHurt, t);
            }
        }

        void Fell(Pawn t, bool died)
        {
            WatcherUtility.GainMemory(pawn, died ? WWDefOf.WW_WatchedDie : WWDefOf.WW_WatchedFall, t);
            Comp?.Notify_SawFall(pawn, t);
            if (pawn.Spawned)
            {
                MoteBubble_Worry grief = MoteBubble_Worry.Make(WWDefOf.WW_Mote_Grief, pawn, t.Spawned ? t : null);
                grief.Shake(GriefShakeTicks, MoteBubble_Worry.GriefTint, keepTint: true);
            }
            EndJobWith(JobCondition.Incompletable);
        }
    }

    /// <summary>After the threat: go to the loved one who made it back (A) and embrace them.</summary>
    public class JobDriver_Reunite : JobDriver
    {
        public const int MaxWaitTicks = 300;

        int waited;

        Pawn Target => job.targetA.Thing as Pawn;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref waited, "waited");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            if (Target != null) MapComponent_WindowWatchers.For(pawn.Map)?.ConsumeReunion(pawn, Target);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Target.Downed || Target.Dead);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil hug = ToilMaker.MakeToil("WW_Embrace");
            hug.initAction = () => waited = 0;
            hug.tickIntervalAction = delta =>
            {
                Pawn t = Target;
                pawn.rotationTracker.FaceTarget(t);
                waited += delta;
                if (!pawn.Position.AdjacentTo8WayOrInside(t.Position))
                {
                    if (waited > MaxWaitTicks) EndJobWith(JobCondition.Incompletable);
                    else if (!pawn.pather.Moving) pawn.pather.StartPath(t, PathEndMode.Touch);
                    return;
                }
                if (pawn.interactions.CanInteractNowWith(t, WWDefOf.WW_Embrace))
                {
                    pawn.interactions.TryInteractWith(t, WWDefOf.WW_Embrace);
                    EndJobWith(JobCondition.Succeeded);
                }
                else if (waited > MaxWaitTicks) EndJobWith(JobCondition.Incompletable);
            };
            hug.defaultCompleteMode = ToilCompleteMode.Never;
            hug.handlingFacing = true;
            hug.socialMode = RandomSocialMode.Off;
            yield return hug;
        }
    }
}
