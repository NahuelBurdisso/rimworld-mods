using RimWorld;
using Verse;

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

        static GestaDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(GestaDefOf)); }
    }
}
