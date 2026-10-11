using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Aftermath
{
    public class AftermathMod : Mod
    {
        public static AftermathSettings Settings;

        public AftermathMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<AftermathSettings>();
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Aftermath] loaded");
        }

        public override string SettingsCategory() => "Aftermath";

        static Vector2 scroll;
        static float contentHeight = 600f;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var view = new Rect(0f, 0f, inRect.width - 20f, contentHeight);
            Widgets.BeginScrollView(inRect, ref scroll, view);
            var list = new Listing_Standard();
            list.Begin(view);
            Section(list, "Aftermath_Settings_BracedDoors", ref Settings.bracedDoors);
            Section(list, "Aftermath_Settings_TrophyStandards", ref Settings.trophyStandards);
            Section(list, "Aftermath_Settings_HonoredFoes", ref Settings.honoredFoes);
            Section(list, "Aftermath_Settings_VigilLamps", ref Settings.vigilLamps);
            Section(list, "Aftermath_Settings_BedsideOfferings", ref Settings.bedsideOfferings, last: true);
            contentHeight = list.CurHeight + 20f;
            list.End();
            Widgets.EndScrollView();
        }

        static void Section(Listing_Standard list, string key, ref bool value, bool last = false)
        {
            list.Label(key.Translate());
            list.CheckboxLabeled("Aftermath_Settings_Enabled".Translate(), ref value, (key + "Desc").Translate());
            if (!last) list.GapLine();
        }
    }

    public class AftermathSettings : ModSettings
    {
        public bool bracedDoors = true;
        public bool trophyStandards = true;
        public bool honoredFoes = true;
        public bool vigilLamps = true;
        public bool bedsideOfferings = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref bracedDoors, "bracedDoors", true);
            Scribe_Values.Look(ref trophyStandards, "trophyStandards", true);
            Scribe_Values.Look(ref honoredFoes, "honoredFoes", true);
            Scribe_Values.Look(ref vigilLamps, "vigilLamps", true);
            Scribe_Values.Look(ref bedsideOfferings, "bedsideOfferings", true);
        }
    }

    /// <summary>The numbers of DESIGN.md. Static fields so the test harness can shorten waits.</summary>
    public static class AftermathTuning
    {
        // Braced door
        public static float BatteredHitPointsFraction = 0.30f;
        public static int OneBreachPerTicks = GenDate.TicksPerDay;
        public static int MaxBreachesPerCell = 6;
        public static int MaxPlanksDrawn = 4;

        // Trophy standards
        public static int StandardScanInterval = 250;
        public static int MinRaidMembers = 2;

        // Honored foe
        public static int WorthyVictims = 2;
        public static int WorthyKills = 1;
        public static int DeedsForgetTicks = 15 * GenDate.TicksPerDay;
        public static int UnburiedRecordForgetTicks = 60 * GenDate.TicksPerDay;
        public const int FoeCleanupInterval = 2500;

        // Vigil lamp
        public static int VigilPollInterval = 250;
        public static int BleedOutDangerTicks = GenDate.TicksPerHour * 6;
        public static float LethalSeverityDanger = 0.6f;
        public static int VigilFadeSteps = 10;
        public static ColorInt VigilColor = new ColorInt(64, 38, 10, 0);

        // Bedside offerings
        public static int MinVisitTicks = 600;
        public static int MinOpinionForGift = 0;
        public static int FriendOpinion = 20;
        public static int DoneOfferingVanishTicks = GenDate.TicksPerHour * 6;
        public static int ShrineVanishTicks = 3 * GenDate.TicksPerDay;
    }
}
