using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace PackHowls
{
    /// <summary>
    /// Sit still facing targetA (where the fallen died; north if none), howl twice and send faint rings up.
    /// One Delay toil; JobDriver.DriverTick runs every tick and counts ticksLeftThisToil down.
    /// </summary>
    public class JobDriver_Howl : JobDriver
    {
        public const int Duration = 420;
        public const int SecondHowlAt = 210;
        public const int RingEvery = 35;
        public const int RingsPerHowl = 4;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        private int Stagger => pawn.thingIDNumber % 40;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil howl = ToilMaker.MakeToil("Howl");
            howl.initAction = () => pawn.pather?.StopDead();
            howl.handlingFacing = true;
            howl.tickAction = () =>
            {
                Face();
                // DriverTick decrements ticksLeftThisToil before tickAction runs: first call sees Duration - 1.
                int elapsed = Duration - 1 - ticksLeftThisToil - Stagger;
                if (elapsed < 0) return;
                if (elapsed == 0 || elapsed == SecondHowlAt) PackHowlsUtility.PlayHowl(pawn);
                int sinceHowl = elapsed >= SecondHowlAt ? elapsed - SecondHowlAt : elapsed;
                if (sinceHowl % RingEvery == 0 && sinceHowl < RingEvery * RingsPerHowl) PackHowlsUtility.MakeRing(pawn);
            };
            howl.defaultCompleteMode = ToilCompleteMode.Delay;
            howl.defaultDuration = Duration;
            yield return howl;
        }

        private void Face()
        {
            LocalTargetInfo t = job.targetA;
            if (t.IsValid && t.Cell != pawn.Position) pawn.rotationTracker.FaceCell(t.Cell);
            else pawn.Rotation = Rot4.North;
        }
    }
}
