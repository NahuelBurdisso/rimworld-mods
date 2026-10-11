using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace OldScores
{
    public class OldScoresMod : Mod
    {
        public static OldScoresSettings Settings;

        public OldScoresMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<OldScoresSettings>();
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Old Scores] loaded");
        }

        public override string SettingsCategory() => "Old Scores";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.CheckboxLabeled("OldScores_Settings_Nicknames".Translate(), ref Settings.nicknames,
                "OldScores_Settings_NicknamesDesc".Translate());
            list.Gap();
            list.CheckboxLabeled("OldScores_Settings_Spared".Translate(), ref Settings.spared,
                "OldScores_Settings_SparedDesc".Translate());
            list.Gap();
            list.CheckboxLabeled("OldScores_Settings_Beasts".Translate(), ref Settings.beasts,
                "OldScores_Settings_BeastsDesc".Translate());
            list.End();
        }
    }

    public class OldScoresSettings : ModSettings
    {
        public bool nicknames = true;
        public bool spared = true;
        public bool beasts = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref nicknames, "nicknames", true);
            Scribe_Values.Look(ref spared, "spared", true);
            Scribe_Values.Look(ref beasts, "beasts", true);
        }
    }

    /// <summary>The numbers of DESIGN.md. Static fields so the test harness can force the rolls.</summary>
    public static class OldScoresTuning
    {
        // Enemy Nickname
        public static int KillsForNickname = 5;
        public static int DreadInterval = 250;
        public static float DreadRadius = 15f;
        public static float DreadChance = 0.15f;

        // The One You Spared
        public static int SparedCheckInterval = 30;
        public static int SparedMinTicksAway = 3 * GenDate.TicksPerDay;
        public static float SparedReturnChance = 0.35f;
        public static float SparedRecognizeRadius = 12f;

        // The Beast That Came Back
        public static float BeastMinDamage = 10f;
        public static IntRange BeastReturnDelay = new IntRange(GenDate.TicksPerQuadrum, 2 * GenDate.TicksPerQuadrum);
        public static IntRange BeastStayTicks = new IntRange(GenDate.TicksPerDay, 2 * GenDate.TicksPerDay);

        public const int CleanupInterval = 2500;
    }
}
