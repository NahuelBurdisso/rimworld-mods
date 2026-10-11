using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gesta
{
    /// <summary>
    /// Who last hurt each pawn, so a raider who bleeds out (Pawn.Kill with no DamageInfo) still has a
    /// killer for the poppy. Not saved; entries older than a day are ignored.
    /// </summary>
    public static class LastAttackers
    {
        public const int MaxAgeTicks = 60000;
        static readonly Dictionary<int, (Pawn attacker, int tick)> byVictim = new Dictionary<int, (Pawn, int)>();

        public static void Record(Pawn victim, Pawn attacker)
        {
            if (byVictim.Count > 2000) byVictim.Clear();   // never grows without bound
            byVictim[victim.thingIDNumber] = (attacker, Find.TickManager.TicksGame);
        }

        public static Pawn Take(Pawn victim)
        {
            if (!byVictim.TryGetValue(victim.thingIDNumber, out var e)) return null;
            byVictim.Remove(victim.thingIDNumber);
            return Find.TickManager.TicksGame - e.tick <= MaxAgeTicks ? e.attacker : null;
        }

        /// <summary>Who last hurt this pawn (within a day), without forgetting it.</summary>
        public static Pawn Peek(Pawn victim) =>
            byVictim.TryGetValue(victim.thingIDNumber, out var e) && Find.TickManager.TicksGame - e.tick <= MaxAgeTicks ? e.attacker : null;

        public static void Clear() => byVictim.Clear();
    }

    /// <summary>Pawn.PostApplyDamage(DamageInfo dinfo, float totalDamageDealt).</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_PawnPostApplyDamage
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            BloodStains.Notify_Damage(__instance, dinfo, totalDamageDealt);
            if (totalDamageDealt <= 0f || !(dinfo.Instigator is Pawn attacker) || attacker == __instance) return;
            BrothersTracker.Instance?.Notify_Hit(__instance, attacker, totalDamageDealt);
            if (__instance.HostileTo(Faction.OfPlayer)) WarStories.Instance?.Notify_Fought(attacker, attacker.MapHeld);
            if (__instance.Faction == Faction.OfPlayer) ChampionsChallenge.Instance?.Notify_ColonistHit(__instance, attacker);
            if (__instance.RaceProps == null || !__instance.RaceProps.Humanlike) return;
            LastAttackers.Record(__instance, attacker);
        }
    }
}
