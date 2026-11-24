using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AfterTheRain
{
    /// <summary>
    /// The rainbow itself: a map condition that fades in and out, gives a small memory to colonists who see it
    /// under open sky, at most one inspiration per rainbow, and draws a faint screen-space arc on the visible map.
    /// </summary>
    public class GameCondition_Rainbow : GameCondition
    {
        public const int Fade = 600;
        public const int CheckInterval = 250;
        /// <summary>Witnesses only count once the arc is visibly there.</summary>
        public const float MinVisibleStrength = 0.3f;
        /// <summary>The sun glow below which the light is gone and the rainbow fades early.</summary>
        public const float EndGlow = 0.1f;

        /// <summary>-1 left (west), +1 right (east): the anti-solar side, fixed when the rainbow starts.</summary>
        public float side;
        public bool inspiredOne;
        public bool endingEarly;
        HashSet<int> seen = new HashSet<int>();

        public static int DrawCount;

        public override int TransitionTicks => Fade;

        public float Strength => GameConditionUtility.LerpInOutValue(this, TransitionTicks);

        public bool HasSeen(Pawn p) => seen.Contains(p.thingIDNumber);

        /// <summary>
        /// The anti-solar side: the morning sun rises in the east (screen right), so the rainbow stands west (left);
        /// in the afternoon the other way round. (Not the shadow vector: GenCelestial switches it to the moon branch
        /// whenever glow is 0.6 or less, which flips it at dawn and dusk.)
        /// </summary>
        public static float SideFor(float dayPercent) => dayPercent < 0.5f ? -1f : 1f;

        static readonly List<Pawn> tmpPawns = new List<Pawn>();

        public override void Init()
        {
            base.Init();
            Map map = SingleMap;
            side = map != null ? SideFor(GenLocalDate.DayPercent(map)) : (Rand.Bool ? -1f : 1f);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref side, "side", 1f);
            Scribe_Values.Look(ref inspiredOne, "inspiredOne", false);
            Scribe_Values.Look(ref endingEarly, "endingEarly", false);
            Scribe_Collections.Look(ref seen, "seen", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && seen == null) seen = new HashSet<int>();
        }

        public override float SkyGazeChanceFactor(Map map) => 3f;

        public override float SkyGazeJoyGainFactor(Map map) => 1.5f;

        public override void GameConditionTick()
        {
            Map map = SingleMap;
            if (map == null || (Find.TickManager.TicksGame + uniqueID) % CheckInterval != 0) return;
            try
            {
                CheckFadeEarly(map);
                if (!endingEarly && Strength >= MinVisibleStrength) CheckWitnesses(map);
            }
            catch (System.Exception ex)
            {
                Log.ErrorOnce("[After the Rain] rainbow tick: " + ex, 0x7a1b1);
            }
        }

        /// <summary>Rain or fog again, the sun going down, an eclipse: fade out now instead of at the planned end.</summary>
        public void CheckFadeEarly(Map map)
        {
            if (endingEarly || TicksLeft <= TransitionTicks) return;
            float glow = RainbowWatcher.SunGlow(map);
            bool gone = !RainbowWatcher.IsClearSky(map.weatherManager.curWeather) || glow < EndGlow
                        || !RainbowWatcher.InGraceWindow(GenLocalDate.DayPercent(map))
                        || RainbowWatcher.Suppressed(map) || !RainbowWatcher.HasSky(map);
            if (!gone) return;
            endingEarly = true;
            TicksLeft = TransitionTicks;
        }

        /// <summary>Public for tests. Returns how many new witnesses got the memory.</summary>
        public int CheckWitnesses(Map map)
        {
            int added = 0;
            // copy: FreeColonistsSpawned is a cached list refilled on every call, and memories/letters below may ask again
            tmpPawns.Clear();
            tmpPawns.AddRange(map.mapPawns.FreeColonistsSpawned);
            for (int i = 0; i < tmpPawns.Count; i++)
            {
                Pawn p = tmpPawns[i];
                if (seen.Contains(p.thingIDNumber) || !CanSee(p)) continue;
                seen.Add(p.thingIDNumber);
                p.needs.mood.thoughts.memories.TryGainMemory(RainbowDefOf.AfterTheRain_SawRainbow);
                added++;
                TryInspire(p);
            }
            tmpPawns.Clear();
            return added;
        }

        public static bool CanSee(Pawn p)
        {
            if (p?.needs?.mood == null || !p.Spawned || p.Dead) return false;
            if (!p.Awake() || PawnUtility.IsBiologicallyOrArtificiallyBlind(p)) return false;
            return !p.Position.Roofed(p.Map);
        }

        void TryInspire(Pawn p)
        {
            if (inspiredOne || p.mindState?.inspirationHandler == null || p.Inspired) return;
            if (!Rand.Chance(AfterTheRainMod.Settings.inspirationChance)) return;
            InspirationDef def = p.mindState.inspirationHandler.GetRandomAvailableInspirationDef();
            if (def == null) return;
            if (p.mindState.inspirationHandler.TryStartInspiration(def, "AfterTheRain_InspirationReason".Translate()))
                inspiredOne = true;
        }

        public override void GameConditionDraw(Map map)
        {
            if (map != Find.CurrentMap) return;
            float a = Strength * AfterTheRainMod.Settings.opacity;
            if (a <= 0.001f) return;
            DrawCount++;
            RainbowDrawer.Draw(a, side);
        }
    }
}
