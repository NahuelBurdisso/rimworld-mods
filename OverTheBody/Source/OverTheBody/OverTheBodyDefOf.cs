using RimWorld;
using Verse;

namespace OverTheBody
{
    [DefOf]
    public static class OverTheBodyDefOf
    {
        public static JobDef OverTheBody_Rush;
        public static JobDef OverTheBody_Guard;
        public static JobDef OverTheBody_Bite;
        public static JobDef OverTheBody_Follow;
        public static ThoughtDef OverTheBody_StoodOverMe;
        public static ThoughtDef OverTheBody_DiedOverMe;
        public static TaleDef OverTheBody_StoodOverTale;
        public static TaleDef OverTheBody_DiedOverTale;

        static OverTheBodyDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(OverTheBodyDefOf)); }
    }
}
