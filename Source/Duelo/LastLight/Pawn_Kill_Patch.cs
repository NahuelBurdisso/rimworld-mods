using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Duelo
{
    /// <summary>
    /// Every death goes through Pawn.Kill. The bond is measured in the prefix, while the victim
    /// is still alive and spawned; the light is released in the postfix, only if the pawn really
    /// died (Anomaly's death refusal can cancel it). The postfix also writes the eulogy, while the
    /// cause of death and the volatile tales are still known.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Pawn_Kill_Patch
    {
        public struct State
        {
            public bool wasAlive;
            public Map map;
            public Vector3 pos;
            public Pawn target;
        }

        public static void Prefix(Pawn __instance, out State __state)
        {
            __state = default;
            if (Current.ProgramState != ProgramState.Playing || __instance.Dead) return;
            __state.wasAlive = true;

            if (!DueloMod.Settings.lastLight || !__instance.Spawned || !LastLightUtility.Qualifies(__instance)) return;
            Map map = __instance.Map;
            Pawn target = LastLightUtility.FindStrongestBond(__instance, map, out _);
            if (target == null && !__instance.RaceProps.Humanlike) return;   // nobody missed this animal

            __state.map = map;
            __state.pos = __instance.DrawPos;
            __state.target = target;
        }

        public static void Postfix(Pawn __instance, State __state, DamageInfo? dinfo, Hediff exactCulprit)
        {
            if (!__state.wasAlive || !__instance.Dead) return;

            if (__state.map != null && !__state.map.Disposed)
                LastLightUtility.Emit(__instance, __state.map, __state.pos, __state.target);

            if (DueloMod.Settings.eulogy && EulogyComposer.Qualifies(__instance))
            {
                try
                {
                    GameComponent_DueloMemory.Instance?.Store(EulogyComposer.ComposeRecord(__instance, dinfo, exactCulprit, causeKnown: true));
                }
                catch (Exception e)
                {
                    Log.Error("[Duelo] could not write the eulogy for " + __instance + ": " + e);
                }
            }
        }
    }
}
