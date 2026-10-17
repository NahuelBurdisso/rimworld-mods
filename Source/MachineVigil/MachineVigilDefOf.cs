using RimWorld;
using Verse;

namespace MachineVigil
{
    [DefOf]
    public static class MachineVigilDefOf
    {
        public static JobDef MachineVigil_Approach;
        public static JobDef MachineVigil_Keep;
        public static ThoughtDef MachineVigil_WitnessedVigil;

        static MachineVigilDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(MachineVigilDefOf)); }
    }
}
