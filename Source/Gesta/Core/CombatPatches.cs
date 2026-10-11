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

    /// <summary>What a death looked like while the victim was still alive (read in the Kill prefix).</summary>
    public class KillContext
    {
        public Map map;
        public IntVec3 position;
        public Pawn killer;
        public DamageDef damageDef;
        /// <summary>Hostile, on its feet and not one of ours (a berserk colonist does not count).</summary>
        public bool victimWasHostile;
        public bool killerAdjacent;
        public bool victimWasLeader;
        public float victimBodySize;
        public float victimCombatPower;

        /// <summary>Fire carries its instigator for minutes; it never counts as a feat.</summary>
        public bool ByFire => damageDef != null && (damageDef == DamageDefOf.Flame || damageDef == DamageDefOf.Burn);

        /// <summary>A blow struck from the next cell, not a projectile, blast or fire.</summary>
        public bool Melee => damageDef != null && !damageDef.isRanged && !damageDef.isExplosive && !ByFire && killerAdjacent;
    }

    /// <summary>
    /// Pawn.Kill(DamageInfo? dinfo, Hediff exactCulprit). The prefix still sees the pawn spawned,
    /// with its map, faction and leader status; the postfix runs after it is dead and despawned.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_PawnKill
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, out KillContext __state)
        {
            __state = null;
            if (__instance.Dead) return;
            Map map = __instance.MapHeld;
            if (map == null) return;
            Pawn killer = dinfo?.Instigator as Pawn;
            __state = new KillContext
            {
                map = map,
                position = __instance.PositionHeld,
                killer = killer,
                damageDef = dinfo?.Def,
                victimWasHostile = __instance.HostileTo(Faction.OfPlayer) && !__instance.Downed && __instance.Faction != Faction.OfPlayer,
                killerAdjacent = killer != null && killer.Spawned && killer.Map == map
                                 && killer.Position.AdjacentTo8WayOrInside(__instance.PositionHeld),
                // only the real leader of the faction or a boss: every raid group can roll a PirateBoss
                victimWasLeader = (__instance.Faction != null && __instance.Faction.leader == __instance) || __instance.kindDef.isBoss,
                victimBodySize = __instance.BodySize,
                victimCombatPower = __instance.kindDef.combatPower,
            };
        }

        public static void Postfix(Pawn __instance, KillContext __state)
        {
            if (__state == null || !__instance.Dead) return;
            LastStandTracker.Instance?.Notify_PawnDied(__instance);
            FeatTracker.Instance?.Notify_Killed(__instance, __state);
            __state.map.GetComponent<PoppyField>()?.Notify_Killed(__instance, __state);
            if (__instance.Faction == Faction.OfPlayer) EngagementTracker.Instance?.QueueCheck(__state.map);
        }
    }
}
