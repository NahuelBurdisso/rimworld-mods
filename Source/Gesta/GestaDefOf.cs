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

        static GestaDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(GestaDefOf)); }
    }
}
