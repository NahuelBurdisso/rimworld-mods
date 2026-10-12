using RimWorld;
using Verse;

namespace Omens
{
    [DefOf]
    public static class OmensDefOf
    {
        public static ThingDef Omens_Crow;
        public static ThingDef Omens_ColdCamp;
        public static FleckDef Omens_RainSplash;
        public static FleckDef Omens_SnowDust;

        static OmensDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(OmensDefOf)); }
    }
}
