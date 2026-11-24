using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyTemplate
{
    // Schedule, work priorities and per-pawn policies for every free colonist on a map.
    public static class ColonySetup
    {
        // Work types without relevant skills: fixed priority (1 = always, 4 = when idle).
        static readonly Dictionary<string, int> FixedPriority = new Dictionary<string, int>
        {
            { "Firefighter", 1 }, { "Patient", 1 }, { "PatientBedRest", 1 },
            { "BasicWorker", 2 }, { "Childcare", 3 }, { "Hauling", 3 }, { "Cleaning", 3 },
        };

        public static int Apply(Map map)
        {
            ColonyTemplateSettings s = ColonyTemplateMod.Settings;
            List<Pawn> pawns = CrewCapture.MapColonists(map);
            if (s.applySchedule)
                foreach (Pawn p in pawns) ApplySchedule(p, s.schedule);
            if (s.applyPolicies)
                foreach (Pawn p in pawns) ApplyPolicies(p, s);
            if (s.applyPriorities)
                ApplyPriorities(pawns);
            return pawns.Count;
        }

        // 24 chars from hour 0: S sleep, W work, J joy, A anything, M meditate.
        // Night owls get it shifted 12 hours so they sleep in the day.
        static void ApplySchedule(Pawn p, string schedule)
        {
            if (p.timetable == null || schedule == null || schedule.Length != 24) return;
            TraitDef nightOwl = DefDatabase<TraitDef>.GetNamedSilentFail("NightOwl");
            int shift = nightOwl != null && p.story?.traits.HasTrait(nightOwl) == true ? 12 : 0;
            for (int h = 0; h < 24; h++)
            {
                TimeAssignmentDef ta = Assignment(schedule[(h + 24 - shift) % 24]);
                if (ta != null) p.timetable.SetAssignment(h, ta);
            }
        }

        public static TimeAssignmentDef Assignment(char c)
        {
            switch (char.ToUpperInvariant(c))
            {
                case 'S': return TimeAssignmentDefOf.Sleep;
                case 'W': return TimeAssignmentDefOf.Work;
                case 'J': return TimeAssignmentDefOf.Joy;
                case 'M': return ModsConfig.RoyaltyActive ? TimeAssignmentDefOf.Meditate : TimeAssignmentDefOf.Joy;
                case 'A': return TimeAssignmentDefOf.Anything;
                default: return null;
            }
        }

        static void ApplyPolicies(Pawn p, ColonyTemplateSettings s)
        {
            if (p.playerSettings == null) return;
            p.playerSettings.medCare = s.medCare;
            p.playerSettings.selfTend = s.selfTend && !p.WorkTypeIsDisabled(WorkTypeDefOf.Doctor);
            // pawns that cannot fight run away instead of standing there
            p.playerSettings.hostilityResponse = p.WorkTagIsDisabled(WorkTags.Violent)
                ? HostilityResponseMode.Flee : HostilityResponseMode.Attack;
        }

        // Best colonist at each skilled job gets 1; then major passion or skill 10+ gets 2,
        // minor passion or skill 6+ gets 3, everyone else 4.
        static void ApplyPriorities(List<Pawn> pawns)
        {
            Find.PlaySettings.useWorkPriorities = true;
            foreach (Pawn p in pawns) p.workSettings?.EnableAndInitializeIfNotAlreadyInitialized();

            foreach (WorkTypeDef w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (!w.visible) continue;
                List<Pawn> able = pawns.Where(p => p.workSettings != null && !p.WorkTypeIsDisabled(w)).ToList();
                if (w.relevantSkills.NullOrEmpty())
                {
                    int fixedPrio = FixedPriority.TryGetValue(w.defName, out int fp) ? fp : 3;
                    foreach (Pawn p in able) p.workSettings.SetPriority(w, fixedPrio);
                    continue;
                }
                Pawn best = able.OrderByDescending(p => Score(p, w)).FirstOrDefault();
                foreach (Pawn p in able)
                {
                    float level = p.skills.AverageOfRelevantSkillsFor(w);
                    Passion passion = p.skills.MaxPassionOfRelevantSkillsFor(w);
                    int prio = p == best ? 1
                        : passion == Passion.Major || level >= 10f ? 2
                        : passion == Passion.Minor || level >= 6f ? 3
                        : 4;
                    p.workSettings.SetPriority(w, prio);
                }
            }
        }

        static float Score(Pawn p, WorkTypeDef w)
        {
            Passion passion = p.skills.MaxPassionOfRelevantSkillsFor(w);
            return p.skills.AverageOfRelevantSkillsFor(w) + (passion == Passion.Major ? 4f : passion == Passion.Minor ? 2f : 0f);
        }
    }
}
