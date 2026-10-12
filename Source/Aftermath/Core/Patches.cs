using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Aftermath
{
    /// <summary>
    /// Pawn.Kill(DamageInfo? dinfo, Hediff exactCulprit) - Verse/Pawn.cs:2096. The prefix still sees the
    /// victim alive, in his lord (Kill removes him later), in his bed and with his equipment.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_PawnKill
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            if (__instance == null || __instance.Dead) return;
            try { VigilTracker.Notify_PawnDying(__instance); }
            catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] vigil kill handling failed: " + ex, 0x0AF7E01); }
            try { StandardTracker.Notify_Falling(__instance, dinfo); }
            catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] standard kill handling failed: " + ex, 0x0AF7E02); }
            try { FoeTracker.Instance?.Notify_PawnDying(__instance, dinfo); }
            catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] foe kill handling failed: " + ex, 0x0AF7E03); }
            try { SentinelUtility.Notify_Takedown(__instance, dinfo); }
            catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] sentinel kill handling failed: " + ex, 0x0AF7E08); }
        }
    }

    /// <summary>
    /// private Pawn_HealthTracker.MakeDowned(DamageInfo? dinfo, Hediff hediff) - Verse/Pawn_HealthTracker.cs:837.
    /// Prefix: the pawn is not downed yet and still in his lord (Notify_PawnLost(Incapped) comes at :867).
    /// </summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_MakeDowned
    {
        public static void Prefix(Pawn_HealthTracker __instance, Pawn ___pawn, DamageInfo? dinfo)
        {
            if (___pawn == null || __instance.Downed || ___pawn.Dead) return;
            try { StandardTracker.Notify_Falling(___pawn, dinfo); }
            catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] standard downed handling failed: " + ex, 0x0AF7E04); }
            try { FoeTracker.Instance?.Notify_Downed(___pawn, dinfo); }
            catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] foe downed handling failed: " + ex, 0x0AF7E05); }
            try { SentinelUtility.Notify_Takedown(___pawn, dinfo); }
            catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] sentinel downed handling failed: " + ex, 0x0AF7E09); }
        }
    }

    /// <summary>
    /// Pawn.PostApplyDamage(DamageInfo dinfo, float totalDamageDealt) - Verse/Pawn.cs:1935: standard veterans,
    /// sentinel engagements and old hands.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_PawnPostApplyDamage
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo)
        {
            if (dinfo.Instigator == null) return;
            if (dinfo.Instigator is Pawn attacker)
            {
                try { StandardTracker.Notify_Damage(__instance, attacker); }
                catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] veteran tracking failed: " + ex, 0x0AF7E06); }
            }
            try { SentinelUtility.Notify_Damage(__instance, dinfo); }
            catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] sentinel damage tracking failed: " + ex, 0x0AF7E0A); }
        }
    }

    /// <summary>JobDriver.Cleanup(JobCondition condition) - Verse.AI/JobDriver.cs:274. Runs once when a job ends.</summary>
    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.Cleanup))]
    public static class Patch_JobDriverCleanup
    {
        public static void Postfix(JobDriver __instance, JobCondition condition)
        {
            if (!(__instance is JobDriver_VisitSickPawn)) return;
            try { OfferingUtility.Notify_VisitEnded(__instance, condition); }
            catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] visit handling failed: " + ex, 0x0AF7E07); }
        }
    }
}
