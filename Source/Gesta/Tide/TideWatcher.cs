using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gesta
{
    /// <summary>
    /// Turning Tide: the sky goes cold and grey while the colony is losing a fight (outmatched AND
    /// already losing people), and warms to gold if they turn it around by bringing the enemy down.
    /// The tint is applied to the colours of the final sky target only - glow (light levels, solar
    /// power, darkness combat) is never touched, and night stays night. See DESIGN.md "Feature 5".
    /// </summary>
    public class TideWatcher : MapComponent
    {
        public const float LosingRatio = 2.5f;    // hostile strength / colony strength
        public const float TurnedRatio = 0.8f;
        const int CheckInterval = 250, ChecksToLose = 2;
        public const int GoldTicks = 2500, GoldFadeTicks = 600, GoldRiseTicks = 180;
        const float EasePerTick = 1f / 300f;

        int losingChecks;
        bool wasLosing;                  // this engagement had a losing phase
        int hostilesAtLosing;            // active hostiles when the colony started losing
        public int hostilesFelled;       // hostile pawns killed or downed since then
        public float losingIntensity;    // 0..1, eased
        public int goldTicksLeft;

        public bool Losing => losingChecks >= ChecksToLose;
        public bool Gold => goldTicksLeft > 0;

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
            // while losing, notice the last enemy falling within a second instead of waiting for the poll
            else if (wasLosing && now % 60 == 0 && CombatUtil.ActiveHostilePawns(map) == 0) TryTurn();

            float target = Losing && !Gold ? 1f : 0f;
            losingIntensity = Mathf.MoveTowards(losingIntensity, target, EasePerTick);
            if (goldTicksLeft > 0) goldTicksLeft--;
        }

        /// <summary>Kill/down hooks: hostile pawns falling on this map.</summary>
        public void Notify_HostileFell()
        {
            if (wasLosing) hostilesFelled++;
        }

        /// <summary>Public for tests.</summary>
        public void Evaluate()
        {
            EngagementTracker.Engagement e = EngagementTracker.Instance?.Get(map);
            if (e == null || !e.active)
            {
                if (wasLosing) TryTurn();
                losingChecks = 0;
                wasLosing = false;
                return;
            }
            int fighters = CombatUtil.CountFighters(map);
            float ratio = Ratio(map);
            bool bleeding = fighters < e.maxFighters;   // someone already went down or died
            if (ratio >= LosingRatio && bleeding)
            {
                losingChecks++;
                if (Losing && !wasLosing)
                {
                    wasLosing = true;
                    hostilesAtLosing = CombatUtil.ActiveHostilePawns(map);
                    hostilesFelled = 0;
                }
            }
            else
            {
                if (wasLosing && ratio <= TurnedRatio) TryTurn();
                losingChecks = 0;
            }
        }

        /// <summary>
        /// Gold only for a real turnaround: a fighter still standing and at least half of the enemy
        /// present when the colony started losing brought down (not enemies walking off with loot).
        /// </summary>
        void TryTurn()
        {
            bool earned = CombatUtil.CountFighters(map) > 0 && hostilesFelled >= Mathf.Max(1, (hostilesAtLosing + 1) / 2);
            wasLosing = false;
            losingChecks = 0;
            if (!earned) return;
            goldTicksLeft = GoldTicks;
            losingIntensity = 0f;
            Log.Message($"[Gesta] the tide turned on {map} ({hostilesFelled}/{hostilesAtLosing} enemies brought down)");
        }

        public bool Suppressed() =>
            map.Biome.disableSkyLighting
            || map.gameConditionManager.ConditionIsActive(GameConditionDefOf.Eclipse)
            || map.gameConditionManager.ConditionIsActive(GameConditionDefOf.Aurora);

        /// <summary>0..1 strength of the current tint and whether it is gold.</summary>
        public float TintFactor(out bool gold)
        {
            gold = Gold;
            if (gold)
            {
                int left = goldTicksLeft, elapsed = GoldTicks - left;
                return Mathf.Min(1f, elapsed / (float)GoldRiseTicks) * Mathf.Min(1f, left / (float)GoldFadeTicks);
            }
            return losingIntensity;
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

    /// <summary>
    /// SkyManager.CurrentSkyTarget() (private): after vanilla has blended weather, conditions, events
    /// and sky comps, tint only the colours - multiplicatively, so night stays dark. Glow and shine
    /// are left exactly as vanilla computed them.
    /// </summary>
    [HarmonyPatch(typeof(SkyManager), "CurrentSkyTarget")]
    public static class Patch_SkyManager_CurrentSkyTarget
    {
        static readonly Color ColdTint = new Color(0.82f, 0.9f, 1.12f);
        static readonly Color GoldTint = new Color(1.18f, 1.0f, 0.72f);
        const float ColdSaturation = 0.25f, GoldSaturation = 1.45f;

        public static void Postfix(Map ___map, ref SkyTarget __result)
        {
            if (___map == null || !GestaMod.Settings.turningTide) return;
            TideWatcher tide = ___map.GetComponent<TideWatcher>();
            if (tide == null) return;
            float f = tide.TintFactor(out bool gold);
            if (f <= 0f || tide.Suppressed()) return;
            Color tint = gold ? GoldTint : ColdTint;
            SkyColorSet c = __result.colors;
            c.sky = Color.Lerp(c.sky, c.sky * tint, f);
            c.shadow = Color.Lerp(c.shadow, c.shadow * tint, f);
            c.overlay = Color.Lerp(c.overlay, c.overlay * tint, f);
            c.saturation = Mathf.Lerp(c.saturation, gold ? GoldSaturation : ColdSaturation, f);
            __result.colors = c;
        }
    }
}
