using HarmonyLib;
using RimWorld;
using Verse;

namespace Gesta
{
    /// <summary>Pawn_HealthTracker.MakeDowned(DamageInfo? dinfo, Hediff hediff) is private.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_MakeDowned
    {
        public static void Postfix(Pawn ___pawn)
        {
            if (___pawn == null || !___pawn.Downed || !___pawn.Spawned) return;
            if (___pawn.Faction != Faction.OfPlayer) return;
            LastStandTracker.Instance?.Notify_PawnDowned(___pawn);
            EngagementTracker.Instance?.QueueCheck(___pawn.Map);
        }
    }

    /// <summary>
    /// Pawn.Kill(DamageInfo? dinfo, Hediff exactCulprit). The prefix still sees the pawn spawned,
    /// with its map; the postfix runs after it is dead and despawned.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_PawnKill
    {
        public static void Prefix(Pawn __instance, out Map __state)
        {
            __state = __instance.Dead ? null : __instance.MapHeld;
        }

        public static void Postfix(Pawn __instance, Map __state)
        {
            if (__state == null || !__instance.Dead) return;
            LastStandTracker.Instance?.Notify_PawnDied(__instance);
            if (__instance.Faction == Faction.OfPlayer) EngagementTracker.Instance?.QueueCheck(__state);
        }
    }
}
