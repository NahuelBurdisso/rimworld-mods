using RimWorld;
using UnityEngine;
using Verse;

namespace QuickBuild
{
    public class QuickBuildSettings : ModSettings
    {
        public float multiplier = 10f;
        public bool playerOnly = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref multiplier, "multiplier", 10f);
            Scribe_Values.Look(ref playerOnly, "playerOnly", true);
        }
    }

    public class QuickBuildMod : Mod
    {
        public static QuickBuildSettings Settings;

        public QuickBuildMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<QuickBuildSettings>();
        }

        public override string SettingsCategory() => "QB_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            float v = l.SliderLabeled(
                "QB_Multiplier".Translate(Settings.multiplier.ToString("0.#")),
                Settings.multiplier, 1f, 20f, 0.5f, "QB_MultiplierTip".Translate());
            Settings.multiplier = Mathf.Round(v * 2f) / 2f;
            l.CheckboxLabeled("QB_PlayerOnly".Translate(), ref Settings.playerOnly, "QB_PlayerOnlyTip".Translate());
            l.End();
        }
    }

    /// <summary>Multiplies ConstructionSpeed (build, deconstruct, repair, roofs, floors) by the setting.</summary>
    public class StatPart_QuickBuild : StatPart
    {
        bool Applies(StatRequest req)
        {
            var s = QuickBuildMod.Settings;
            if (s == null || s.multiplier == 1f) return false;
            if (!(req.Thing is Pawn pawn)) return false;
            return !s.playerOnly || pawn.Faction == Faction.OfPlayerSilentFail;
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (Applies(req)) val *= QuickBuildMod.Settings.multiplier;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!Applies(req)) return null;
            return "QB_Explanation".Translate() + ": x" + QuickBuildMod.Settings.multiplier.ToString("0.#");
        }
    }
}
