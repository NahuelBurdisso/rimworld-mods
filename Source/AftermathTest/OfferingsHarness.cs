using System.Collections.Generic;
using System.Linq;
using Aftermath;
using RimWorld;
using Verse;
using Verse.AI;

namespace AftermathTest
{
    /// <summary>Feature 5 — Bedside Offerings, through real VisitSickPawn jobs (joyDuration shortened for the run).</summary>
    public class OfferingsHarness : HarnessBase
    {
        public OfferingsHarness(Game game) { }

        protected override int StartTick => TestUtil.OfferingsStart;
        protected override string Name => "Offerings";

        const int VisitTicks = 300;
        const int WaitLimit = 1100;

        class Visit
        {
            public Pawn visitor, patient;
            public bool started, ended;
            public int tries, lastTry = -999;
        }

        IntVec3 root;
        Building_Bed bed1, bed2;
        Pawn p1, p2, lover, hater, neutral, quitter, stayer;
        int oldMinVisit = -1;
        readonly List<Pawn> colonists = new List<Pawn>();
        readonly List<Visit> visits = new List<Visit>();
        int oldJoyDuration = -1;
        int givers1;

        protected override void Step()
        {
            foreach (Visit v in visits) Drive(v);
            switch (phase)
            {
                case 0:
                    Next();
                    Setup();
                    break;
                case 1:
                    if (!AllEnded() && InPhase < WaitLimit) break;
                    CheckVisitEnded("the lover's and the hater's visits");
                    FirstVisits();
                    Next();
                    Start(lover, p1);
                    Start(neutral, p2);
                    break;
                case 2:
                    if (!AllEnded() && InPhase < WaitLimit) break;
                    CheckVisitEnded("the second round of visits");
                    SecondVisits();
                    Next();
                    Start(quitter, p1);
                    break;
                case 3:
                    if (InPhase < 100) break;
                    quitter.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    visits.Clear();
                    // drafted, he cannot go straight back to the bedside for joy and hold the patient
                    if (quitter.drafter != null) { quitter.drafter.Drafted = true; blockers.Add(quitter); }
                    Kit.Check(OfferingUtility.FindGift(p1, quitter.thingIDNumber, map) == null,
                        $"a visit cut short within {AftermathTuning.MinVisitTicks} ticks leaves nothing (negative)");
                    Next();
                    Start(stayer, p1);
                    break;
                case 4:
                    {
                        // interrupted, but only after sitting with him for 200+ ticks: that counts
                        if (p1.needs?.rest != null) p1.needs.rest.CurLevel = 1f;   // an asleep patient ends the visit (FailOn !Awake)
                        JobDriver drv = stayer.jobs.curDriver;
                        bool sat = drv is JobDriver_VisitSickPawn && drv.OnLastToil && VisitTicks - drv.ticksLeftThisToil >= 200;
                        if (!sat && InPhase < 1000)
                        {
                            // a visit that ended early (patient asleep, someone else's visit) is ordered again
                            Visit v = visits.FirstOrDefault();
                            if (v != null && (v.ended || (!v.started && v.tries >= MaxTries)) && restarts < 3)
                            {
                                restarts++;
                                visits.Clear();
                                Start(stayer, p1);
                            }
                            break;
                        }
                        int opinion = stayer.relations.OpinionOf(p1);
                        string facts = Facts();
                        stayer.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        visits.Clear();
                        bool want = opinion >= AftermathTuning.MinOpinionForGift;
                        Offering got = OfferingUtility.FindGift(p1, stayer.thingIDNumber, map);
                        Kit.Check(sat && (got != null) == want,
                            $"a visit interrupted after 200 ticks at the bedside counts (sat {sat}, opinion {opinion}, gift {got?.def?.label ?? "none"}, restarts {restarts}, drafted blockers {blockers.Count})"
                            + (sat ? "" : $" [{facts}]"));
                        Next();
                        ReleaseBlockers();
                        Recover();
                        break;
                    }
                case 5:
                    {
                        Offering o = OfferingUtility.OfferingsFor(p1, map).FirstOrDefault(x => x.visitorId == lover.thingIDNumber);
                        if (o != null && o.state == OfferingState.Active && InPhase < 600) break;
                        Thought_Memory m = p1.needs?.mood?.thoughts.memories.GetFirstMemoryOfDef(AftermathDefOf.Aftermath_CameToSeeMe);
                        int want = OfferingUtility.StageFor(givers1);
                        Kit.Check(o != null && o.state == OfferingState.Done, $"back on his feet, {p1.LabelShort}'s gifts are done ({o?.state})");
                        Kit.Check(m != null && m.CurStageIndex == want, $"...and he remembers who came: stage {m?.CurStageIndex} for {givers1} giver(s), expected {want}");
                        Next();
                        Kit.KillBy(p2, null);
                        break;
                    }
                case 6:
                    {
                        List<Offering> left = new List<Offering>(OfferingUtility.OfferingsFor(p2, map));
                        if (left.Count == 0)
                        {
                            Log.Message("[Test] (no gift was left for the second patient; skipped the shrine check)");
                            Finish();
                            break;
                        }
                        if (left.Any(o => o.state == OfferingState.Active) && InPhase < 600) break;
                        Offering s = left[0];
                        Kit.Check(left.All(o => o.state == OfferingState.Shrine) && s.GetInspectString().Contains("In memory of " + p2.LabelShort),
                            $"when the patient dies his gifts become a shrine: \"{s.GetInspectString().Replace("\n", " | ")}\"");
                        Finish();
                        break;
                    }
            }
        }

        void Setup()
        {
            root = TestUtil.PrepareStrip(map, TestUtil.Anchor(map, 0, 16), 9, 5, Name);
            Kit.Check(root.IsValid, "setup: a clear strip for the sickroom");
            if (!root.IsValid) { Finish(); return; }
            oldJoyDuration = JobDefOf.VisitSickPawn.joyDuration;
            JobDefOf.VisitSickPawn.joyDuration = VisitTicks;
            oldMinVisit = AftermathTuning.MinVisitTicks;
            AftermathTuning.MinVisitTicks = 150;   // an interrupted visit counts after 150 ticks at the bedside (600 in play)
            bed1 = Bed(root + new IntVec3(2, 0, 3));
            bed2 = Bed(root + new IntVec3(6, 0, 3));
            p1 = Patient(bed1);
            p2 = Patient(bed2);
            lover = Colonist(root + new IntVec3(1, 0, 0));
            hater = Colonist(root + new IntVec3(7, 0, 0));
            // someone with a neutral opinion of p2 (opinion needs a spawned pawn: try a few, extras just stay colonists)
            for (int i = 0; i < 6; i++)
            {
                neutral = Colonist(root + new IntVec3(8, 0, 0));
                int o = neutral.relations.OpinionOf(p2);
                if (o >= 0 && o < AftermathTuning.FriendOpinion) break;
            }
            quitter = Colonist(root + new IntVec3(3, 0, 0));
            stayer = Colonist(root + new IntVec3(5, 0, 0));
            p1.relations.AddDirectRelation(PawnRelationDefOf.Lover, lover);
            for (int i = 0; i < 3; i++) hater.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.HadAngeringFight, p2);
            Kit.Check(LovePartnerRelationUtility.LovePartnerRelationExists(lover, p1), $"setup: {lover.LabelShort} is {p1.LabelShort}'s lover");
            Kit.Check(hater.relations.OpinionOf(p2) < 0, $"setup: {hater.LabelShort} dislikes {p2.LabelShort} (opinion {hater.relations.OpinionOf(p2)})");
            Kit.Check(p1.CurrentBed() == bed1 && p2.CurrentBed() == bed2 && p1.Awake() && p2.Awake(), "setup: both patients lie awake in their beds");
            Start(lover, p1);
            Start(hater, p2);
        }

        void FirstVisits()
        {
            List<Offering> mine = OfferingUtility.OfferingsFor(p1, map).Where(o => o.visitorId == lover.thingIDNumber).ToList();
            Offering o1 = mine.FirstOrDefault();
            Kit.Check(mine.Count == 1 && o1.def == AftermathDefOf.Aftermath_Offering_Flowers && o1.visits == 1,
                $"the lover's visit leaves flowers ({mine.Count}, {o1?.def?.defName})");
            Kit.Check(o1 != null && o1.Position.AdjacentTo8WayOrInside(bed1) && !bed1.OccupiedRect().Contains(o1.Position),
                $"...on a cell next to the bed ({o1?.Position} by bed {bed1.Position})");
            Kit.Check(o1 != null && o1.GetInspectString().Contains(lover.LabelShort) && o1.LabelNoCount.Contains(p1.LabelShort),
                $"...saying who left it for whom: \"{o1?.LabelNoCount}: {o1?.GetInspectString().Replace("\n", " | ")}\"");
            Kit.Check(OfferingUtility.FindGift(p2, hater.thingIDNumber, map) == null,
                $"a visitor who dislikes the patient leaves nothing (negative; opinion {hater.relations.OpinionOf(p2)})");
        }

        void SecondVisits()
        {
            List<Offering> mine = OfferingUtility.OfferingsFor(p1, map).Where(o => o.visitorId == lover.thingIDNumber).ToList();
            Kit.Check(mine.Count == 1 && mine[0].visits == 2, $"the lover's second visit adds to the same gift ({mine.Count} gift(s), {mine.FirstOrDefault()?.visits} visits)");
            int opinion = neutral.relations.OpinionOf(p2);
            ThingDef want = opinion < AftermathTuning.MinOpinionForGift ? null
                : opinion >= AftermathTuning.FriendOpinion ? AftermathDefOf.Aftermath_Offering_Figure : AftermathDefOf.Aftermath_Offering_Candle;
            Offering got = OfferingUtility.FindGift(p2, neutral.thingIDNumber, map);
            Kit.Check(got?.def == want, $"a colonist with opinion {opinion} leaves {want?.label ?? "nothing"} ({got?.def?.label ?? "nothing"})");
        }

        void Recover()
        {
            givers1 = OfferingUtility.OfferingsFor(p1, map).Where(o => o.state == OfferingState.Active).Select(o => o.visitorId).Distinct().Count();
            TestUtil.Heal(p1);
            p1.jobs.EndCurrentJob(JobCondition.InterruptForced);
            TestUtil.Teleport(p1, root + new IntVec3(4, 0, 0));
            p1.drafter.Drafted = true;
        }

        // ------------------------------------------------------------------ visits

        void Start(Pawn visitor, Pawn patient) => visits.Add(new Visit { visitor = visitor, patient = patient });

        /// <summary>Orders the visit (retrying if a reservation blocks it) and notices when it ends.</summary>
        void Drive(Visit v)
        {
            if (v.ended) return;
            Job cur = v.visitor.CurJob;
            bool visiting = cur != null && cur.def == JobDefOf.VisitSickPawn && cur.targetA.Thing == v.patient;
            if (visiting) { v.started = true; return; }
            if (v.started) { v.ended = true; return; }
            if (v.tries >= MaxTries || ticks - v.lastTry < 30) return;
            v.tries++;
            v.lastTry = ticks;
            if (v.patient.needs?.rest != null) v.patient.needs.rest.CurLevel = 1f;
            if (!v.visitor.CanReserve(v.patient))
            {
                // someone else holds him - usually a colonist who chose to visit him for joy (the earlier test
                // visitors do, once their ordered visit ends). Draft that colonist so the reservation is
                // released, and retry; blockers are undrafted when the phase ends.
                Pawn holder = map.reservationManager.FirstRespectedReserver(v.patient, v.visitor);
                if (holder != null && holder != v.visitor && holder.drafter != null && holder.IsColonistPlayerControlled && !holder.Drafted)
                {
                    holder.drafter.Drafted = true;
                    blockers.Add(holder);
                }
                return;
            }
            Job job = JobMaker.MakeJob(JobDefOf.VisitSickPawn, v.patient);
            job.ignoreJoyTimeAssignment = true;   // JoyUtility.JoyTickCheckEnd would end it outside joy hours
            v.visitor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            cur = v.visitor.CurJob;
            if (cur != null && cur.def == JobDefOf.VisitSickPawn) v.started = true;
        }

        bool AllEnded() => visits.All(v => v.ended || (!v.started && v.tries >= MaxTries));

        const int MaxTries = 20;
        int restarts;
        readonly List<Pawn> blockers = new List<Pawn>();

        void ReleaseBlockers()
        {
            foreach (Pawn b in blockers) if (b.drafter != null && b.Spawned && !b.Dead) b.drafter.Drafted = false;
            blockers.Clear();
        }

        void CheckVisitEnded(string what)
        {
            bool ok = visits.All(v => v.ended);
            Kit.Check(ok, $"setup: {what} took place ({string.Join(", ", visits.Select(v => $"{v.visitor.LabelShort}->{v.patient.LabelShort} started {v.started} ended {v.ended} tries {v.tries}"))})");
            visits.Clear();
        }

        // ------------------------------------------------------------------ scene

        Building_Bed Bed(IntVec3 at)
        {
            var b = TestUtil.SpawnBuilding<Building_Bed>(ThingDefOf.Bed, ThingDefOf.WoodLog, at, map, Rot4.South);
            b.Medical = true;
            return b;
        }

        Pawn Patient(Building_Bed bed)
        {
            Pawn p = Colonist(bed.Position + new IntVec3(0, 0, 2));
            // a tended cut on a leg: needs medical rest, no doctor will come to reserve him
            BodyPartRecord leg = TestUtil.Part(p, BodyPartDefOf.Leg);
            Hediff cut = HediffMaker.MakeHediff(HediffDefOf.Cut, p, leg);
            cut.Severity = 6f;
            p.health.AddHediff(cut, leg);
            cut.Tended(1f, 1f);
            if (p.needs?.rest != null) p.needs.rest.CurLevel = 1f;
            if (p.needs?.food != null) p.needs.food.CurLevel = 1f;
            p.jobs.Notify_TuckedIntoBed(bed);
            return p;
        }

        Pawn Colonist(IntVec3 at, System.Predicate<Pawn> accept = null)
        {
            Pawn p = TestUtil.NewColonist(map, at, accept);
            colonists.Add(p);
            return p;
        }

        protected override string Facts() =>
            "visits: " + string.Join(", ", visits.Select(v => $"{v.visitor?.LabelShort}->{v.patient?.LabelShort} job {v.visitor?.CurJobDef?.defName} started {v.started} ended {v.ended} tries {v.tries}"))
            + $"; p1 in bed {p1?.CurrentBed() != null} awake {p1?.Awake()}, p2 in bed {p2?.CurrentBed() != null}"
            + $"; p1 held by {(p1 != null && p1.Spawned ? map.reservationManager.FirstRespectedReserver(p1, stayer)?.LabelShort ?? "nobody" : "-")}";

        protected override void Cleanup()
        {
            if (oldJoyDuration >= 0) JobDefOf.VisitSickPawn.joyDuration = oldJoyDuration;
            if (oldMinVisit >= 0) AftermathTuning.MinVisitTicks = oldMinVisit;
            if (p1?.drafter != null && p1.Spawned) p1.drafter.Drafted = false;
            ReleaseBlockers();
            foreach (Pawn p in colonists) TestUtil.Heal(p);
        }
    }
}
