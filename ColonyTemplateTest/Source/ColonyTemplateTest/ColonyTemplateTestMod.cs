using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ColonyTemplate;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonyTemplateTest
{
    // Armed only under `rw launch --quicktest`. Runs from Update (frames), not ticks,
    // so a mod dialog that pauses the quicktest map does not stall it.
    public class HarnessComponent : GameComponent
    {
        bool active;
        int frames;

        public HarnessComponent(Game game) { }

        public override void FinalizeInit()
        {
            active = GenCommandLine.CommandLineArgPassed("quicktest");
        }

        public override void GameComponentUpdate()
        {
            if (!active || Find.CurrentMap == null || ++frames != 120) return;
            try
            {
                TestColonySetup();
                TestFileRoundTrip();
                TestApplyToStartingPawns();
                Log.Message("[Test] done");
            }
            catch (Exception ex)
            {
                Log.Error("[Test] FAIL exception: " + ex);
            }
        }

        static void TestColonySetup()
        {
            Map map = Find.CurrentMap;
            ColonyTemplateSettings s = ColonyTemplateMod.Settings;
            s.applySchedule = s.applyPriorities = s.applyPolicies = true;
            s.schedule = ColonyTemplateSettings.DefaultSchedule;
            s.medCare = MedicalCareCategory.NormalOrWorse;
            int n = ColonySetup.Apply(map);
            List<Pawn> pawns = CrewCapture.MapColonists(map);
            Check(n == pawns.Count && n > 0, $"setup touched {n} colonists");
            Check(Find.PlaySettings.useWorkPriorities, "manual work priorities on");

            TraitDef owl = DefDatabase<TraitDef>.GetNamed("NightOwl");
            foreach (Pawn p in pawns)
            {
                bool isOwl = p.story.traits.HasTrait(owl);
                TimeAssignmentDef h0 = p.timetable.GetAssignment(isOwl ? 12 : 0);
                TimeAssignmentDef h10 = p.timetable.GetAssignment(isOwl ? 22 : 10);
                TimeAssignmentDef h20 = p.timetable.GetAssignment(isOwl ? 8 : 20);
                Check(h0 == TimeAssignmentDefOf.Sleep && h10 == TimeAssignmentDefOf.Work && h20 == TimeAssignmentDefOf.Joy,
                    $"{p.LabelShort} schedule (owl={isOwl}) {h0.defName}/{h10.defName}/{h20.defName}");
                Check(p.playerSettings.medCare == MedicalCareCategory.NormalOrWorse, $"{p.LabelShort} medicine NormalOrWorse");
                HostilityResponseMode want = p.WorkTagIsDisabled(WorkTags.Violent) ? HostilityResponseMode.Flee : HostilityResponseMode.Attack;
                Check(p.playerSettings.hostilityResponse == want, $"{p.LabelShort} hostility {p.playerSettings.hostilityResponse}");
                WorkTypeDef ff = WorkTypeDefOf.Firefighter;
                if (!p.WorkTypeIsDisabled(ff))
                    Check(p.workSettings.GetPriority(ff) == 1, $"{p.LabelShort} firefighting 1");
            }

            foreach (WorkTypeDef w in DefDatabase<WorkTypeDef>.AllDefsListForReading.Where(w => w.visible && !w.relevantSkills.NullOrEmpty()))
            {
                List<Pawn> able = pawns.Where(p => !p.WorkTypeIsDisabled(w)).ToList();
                if (able.Count == 0) continue;
                int ones = able.Count(p => p.workSettings.GetPriority(w) == 1);
                bool allSet = able.All(p => p.workSettings.GetPriority(w) >= 1 && p.workSettings.GetPriority(w) <= 4);
                Check(ones == 1 && allSet, $"{w.defName}: exactly one colonist at 1 ({ones}), all 1-4");
            }
        }

        static void TestFileRoundTrip()
        {
            List<Pawn> pawns = CrewCapture.MapColonists(Find.CurrentMap);
            CrewTemplate crew = CrewCapture.Capture(pawns);
            TemplateFiles.Save("__harness", crew);
            Check(File.Exists(TemplateFiles.PathFor("__harness")), "template file written");
            Check(TemplateFiles.Names().Contains("__harness"), "template listed");
            CrewTemplate back = TemplateFiles.Load("__harness");
            Check(back != null && back.pawns.Count == crew.pawns.Count, $"round trip pawn count {back?.pawns.Count}");
            for (int i = 0; i < crew.pawns.Count; i++)
            {
                PawnTemplate a = crew.pawns[i], b = back.pawns[i];
                bool same = a.first == b.first && a.last == b.last && a.gender == b.gender
                    && Math.Abs(a.bioAge - b.bioAge) < 0.01f && a.childhood == b.childhood && a.adulthood == b.adulthood
                    && a.traits.Select(t => t.def + t.degree).SequenceEqual(b.traits.Select(t => t.def + t.degree))
                    && a.skills.Select(s => s.def + s.level + s.passion).SequenceEqual(b.skills.Select(s => s.def + s.level + s.passion))
                    && a.apparel.Count == b.apparel.Count && a.weapon?.def == b.weapon?.def
                    && a.hair == b.hair && a.headType == b.headType && a.bodyType == b.bodyType
                    && a.relations.Count == b.relations.Count;
                Check(same, $"round trip keeps {a.Label}");
            }
            File.Delete(TemplateFiles.PathFor("__harness"));
        }

        static void TestApplyToStartingPawns()
        {
            Pawn source = CrewCapture.MapColonists(Find.CurrentMap).First(p => p.apparel.WornApparel.Count > 0);
            var crew = CrewCapture.Capture(new List<Pawn> { source });
            PawnTemplate a = crew.pawns[0];
            a.first = "Ada"; a.nick = "Ada"; a.last = "Tester";
            a.gender = Gender.Female;
            a.bioAge = 40f; a.chronoAge = 40f;
            a.bodyType = "Female";
            a.traits = new List<TraitEntry> { new TraitEntry { def = "Tough" }, new TraitEntry { def = "Industriousness", degree = 2 } };
            a.skills.First(s => s.def == "Shooting").level = 15;
            a.skills.First(s => s.def == "Shooting").passion = Passion.Major;
            a.possessions = new List<GearEntry> { new GearEntry { def = "MedicineIndustrial", count = 5 }, new GearEntry { def = "NoSuchThing_Harness", count = 1 } };
            a.weapon = new GearEntry { def = "Gun_BoltActionRifle", hasQuality = true, quality = QualityCategory.Excellent };
            a.relations = new List<RelationEntry> { new RelationEntry { def = "Spouse", other = 1 } };
            PawnTemplate b = new PawnTemplate { first = "Bo", nick = "Bo", last = "Tester", gender = Gender.Male, bioAge = 30f, chronoAge = 30f };
            b.skills.Add(new SkillEntry { def = "Cooking", level = 12, passion = Passion.Minor });
            crew.pawns.Add(b);

            GameInitData saved = Current.Game.InitData;
            Current.Game.InitData = new GameInitData();
            try
            {
                List<Pawn> start = Find.GameInitData.startingAndOptionalPawns;
                for (int i = 0; i < 2; i++)
                    start.Add(StartingPawnUtility.NewGeneratedStartingPawn(i));
                var problems = new StringBuilder();
                CrewApply.ApplyToStartingPawns(crew, problems);

                Pawn pa = start[0], pb = start[1];
                Check(pa.Name.ToStringShort == "Ada" && ((NameTriple)pa.Name).Last == "Tester", $"name applied: {pa.Name}");
                Check(pa.gender == Gender.Female, "gender applied");
                Check(Math.Abs(pa.ageTracker.AgeBiologicalYearsFloat - 40f) < 1f, $"age applied: {pa.ageTracker.AgeBiologicalYearsFloat:F1}");
                Check(pa.story.Childhood?.defName == a.childhood && pa.story.Adulthood?.defName == a.adulthood, $"backstories applied: {pa.story.Childhood?.defName}/{pa.story.Adulthood?.defName}");
                Check(pa.story.traits.HasTrait(TraitDef.Named("Tough")) && pa.story.traits.HasTrait(TraitDef.Named("Industriousness"), 2)
                    && pa.story.traits.allTraits.Count(t => t.sourceGene == null) == 2, "traits replaced: " + string.Join(",", pa.story.traits.allTraits.Select(t => t.LabelCap)));
                SkillRecord shoot = pa.skills.GetSkill(SkillDefOf.Shooting);
                Check(shoot.Level == 15 && shoot.passion == Passion.Major, $"shooting 15 major: {shoot.Level} {shoot.passion}");
                Check(pb.skills.GetSkill(SkillDefOf.Cooking).Level == 12 && pb.skills.GetSkill(SkillDefOf.Cooking).passion == Passion.Minor, "pawn 2 cooking 12 minor");
                Check(pa.story.hairDef?.defName == a.hair && pa.story.headType?.defName == a.headType && pa.story.bodyType?.defName == "Female", "appearance applied");
                Check(pa.apparel.WornApparel.Count + problems.ToString().Split('\n').Count(l => l.Contains("cannot wear")) >= a.apparel.Count,
                    $"apparel worn {pa.apparel.WornApparel.Count}/{a.apparel.Count}");
                Check(pa.equipment.Primary?.def.defName == "Gun_BoltActionRifle" && pa.equipment.Primary.TryGetQuality(out QualityCategory q) && q == QualityCategory.Excellent,
                    "weapon bolt-action excellent");
                List<ThingDefCount> owned = Find.GameInitData.startingPossessions[pa];
                Check(owned.Count == 1 && owned[0].ThingDef.defName == "MedicineIndustrial" && owned[0].Count == 5, "possessions replaced");
                Check(problems.ToString().Contains("NoSuchThing_Harness"), "missing def reported, not fatal");
                Check(pa.relations.DirectRelationExists(PawnRelationDefOf.Spouse, pb) && pb.relations.DirectRelationExists(PawnRelationDefOf.Spouse, pa), "spouses both ways");
                Check(pb.equipment.Primary == null && pb.apparel.WornApparel.Count == 0 && Find.GameInitData.startingPossessions[pb].Count == 0, "pawn 2 has only template gear (none)");
                Log.Message("[Test] problems reported:\n" + problems);
            }
            finally
            {
                foreach (Pawn p in Find.GameInitData.startingAndOptionalPawns)
                {
                    p.relations.ClearAllRelations();
                    Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard);
                }
                Current.Game.InitData = saved;
            }
        }

        static void Check(bool ok, string what)
        {
            if (ok) Log.Message("[Test] PASS " + what);
            else Log.Error("[Test] FAIL " + what);
        }
    }
}
