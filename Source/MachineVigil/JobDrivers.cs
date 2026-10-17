using Verse;
using Verse.AI;

namespace MachineVigil
{
    /// <summary>Vanilla wait (auto-melee, facing via job.overrideFacing) with a vigil report string.</summary>
    public class JobDriver_KeepVigil : JobDriver_Wait
    {
        public override string GetReport()
        {
            Pawn dead = GameComponent_MachineVigil.Instance?.VigilFor(pawn)?.mechanitor;
            return dead != null ? (string)"MachineVigil_ReportKeep".Translate(dead.Named("PAWN")) : base.GetReport();
        }
    }

    /// <summary>Vanilla goto to the ring cell with a vigil report string.</summary>
    public class JobDriver_ApproachVigil : JobDriver_Goto
    {
        public override string GetReport()
        {
            Pawn dead = GameComponent_MachineVigil.Instance?.VigilFor(pawn)?.mechanitor;
            return dead != null ? (string)"MachineVigil_ReportApproach".Translate(dead.Named("PAWN")) : base.GetReport();
        }
    }
}
