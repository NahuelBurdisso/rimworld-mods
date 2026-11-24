using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AfterTheRain
{
    [DefOf]
    public static class RainbowDefOf
    {
        public static GameConditionDef AfterTheRain_Rainbow;
        public static ThoughtDef AfterTheRain_SawRainbow;
        public static RainbowConfigDef AfterTheRain_Config;

        static RainbowDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(RainbowDefOf)); }
    }

    /// <summary>Weather and condition lists, editable by patches from other mods.</summary>
    public class RainbowConfigDef : Def
    {
        /// <summary>Rainy weathers that never leave a rainbow behind (blood rain...).</summary>
        public List<WeatherDef> excludedRain = new List<WeatherDef>();
        /// <summary>Weathers that never count as a clear sky even if they look like one.</summary>
        public List<WeatherDef> excludedClear = new List<WeatherDef>();
        /// <summary>Weathers that always count as a clear sky (modded clear variants).</summary>
        public List<WeatherDef> extraClear = new List<WeatherDef>();
        /// <summary>Active conditions that prevent or end a rainbow.</summary>
        public List<GameConditionDef> suppressedBy = new List<GameConditionDef>();
    }
}
