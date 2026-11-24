using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PackHowls
{
    public class PackHowlsMod : Mod
    {
        public static PackHowlsSettings Settings;

        public PackHowlsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PackHowlsSettings>();
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Pack Howls] loaded");
        }

        public override string SettingsCategory() => "PackHowls_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            l.CheckboxLabeled("PackHowls_Setting_WildAnswer".Translate(), ref Settings.wildAnswer, "PackHowls_Setting_WildAnswer_Desc".Translate());
            l.CheckboxLabeled("PackHowls_Setting_Moodlets".Translate(), ref Settings.moodlets, "PackHowls_Setting_Moodlets_Desc".Translate());
            l.CheckboxLabeled("PackHowls_Setting_Message".Translate(), ref Settings.message, "PackHowls_Setting_Message_Desc".Translate());
            l.End();
        }
    }

    public class PackHowlsSettings : ModSettings
    {
        public bool wildAnswer = true;
        public bool moodlets = true;
        public bool message = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref wildAnswer, "wildAnswer", true);
            Scribe_Values.Look(ref moodlets, "moodlets", true);
            Scribe_Values.Look(ref message, "message", true);
        }
    }

    [DefOf]
    public static class PackHowlsDefOf
    {
        public static JobDef PackHowls_Howl;
        public static SoundDef PackHowls_HowlSound;
        public static FleckDef PackHowls_HowlRing;
        public static ThoughtDef PackHowls_PackHowled;
        public static ThoughtDef PackHowls_PackHowledForMine;
        public static HowlerListDef PackHowls_Howlers;

        static PackHowlsDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(PackHowlsDefOf)); }
    }
}
