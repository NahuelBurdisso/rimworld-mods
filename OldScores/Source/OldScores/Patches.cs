using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OldScores
{
    /// <summary>
    /// Pawn.Kill(DamageInfo? dinfo, Hediff exactCulprit) - Verse/Pawn.cs:2096. The prefix still sees
    /// the victim alive and spawned (faction, hostility, leadership, position); the postfix runs after.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_PawnKill
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, out KillContext __state)
        {
            __state = null;
            if (__instance.Dead || OldScoresTracker.Instance == null) return;
            Faction faction = __instance.Faction;
            __state = new KillContext
            {
                map = __instance.MapHeld,
                position = __instance.PositionHeld,
                killer = dinfo?.Instigator as Pawn,
                victimFaction = faction,
                victimHostile = __instance.HostileTo(Faction.OfPlayer),
                victimDowned = __instance.Downed,
                victimHumanlike = __instance.RaceProps.Humanlike,
                victimWasLeader = faction != null && faction.leader == __instance,
            };
        }

        public static void Postfix(Pawn __instance, KillContext __state)
        {
            if (__state == null || !__instance.Dead) return;
            OldScoresTracker tracker = OldScoresTracker.Instance;
            if (tracker == null) return;
            try { tracker.Notify_PawnKilled(__instance, __state); }
            catch (System.Exception ex) { Log.ErrorOnce("[Old Scores] kill handling failed: " + ex, 0x0D5C10); }
        }
    }

    /// <summary>Pawn.PostApplyDamage(DamageInfo dinfo, float totalDamageDealt) - Verse/Pawn.cs:1935.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_PawnPostApplyDamage
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (!(dinfo.Instigator is Pawn)) return;
            OldScoresTracker tracker = OldScoresTracker.Instance;
            if (tracker == null) return;
            // never let an error here escape into vanilla's damage code (verbs, projectiles)
            try { tracker.Notify_ColonistDamaged(__instance, dinfo, totalDamageDealt); }
            catch (System.Exception ex) { Log.ErrorOnce("[Old Scores] damage handling failed: " + ex, 0x0D5C17); }
            try { tracker.Notify_AnimalHurt(__instance, dinfo, totalDamageDealt); }
            catch (System.Exception ex) { Log.ErrorOnce("[Old Scores] beast memory damage handling failed: " + ex, 0x0D5C18); }
        }
    }

    /// <summary>Pawn.ExitMap(bool allowedToJoinOrCreateCaravan, Rot4 exitDir) - Verse/Pawn.cs:2513.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    public static class Patch_PawnExitMap
    {
        public static void Prefix(Pawn __instance, out Map __state) { __state = __instance.Map; }

        public static void Postfix(Pawn __instance, Map __state)
        {
            OldScoresTracker tracker = OldScoresTracker.Instance;
            if (tracker == null) return;
            try { tracker.Notify_PawnExitedMap(__instance, __state); }
            catch (System.Exception ex) { Log.ErrorOnce("[Old Scores] exit handling failed: " + ex, 0x0D5C11); }
        }
    }

    /// <summary>Pawn_GuestTracker.CapturedBy(Faction by, Pawn byPawn = null) - RimWorld/Pawn_GuestTracker.cs:561.</summary>
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.CapturedBy))]
    public static class Patch_CapturedBy
    {
        public static void Postfix(Pawn ___pawn, Faction by, Pawn byPawn)
        {
            OldScoresTracker.Instance?.Notify_Captured(___pawn, by, byPawn);
        }
    }

    /// <summary>GenGuest.PrisonerRelease(Pawn p) - RimWorld/GenGuest.cs:8.</summary>
    [HarmonyPatch(typeof(GenGuest), nameof(GenGuest.PrisonerRelease))]
    public static class Patch_PrisonerRelease
    {
        public static void Postfix(Pawn p)
        {
            OldScoresTracker.Instance?.Notify_Released(p);
        }
    }

    /// <summary>
    /// private static PawnGenerator.IsValidCandidateToRedress(Pawn pawn, PawnGenerationRequest request)
    /// - Verse/PawnGenerator.cs:368. Pawns the mod keeps off-map are never reused for new pawns.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "IsValidCandidateToRedress")]
    public static class Patch_IsValidCandidateToRedress
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result && OldScoresTracker.Instance != null && OldScoresTracker.Instance.IsHolding(pawn)) __result = false;
        }
    }

    /// <summary>
    /// protected virtual IncidentWorker_Raid.PostProcessSpawnedPawns(IncidentParms parms, List&lt;Pawn&gt; pawns)
    /// - RimWorld/IncidentWorker_Raid.cs:129. Runs after the raiders spawn and before MakeLords.
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker_Raid), "PostProcessSpawnedPawns")]
    public static class Patch_RaidPostProcess
    {
        public static void Postfix(IncidentParms parms, List<Pawn> pawns)
        {
            OldScoresTracker tracker = OldScoresTracker.Instance;
            if (tracker == null) return;
            try { tracker.Notify_RaidSpawned(parms, pawns); }
            catch (System.Exception ex) { Log.ErrorOnce("[Old Scores] raid handling failed: " + ex, 0x0D5C12); }
        }
    }

    /// <summary>Pawn.GetInspectString() - Verse/Pawn.cs:3131.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetInspectString))]
    public static class Patch_PawnInspect
    {
        public static void Postfix(Pawn __instance, ref string __result)
        {
            string extra = OldScoresTracker.Instance?.InspectLine(__instance);
            if (extra.NullOrEmpty()) return;
            __result = __result.NullOrEmpty() ? extra : __result.TrimEndNewlines() + "\n" + extra;
        }
    }
}
