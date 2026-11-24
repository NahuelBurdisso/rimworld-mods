using RimWorld;
using Verse;

namespace WindowWatchers
{
    [DefOf]
    public static class WWDefOf
    {
        public static JobDef WW_Watch;
        public static JobDef WW_Reunite;
        public static ThoughtDef WW_WatchedHurt;
        public static ThoughtDef WW_WatchedFall;
        public static ThoughtDef WW_WatchedDie;
        public static ThoughtDef WW_Relief;
        public static ThoughtDef WW_Embraced;
        public static InteractionDef WW_Embrace;
        public static ThingDef WW_Mote_Worry;
        public static ThingDef WW_Mote_Grief;

        static WWDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(WWDefOf)); }
    }
}
