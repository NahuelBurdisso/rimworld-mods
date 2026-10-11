using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Gesta
{
    /// <summary>Gesta_ChampChallenge duty: walk to the cell beside the banner (duty focusSecond), then wait facing it.</summary>
    public class JobGiver_GestaChampionChallenge : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            PawnDuty duty = pawn.mindState?.duty;
            if (duty == null || !duty.focus.IsValid) return JobGiver_GestaHold.StandJob();
            IntVec3 banner = duty.focus.Cell;
            IntVec3 stand = duty.focusSecond.IsValid ? duty.focusSecond.Cell : banner;
            if (pawn.Position == stand || pawn.Position.InHorDistOf(banner, Mathf.Max(1.5f, duty.radius))
                || !pawn.CanReach(stand, PathEndMode.OnCell, Danger.Deadly))
                return JobGiver_GestaHold.StandJob();
            Job go = JobMaker.MakeJob(JobDefOf.Goto, stand);
            go.locomotionUrgency = LocomotionUrgency.Jog;
            go.expiryInterval = 600;
            go.checkOverrideOnExpire = true;
            return go;
        }
    }

    /// <summary>
    /// Gesta_ChampHold duty (and the champion's wait): stand still. Not JobDefOf.Wait, whose driver
    /// melees any adjacent hostile: colonists are not threat-disabled, so every passer-by would be hit
    /// and the truce would break by itself.
    /// </summary>
    public class JobGiver_GestaHold : ThinkNode_JobGiver
    {
        public static Job StandJob()
        {
            Job job = JobMaker.MakeJob(GestaDefOf.Gesta_ChampStand);
            job.expiryInterval = 120;
            job.checkOverrideOnExpire = true;   // re-think now and then, without a Succeeded end
            return job;
        }

        protected override Job TryGiveJob(Pawn pawn) => StandJob();
    }

    /// <summary>Stands in place facing the duty focus (if any). Never attacks anyone.</summary>
    public class JobDriver_GestaStand : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil stand = ToilMaker.MakeToil("GestaStand");
            stand.initAction = () => pawn.pather?.StopDead();
            stand.tickAction = () =>
            {
                LocalTargetInfo focus = pawn.mindState?.duty?.focus ?? LocalTargetInfo.Invalid;
                if (focus.IsValid) pawn.rotationTracker.FaceTarget(focus);
            };
            stand.handlingFacing = true;
            stand.defaultCompleteMode = ToilCompleteMode.Never;
            yield return stand;
        }
    }

    /// <summary>Gesta_ChampDuel duty: melee the challenger (duty focus) and nobody else; never finish them off.</summary>
    public class JobGiver_GestaChampionDuel : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!(pawn.mindState?.duty?.focus.Thing is Pawn foe) || foe.Dead || foe.Downed || !foe.Spawned || foe.Map != pawn.Map) return null;
            Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, foe);
            job.expiryInterval = 300;
            job.checkOverrideOnExpire = true;
            job.killIncappedTarget = false;
            return job;
        }
    }

    /// <summary>The champion's banner: shows who is calling and how long is left.</summary>
    public class Building_ChallengeBanner : Building
    {
        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            Challenge c = ChampionsChallenge.Instance?.ForBanner(this);
            if (c == null) return s;
            string line;
            if (c.phase == ChallengePhase.Duel)
                line = "Gesta_Champion_InspectDuel".Translate(c.challenger?.LabelShort ?? "?", c.champion?.LabelShort ?? "?");
            else
            {
                int left = Mathf.Max(0, ChampionsChallenge.AcceptTicks - (Find.TickManager.TicksGame - c.startTick));
                line = "Gesta_Champion_Inspect".Translate(c.champion?.LabelShort ?? "?", left.ToStringTicksToPeriod());
            }
            return s.NullOrEmpty() ? line : s + "\n" + line;
        }
    }

    /// <summary>
    /// LordToil_AssaultColony.UpdateAllDuties() replaces every duty that is not AssaultColony; vanilla
    /// calls it on any down, undown or new pawn. While a challenge is on, put ours back.
    /// </summary>
    [HarmonyPatch(typeof(LordToil_AssaultColony), nameof(LordToil_AssaultColony.UpdateAllDuties))]
    public static class Patch_AssaultUpdateAllDuties
    {
        public static void Postfix(LordToil_AssaultColony __instance)
        {
            ChampionsChallenge cc = ChampionsChallenge.Instance;
            if (cc != null && cc.Active.Count > 0) cc.Notify_DutiesUpdated(__instance.lord);
        }
    }

    /// <summary>Lord.Notify_PawnDamaged(Pawn victim, DamageInfo dinfo), from Pawn_HealthTracker.PreApplyDamage.</summary>
    [HarmonyPatch(typeof(Lord), nameof(Lord.Notify_PawnDamaged))]
    public static class Patch_LordPawnDamaged
    {
        public static void Postfix(Lord __instance, Pawn victim, DamageInfo dinfo)
        {
            ChampionsChallenge cc = ChampionsChallenge.Instance;
            if (cc != null && cc.Active.Count > 0) cc.Notify_RaiderDamaged(__instance, victim, dinfo);
        }
    }
}
