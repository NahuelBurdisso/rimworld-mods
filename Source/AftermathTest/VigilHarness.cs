using System.Collections.Generic;
using Aftermath;
using RimWorld;
using Verse;

namespace AftermathTest
{
    /// <summary>Feature 4 — Vigil Lamp: lit for a patient in danger, cut on death, faded on recovery, negatives.</summary>
    public class VigilHarness : HarnessBase
    {
        public VigilHarness(Game game) { }

        protected override int StartTick => TestUtil.VigilStart;
        protected override string Name => "Vigil";

        const int Poll = 30;   // poll interval during the test (restored after)
        IntVec3 root;
        Building_Bed bedDying, bedMild, bedRecover;
        Pawn dying, mild, recovering;
        readonly List<Pawn> colonists = new List<Pawn>();
        int fadeStart;

        VigilTracker T => VigilTracker.For(map);

        protected override void Step()
        {
            switch (phase)
            {
                case 0:
                    Next();
                    Setup();
                    break;
                case 1:
                    if (InPhase < Poll + 2) break;
                    Next();
                    Lit();
                    break;
                case 2:
                    // one poll after the cure: dimmer
                    if (InPhase < Poll + 2) break;
                    Next();
                    int step = T.FadeStepAt(bedRecover);
                    Thing g = T.GlowerAt(bedRecover);
                    int red = g?.TryGetComp<CompGlower>()?.GlowColor.r ?? -1;
                    Kit.Check(step >= 1 && g != null && red < AftermathTuning.VigilColor.r,
                        $"when the danger passes the light starts to fade (step {step}, red {red} < {AftermathTuning.VigilColor.r})");
                    break;
                case 3:
                    if (T.GlowerAt(bedRecover) != null && InPhase < Poll * (AftermathTuning.VigilFadeSteps + 3)) break;
                    Kit.Check(T.GlowerAt(bedRecover) == null, $"...and is gone after the fade ({TestUtil.Now - fadeStart} ticks)");
                    Next();
                    FeatureOff();
                    break;
                case 4:
                    if (InPhase < Poll + 2) break;
                    Kit.Check(T.GlowerAt(bedRecover) == null && T.Count == 0,
                        $"with the feature off, a patient in danger gets no light (negative; {T.Count} lights)");
                    Finish();
                    break;
            }
        }

        void Setup()
        {
            AftermathTuning.VigilPollInterval = Poll;
            ThingDef def = AftermathDefOf.Aftermath_VigilGlow;
            Kit.Check(def != null && def.graphicData == null && !def.isSaveable && def.drawerType == DrawerType.None && def.HasComp(typeof(CompGlower)),
                "the vigil light def has no graphic, is never saved, draws nothing and carries a CompGlower (and loaded)");
            root = TestUtil.PrepareStrip(map, TestUtil.Anchor(map, 14, -12), 9, 5, Name);
            Kit.Check(root.IsValid, "setup: a clear strip for the beds");
            if (!root.IsValid) { Finish(); return; }
            bedDying = Bed(root + new IntVec3(1, 0, 3));
            bedMild = Bed(root + new IntVec3(4, 0, 3));
            bedRecover = Bed(root + new IntVec3(7, 0, 3));
            dying = Patient(bedDying, 0.65f);
            mild = Patient(bedMild, 0.2f);
            recovering = Patient(bedRecover, 0.65f);
            Kit.Check(dying.CurrentBed() == bedDying && mild.CurrentBed() == bedMild && recovering.CurrentBed() == bedRecover,
                $"setup: the three patients lie in their beds ({dying.CurrentBed() != null}, {mild.CurrentBed() != null}, {recovering.CurrentBed() != null})");
            Kit.Check(VigilTracker.InDanger(dying) && !VigilTracker.InDanger(mild), "65% blood loss is mortal danger, 20% is not");
        }

        void Lit()
        {
            Thing g = T.GlowerAt(bedDying);
            CompGlower glow = g?.TryGetComp<CompGlower>();
            Kit.Check(g != null && g.Spawned && g.Position == bedDying.Position && glow != null && glow.Glows,
                $"within one poll the dying patient's bed has a lit amber light ({g?.Position}, lit {glow?.Glows})");
            Kit.Check(T.GlowerAt(bedMild) == null, "a lightly hurt patient's bed stays dark (negative)");

            // death: the light goes out in the same tick
            Kit.KillBy(dying, null);
            Kit.Check(dying.Dead && g != null && g.Destroyed && T.GlowerAt(bedDying) == null, "when the patient dies the light is destroyed in the same tick");

            // recovery: cure the blood loss, the light fades over the following polls
            Kit.Check(T.GlowerAt(bedRecover) != null, "setup: the second patient in danger has a light too");
            // tired first (MakeUndowned re-checks his job at once): he falls asleep in that bed instead of
            // getting up, which would cut the light at once rather than fade it
            if (recovering.needs?.rest != null) recovering.needs.rest.CurLevel = 0.15f;
            Hediff loss = recovering.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (loss != null) recovering.health.RemoveHediff(loss);
            fadeStart = TestUtil.Now;
        }

        void FeatureOff()
        {
            AftermathMod.Settings.vigilLamps = false;
            Hediff h = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, recovering);
            h.Severity = 0.65f;
            recovering.health.AddHediff(h);
            if (recovering.CurrentBed() == null && !recovering.Dead) recovering.jobs.Notify_TuckedIntoBed(bedRecover);
        }

        Building_Bed Bed(IntVec3 at) => TestUtil.SpawnBuilding<Building_Bed>(ThingDefOf.Bed, ThingDefOf.WoodLog, at, map, Rot4.South);

        Pawn Patient(Building_Bed bed, float bloodLoss)
        {
            Pawn p = TestUtil.NewColonist(map, bed.Position + new IntVec3(0, 0, 2));
            colonists.Add(p);
            Hediff h = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, p);
            h.Severity = bloodLoss;
            p.health.AddHediff(h);
            if (p.needs?.rest != null) p.needs.rest.CurLevel = 1f;
            p.jobs.Notify_TuckedIntoBed(bed);   // what Toils_Bed.TuckIntoBed does after a rescue
            return p;
        }

        protected override string Facts() =>
            $"lights {T?.Count}, recovering in bed {recovering?.CurrentBed() != null}, danger {(recovering != null ? VigilTracker.InDanger(recovering) : false)}";

        protected override void Cleanup()
        {
            AftermathMod.Settings.vigilLamps = true;
            AftermathTuning.VigilPollInterval = 250;
            foreach (Pawn p in colonists) TestUtil.Heal(p);
        }
    }
}
