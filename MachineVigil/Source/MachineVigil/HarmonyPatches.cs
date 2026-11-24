using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MachineVigil
{
    /// <summary>
    /// RimWorld/PawnRelationWorker_Overseer.cs:33 - called from Pawn_RelationsTracker.RemoveRelationsOnDeath
    /// (inside Pawn.Kill, before the pawn despawns) with (dead pawn, other pawn). Vanilla has already
    /// disconnected the mech; we register it in the dead mechanitor's vigil.
    /// </summary>
    [HarmonyPatch(typeof(PawnRelationWorker_Overseer), nameof(PawnRelationWorker_Overseer.Notify_PostRemovedByDeath))]
    static class Patch_Overseer_PostRemovedByDeath
    {
        static void Postfix(Pawn firstPawn, Pawn secondPawn)
        {
            try
            {
                if (firstPawn == null || secondPawn == null || Current.ProgramState != ProgramState.Playing) return;
                bool firstIsMech = firstPawn.RaceProps?.IsMechanoid == true;
                Pawn mechanitor = firstIsMech ? secondPawn : firstPawn;
                Pawn mech = firstIsMech ? firstPawn : secondPawn;
                // Pawn.Kill calls this before health.SetDead(): the mechanitor is not Dead yet, only isBeingKilled.
                bool dying = mechanitor.Dead || mechanitor.health?.isBeingKilled == true;
                if (!dying || mechanitor.RaceProps.IsMechanoid) return;
                if (mech.Dead || mech.Destroyed || mech.RaceProps?.IsMechanoid != true || mech.Faction != Faction.OfPlayer) return;
                GameComponent_MachineVigil.Instance?.Register(mechanitor, mech);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Machine Vigil] failed to register a vigil: " + e, 0x4D56_0001);
            }
        }
    }

    /// <summary>
    /// RimWorld/PawnRelationWorker_Overseer.cs:7 - runs from AddDirectRelation BEFORE the relation is
    /// stored, so GetOverseer() is still null here: wake the mech directly.
    /// </summary>
    [HarmonyPatch(typeof(PawnRelationWorker_Overseer), nameof(PawnRelationWorker_Overseer.OnRelationCreated))]
    static class Patch_Overseer_OnRelationCreated
    {
        static void Postfix(Pawn firstPawn, Pawn secondPawn)
        {
            try
            {
                var comp = GameComponent_MachineVigil.Instance;
                if (comp == null || firstPawn == null || secondPawn == null) return;
                Pawn mech = firstPawn.RaceProps?.IsMechanoid == true ? firstPawn : secondPawn;
                if (comp.VigilFor(mech) != null) comp.Wake(mech, showMessage: true);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Machine Vigil] failed to wake a mech: " + e, 0x4D56_0002);
            }
        }
    }
}
