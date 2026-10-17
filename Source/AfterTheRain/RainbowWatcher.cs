using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AfterTheRain
{
    public enum RainbowResult
    {
        None,
        Disabled,
        NotTransition,
        AlreadyRolled,
        AlreadyActive,
        NoSky,
        Suppressed,
        OutOfWindow,
        NoWitness,
        RollFailed,
        Started
    }

    /// <summary>
    /// Every 250 ticks: did the rain just give way to a clear sky, with the sun low enough for a rainbow?
    /// Reads WeatherManager's public fields only, so it keeps no weather state of its own; the only saved
    /// value is which rain-to-clear transition was already rolled. See DESIGN.md "Disparo".
    /// </summary>
    public class RainbowWatcher : MapComponent
    {
        public const int CheckInterval = 250;
        /// <summary>From the middle of vanilla's 4000-tick weather fade to three hours after the change.</summary>
        public const int MinAge = 2000, MaxAge = 7500;
        /// <summary>
        /// Low-to-middle sun = time of day. Vanilla's sun glow cannot measure elevation: GenCelestial.SunPosition pulls
        /// the sun up to 36 degrees toward the zenith before the glow is clamped, so it reads 1.0 from about 7:00 to
        /// 17:00 at low and middle latitudes. The hour windows keep midday out; the glow floor keeps out a dark dawn.
        /// </summary>
        public const float MorningStart = 0.25f, MorningEnd = 0.42f, AfternoonStart = 0.58f, AfternoonEnd = 0.77f;
        /// <summary>How far past a window an already-shining rainbow may last before it fades early.</summary>
        public const float WindowGrace = 0.04f;
        public const float MinGlow = 0.2f;
        public static readonly IntRange DurationTicks = new IntRange(2500, 5000);

        int rolledTransitionStart = -1;

        /// <summary>Where the sun glow comes from. Tests swap it to pin the sun; the game never changes it.</summary>
        public static System.Func<Map, float> SunGlowSource = GenCelestial.CurCelestialSunGlow;
        public static float SunGlow(Map map) => SunGlowSource(map);

        public RainbowResult LastResult = RainbowResult.None;
        public int LastEvaluateTick = -1;

        public RainbowWatcher(Map map) : base(map) { }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref rolledTransitionStart, "rolledTransitionStart", -1);
        }

        public override void MapComponentTick()
        {
            if ((Find.TickManager.TicksGame + map.uniqueID) % CheckInterval != 0) return;
            try
            {
                Evaluate(GenLocalDate.DayPercent(map), RainbowWatcher.SunGlow(map), AfterTheRainMod.Settings.chance);
            }
            catch (System.Exception ex)
            {
                Log.ErrorOnce("[After the Rain] watcher: " + ex, 0x7a1b0);
            }
        }

        /// <summary>One check with explicit sun values and chance (the tick passes the real ones; tests pass their own).</summary>
        public RainbowResult Evaluate(float dayPercent, float sunGlow, float chance)
        {
            LastEvaluateTick = Find.TickManager.TicksGame;
            return LastResult = EvaluateInner(dayPercent, sunGlow, chance);
        }

        RainbowResult EvaluateInner(float dayPercent, float sunGlow, float chance)
        {
            if (!AfterTheRainMod.Settings.enabled) return RainbowResult.Disabled;
            WeatherManager wm = map.weatherManager;
            if (wm == null || wm.curWeatherAge < MinAge || wm.curWeatherAge > MaxAge) return RainbowResult.NotTransition;
            if (!IsRain(wm.lastWeather) || !IsClearSky(wm.curWeather)) return RainbowResult.NotTransition;
            int transitionStart = Find.TickManager.TicksGame - wm.curWeatherAge;
            if (transitionStart == rolledTransitionStart) return RainbowResult.AlreadyRolled;
            if (ActiveRainbow(map) != null) return RainbowResult.AlreadyActive;
            if (!HasSky(map)) return RainbowResult.NoSky;
            if (Suppressed(map)) return RainbowResult.Suppressed;
            if (!SunWindow(dayPercent, sunGlow)) return RainbowResult.OutOfWindow;
            if (map.mapPawns.FreeColonistsSpawnedCount == 0) return RainbowResult.NoWitness;

            rolledTransitionStart = transitionStart;
            if (!Rand.Chance(chance)) return RainbowResult.RollFailed;
            StartRainbow(map, DurationTicks.RandomInRange);
            return RainbowResult.Started;
        }

        /// <summary>Registers the condition and posts the optional message. Public for the showcase.</summary>
        public static GameCondition_Rainbow StartRainbow(Map map, int duration)
        {
            var cond = (GameCondition_Rainbow)GameConditionMaker.MakeCondition(RainbowDefOf.AfterTheRain_Rainbow, duration);
            map.gameConditionManager.RegisterCondition(cond);
            if (AfterTheRainMod.Settings.showMessage)
            {
                string where = map.Parent?.LabelCap ?? map.ToString();
                Messages.Message("AfterTheRain_Message".Translate(where), MessageTypeDefOf.PositiveEvent);
            }
            return cond;
        }

        public static GameCondition_Rainbow ActiveRainbow(Map map)
        {
            List<GameCondition> list = map.gameConditionManager.ActiveConditions;
            for (int i = 0; i < list.Count; i++)
                if (list[i] is GameCondition_Rainbow r) return r;
            return null;
        }

        // ---- classification -------------------------------------------------------------------------------------

        public static bool IsRain(WeatherDef w)
        {
            if (w == null || w.rainRate <= 0f || w.snowRate > 0f || w.sandRate > 0f) return false;
            if (HasLightning(w) || IsFoggy(w)) return false;
            return !RainbowDefOf.AfterTheRain_Config.excludedRain.Contains(w);
        }

        public static bool IsClearSky(WeatherDef w)
        {
            if (w == null) return false;
            RainbowConfigDef cfg = RainbowDefOf.AfterTheRain_Config;
            if (cfg.extraClear.Contains(w)) return true;
            if (cfg.excludedClear.Contains(w)) return false;
            if (w.rainRate > 0f || w.snowRate > 0f || w.sandRate > 0f) return false;
            if (HasLightning(w) || IsFoggy(w)) return false;
            if (w.maxGlow < 1f) return false;                       // Odyssey overcast
            return w.skyColorsDay.sky.grayscale >= 0.9f;              // a dim, grey day sky is not "clear"
        }

        public static bool HasLightning(WeatherDef w)
        {
            List<WeatherEventMaker> makers = w.eventMakers;
            if (makers == null) return false;
            for (int i = 0; i < makers.Count; i++)
                if (makers[i]?.eventClass != null && typeof(WeatherEvent_LightningFlash).IsAssignableFrom(makers[i].eventClass)) return true;
            return false;
        }

        public static bool IsFoggy(WeatherDef w)
        {
            List<System.Type> overlays = w.overlayClasses;
            if (overlays == null) return false;
            for (int i = 0; i < overlays.Count; i++)
                if (overlays[i] != null && overlays[i].Name.IndexOf("Fog", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool SunWindow(float dayPercent, float sunGlow)
        {
            bool hour = (dayPercent >= MorningStart && dayPercent <= MorningEnd) || (dayPercent >= AfternoonStart && dayPercent <= AfternoonEnd);
            return hour && sunGlow >= MinGlow;
        }

        /// <summary>The hour windows widened by <see cref="WindowGrace"/>: outside this a visible rainbow fades out.</summary>
        public static bool InGraceWindow(float dayPercent) =>
            (dayPercent >= MorningStart - WindowGrace && dayPercent <= MorningEnd + WindowGrace)
            || (dayPercent >= AfternoonStart - WindowGrace && dayPercent <= AfternoonEnd + WindowGrace);

        /// <summary>Maps with a sky someone could see a rainbow in.</summary>
        public static bool HasSky(Map map)
        {
            if (map == null || map.IsPocketMap) return false;
            if (map.generatorDef != null && map.generatorDef.isUnderground) return false;
            if (map.Tile.Valid && map.Tile.LayerDef != null && map.Tile.LayerDef.isSpace) return false;
            BiomeDef biome = map.Biome;
            if (biome != null && (biome.inVacuum || biome.disableSkyLighting)) return false;
            return !map.gameConditionManager.IsAlwaysDarkOutside;
        }

        public static bool Suppressed(Map map)
        {
            List<GameConditionDef> list = RainbowDefOf.AfterTheRain_Config.suppressedBy;
            for (int i = 0; i < list.Count; i++)
                if (map.gameConditionManager.ConditionIsActive(list[i])) return true;
            // any no-sunlight condition, modded ones included (eclipse-like darkness)
            for (GameConditionManager m = map.gameConditionManager; m != null; m = m.Parent)
            {
                List<GameCondition> active = m.ActiveConditions;
                for (int i = 0; i < active.Count; i++)
                    if (active[i] is GameCondition_NoSunlight) return true;
            }
            return false;
        }
    }
}
