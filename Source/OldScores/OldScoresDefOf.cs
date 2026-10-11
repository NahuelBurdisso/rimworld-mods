using RimWorld;
using Verse;

namespace OldScores
{
    [DefOf]
    public static class OldScoresDefOf
    {
        public static MentalStateDef OldScores_Dread;
        public static FleckDef OldScores_Fleck_Dread;
        public static RulePackDef OldScores_Namer_EnemyNickname;
        public static RulePackDef OldScores_Namer_Beast;
        public static IncidentDef OldScores_BeastReturns;
        public static HediffDef OldScores_Veteran;
        public static ThingDef OldScores_BeastTrophy;
        public static ThoughtDef OldScores_SettledOldScore;

        static OldScoresDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(OldScoresDefOf)); }
    }
}
