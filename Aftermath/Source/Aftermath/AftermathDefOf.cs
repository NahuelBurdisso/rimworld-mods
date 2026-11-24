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
        public static ThingDef Aftermath_SentinelRemains;
        public static ThoughtDef Aftermath_TrophyStandard;
        public static ThoughtDef Aftermath_TrophyStandardVeteran;
        public static ThoughtDef Aftermath_CameToSeeMe;
        public static ThoughtDef Aftermath_SentinelFell;
        public static ThoughtDef Aftermath_SentinelFellComrade;
        public static RulePackDef Aftermath_NamerSentinel;

        static AftermathDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(AftermathDefOf)); }
    }
}
