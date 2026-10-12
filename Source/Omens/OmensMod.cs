using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Omens
{
    public class OmensMod : Mod
    {
        public static OmensSettings Settings;

        public OmensMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<OmensSettings>();
            // No patches yet: every feature runs from MapComponents. Kept for future features of the pack.
            new Harmony(content.PackageId).PatchAll();
        }

        public override string SettingsCategory() => "Omens";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            Section(list, "Omens_Settings_RainReveals", ref Settings.rainReveals);
            Section(list, "Omens_Settings_Crows", ref Settings.crows);
            Section(list, "Omens_Settings_ColdCamp", ref Settings.coldCamp, last: true);
            list.End();
        }

        static void Section(Listing_Standard list, string key, ref bool value, bool last = false)
        {
            list.Label(key.Translate());
            list.CheckboxLabeled("Omens_Settings_Enabled".Translate(), ref value, (key + "Desc").Translate());
            if (!last) list.GapLine();
        }
    }

    public class OmensSettings : ModSettings
    {
        public bool rainReveals = true;
        public bool crows = true;
        public bool coldCamp = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref rainReveals, "rainReveals", true);
            Scribe_Values.Look(ref crows, "crows", true);
            Scribe_Values.Look(ref coldCamp, "coldCamp", true);
        }
    }

    /// <summary>The numbers of DESIGN.md, in one place. Static fields so the test harness can shorten waits.</summary>
    public static class OmensTuning
    {
        // Shared
        public static int StagingPollInterval = 60;
        public static int FollowTimeoutTicks = 3 * GenDate.TicksPerDay;

        // Rain reveals the unseen
        public static int RainPulseInterval = 20;
        public static int RainScanInterval = 240;
        public static float MinFallRate = 0.1f;
        public static int SplashesPerPulse = 10;
        public static int PuffsPerPulse = 7;

        // Crows
        public static int CrowPollInterval = 60;
        public static int CrowSpawnGapTicks = 90;
        public static int CarrionPollInterval = 250;
        public static int CarrionMinCorpses = 3;
        public static int CarrionManyCorpses = 8;
        public static int CarrionDurationTicks = 2 * GenDate.TicksPerDay;
        public static float FlockSpreadDegrees = 25f;

        // Cold camp
        public static int MinWaitTicks = 1000;
        public static float CampMergeRadius = 5f;
        public static int CampPlaceRadius = 4;
        public static int CampEntriesShown = 6;
    }

    public static class OmensUtil
    {
        /// <summary>Crows need open sky: not roofed, not fogged, in bounds.</summary>
        public static bool OpenSky(IntVec3 c, Map map) => c.InBounds(map) && !c.Roofed(map) && !c.Fogged(map);

        /// <summary>Maps where birds can fly: no pocket maps (Anomaly), no vacuum (Odyssey orbit).</summary>
        public static bool BirdsCanFly(Map map) => map != null && !map.IsPocketMap && !(map.Biome?.inVacuum ?? false);

        /// <summary>Mean position of the free colonists, or the map centre.</summary>
        public static Vector3 ColonyCenter(Map map)
        {
            var list = map.mapPawns.FreeColonistsSpawned;
            if (list.Count == 0) return map.Center.ToVector3Shifted();
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < list.Count; i++) sum += list[i].DrawPos;
            return sum / list.Count;
        }
    }

    /// <summary>Counters for the test harness and for debugging; never saved.</summary>
    public static class OmensStats
    {
        public static int RainSplashes, SnowPuffs, CrowsSpawned, FlockCrows, CampsSpawned, CampEntries, CarrionOmens;
    }
}
