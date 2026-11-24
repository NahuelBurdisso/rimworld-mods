using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheDogKnows
{
    public class TheDogKnowsMod : Mod
    {
        public static TheDogKnowsSettings Settings;

        public TheDogKnowsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<TheDogKnowsSettings>();
            // No patches today; kept so a future Postfix needs no scaffolding.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[The Dog Knows] loaded");
        }

        public override string SettingsCategory() => "TDK_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            l.CheckboxLabeled("TDK_Setting_Message".Translate(), ref Settings.message, "TDK_Setting_Message_Desc".Translate());
            l.CheckboxLabeled("TDK_Setting_Growl".Translate(), ref Settings.growl, "TDK_Setting_Growl_Desc".Translate());
            l.End();
        }
    }

    public class TheDogKnowsSettings : ModSettings
    {
        public bool message = true;
        public bool growl = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref message, "message", true);
            Scribe_Values.Look(ref growl, "growl", true);
        }
    }

    [DefOf]
    public static class TDKDefOf
    {
        public static JobDef TDK_StareAtUnseen;
        public static ThingDef TDK_Mote_Hackles;

        static TDKDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(TDKDefOf)); }
    }
}
