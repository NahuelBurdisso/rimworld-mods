using RimWorld;
using Verse;
using Verse.AI;

namespace Gesta
{
    [DefOf]
    public static class GestaDefOf
    {
        public static HediffDef Gesta_LastStand;
        public static ThoughtDef Gesta_StoodAlone;
        public static ThoughtDef Gesta_SavedByLastStand;
        public static TaleDef Gesta_StoodAloneTale;
        public static TaleDef Gesta_FellAloneTale;

        public static FeatDef Gesta_Feat_TripleKill;
        public static FeatDef Gesta_Feat_BeastBreaker;
        public static FeatDef Gesta_Feat_Kingslayer;
        public static FeatDef Gesta_Feat_Lifeline;
        public static ThoughtDef Gesta_DidAFeat;
        public static FleckDef Gesta_Fleck_FeatBeam;
        public static FleckDef Gesta_Fleck_FeatBloom;

        public static ThingDef Gesta_PoppyRed;
        public static ThingDef Gesta_PoppyWhite;
        public static ThoughtDef Gesta_VisitedPoppy;

        public static ThingDef Gesta_AshSilhouette;

        public static HediffDef Gesta_Bloodstained;

        public static PawnRelationDef Gesta_BrotherInArms;
        public static ThoughtDef Gesta_BrotherInArmsDied;
        public static ThoughtDef Gesta_SisterInArmsDied;
        public static ThoughtDef Gesta_BrotherDowned;
        public static HediffDef Gesta_BrotherSash;
        public static HediffDef Gesta_BrotherSashMourning;

        public static FleckDef Gesta_Fleck_Tracer;

        public static InteractionDef Gesta_RecountsBattle;
        public static ThoughtDef Gesta_NotHowItHappened;
        public static ThoughtDef Gesta_InspiredByWarStory;
        public static FleckDef Gesta_Fleck_WarStory;

        public static DutyDef Gesta_ChampHold;
        public static DutyDef Gesta_ChampChallenge;
        public static DutyDef Gesta_ChampDuel;
        public static JobDef Gesta_ChampStand;
        public static ThingDef Gesta_ChallengeBanner;
        public static ThoughtDef Gesta_ChampionVictory;
        public static ThoughtDef Gesta_ChallengeDishonor;
        public static ThoughtDef Gesta_ChallengeRefused;
        public static TaleDef Gesta_ChampionDuelTale;
        public static FeatDef Gesta_Feat_Champion;

        public static InteractionDef Gesta_TrashTalk;
        public static InteractionDef Gesta_TrashTalkReply;
        public static InteractionDef Gesta_TrashTalkMock;
        public static ThoughtDef Gesta_TauntBackfired;
        public static RulePackDef Gesta_TrashTalk_Shouts;
        public static RulePackDef Gesta_TrashTalk_Retorts;
        public static RulePackDef Gesta_TrashTalk_Mockery;
        public static RulePackDef Gesta_TrashTalk_Hit;
        public static RulePackDef Gesta_TrashTalk_Backfire;
        public static RulePackDef Gesta_TrashTalk_Break;

        static GestaDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(GestaDefOf)); }
    }
}
