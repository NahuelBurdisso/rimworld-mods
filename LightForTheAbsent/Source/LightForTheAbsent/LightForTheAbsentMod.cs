using RimWorld;
using UnityEngine;
using Verse;

namespace LightForTheAbsent
{
    public class LightForTheAbsentMod : Mod
    {
        public static LightSettings Settings;

        public LightForTheAbsentMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<LightSettings>();
            // No Harmony: everything is polled, so the mod does not depend on it.
            Log.Message("[Light for the Absent] loaded");
        }

        public override string SettingsCategory() => "LightForTheAbsent_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            l.CheckboxLabeled("LightForTheAbsent_Setting_Moodlets".Translate(), ref Settings.moodlets, "LightForTheAbsent_Setting_Moodlets_Desc".Translate());
            l.CheckboxLabeled("LightForTheAbsent_Setting_Message".Translate(), ref Settings.message, "LightForTheAbsent_Setting_Message_Desc".Translate());
            l.Gap();
            l.Label("LightForTheAbsent_Setting_Brightness".Translate(Settings.brightness.ToStringPercent()), tooltip: "LightForTheAbsent_Setting_Brightness_Desc".Translate());
            Settings.brightness = Mathf.Round(l.Slider(Settings.brightness, LightSettings.MinBrightness, LightSettings.MaxBrightness) * 20f) / 20f;
            l.End();
        }
    }

    public class LightSettings : ModSettings
    {
        public const float MinBrightness = 0.25f, MaxBrightness = 2f;

        public bool moodlets = true;
        public bool message = true;
        public float brightness = 1f;

        public float Brightness => Mathf.Clamp(brightness, MinBrightness, MaxBrightness);

        public override void ExposeData()
        {
            Scribe_Values.Look(ref moodlets, "moodlets", true);
            Scribe_Values.Look(ref message, "message", true);
            Scribe_Values.Look(ref brightness, "brightness", 1f);
        }
    }

    [DefOf]
    public static class LightDefOf
    {
        public static ThingDef LightForTheAbsent_Light;
        public static ThoughtDef LightForTheAbsent_LeftALightOn;
        public static ThoughtDef LightForTheAbsent_SleptLightly;
        public static ThoughtDef LightForTheAbsent_LightWentOut;

        static LightDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(LightDefOf)); }
    }
}
