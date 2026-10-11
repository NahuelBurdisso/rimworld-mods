using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gesta
{
    public class GestaMod : Mod
    {
        public static GestaSettings Settings;

        public GestaMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<GestaSettings>();
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Gesta] loaded");
        }

        public override string SettingsCategory() => "Gesta";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.Label("Gesta_Settings_LastStand".Translate());
            list.CheckboxLabeled("Gesta_Settings_Enabled".Translate(), ref Settings.lastStand);
            list.CheckboxLabeled("Gesta_Settings_Camera".Translate(), ref Settings.lastStandCamera);
            list.CheckboxLabeled("Gesta_Settings_SlowDown".Translate(), ref Settings.lastStandSlowDown);
            list.CheckboxLabeled("Gesta_Settings_Music".Translate(), ref Settings.lastStandMusic);
            list.CheckboxLabeled("Gesta_Settings_Vignette".Translate(), ref Settings.lastStandVignette);
            list.GapLine();
            list.Label("Gesta_Settings_Feats".Translate());
            list.CheckboxLabeled("Gesta_Settings_Enabled".Translate(), ref Settings.feats);
            list.CheckboxLabeled("Gesta_Settings_FeatTitles".Translate(), ref Settings.featTitles);
            list.GapLine();
            list.Label("Gesta_Settings_Poppies".Translate());
            list.CheckboxLabeled("Gesta_Settings_Enabled".Translate(), ref Settings.poppies);
            list.GapLine();
            list.Label("Gesta_Settings_Ash".Translate());
            list.CheckboxLabeled("Gesta_Settings_Enabled".Translate(), ref Settings.ashSilhouettes);
            list.GapLine();
            list.Label("Gesta_Settings_Tide".Translate());
            list.CheckboxLabeled("Gesta_Settings_Enabled".Translate(), ref Settings.turningTide);
            list.GapLine();
            list.Label("Gesta_Settings_Blood".Translate());
            list.CheckboxLabeled("Gesta_Settings_Enabled".Translate(), ref Settings.bloodStains);
            list.GapLine();
            list.Label("Gesta_Settings_Brothers".Translate());
            list.CheckboxLabeled("Gesta_Settings_Enabled".Translate(), ref Settings.brothers);
            list.GapLine();
            list.Label("Gesta_Settings_Chronicler".Translate());
            list.CheckboxLabeled("Gesta_Settings_Enabled".Translate(), ref Settings.chronicler);
            list.GapLine();
            list.Label("Gesta_Settings_Killcam".Translate());
            list.CheckboxLabeled("Gesta_Settings_Enabled".Translate(), ref Settings.killcam);
            list.CheckboxLabeled("Gesta_Settings_Camera".Translate(), ref Settings.killcamCamera);
            list.CheckboxLabeled("Gesta_Settings_SlowMo".Translate(), ref Settings.killcamSlowMo);
            list.Label("Gesta_Settings_KillcamPoints".Translate(Mathf.RoundToInt(Settings.killcamMinPoints)));
            Settings.killcamMinPoints = list.Slider(Settings.killcamMinPoints, 200f, 5000f);
            list.End();
        }
    }

    public class GestaSettings : ModSettings
    {
        public bool lastStand = true;
        public bool lastStandCamera = true;
        public bool lastStandSlowDown = true;
        public bool lastStandMusic = true;
        public bool lastStandVignette = true;
        public bool feats = true;
        public bool featTitles = true;
        public bool poppies = true;
        public bool ashSilhouettes = true;
        public bool turningTide = true;
        public bool bloodStains = true;
        public bool brothers = true;
        public bool chronicler = true;
        public bool killcam = true;
        public bool killcamCamera = true;
        public bool killcamSlowMo = true;
        public float killcamMinPoints = 1000f;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref lastStand, "lastStand", true);
            Scribe_Values.Look(ref lastStandCamera, "lastStandCamera", true);
            Scribe_Values.Look(ref lastStandSlowDown, "lastStandSlowDown", true);
            Scribe_Values.Look(ref lastStandMusic, "lastStandMusic", true);
            Scribe_Values.Look(ref lastStandVignette, "lastStandVignette", true);
            Scribe_Values.Look(ref feats, "feats", true);
            Scribe_Values.Look(ref featTitles, "featTitles", true);
            Scribe_Values.Look(ref poppies, "poppies", true);
            Scribe_Values.Look(ref ashSilhouettes, "ashSilhouettes", true);
            Scribe_Values.Look(ref turningTide, "turningTide", true);
            Scribe_Values.Look(ref bloodStains, "bloodStains", true);
            Scribe_Values.Look(ref brothers, "brothers", true);
            Scribe_Values.Look(ref chronicler, "chronicler", true);
            Scribe_Values.Look(ref killcam, "killcam", true);
            Scribe_Values.Look(ref killcamCamera, "killcamCamera", true);
            Scribe_Values.Look(ref killcamSlowMo, "killcamSlowMo", true);
            Scribe_Values.Look(ref killcamMinPoints, "killcamMinPoints", 1000f);
        }
    }
}
