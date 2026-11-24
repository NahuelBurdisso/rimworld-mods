using System.Collections.Generic;
using System.Linq;
using Duelo;
using RimWorld;
using Verse;
using Verse.AI;

namespace DueloTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N` (N >= 9000 for the funeral part). Embers of the Eulogy:
    ///  A. composer: a colonist with real records/tales/relations killed by a raider -> stored eulogy with
    ///     the facts, opening from the pod tale, last line = how they died; a colonist with nothing -> one sad line.
    ///  B. burials (real haul-to-grave jobs): spouse buries with no funeral owed -> reading + letter + grave text;
    ///     a funeral is owed -> burial defers (negative); nobody cared -> silent (negative);
    ///     a stranger -> silent, no record (negative).
    ///  C. funeral (Ideology): started through RitualBehaviorWorker.TryExecuteOn like the begin-ritual dialog;
    ///     lines rise during the ceremony and the letter arrives when it completes.
    /// </summary>
    public class EulogyHarness : GameComponent
    {
        const int StartTick = 300;

        bool active, done;
        int ticks, step, stepTick;
        Map map;

        /// <summary>True once this harness is over (or never armed): MossHarness waits for it, because its
        /// deaths would otherwise call off the funeral and its strangers would blur the mourner checks.</summary>
        public static bool Finished = true;

        Pawn elder, elderSpouse;          // A + C: rich life, buried while a funeral is owed, then the funeral
        Pawn widow, widowSpouse;          // B: buried by a mourner, no funeral owed
        Pawn loner, lonerHauler;          // B negative: nobody cared
        Pawn stranger, strangerHauler;    // B negative: not ours
        Building_Grave graveElder, graveWidow, graveLoner, graveStranger;

        int lettersBefore, readingsBefore, deferredBefore, linesBefore;
        Precept_Ritual funeral;

        public EulogyHarness(Game game) { }

        public override void FinalizeInit()
        {
            active = GenCommandLine.TryGetCommandLineArg("rwtools-ticks", out _);
            Finished = !active;
        }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (ticks < StartTick || ticks % 30 != 0) return;
            try
            {
                Step();
            }
            catch (System.Exception e)
            {
                Check(false, "eulogy harness threw at step " + step + ": " + e);
                done = true;
                Finished = true;
            }
        }

        void Next() { step++; stepTick = ticks; }
        int InStep => ticks - stepTick;

        void Step()
        {
            switch (step)
            {
                case 0: Setup(); Next(); break;
                case 1: Composer(); Next(); break;
                case 2: OrderBurials(); Next(); break;
                case 3: WaitBurials(); break;
                case 4: StartFuneral(); break;
                case 5: WaitFuneral(); break;
                default: done = true; Finished = true; break;
            }
        }

        // ------------------------------------------------------------ setup

        Pawn NewColonist(IntVec3 near)
        {
            Ideo ideo = Faction.OfPlayer.ideos?.PrimaryIdeo;
            var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true,
                canGeneratePawnRelations: false, fixedIdeo: ideo, forbidAnyTitle: true);
            Pawn p = PawnGenerator.GeneratePawn(req);
            GenSpawn.Spawn(p, LandCellNear(near, 6), map);
            return p;
        }

        static void MakePlain(Pawn p)
        {
            foreach (Trait tr in p.story.traits.allTraits.ToList()) p.story.traits.RemoveTrait(tr);
            if (ModsConfig.BiotechActive && p.genes != null) p.genes.SetXenotype(XenotypeDefOf.Baseliner);
        }

        /// <summary>A standable, reachable cell near <paramref name="near"/>; quicktest maps can put water anywhere.</summary>
        IntVec3 LandCellNear(IntVec3 near, int radius)
        {
            IntVec3 anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center;
            if (CellFinder.TryFindRandomCellNear(near, map, radius, c => c.Standable(map) && !c.Fogged(map) && map.reachability.CanReach(anchor, c, Verse.AI.PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)), out IntVec3 cell))
                return cell;
            return CellFinder.StandableCellNear(anchor, map, 8f);
        }

        IntVec3 GraveCellNear(IntVec3 near, System.Predicate<IntVec3> ok)
        {
            for (int r = 4; r <= 24; r += 4)
                if (CellFinder.TryFindRandomCellNear(near, map, r, c => ok(c) && c.Standable(map) && !c.Fogged(map), out IntVec3 cell)) return cell;
            return LandCellNear(near, 10);
        }

        Building_Grave NewGrave(IntVec3 near)
        {
            IntVec3 cell = GraveCellNear(near, c =>
                GenConstruct.CanPlaceBlueprintAt(ThingDefOf.Grave, c, Rot4.South, map).Accepted
                && GenAdj.OccupiedRect(c, Rot4.South, ThingDefOf.Grave.size).Cells.All(x => x.Standable(map) && x.GetFirstPawn(map) == null));
            var g = (Building_Grave)ThingMaker.MakeThing(ThingDefOf.Grave);
            g.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(g, cell, map, Rot4.South);
            return g;
        }

        void Setup()
        {
            map = Find.CurrentMap;
            MapComponent_Duelo.MinRealSecondsBetweenLines = 0f;   // the test runs far faster than real time
            DueloMod.Settings.eulogy = true;
            DueloMod.Settings.eulogyLetter = true;
            DueloMod.Settings.eulogyAtBurialEvenWithFuneral = false;

            Check(GraveCompInjector.Injected.Contains(ThingDefOf.Grave) && ThingDefOf.Grave.HasComp(typeof(CompDueloGrave)),
                $"grave comp injected into {GraveCompInjector.Injected.Count} grave defs ({string.Join(", ", GraveCompInjector.Injected.Select(d => d.defName))})");

            IntVec3 home = map.mapPawns.FreeColonistsSpawned.First().Position;
            IntVec3 a = LandCellNear(home + new IntVec3(-14, 0, 8), 10);
            IntVec3 b = LandCellNear(home + new IntVec3(14, 0, 8), 10);
            IntVec3 c = LandCellNear(home + new IntVec3(-14, 0, -12), 10);
            IntVec3 d = LandCellNear(home + new IntVec3(14, 0, -12), 10);

            elder = NewColonist(a); elderSpouse = NewColonist(a);
            widow = NewColonist(b); widowSpouse = NewColonist(b);
            loner = NewColonist(c); lonerHauler = NewColonist(c);
            MakePlain(loner);   // no Pretty/Beautiful opinion: "nobody cared" must not depend on looks
            strangerHauler = NewColonist(d);
            stranger = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, null, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(stranger, LandCellNear(d, 6), map);

            graveElder = NewGrave(a + new IntVec3(3, 0, 0));
            graveWidow = NewGrave(b + new IntVec3(3, 0, 0));
            graveLoner = NewGrave(c + new IntVec3(3, 0, 0));
            graveStranger = NewGrave(d + new IntVec3(3, 0, 0));

            // the elder's life: real records, tales and relations
            elder.relations.AddDirectRelation(PawnRelationDefOf.Spouse, elderSpouse);
            TaleRecorder.RecordTale(TaleDefOf.Marriage, elder, elderSpouse);
            TaleRecorder.RecordTale(TaleDefOf.LandedInPod, elder);
            elder.records.AddTo(RecordDefOf.KillsHumanlikes, 12);
            elder.records.AddTo(RecordDefOf.OperationsPerformed, 7);
            elder.records.AddTo(RecordDefOf.MealsCooked, 55);
            // Time records cannot be AddTo'd; the tracker's own map is set directly
            var recs = (DefMap<RecordDef, float>)HarmonyLib.AccessTools.Field(typeof(Pawn_RecordsTracker), "records").GetValue(elder.records);
            recs[RecordDefOf.TimeAsColonistOrColonyAnimal] = GenDate.TicksPerYear + 2 * GenDate.TicksPerQuadrum;
            elder.skills.skills.First(sk => !sk.TotallyDisabled).Level = 15;   // a best skill >= 8, whatever they can do

            widow.relations.AddDirectRelation(PawnRelationDefOf.Spouse, widowSpouse);
            widow.records.AddTo(RecordDefOf.ThingsConstructed, 40);
        }

        // ------------------------------------------------------------ A. composer

        void Composer()
        {
            List<string> cands = EulogyComposer.CandidateSymbols(elder, out string opening);
            Log.Message("[Test] elder candidates: " + string.Join(", ", cands) + " | opening " + opening);
            foreach (string s in new[] { "eulogy_kills", "eulogy_surgeon", "eulogy_cook", "eulogy_skill", "eulogy_tale_Marriage" })
                Check(cands.Contains(s), $"composer finds fact {s} in the elder's real life");
            // the marriage tale already names the spouse
            Check(!cands.Contains("eulogy_partner"), "partner line skipped: the marriage tale already names the spouse");
            Check(opening == "eulogy_open_pod", "opening uses the LandedInPod tale (got " + opening + ")");

            Pawn raider = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Pirate,
                Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate), forceGenerateNewPawn: true));
            int storedBefore = GameComponent_DueloMemory.Instance.Count;
            elder.Kill(new DamageInfo(DamageDefOf.Bullet, 40f, instigator: raider), null);
            Check(elder.Dead, "elder died");
            EulogyRecord rec = GameComponent_DueloMemory.Instance.Find(elder.ThingID);
            Check(rec != null && GameComponent_DueloMemory.Instance.Count == storedBefore + 1, "eulogy stored at death");
            if (rec != null)
            {
                Log.Message("[Test] elder eulogy:\n  " + string.Join("\n  ", rec.lines));
                Check(rec.lines.Count >= 4 && rec.lines.Count <= 6, $"eulogy has 4-6 lines (got {rec.lines.Count})");
                List<string> syms = EulogyComposer.LastSymbols;
                Check(syms.Count > 0 && syms[0] == "eulogy_open_pod", "first line is the opening (" + (syms.Count > 0 ? syms[0] : "none") + ")");
                string wantDeath = raider.Faction != null ? "eulogy_death_person" : "eulogy_death_person_nofaction";
                Check(syms.Count > 0 && syms[syms.Count - 1] == wantDeath, "last line says who killed them (" + (syms.Count > 0 ? syms[syms.Count - 1] : "none") + ")");
                Check(rec.lines[rec.lines.Count - 1].Contains(raider.LabelShort), $"death line contains the killer's name {raider.LabelShort}");
                Check(rec.lines.All(l => !l.Contains("[") && !l.Contains("ERR")), "no unresolved grammar symbols");
                Check(rec.lines.Any(l => l.Contains("12") || l.Contains("7") || l.Contains("55") || l.Contains("medicine") || l.Contains(elderSpouse.LabelShort)),
                    "lines carry real numbers/names from the elder's life");
            }

            // illness: the culprit hediff names the cause
            Hediff plague = HediffMaker.MakeHediff(HediffDefOf.Plague, elder);
            List<string> ill = EulogyComposer.Compose(elder, null, plague, causeKnown: true);
            Check(EulogyComposer.LastSymbols.Last() == "eulogy_death_illness" && ill.Last().Contains(HediffDefOf.Plague.label),
                "illness death line names the culprit: " + ill.Last());

            // nothing to tell: one sad line (negative of the facts rule)
            Pawn nobody = NewColonist(LandCellNear(map.mapPawns.FreeColonistsSpawned.First().Position + new IntVec3(0, 0, -20), 10));
            nobody.Kill(null, null);
            EulogyRecord sad = GameComponent_DueloMemory.Instance.Find(nobody.ThingID);
            Check(sad != null && sad.lines.Count == 1 && EulogyComposer.LastSymbols.SequenceEqual(new[] { "eulogy_sad" }),
                "a life with no deeds or bonds gets one sad line: " + (sad?.lines.FirstOrDefault() ?? "none"));

            widow.Kill(new DamageInfo(DamageDefOf.Cut, 30f, instigator: raider), null);
            loner.Kill(null, null);
            stranger.Kill(null, null);
            Check(GameComponent_DueloMemory.Instance.Find(stranger.ThingID) == null, "no eulogy written for a stranger");
        }

        // ------------------------------------------------------------ B. burials

        static Corpse CorpseOf(Pawn p) => p.Corpse;

        bool RemoveFuneralObligation(Pawn p)
        {
            bool removed = false;
            if (Faction.OfPlayer.ideos == null) return false;
            foreach (Ideo ideo in Faction.OfPlayer.ideos.AllIdeos)
                foreach (Precept pr in ideo.PreceptsListForReading)
                    if (pr is Precept_Ritual r && EulogyUtility.IsFuneral(r) && r.activeObligations != null)
                        removed |= r.activeObligations.RemoveAll(o => o.targetA.Thing == p || (o.targetA.Thing is Corpse c && c.InnerPawn == p)) > 0;
            return removed;
        }

        void Bury(Pawn hauler, Pawn deceased, Building_Grave grave)
        {
            Corpse corpse = CorpseOf(deceased);
            corpse.SetForbidden(false, false);
            hauler.jobs.StopAll();
            Job job = HaulAIUtility.HaulToContainerJob(hauler, corpse, grave);
            bool ok = hauler.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            Check(ok, $"{hauler.LabelShort} ordered to bury {deceased.LabelShort}");
        }

        void OrderBurials()
        {
            lettersBefore = MapComponent_Duelo.LettersSent;
            readingsBefore = MapComponent_Duelo.ReadingsStarted;
            deferredBefore = MapComponent_Duelo.BurialsDeferredToFuneral;
            linesBefore = MapComponent_Duelo.LinesEmitted;

            bool ideology = ModsConfig.IdeologyActive;
            bool elderOwed = EulogyUtility.FuneralPending(elder);
            Log.Message($"[Test] ideology {ideology}, funeral owed to elder: {elderOwed}");
            if (ideology) Check(elderOwed, "Ideology: the colony owes the elder a funeral (obligation created at death)");

            // widow and loner stand for a game without a funeral: drop their obligations
            RemoveFuneralObligation(widow);
            RemoveFuneralObligation(loner);
            Check(!EulogyUtility.FuneralPending(widow) && !EulogyUtility.FuneralPending(loner), "no funeral owed to widow/loner after removing the obligation");
            Check(EulogyUtility.Cares(widowSpouse, widow), "widow's spouse counts as a mourner");
            Check(!EulogyUtility.Cares(lonerHauler, loner), $"loner's hauler does not (opinion {EulogyComposer.SafeOpinion(lonerHauler, loner)})");

            Bury(elderSpouse, elder, graveElder);
            Bury(widowSpouse, widow, graveWidow);
            Bury(lonerHauler, loner, graveLoner);
            Bury(strangerHauler, stranger, graveStranger);
        }

        void WaitBurials()
        {
            bool all = graveElder.HasCorpse && graveWidow.HasCorpse && graveLoner.HasCorpse && graveStranger.HasCorpse;
            bool widowDone = graveWidow.HasCorpse && !MapComponent_Duelo.For(map).IsReadingAt(graveWidow);
            if (!(all && widowDone) && InStep < 3000) return;

            Check(all, $"all four corpses buried (elder {graveElder.HasCorpse}, widow {graveWidow.HasCorpse}, loner {graveLoner.HasCorpse}, stranger {graveStranger.HasCorpse})");

            CompDueloGrave cw = graveWidow.GetComp<CompDueloGrave>();
            Check(cw != null && cw.HasEulogyForCurrent && !cw.spokenAtFuneral, "burial with a mourner: eulogy spoken and kept on the grave");
            Check(MapComponent_Duelo.LinesEmitted - linesBefore == (cw?.eulogyLines?.Count ?? -1),
                $"every line rose from the grave ({MapComponent_Duelo.LinesEmitted - linesBefore} lines)");
            Check(MapComponent_Duelo.LastLine != null && MapComponent_Duelo.LastLine.def == DueloDefOf.Duelo_Mote_EulogyLine, "lines are eulogy text motes");
            string inspect = cw?.CompInspectStringExtra();
            Check(!inspect.NullOrEmpty() && inspect.Contains(cw.eulogyLines[0]), "grave inspect shows the eulogy: " + inspect);
            Check(cw != null && cw.CompGetGizmosExtra().Any(g => g is Command_Action), "grave offers a Read eulogy button");
            Check(MapComponent_Duelo.LettersSent == lettersBefore + 1 &&
                  Find.LetterStack.LettersListForReading.Any(l => l.Label.ToString() == "Duelo_EulogyLetterLabel".Translate(widow.LabelShortCap).ToString()),
                "eulogy letter arrived for the widow");

            CompDueloGrave ce = graveElder.GetComp<CompDueloGrave>();
            if (ModsConfig.IdeologyActive)
                Check(MapComponent_Duelo.BurialsDeferredToFuneral == deferredBefore + 1 && ce != null && !ce.HasEulogyForCurrent,
                    "funeral owed: the burial stays silent and waits for the funeral");
            Check(!graveLoner.GetComp<CompDueloGrave>().HasEulogyForCurrent, "nobody who cared at the burial: no eulogy");
            Check(!graveStranger.GetComp<CompDueloGrave>().HasEulogyForCurrent, "stranger's burial: no eulogy");
            Check(MapComponent_Duelo.ReadingsStarted == readingsBefore + 1, $"exactly one reading at the four burials ({MapComponent_Duelo.ReadingsStarted - readingsBefore})");
            Next();
        }

        // ------------------------------------------------------------ C. funeral

        void StartFuneral()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message("[Test] SKIP funeral: Ideology not active");
                Next(); Next();
                return;
            }
            funeral = null;
            foreach (Ideo ideo in Faction.OfPlayer.ideos.AllIdeos)
                foreach (Precept pr in ideo.PreceptsListForReading)
                    if (pr is Precept_Ritual r && EulogyUtility.IsFuneral(r)) funeral = r;
            if (funeral == null)
            {
                Log.Message("[Test] SKIP funeral: the colony ideo has no funeral ritual");
                Next(); Next();
                return;
            }
            RitualObligation obligation = funeral.activeObligations?.FirstOrDefault(o => o.targetA.Thing == elder || (o.targetA.Thing is Corpse c && c.InnerPawn == elder));
            var target = new TargetInfo(graveElder);
            string reason = funeral.behavior.CanStartRitualNow(target, funeral);
            Check(reason.NullOrEmpty(), "funeral can start: " + (reason ?? "ok"));
            RitualRoleAssignments assignments = Dialog_BeginRitual.CreateRitualRoleAssignments(funeral, target, map, null, null, null, null);
            assignments.FillPawns(null, target);   // what the begin-ritual dialog does when it opens
            lettersBefore = MapComponent_Duelo.LettersSent;
            readingsBefore = MapComponent_Duelo.FuneralReadingsStarted;
            linesBefore = MapComponent_Duelo.LinesEmitted;
            funeral.behavior.TryExecuteOn(target, null, funeral, obligation, assignments, playerForced: true);
            bool started = map.lordManager.lords.Any(l => l.LordJob is LordJob_Ritual lj && lj.Ritual == funeral);
            Check(started, $"funeral started at the elder's grave with {assignments.Participants.Count} participants");
            Next();
        }

        bool sawLineDuringFuneral;

        void WaitFuneral()
        {
            bool running = map.lordManager.lords.Any(l => l.LordJob is LordJob_Ritual lj && lj.Ritual == funeral);
            if (running && MapComponent_Duelo.LinesEmitted > linesBefore) sawLineDuringFuneral = true;
            bool letter = MapComponent_Duelo.LettersSent > lettersBefore;
            if (!letter && InStep < 9000) return;

            CompDueloGrave ce = graveElder.GetComp<CompDueloGrave>();
            Check(MapComponent_Duelo.FuneralReadingsStarted == readingsBefore + 1, "the funeral started a eulogy reading");
            Check(sawLineDuringFuneral, "lines rose while the ceremony was still running");
            Check(ce != null && ce.HasEulogyForCurrent && ce.spokenAtFuneral, "the grave keeps the funeral eulogy");
            Check(ce?.eulogyLines != null && GameComponent_DueloMemory.Instance.Find(elder.ThingID)?.lines.SequenceEqual(ce.eulogyLines) == true,
                "the funeral read the eulogy written at death");
            Check(letter && Find.LetterStack.LettersListForReading.Any(l => l.Label.ToString() == "Duelo_EulogyLetterLabel".Translate(elder.LabelShortCap).ToString()),
                $"funeral eulogy letter arrived after the ceremony (after {InStep} ticks)");
            Next();
        }

        static void Check(bool ok, string what)
        {
            if (ok) Log.Message("[Test] PASS " + what);
            else Log.Error("[Test] FAIL " + what);
        }
    }
}
