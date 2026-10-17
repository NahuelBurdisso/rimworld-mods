using UnityEngine;
using Verse;

namespace ReactorSlag
{
    public class ReactorSlagSettings : ModSettings
    {
        public const float DefaultCoolingDays = 3f;

        /// <summary>Days a core takes to go from white-hot to cold.</summary>
        public float coolingDays = DefaultCoolingDays;
        /// <summary>Draw the glowing halo over the slag (the light itself is always there).</summary>
        public bool drawHalo = true;
        /// <summary>With Odyssey: cooled lava terrain at the centre of a cooled pool, on natural ground only.</summary>
        public bool groundScar = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref coolingDays, "coolingDays", DefaultCoolingDays);
            Scribe_Values.Look(ref drawHalo, "drawHalo", true);
            Scribe_Values.Look(ref groundScar, "groundScar", true);
        }
    }

    public class ReactorSlagMod : Mod
    {
        public static ReactorSlagSettings Settings;

        public ReactorSlagMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ReactorSlagSettings>();
        }

        public override string SettingsCategory() => "ReactorSlag_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.Label("ReactorSlag_CoolingDays".Translate(Settings.coolingDays.ToString("0.#")),
                tooltip: "ReactorSlag_CoolingDaysDesc".Translate());
            Settings.coolingDays = Mathf.Round(list.Slider(Settings.coolingDays, 0.5f, 10f) * 2f) / 2f;
            list.CheckboxLabeled("ReactorSlag_DrawHalo".Translate(), ref Settings.drawHalo, "ReactorSlag_DrawHaloDesc".Translate());
            list.CheckboxLabeled("ReactorSlag_GroundScar".Translate(), ref Settings.groundScar, "ReactorSlag_GroundScarDesc".Translate());
            list.End();
        }
    }
}
