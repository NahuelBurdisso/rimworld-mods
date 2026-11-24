using HarmonyLib;
using UnityEngine;
using Verse;

namespace Duelo
{
    public class DueloMod : Mod
    {
        public static DueloSettings Settings;

        public DueloMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<DueloSettings>();
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Duelo] loaded");
        }

        public override string SettingsCategory() => "Duelo_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            l.CheckboxLabeled("Duelo_Setting_LastLight".Translate(), ref Settings.lastLight, "Duelo_Setting_LastLight_Desc".Translate());
            l.GapLine();
            l.CheckboxLabeled("Duelo_Setting_Eulogy".Translate(), ref Settings.eulogy, "Duelo_Setting_Eulogy_Desc".Translate());
            l.CheckboxLabeled("Duelo_Setting_EulogyLetter".Translate(), ref Settings.eulogyLetter, "Duelo_Setting_EulogyLetter_Desc".Translate());
            l.CheckboxLabeled("Duelo_Setting_EulogyAtBurial".Translate(), ref Settings.eulogyAtBurialEvenWithFuneral, "Duelo_Setting_EulogyAtBurial_Desc".Translate());
            l.GapLine();
            l.CheckboxLabeled("Duelo_Setting_Moss".Translate(), ref Settings.moss, "Duelo_Setting_Moss_Desc".Translate());
            l.Label("Duelo_Setting_MossBrightness".Translate(Settings.mossBrightness.ToStringPercent()));
            Settings.mossBrightness = l.Slider(Settings.mossBrightness, 0.25f, 2f);
            l.End();
        }
    }
}
