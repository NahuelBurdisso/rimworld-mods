using HarmonyLib;
using UnityEngine;
using Verse;

namespace UnlikelyHero
{
    public class UnlikelyHeroMod : Mod
    {
        public static UnlikelyHeroSettings Settings;

        public UnlikelyHeroMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<UnlikelyHeroSettings>();
            new Harmony(content.PackageId).PatchAll();
        }

        public override string SettingsCategory() => "UnlikelyHero_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            UnlikelyHeroSettings s = Settings;

            l.Label("UnlikelyHero_Setting_BonusChance".Translate(s.bonusChance.ToStringPercent()),
                tooltip: "UnlikelyHero_Setting_BonusChance_Desc".Translate());
            s.bonusChance = Mathf.Round(l.Slider(s.bonusChance, 0f, 1f) * 100f) / 100f;

            l.Label("UnlikelyHero_Setting_MaxCombatSkill".Translate(s.maxCombatSkill),
                tooltip: "UnlikelyHero_Setting_MaxCombatSkill_Desc".Translate());
            s.maxCombatSkill = Mathf.RoundToInt(l.Slider(s.maxCombatSkill, 0f, 10f));

            l.Label("UnlikelyHero_Setting_CrafterCombatCap".Translate(s.crafterCombatCap),
                tooltip: "UnlikelyHero_Setting_CrafterCombatCap_Desc".Translate());
            s.crafterCombatCap = Mathf.RoundToInt(l.Slider(s.crafterCombatCap, 0f, 20f));

            l.Gap();
            l.CheckboxLabeled("UnlikelyHero_Setting_RequireFirstKill".Translate(), ref s.requireFirstKill,
                "UnlikelyHero_Setting_RequireFirstKill_Desc".Translate());
            l.CheckboxLabeled("UnlikelyHero_Setting_ShowMessage".Translate(), ref s.showMessage);
            l.CheckboxLabeled("UnlikelyHero_Setting_QuietWithGesta".Translate(), ref s.quietWithGesta,
                "UnlikelyHero_Setting_QuietWithGesta_Desc".Translate());
            l.End();
        }
    }

    public class UnlikelyHeroSettings : ModSettings
    {
        public float bonusChance = 0.15f;
        public int maxCombatSkill = 4;
        public int crafterCombatCap = 8;
        public bool requireFirstKill = true;
        public bool showMessage = true;
        public bool quietWithGesta = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref bonusChance, "bonusChance", 0.15f);
            Scribe_Values.Look(ref maxCombatSkill, "maxCombatSkill", 4);
            Scribe_Values.Look(ref crafterCombatCap, "crafterCombatCap", 8);
            Scribe_Values.Look(ref requireFirstKill, "requireFirstKill", true);
            Scribe_Values.Look(ref showMessage, "showMessage", true);
            Scribe_Values.Look(ref quietWithGesta, "quietWithGesta", true);
        }
    }
}
