using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace UnlikelyHero
{
    /// <summary>The rules of DESIGN.md "Reglas exactas". Each check returns null when it passes, else the reason.</summary>
    public static class HeroRules
    {
        /// <summary>Why the last death seen by the patch did not make a hero (for tests and debugging).</summary>
        public static string LastRejection;

        public static string Check(Pawn victim, DamageInfo? dinfo, out Pawn hero)
        {
            hero = dinfo?.Instigator as Pawn;
            if (hero == null) return "no pawn instigator";
            return CheckKiller(hero, victim) ?? CheckVictim(victim, hero) ?? CheckDamage(dinfo.Value, victim);
        }

        public static string CheckKiller(Pawn hero, Pawn victim)
        {
            if (!hero.RaceProps.Humanlike || !hero.IsColonist || hero.IsQuestLodger()) return "killer is not a colonist";
            if (hero.IsMutant) return "killer is a mutant";
            if (hero.story == null || hero.skills == null || hero.needs?.mood == null) return "killer has no story, skills or mood";
            if (hero.Dead || hero.Downed || !hero.Spawned || hero.Map != victim.MapHeld) return "killer is not standing on the victim's map";
            if (hero.InMentalState) return "killer is in a mental state";
            if (HeroLedger.Instance != null && HeroLedger.Instance.IsHero(hero)) return "killer already had the moment";

            HeroConfigDef cfg = HeroDefOf.UnlikelyHero_Config;
            List<Trait> traits = hero.story.traits.allTraits;
            for (int i = 0; i < traits.Count; i++)
                if (!traits[i].Suppressed && cfg.violentTraits.Contains(traits[i].def)) return "killer has a violent trait: " + traits[i].def.defName;

            if (!IsNonCombatant(hero)) return "killer is a fighter";

            if (UnlikelyHeroMod.Settings.requireFirstKill)
            {
                // the records were already incremented for this death (RecordsUtility.Notify_PawnKilled runs first)
                int humans = hero.records.GetAsInt(RecordDefOf.KillsHumanlikes) - (CountsAsHumanlikeKill(victim) ? 1 : 0);
                int mechs = hero.records.GetAsInt(RecordDefOf.KillsMechanoids);
                if (humans > 0 || mechs > 0) return "killer has killed before";
            }
            return null;
        }

        /// <summary>Same branch as RecordsUtility.Notify_PawnKilled for KillsHumanlikes.</summary>
        static bool CountsAsHumanlikeKill(Pawn victim)
        {
            RaceProperties race = victim.RaceProps;
            if (ModsConfig.AnomalyActive && (victim.IsSubhuman || race.IsAnomalyEntity)) return false;
            return race.Humanlike;
        }

        /// <summary>Rule A (both combat skills low) or rule B (a gentle craft is the best skill).</summary>
        public static bool IsNonCombatant(Pawn p)
        {
            UnlikelyHeroSettings s = UnlikelyHeroMod.Settings;
            int shooting = p.skills.GetSkill(SkillDefOf.Shooting).Level;
            int melee = p.skills.GetSkill(SkillDefOf.Melee).Level;
            if (shooting <= s.maxCombatSkill && melee <= s.maxCombatSkill) return true;
            if (shooting > s.crafterCombatCap || melee > s.crafterCombatCap) return false;

            int top = 0;
            List<SkillRecord> skills = p.skills.skills;
            for (int i = 0; i < skills.Count; i++)
                if (skills[i].Level > top) top = skills[i].Level;
            if (shooting >= top || melee >= top) return false;
            List<SkillDef> gentle = HeroDefOf.UnlikelyHero_Config.gentleSkills;
            for (int i = 0; i < skills.Count; i++)
                if (skills[i].Level == top && gentle.Contains(skills[i].def)) return true;
            return false;
        }

        public static string CheckVictim(Pawn victim, Pawn hero)
        {
            RaceProperties race = victim.RaceProps;
            if (!race.Humanlike && !race.Animal) return "victim is neither humanlike nor animal";
            if (race.IsAnomalyEntity || victim.IsSubhuman || victim.IsMutant) return "victim is an entity or a mutant";
            if (victim.Faction == Faction.OfPlayer) return "victim belongs to the colony";
            if (victim.IsPrisoner) return "victim is a prisoner";
            if (victim.Downed) return "victim was already down";
            if (victim.InBed()) return "victim was in bed";
            if (!victim.Spawned) return "victim is not spawned";
            if (!WasActiveThreat(victim)) return "victim was not an active threat";
            return null;
        }

        /// <summary>
        /// GenHostility.IsActiveThreatTo (GenHostility.cs:311) minus its Awake() test: Awake reads
        /// capacities.CanBeAwake, and a head shot has already dropped Consciousness to 0 by the time Kill runs
        /// (Pawn_HealthTracker.AddHediff -> CheckForStateChange -> Kill). Sleep is read from the job instead.
        /// </summary>
        public static bool WasActiveThreat(Pawn v)
        {
            if (!v.HostileTo(Faction.OfPlayer) || v.Fogged()) return false;
            Lord lord = v.GetLord();
            if (lord != null && lord.LordJob is LordJob_DefendAndExpandHive && (v.mindState.duty == null || v.mindState.duty.def != DutyDefOf.AssaultColony))
                return false;   // vanilla: insects guarding their hive are not a threat to the colony
            if (v.ThreatDisabled(null) || v.IsPrisoner || v.MentalStateDef == MentalStateDefOf.PanicFlee) return false;
            if (v.CurJob != null && v.jobs.curDriver != null && v.jobs.curDriver.asleep) return false;
            CompCanBeDormant dormant = v.TryGetComp<CompCanBeDormant>();
            if (dormant != null && !dormant.Awake) return false;
            return true;
        }

        public static string CheckDamage(DamageInfo dinfo, Pawn victim)
        {
            DamageDef def = dinfo.Def;
            if (def == null) return "no damage def";
            if (def == DamageDefOf.ExecutionCut) return "execution";
            if (HeroDefOf.UnlikelyHero_Config.excludedDamage.Contains(def)) return "fire";
            if (def.isExplosive) return "explosion";
            if (!def.ExternalViolenceFor(victim)) return "not violence";
            return null;
        }
    }
}
