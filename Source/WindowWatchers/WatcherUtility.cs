using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace WindowWatchers
{
    /// <summary>Who watches, whom, and when it is fine to pull them off what they are doing. Rules: DESIGN.md.</summary>
    public static class WatcherUtility
    {
        public const float ElderlyLifeFraction = 0.8f;
        public const int FriendOpinion = 40;
        public const int PartnerScore = 200;
        public const int FamilyScore = 150;

        /// <summary>Static part of the watcher rule: a colonist who does not fight (child, elder, pacifist, sick).</summary>
        public static bool IsNonCombatant(Pawn p)
        {
            if (p.DevelopmentalStage.Child()) return true;
            if (p.WorkTagIsDisabled(WorkTags.Violent)) return true;
            if (p.ageTracker.AgeBiologicalYearsFloat >= p.RaceProps.lifeExpectancy * ElderlyLifeFraction) return true;
            if (HealthAIUtility.ShouldSeekMedicalRest(p) || p.health.hediffSet.AnyHediffMakesSickThought) return true;
            return false;
        }

        /// <summary>Can be a watcher right now, ignoring needs (the scan and the giver both use it).</summary>
        public static bool CanBeWatcher(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed || !p.RaceProps.Humanlike || !p.IsColonist) return false;
            if (p.IsMutant || p.Drafted || p.InMentalState || p.IsBurning() || p.IsFighting()) return false;
            if (p.DevelopmentalStage.Baby()) return false;
            if (p.GetLord() != null) return false;
            if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Moving) || !p.health.capacities.CapableOf(PawnCapacityDefOf.Sight)) return false;
            if (p.story?.traits != null && p.story.traits.HasTrait(TraitDefOf.Psychopath)) return false;
            if (ModsConfig.AnomalyActive && p.Inhumanized()) return false;
            if (p.needs?.mood == null) return false;
            return IsNonCombatant(p);
        }

        /// <summary>Priority guard used when the job would be handed out: food, sleep, urgent care, doctoring.</summary>
        public static bool NeedsAllowWatching(Pawn p, bool patientsWaiting)
        {
            Need_Food food = p.needs?.food;
            if (food != null && food.CurCategory >= HungerCategory.Hungry) return false;
            Need_Rest rest = p.needs?.rest;
            if (rest != null && rest.CurCategory >= RestCategory.Tired) return false;
            if (p.timetable != null && p.timetable.CurrentAssignment == TimeAssignmentDefOf.Sleep) return false;
            if (HealthAIUtility.ShouldSeekMedicalRestUrgent(p)) return false;
            // a resting patient stays in bed (LayDown re-thinks every 211 ticks, Toils_LayDown.cs:74), and so does anyone
            // fighting off a disease (bed immunity bonus)
            if (HealthAIUtility.ShouldSeekMedicalRest(p) && (p.InBed() || p.health.hediffSet.HasImmunizableNotImmuneHediff())) return false;
            if (patientsWaiting && p.workSettings != null && p.workSettings.EverWork && p.workSettings.WorkIsActive(WorkTypeDefOf.Doctor)) return false;
            return true;
        }

        /// <summary>A player colonist standing and fighting on this map.</summary>
        public static bool IsFighter(Pawn f, Map map)
        {
            if (f == null || f.Dead || f.Downed || !f.Spawned || f.Map != map) return false;
            if (!f.RaceProps.Humanlike || f.Faction != Faction.OfPlayer) return false;
            return f.Drafted || f.IsFighting();
        }

        /// <summary>How much the watcher cares about this pawn; 0 = not a loved one.</summary>
        public static int BondScore(Pawn watcher, Pawn other)
        {
            if (watcher.relations == null || other == watcher) return 0;
            if (LovePartnerRelationUtility.LovePartnerRelationExists(watcher, other)) return PartnerScore;
            // Child is an implied relation (stored as Parent on the child's side): check both directions via Parent
            if (watcher.relations.DirectRelationExists(PawnRelationDefOf.Parent, other)
                || (other.relations != null && other.relations.DirectRelationExists(PawnRelationDefOf.Parent, watcher))) return FamilyScore;
            if (other.RaceProps.Humanlike)
            {
                int opinion = watcher.relations.OpinionOf(other);
                if (opinion >= FriendOpinion) return opinion;
            }
            return 0;
        }

        /// <summary>
        /// Whether the scan may interrupt the current job to send the pawn watching now. Only casual work, idling, joy
        /// and learning; never sleep, food, medical rest, doctoring, firefighting, rescues or player orders.
        /// </summary>
        public static bool CurrentJobInterruptible(Pawn p)
        {
            Job job = p.CurJob;
            if (job == null) return true;
            if (job.playerForced || !job.def.casualInterruptible) return false;
            if (job.def == WWDefOf.WW_Watch || job.def == WWDefOf.WW_Reunite) return false;
            if (p.carryTracker?.CarriedThing is Pawn) return false;
            WorkTypeDef work = job.workGiverDef?.workType;
            if (work != null && (work == WorkTypeDefOf.Doctor || work == WorkTypeDefOf.Firefighter || work == WorkTypeDefOf.Childcare)) return false;
            switch (p.mindState.lastJobTag)
            {
                case JobTag.Idle:
                case JobTag.MiscWork:
                case JobTag.Fieldwork:
                case JobTag.ChangingApparel:
                case JobTag.UnloadingOwnInventory:
                case JobTag.TakeForInventoryStock:
                    return true;
                case JobTag.SatisfyingNeeds:
                    return job.def.joyKind != null || IsLearningJob(job.def);
                default:
                    return false;
            }
        }

        static bool IsLearningJob(JobDef def)
        {
            if (!ModsConfig.BiotechActive) return false;
            var all = DefDatabase<LearningDesireDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
                if (all[i].jobDef == def) return true;
            return false;
        }

        /// <summary>Distance-squared to the nearest active hostile threat to the player, or float.MaxValue.</summary>
        public static float NearestThreatDistSq(Map map, IntVec3 cell)
        {
            float best = float.MaxValue;
            foreach (IAttackTarget t in map.attackTargetsCache.TargetsHostileToColony)
            {
                if (!GenHostility.IsActiveThreatToPlayer(t)) continue;
                float d = (t.Thing.Position - cell).LengthHorizontalSquared;
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Enemy too close to the spot (SafeRadius) or to the door it is watched through (HostileSightRadius).</summary>
        public static bool ThreatTooClose(Map map, IntVec3 cell, Building_Door door)
        {
            if (NearestThreatDistSq(map, cell) < WatchSight.SafeRadius * WatchSight.SafeRadius) return true;
            if (door != null && NearestThreatDistSq(map, door.Position) < WatchSight.DoorSafeRadius * WatchSight.DoorSafeRadius) return true;
            return false;
        }

        /// <summary>Door the watcher may keep ajar: the player's, openable by the pawn, not forbidden to pass, not in vacuum.</summary>
        public static bool DoorUsable(Building_Door door, Pawn pawn)
        {
            if (door == null || !door.Spawned || door.Faction != Faction.OfPlayer) return false;
            if (door.Map.Biome.inVacuum) return false;
            return door.PawnCanOpen(pawn) && !door.IsForbiddenToPass(pawn);
        }

        public static void GainMemory(Pawn p, ThoughtDef def, Pawn other)
        {
            p.needs?.mood?.thoughts?.memories?.TryGainMemory(def, other);
        }
    }
}
