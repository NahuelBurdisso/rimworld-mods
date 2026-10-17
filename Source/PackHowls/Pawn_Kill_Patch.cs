using System;
using HarmonyLib;
using Verse;

namespace PackHowls
{
    /// <summary>
    /// Every death goes through Pawn.Kill(DamageInfo?, Hediff) (Verse/Pawn.cs:2096). The victim and the fight are
    /// judged in the prefix, while the pawn is still alive and spawned; the howl is queued in the postfix, only if
    /// the pawn really died (Anomaly's death refusal can cancel it). Never skips the original.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Pawn_Kill_Patch
    {
        public struct State
        {
            public Map map;
            public IntVec3 cell;
        }

        public static void Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit, out State __state)
        {
            __state = default;
            try
            {
                if (Current.ProgramState != ProgramState.Playing || __instance.Dead) return;
                if (!PackHowlsUtility.IsMournedVictim(__instance)) return;
                Map map = __instance.MapHeld;
                if (map == null || !map.IsPlayerHome) return;
                if (!PackHowlsUtility.IsCombatDeath(__instance, dinfo, exactCulprit, map)) return;
                __state.map = map;
                __state.cell = __instance.PositionHeld;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pack Howls] error judging a death: " + e, 0x5ac4_0001);
            }
        }

        public static void Postfix(Pawn __instance, State __state)
        {
            if (__state.map == null || !__instance.Dead || __state.map.Disposed) return;
            try
            {
                MapComponent_PackHowls.For(__state.map)?.Notify_Fallen(__instance, __state.cell);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pack Howls] error queueing a howl: " + e, 0x5ac4_0002);
            }
        }
    }
}
