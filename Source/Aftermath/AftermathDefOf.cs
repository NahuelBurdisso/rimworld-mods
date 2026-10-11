using RimWorld;
using Verse;

namespace Aftermath
{
    [DefOf]
    public static class AftermathDefOf
    {
        public static ThingDef Aftermath_CapturedStandard;
        public static ThingDef Aftermath_VigilGlow;
        public static ThingDef Aftermath_Offering_Flowers;
        public static ThingDef Aftermath_Offering_Letter;
        public static ThingDef Aftermath_Offering_Figure;
        public static ThingDef Aftermath_Offering_Candle;
        public static ThoughtDef Aftermath_TrophyStandard;
        public static ThoughtDef Aftermath_TrophyStandardVeteran;
        public static ThoughtDef Aftermath_CameToSeeMe;

        static AftermathDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(AftermathDefOf)); }
    }
}
