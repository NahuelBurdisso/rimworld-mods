using System;
using System.Collections.Generic;
using System.Linq;
using OverTheBody;
using RimWorld;
using Verse;
using Verse.AI;

namespace OverTheBodyTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N`. Colonist A + bonded husky H1, colonist B, colonist C + bonded husky H3,
    /// an unbonded husky H2. Downs pawns with anesthetic (deterministic, no crawling) and drives hostiles by hand.
    /// Rules: no danger -> no guard; on feet -> no guard; downed + enemy -> rush + guard beside, facing the threat;
    /// hostile at 5 cells ignored, at 2 bitten; carried by a friend -> follow to the bed, guard by the bed;
    /// gets up -> memory + tale; dies guarding -> memory + tale (+ vanilla); kidnapped -> bites the kidnapper.
    /// </summary>
    public class OverTheBodyHarness : GameComponent
    {
        bool active;
        int ticks;
        int step = -1;
        int stepStart;

        Map map;
        Pawn a, b, c;
        Pawn h1, h2, h3;
        Pawn r, r2, r3, k;
        IntVec3 center, center2, rHold, r2Hold, r3Hold;
        Building_Bed bed;

        bool settledGuard, h1GuardNoDanger, h1GuardOnFeet, h1BiteAt5, h2EverGuard, sawFollow, sawKidnapBite;
        int storedTalesBefore, diedTalesBefore;

        public OverTheBodyHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        GameComponent_OverTheBody Comp => GameComponent_OverTheBody.Instance;

        public override void GameComponentTick()
        {
            if (!active || step == 99) return;
            ticks++;
            try
            {
                if (ticks == 60) { Setup(); return; }
                if (ticks < 60 || ticks % 30 != 0) return;
                Poll();
            }
            catch (Exception e)
            {
                Kit.Check(false, "harness exception at step " + step + ": " + e);
                Finish();
            }
        }

        static bool OpenArea(IntVec3 c, Map m, float radius) =>
            GenRadial.RadialCellsAround(c, radius, true).All(x => x.InBounds(m) && x.Standable(m) && !x.Fogged(m) && x.GetEdifice(m) == null
                && x.GetFirstPawn(m) == null);

        Pawn MakeHusky(IntVec3 near)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed("Husky");
            Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfPlayer, fixedBiologicalAge: 4f, fixedChronologicalAge: 4f));
            GenSpawn.Spawn(p, CellFinder.StandableCellNear(near, map, 3f), map);
            return p;
        }

        static void Bond(Pawn animal, Pawn human)
        {
            animal.relations.AddDirectRelation(PawnRelationDefOf.Bond, human);
            animal.playerSettings.Master = human;
        }

        Pawn Hostile(IntVec3 cell)
        {
            Pawn p = Kit.SpawnHostile(map, cell);
            p.equipment?.DestroyAllEquipment();
            p.inventory?.DestroyAll();
            Hold(p, p.Position);
            return p;
        }

        static void Hold(Pawn p, IntVec3 cell)
        {
            if (p == null || p.Dead || p.Downed || !p.Spawned) return;
            if (p.Position != cell) Kit.Teleport(p, cell);
            if (p.CurJobDef != JobDefOf.Wait)
            {
                Job wait = JobMaker.MakeJob(JobDefOf.Wait);
                wait.expiryInterval = 2000;
                p.jobs.StartJob(wait, JobCondition.InterruptForced);
            }
        }

        static void Anesthetize(Pawn p) => p.health.AddHediff(HediffDefOf.Anesthetic);

        static void WakeUp(Pawn p)
        {
            Hediff h;
            while ((h = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Anesthetic)) != null) p.health.RemoveHediff(h);
        }

        static void Disarm(Pawn p) => p.equipment?.DestroyAllEquipment();

        void Setup()
        {
            map = Find.CurrentMap;
            List<Pawn> cols = map.mapPawns.FreeColonistsSpawned.ToList();
            if (cols.Count < 3) { Kit.Check(false, "need 3 colonists, found " + cols.Count); Finish(); return; }
            c = cols.FirstOrDefault(p => p.story?.traits?.HasTrait(TraitDefOf.Psychopath) != true) ?? cols[0];
            cols.Remove(c);
            a = cols[0];
            b = cols[1];

            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 60, x => OpenArea(x, map, 8f), out center))
            { Kit.Check(false, "found an open area for the test"); Finish(); return; }
            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 80, x => OpenArea(x, map, 6f) && x.DistanceTo(center) >= 40f, out center2))
            { Kit.Check(false, "found a second open area 40+ cells away"); Finish(); return; }

            foreach (Pawn p in new[] { a, b, c }) Disarm(p);
            Kit.Teleport(a, center);
            Kit.Teleport(b, CellFinder.StandableCellNear(center + new IntVec3(-14, 0, 0), map, 3f));
            b.drafter.Drafted = true;
            Kit.Teleport(c, center2);
            c.drafter.Drafted = true;

            h1 = MakeHusky(center + new IntVec3(-6, 0, 4));
            Bond(h1, a);
            h2 = MakeHusky(center + new IntVec3(-6, 0, -4));
            h3 = MakeHusky(center2 + new IntVec3(-5, 0, 0));
            Bond(h3, c);
            Kit.Check(OverTheBodyUtility.HasBondWith(h1, a) && !OverTheBodyUtility.HasBondWith(h2, a), "setup: H1 bonded to A, H2 not bonded");

            bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
            bed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(bed, center + new IntVec3(0, 0, -6), map, Rot4.South);
            Kit.Check(bed.Spawned, "setup: bed spawned near A");

            // 1. downed with no enemy anywhere: no guard
            Anesthetize(a);
            Kit.Check(a.Downed, $"{a.LabelShort} downed by anesthetic");
            Go(1);
        }

        void Go(int s) { step = s; stepStart = ticks; }
        int InStep => ticks - stepStart;
        bool IsJob(Pawn p, JobDef def) => p.CurJobDef == def;

        void Poll()
        {
            if (step <= 10 && h2 != null && OverTheBodyUtility.IsGuardJob(h2.CurJobDef)) h2EverGuard = true;
            switch (step)
            {
                case 1:
                    if (OverTheBodyUtility.IsGuardJob(h1.CurJobDef)) h1GuardNoDanger = true;
                    if (InStep < 300) return;
                    Kit.Check(!h1GuardNoDanger && Comp.RecordFor(h1) == null, "no enemies near: the bonded husky does not guard the downed colonist");
                    WakeUp(a);
                    Kit.Check(!a.Downed, $"{a.LabelShort} back on feet");
                    rHold = CellFinder.StandableCellNear(center + new IntVec3(12, 0, 0), map, 2f);
                    r = Hostile(rHold);
                    rHold = r.Position;
                    Go(2);
                    return;

                case 2:
                    Hold(r, rHold);
                    if (OverTheBodyUtility.IsGuardJob(h1.CurJobDef)) h1GuardOnFeet = true;
                    if (InStep < 150) return;
                    Kit.Check(!h1GuardOnFeet, "enemy near but the colonist is on their feet: no guard");
                    Kit.Teleport(h1, CellFinder.StandableCellNear(a.Position + new IntVec3(-8, 0, 0), map, 2f));   // start away from A
                    Anesthetize(a);
                    Go(3);
                    return;

                case 3:
                {
                    Hold(r, rHold);
                    bool guarding = IsJob(h1, OverTheBodyDefOf.OverTheBody_Guard) && OverTheBodyUtility.AdjacentTo(h1, a);
                    if (guarding && !settledGuard) { settledGuard = true; return; }   // let it stand one poll before judging
                    if (!guarding && InStep < 1200) return;
                    Kit.Check(guarding, $"downed + enemy 12 cells away: H1 stands guard beside A within {InStep} ticks (job {h1.CurJobDef?.defName}, dist {h1.Position.DistanceTo(a.Position):F1})");
                    GuardRecord rec = Comp.RecordFor(h1);
                    Kit.Check(rec != null && rec.master == a, "a guard record H1 -> A exists");
                    Kit.Check(h1.Position.x > a.Position.x, $"H1 stands on the threat's side (east): H1 {h1.Position} A {a.Position}");
                    Kit.Check(h1.Rotation == Rot4.East, $"H1 faces the threat (east): {h1.Rotation}");
                    string report = h1.jobs.curDriver?.GetReport() ?? "";
                    Kit.Check(report.Contains(a.LabelShort), $"inspect report names A: \"{report}\"");
                    rHold = CellFinder.StandableCellNear(a.Position + new IntVec3(5, 0, 0), map, 1f);
                    Hold(r, rHold);
                    Go(4);
                    return;
                }

                case 4:
                    Hold(r, rHold);
                    if (IsJob(h1, OverTheBodyDefOf.OverTheBody_Bite)) h1BiteAt5 = true;
                    if (InStep < 300) return;
                    Kit.Check(!h1BiteAt5 && IsJob(h1, OverTheBodyDefOf.OverTheBody_Guard),
                        $"hostile {r.Position.DistanceTo(a.Position):F1} cells from A: H1 does not leave its post (job {h1.CurJobDef?.defName})");
                    rHold = a.Position + new IntVec3(-2, 0, 0);
                    if (!rHold.Standable(map)) rHold = CellFinder.StandableCellNear(rHold, map, 1f);
                    Hold(r, rHold);
                    Go(5);
                    return;

                case 5:
                {
                    // ours, or vanilla's reaction if the hostile struck first - both are "the animal fights for the body"
                    bool biting = (IsJob(h1, OverTheBodyDefOf.OverTheBody_Bite) || IsJob(h1, JobDefOf.AttackMelee)) && h1.CurJob.targetA.Thing == r;
                    if (!biting && InStep < 400) return;
                    Kit.Check(biting, $"hostile {rHold.DistanceTo(a.Position):F1} cells from A: H1 attacks it (job {h1.CurJobDef?.defName} -> {h1.CurJob?.targetA.Thing?.LabelShort})");
                    if (!r.Dead) r.Kill(null);
                    r2Hold = CellFinder.StandableCellNear(center + new IntVec3(18, 0, 0), map, 2f);
                    r2 = Hostile(r2Hold);
                    r2Hold = r2.Position;
                    // B carries A to the bed (real ordered rescue)
                    b.drafter.Drafted = false;
                    Job rescue = JobMaker.MakeJob(JobDefOf.Rescue, a, bed);
                    rescue.count = 1;
                    bool took = b.jobs.TryTakeOrderedJob(rescue, JobTag.Misc);
                    Kit.Check(took, "B takes the ordered rescue of A");
                    Go(6);
                    return;
                }

                case 6:
                {
                    Hold(r2, r2Hold);
                    if (a.CarriedBy == b && IsJob(h1, OverTheBodyDefOf.OverTheBody_Follow) && h1.CurJob.targetA.Thing == b) sawFollow = true;
                    bool done = a.CurrentBed() == bed && IsJob(h1, OverTheBodyDefOf.OverTheBody_Guard) && OverTheBodyUtility.AdjacentTo(h1, a);
                    if (!done && InStep < 2400) return;
                    Kit.Check(sawFollow, "while B carried A, H1 stayed close to B (OverTheBody_Follow)");
                    Kit.Check(a.CurrentBed() == bed, "A reached the bed");
                    Kit.Check(done, $"enemy still near: H1 guards A beside the bed (job {h1.CurJobDef?.defName}, dist {h1.Position.DistanceTo(a.Position):F1})");
                    b.drafter.Drafted = true;
                    storedTalesBefore = TaleCount(OverTheBodyDefOf.OverTheBody_StoodOverTale);
                    GuardRecord rec = Comp.RecordFor(h1);
                    Kit.Check(rec != null && rec.ticksGuarded >= GameComponent_OverTheBody.MinGuardTicks, $"guard time counted: {rec?.ticksGuarded}");
                    if (!r2.Dead) r2.Kill(null);
                    WakeUp(a);
                    Go(7);
                    return;
                }

                case 7:
                {
                    bool settled = Comp.RecordFor(h1) == null;
                    if (!settled && InStep < 300) return;
                    Kit.Check(settled, "the record closed when A got up");
                    Kit.Check(HasMemory(a, OverTheBodyDefOf.OverTheBody_StoodOverMe, h1), $"A remembers H1 stood over them ({OverTheBodyDefOf.OverTheBody_StoodOverMe.stages[0].baseMoodEffect:+0})");
                    Kit.Check(TaleCount(OverTheBodyDefOf.OverTheBody_StoodOverTale) == storedTalesBefore + 1, "a 'stood over the fallen' tale was recorded");
                    Kit.Check(!HasMemory(b, OverTheBodyDefOf.OverTheBody_StoodOverMe, h1), "B (not the fallen) got no memory");

                    // negative: a friend carrying a fallen colonist the animal was NOT guarding, enemy near -> nothing
                    Anesthetize(a);
                    Pawn r4 = Hostile(CellFinder.StandableCellNear(b.Position + new IntVec3(15, 0, 0), map, 3f));
                    bool carried = b.carryTracker.TryStartCarry(a, 1, reserve: false) > 0;
                    Kit.Check(carried && a.CarriedBy == b && OverTheBodyUtility.FallenMasterFor(h1) == a && OverTheBodyUtility.InDanger(a),
                        "setup: B carries downed A with an enemy near, H1 has no record");
                    Kit.Check(JobGiver_StandOverMaster.TryMakeJob(h1, notify: false) == null && Comp.RecordFor(h1) == null,
                        "carried by a friend without a prior watch: no job for H1 (so the component will not wake or interrupt it)");
                    b.carryTracker.TryDropCarriedThing(b.Position, ThingPlaceMode.Near, out Thing _);
                    r4.Kill(null);
                    WakeUp(a);
                    Kit.Check(!a.Downed && a.Spawned, "A dropped and back on feet");

                    // scenario 2: H3 dies guarding C
                    r3Hold = CellFinder.StandableCellNear(center2 + new IntVec3(10, 0, 0), map, 2f);
                    r3 = Hostile(r3Hold);
                    r3Hold = r3.Position;
                    diedTalesBefore = TaleCount(OverTheBodyDefOf.OverTheBody_DiedOverTale);
                    Anesthetize(c);
                    Go(8);
                    return;
                }

                case 8:
                {
                    Hold(r3, r3Hold);
                    bool guarding = IsJob(h3, OverTheBodyDefOf.OverTheBody_Guard) && OverTheBodyUtility.AdjacentTo(h3, c);
                    if (!guarding && InStep < 1200) return;
                    Kit.Check(guarding, $"H3 stands guard beside C (job {h3.CurJobDef?.defName})");
                    Kit.KillBy(h3, r3);
                    Kit.Check(h3.Dead, "H3 killed while guarding");
                    Go(9);
                    return;
                }

                case 9:
                {
                    Hold(r3, r3Hold);
                    bool settled = Comp.RecordFor(h3) == null;
                    if (!settled && InStep < 300) return;
                    Kit.Check(settled, "H3's record closed on its death");
                    Kit.Check(HasMemory(c, OverTheBodyDefOf.OverTheBody_DiedOverMe, h3), "C remembers H3 died standing over them");
                    Kit.Check(c.needs.mood.thoughts.memories.Memories.Any(m => m.def.defName == "BondedAnimalDied"), "vanilla 'bonded animal died' also applies");
                    Kit.Check(TaleCount(OverTheBodyDefOf.OverTheBody_DiedOverTale) == diedTalesBefore + 1, "a 'died over the fallen' tale was recorded");
                    Kit.Check(!HasMemory(c, OverTheBodyDefOf.OverTheBody_StoodOverMe, h3), "no 'stood over me' for an animal that died");
                    if (!r3.Dead) r3.Kill(null);
                    WakeUp(c);

                    // scenario 3: kidnapping. H2 is bonded to B only once B is in the kidnapper's arms.
                    Kit.Teleport(b, center2 + new IntVec3(0, 0, 3));
                    b.drafter.Drafted = false;
                    Kit.Teleport(h2, CellFinder.StandableCellNear(center2 + new IntVec3(-6, 0, -3), map, 2f));
                    Anesthetize(b);
                    k = Kit.SpawnHostile(map, b.Position + new IntVec3(2, 0, 0));
                    k.equipment?.DestroyAllEquipment();
                    if (!RCellFinder.TryFindBestExitSpot(k, out IntVec3 exit))
                    { Kit.Check(false, "kidnapper found an exit spot"); Finish(); return; }
                    Job kidnap = JobMaker.MakeJob(JobDefOf.Kidnap);
                    kidnap.targetA = b;
                    kidnap.targetB = exit;
                    kidnap.count = 1;
                    k.jobs.StartJob(kidnap, JobCondition.InterruptForced);
                    Go(10);
                    return;
                }

                case 10:
                    if (b.CarriedBy == k)
                    {
                        Bond(h2, b);
                        Kit.Check(!h2EverGuard, "the unbonded husky never took a guard job");
                        Go(11);
                        return;
                    }
                    if (InStep < 900) return;
                    Kit.Check(false, $"kidnapper picked up B (k job {k.CurJobDef?.defName})");
                    Finish();
                    return;

                case 11:
                    if (b.CarriedBy == k && IsJob(h2, OverTheBodyDefOf.OverTheBody_Bite) && h2.CurJob.targetA.Thing == k) sawKidnapBite = true;
                    if (!sawKidnapBite && InStep < 600 && b.CarriedBy == k) return;
                    Kit.Check(sawKidnapBite, $"B carried off by a raider: H2 goes for the kidnapper (job {h2.CurJobDef?.defName}, carried by {b.CarriedBy?.LabelShort})");
                    if (!k.Dead) k.Kill(null);
                    WakeUp(b);
                    Finish();
                    return;
            }
        }

        static int TaleCount(TaleDef def) => Find.TaleManager.AllTalesListForReading.Count(t => t.def == def);

        static bool HasMemory(Pawn p, ThoughtDef def, Pawn other) =>
            p.needs?.mood?.thoughts?.memories?.Memories.Any(m => m.def == def && m.otherPawn == other) == true;

        void Finish()
        {
            step = 99;
            Kit.Summary("Over the Body");
        }
    }
}
