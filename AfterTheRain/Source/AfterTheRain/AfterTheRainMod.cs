using UnityEngine;
using Verse;

namespace AfterTheRain
{
    public class AfterTheRainMod : Mod
    {
        public static RainbowSettings Settings;

        public AfterTheRainMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RainbowSettings>();   // no Harmony: nothing in vanilla is patched
        }

        public override string SettingsCategory() => "AfterTheRain_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            l.CheckboxLabeled("AfterTheRain_SettingEnabled".Translate(), ref Settings.enabled, "AfterTheRain_SettingEnabledTip".Translate());
            l.CheckboxLabeled("AfterTheRain_SettingMessage".Translate(), ref Settings.showMessage);
            l.Gap();
            l.Label("AfterTheRain_SettingChance".Translate(Settings.chance.ToStringPercent()), tooltip: "AfterTheRain_SettingChanceTip".Translate());
            Settings.chance = l.Slider(Settings.chance, 0f, 1f);
            l.Label("AfterTheRain_SettingInspiration".Translate(Settings.inspirationChance.ToStringPercent("0.#")), tooltip: "AfterTheRain_SettingInspirationTip".Translate());
            Settings.inspirationChance = l.Slider(Settings.inspirationChance, 0f, 0.5f);
            l.Label("AfterTheRain_SettingOpacity".Translate(Settings.opacity.ToStringPercent()));
            Settings.opacity = l.Slider(Settings.opacity, 0.25f, 2f);
            l.Gap();
            if (l.ButtonText("AfterTheRain_SettingReset".Translate())) Settings.Reset();
            l.End();
        }
    }

    public class RainbowSettings : ModSettings
    {
        public const float DefaultChance = 0.35f, DefaultInspiration = 0.05f, DefaultOpacity = 1f;

        public bool enabled = true;
        public bool showMessage = true;
        public float chance = DefaultChance;
        public float inspirationChance = DefaultInspiration;
        public float opacity = DefaultOpacity;

        public void Reset()
        {
            enabled = true;
            showMessage = true;
            chance = DefaultChance;
            inspirationChance = DefaultInspiration;
            opacity = DefaultOpacity;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref showMessage, "showMessage", true);
            Scribe_Values.Look(ref chance, "chance", DefaultChance);
            Scribe_Values.Look(ref inspirationChance, "inspirationChance", DefaultInspiration);
            Scribe_Values.Look(ref opacity, "opacity", DefaultOpacity);
        }
    }
}
