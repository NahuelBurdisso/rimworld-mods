using RimWorld;
using Verse;

namespace Duelo
{
    [DefOf]
    public static class DueloDefOf
    {
        public static ThingDef Duelo_Mote_LastLight;
        public static ThoughtDef Duelo_LastLightFarewell;
        public static FleckDef Duelo_Fleck_LastLightTrail;
        public static FleckDef Duelo_Fleck_LastLightBloom;

        public static ThingDef Duelo_Mote_EulogyLine;
        public static FleckDef Duelo_Fleck_Ember;
        public static RulePackDef Duelo_RulePack_Eulogy;

        static DueloDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(DueloDefOf)); }
    }

    [StaticConstructorOnStartup]
    public static class DueloTex
    {
        public static readonly UnityEngine.Texture2D ReadEulogy = ContentFinder<UnityEngine.Texture2D>.Get("UI/Duelo/ReadEulogy");
    }
}
