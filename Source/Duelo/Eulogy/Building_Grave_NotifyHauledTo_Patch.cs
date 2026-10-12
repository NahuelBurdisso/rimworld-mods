using HarmonyLib;
using RimWorld;
using Verse;

namespace Duelo
{
    /// <summary>
    /// Toils_Haul calls INotifyHauledTo.Notify_HauledTo when a hauler deposits a corpse into a grave:
    /// the moment of burial (Building_Sarcophagus calls base). Without Ideology this is the eulogy's
    /// only trigger; with it, a burial waits for the funeral the colony owes the deceased.
    /// </summary>
    [HarmonyPatch(typeof(Building_Grave), nameof(Building_Grave.Notify_HauledTo))]
    public static class Building_Grave_NotifyHauledTo_Patch
    {
        public static void Postfix(Building_Grave __instance)
        {
            if (!DueloMod.Settings.eulogy || !__instance.Spawned) return;
            try
            {
                MapComponent_Duelo.For(__instance.Map)?.TryReadAtBurial(__instance);
            }
            catch (System.Exception e)
            {
                Log.ErrorOnce("[Duelo] eulogy at burial failed: " + e, 0x5D0E3);
            }
        }
    }
}
