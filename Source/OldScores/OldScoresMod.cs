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
            list.Gap();
            list.CheckboxLabeled("OldScores_Settings_Echoes".Translate(), ref Settings.echoes,
                "OldScores_Settings_EchoesDesc".Translate());
            list.End();
        }
    }

    public class OldScoresSettings : ModSettings
    {
        public bool nicknames = true;
        public bool spared = true;
        public bool beasts = true;
        public bool echoes = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref nicknames, "nicknames", true);
            Scribe_Values.Look(ref spared, "spared", true);
            Scribe_Values.Look(ref beasts, "beasts", true);
            Scribe_Values.Look(ref echoes, "echoes", true);
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
        public static int SparedForgetTicks = 2 * GenDate.TicksPerYear;
        public static int SparedPeaceForgetTicks = 30 * GenDate.TicksPerDay;

        // The Beast That Came Back
        public static float BeastMinDamage = 10f;
        public static IntRange BeastReturnDelay = new IntRange(GenDate.TicksPerQuadrum, 2 * GenDate.TicksPerQuadrum);
        public static IntRange BeastStayTicks = new IntRange(GenDate.TicksPerDay, 2 * GenDate.TicksPerDay);
        public static int BeastMaxReturns = 2;
        public static int BeastOnePerMapTicks = GenDate.TicksPerDay;

        // Raid Echoes
        public static int EchoSampleInterval = 60;
        public static int EchoMinStep = 3;
        public static int EchoMaxPointsPerTrack = 64;
        public static int EchoStallSamples = 8;
        public static int EchoPendingTimeout = 5000;
        public static int EchoMaxRaidTicks = 3 * GenDate.TicksPerDay;
        public static float EchoSimplifyEpsilon = 1f;
        public static float EchoLaneMergeDist = 15f;
        public static int EchoMaxLanes = 3;
        public static float EchoBreachMergeDist = 6f;
        public static int EchoMaxBreaches = 6;
        public static int EchoMaxDeaths = 30;
        public static int EchoMaxPerMap = 16;
        public static int EchoFadeTicks = GenDate.TicksPerYear;
        public static int EchoForgetTicks = 2 * GenDate.TicksPerYear;
        public static float EchoMaxAlpha = 0.9f;
        public static float EchoMinAlpha = 0.3f;
        public static float EchoOldRoadDist = 20f;
        public static int EchoMaxDrawsPerFrame = 800;

        public const int CleanupInterval = 2500;
    }
}
