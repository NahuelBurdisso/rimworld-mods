using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace TheDogKnows
{
    /// <summary>
    /// Stand still, keep facing the hidden entity, growl at the start and halfway, shiver.
    /// The entity is a private field (saved by reference), never a job target: see TDKUtility.StartStare.
    /// One Delay toil; the length comes from the race's UnseenSenseDef.stareTicks.
    /// </summary>
    public class JobDriver_StareAtUnseen : JobDriver
    {
        public const int CheckEvery = 30;
        public const int ShiverEvery = 12;

        /// <summary>Handed over by TDKUtility.StartStare for the duration of StartJob.</summary>
        internal static Pawn Pending;

        private Pawn unseen;
        private int duration;
        private int staring;
        [Unsaved(false)] private UnseenSenseDef senseDef;

        public Pawn Unseen => unseen;
        public int Duration => duration;
        private UnseenSenseDef SenseDef => senseDef ??= UnseenSenseDef.For(pawn.def);

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            if (unseen == null) unseen = Pending;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // a despawned entity may not be saved anywhere else: drop it, the stare ends at the next check
            if (Scribe.mode == LoadSaveMode.Saving && unseen != null && !unseen.Spawned) unseen = null;
            Scribe_References.Look(ref unseen, "unseen");
            Scribe_Values.Look(ref duration, "duration", 0);
            Scribe_Values.Look(ref staring, "staring", 0);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil stare = ToilMaker.MakeToil("StareAtUnseen");
            // Runs once at the start only: after a load the toil resumes with ticksLeftThisToil from the save.
            stare.initAction = () =>
            {
                pawn.pather?.StopDead();
                duration = SenseDef != null ? SenseDef.stareTicks.RandomInRange : 360;
                ticksLeftThisToil = duration;
                Face();
                TDKUtility.Hackles(pawn);
            };
            stare.handlingFacing = true;
            stare.tickAction = () =>
            {
                Face();
                if (SenseDef != null && (staring == 0 || staring == duration / 2)) TDKUtility.Growl(pawn, SenseDef);
                if (staring % ShiverEvery == 0) TDKUtility.Shiver(pawn);
                staring++;
                if (staring % CheckEvery == 0 && !StillSensing())
                    EndJobWith(JobCondition.Succeeded);
            };
            stare.defaultCompleteMode = ToilCompleteMode.Delay;
            stare.defaultDuration = 9999; // replaced in initAction
            yield return stare;
        }

        /// <summary>Still hidden, on this map, within radius x 1.5 and (if the def says so) in sight.</summary>
        private bool StillSensing()
        {
            if (SenseDef == null || !TDKUtility.IsUnseen(unseen)) return false;
            return TDKUtility.Senses(pawn, unseen, SenseDef, TDKUtility.LoseInterestFactor);
        }

        private void Face()
        {
            if (unseen != null && unseen.Spawned && unseen.Map == pawn.Map) pawn.rotationTracker.Face(unseen.DrawPos);
        }
    }
}
