using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gesta
{
    /// <summary>
    /// Turning Tide: the sky goes cold and grey while the colony is losing a fight, and opens gold if
    /// they turn it around. Drives a WeatherEvent (weather events may brighten the sky; game
    /// conditions can only darken it). Nothing is saved. See DESIGN.md "Feature 5".
    /// </summary>
    public class TideWatcher : MapComponent
    {
        public const float LosingRatio = 2f;      // hostile strength / colony strength
        public const float TurnedRatio = 0.8f;
        const int CheckInterval = 250, ChecksToLose = 2;
        public const int GoldTicks = 2500, GoldFadeTicks = 600;
        const float EasePerTick = 1f / 300f;

        int losingChecks;
        bool wasLosing;           // this engagement had a losing phase
        public float losingIntensity;   // 0..1, eased
        public int goldTicksLeft;
        WeatherEvent_Tide sky;

        public bool Losing => losingChecks >= ChecksToLose;
        public bool Gold => goldTicksLeft > 0;
        public WeatherEvent_Tide SkyEvent => sky;

        public TideWatcher(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (!GestaMod.Settings.turningTide || !map.IsPlayerHome)
            {
                losingChecks = 0; wasLosing = false; goldTicksLeft = 0; losingIntensity = 0f;
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (now % CheckInterval == 0) Evaluate();
            // while losing, notice the last enemy falling within a second instead of waiting for the engagement poll
            else if (wasLosing && now % 60 == 0 && CombatUtil.ActiveHostilePawns(map) == 0 && CombatUtil.CountFighters(map) > 0)
            {
                StartGold();
                losingChecks = 0;
                wasLosing = false;
            }

            float target = Losing && !Gold ? 1f : 0f;
            losingIntensity = Mathf.MoveTowards(losingIntensity, target, EasePerTick);
            if (goldTicksLeft > 0) goldTicksLeft--;

            if ((losingIntensity > 0f || goldTicksLeft > 0) && !Suppressed()) EnsureSkyEvent();
        }

        /// <summary>Public for tests.</summary>
        public void Evaluate()
        {
            bool fight = EngagementTracker.Instance?.IsActive(map) == true;
            if (!fight)
            {
                if (wasLosing && CombatUtil.CountFighters(map) > 0) StartGold();   // the fight ended with us standing
                losingChecks = 0;
                wasLosing = false;
                return;
            }
            float ratio = Ratio(map);
            if (ratio >= LosingRatio)
            {
                losingChecks++;
                if (Losing) wasLosing = true;
            }
            else
            {
                if (wasLosing && ratio <= TurnedRatio)
                {
                    StartGold();
                    wasLosing = false;
                }
                losingChecks = 0;
            }
        }

        void StartGold()
        {
            goldTicksLeft = GoldTicks;
            losingIntensity = 0f;
            Log.Message($"[Gesta] the tide turned on {map}");
        }

        bool Suppressed() =>
            map.Biome.disableSkyLighting
            || map.gameConditionManager.ConditionIsActive(GameConditionDefOf.Eclipse)
            || map.gameConditionManager.ConditionIsActive(GameConditionDefOf.Aurora);

        void EnsureSkyEvent()
        {
            if (sky != null && !sky.Expired && map.weatherManager.eventHandler.LiveEventsListForReading.Contains(sky)) return;
            sky = new WeatherEvent_Tide(map, this);
            map.weatherManager.eventHandler.AddEvent(sky);
        }

        /// <summary>Hostile strength over colony strength, both weighted by health.</summary>
        public static float Ratio(Map map)
        {
            float hostile = 0f;
            foreach (IAttackTarget t in map.attackTargetsCache.TargetsHostileToColony)
            {
                if (t.Thing is Pawn p && !p.Downed && GenHostility.IsActiveThreatToPlayer(t))
                    hostile += Mathf.Max(p.kindDef.combatPower, 20f) * p.health.summaryHealth.SummaryHealthPercent;
            }
            float ours = 0f;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (!CombatUtil.IsFighter(p)) continue;
                int skill = Mathf.Max(p.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0, p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0);
                ours += (40f + 4f * skill) * p.health.summaryHealth.SummaryHealthPercent;
            }
            if (ours <= 0f) return hostile > 0f ? 99f : 0f;
            return hostile / ours;
        }
    }

    /// <summary>Cold steel-blue while losing, warm gold after a turned fight. Glow follows the current sky so lighting gameplay does not change.</summary>
    public class WeatherEvent_Tide : WeatherEvent
    {
        static readonly SkyColorSet ColdColors = new SkyColorSet(
            new Color(0.56f, 0.63f, 0.78f), new Color(0.38f, 0.43f, 0.58f), new Color(0.6f, 0.66f, 0.8f), 0.25f);
        static readonly SkyColorSet GoldColors = new SkyColorSet(
            new Color(1f, 0.86f, 0.6f), new Color(0.82f, 0.68f, 0.5f), new Color(1f, 0.9f, 0.72f), 1.4f);
        const float MaxColdLerp = 0.8f, MaxGoldLerp = 0.7f;

        readonly TideWatcher watcher;

        public WeatherEvent_Tide(Map map, TideWatcher watcher) : base(map) { this.watcher = watcher; }

        public override bool Expired => watcher.losingIntensity <= 0f && watcher.goldTicksLeft <= 0;

        public override SkyTarget SkyTarget
        {
            get
            {
                float glow = map.skyManager.CurSkyGlow;
                return new SkyTarget(glow, watcher.Gold ? GoldColors : ColdColors, 1f, 1f);
            }
        }

        public override float SkyTargetLerpFactor
        {
            get
            {
                if (watcher.Gold)
                {
                    int left = watcher.goldTicksLeft, elapsed = TideWatcher.GoldTicks - left;
                    float fade = Mathf.Min(1f, elapsed / 180f) * Mathf.Min(1f, left / (float)TideWatcher.GoldFadeTicks);
                    return MaxGoldLerp * fade;
                }
                return MaxColdLerp * watcher.losingIntensity;
            }
        }

        public override void FireEvent() { }

        public override void WeatherEventTick() { }
    }
}
