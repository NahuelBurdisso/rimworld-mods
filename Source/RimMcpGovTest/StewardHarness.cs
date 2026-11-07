using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimMcp;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimMcpGovTest
{
    /// <summary>
    /// The steward's peacetime reflexes, one case each, after the governor fights (GovHarness).
    /// Setup uses dev-style spawning; the steward itself only gives player orders. Every case waits
    /// for the steward event (events.jsonl) and checks the game state it should have changed.
    /// Run: rw launch --quicktest --ticks 36000 --wait --kill (needs RimMcp + this mod).
    /// </summary>
    public class StewardHarness : GameComponent
    {
        const string Mod = "RimMcp/Steward";
        bool armed, started, done;
        int ticks, startTick, caseIdx, caseStart;
        long cursor;
        Map map;
        Pawn patient, forced, armedPawn, newbie;
        Building_Bed bed1, bed2, forcedBed;
        Thing cooler;
        Area_Allowed area;
        ResearchProjectDef queued;
        float windowAt = -1f;
        bool windowCase, runCase;
        string originalPolicy;

        public StewardHarness(Game game) { }

        public override void FinalizeInit() { armed = Kit.HarnessArmed; }

        static readonly string[] Cases = { "rescue+tend+feed", "forced rest", "disease alarm", "research", "letters", "area", "thermostat", "weapon", "window", "stop run" };

        float runEmitAt = -1f;

        public override void GameComponentUpdate()
        {
            if (!armed || !started || done || Current.ProgramState != ProgramState.Playing) return;
            if (runCase && runEmitAt > 0 && (Find.TickManager.Paused || UnityEngine.Time.realtimeSinceStartup - runEmitAt > 10f))
            {
                Kit.Check(Find.TickManager.Paused && (Tools.pauseReason ?? "").Contains("decision_needed"),
                    $"a decision_needed event stops a running `run` at once (pauseReason: {Tools.pauseReason})");
                runCase = false;
                runEmitAt = -1f;
                Next();
                return;
            }
            // the routine-window case is checked per frame: the box pauses the game, ticks stop
            if (windowCase && windowAt > 0 && UnityEngine.Time.realtimeSinceStartup - windowAt > 2f)
            {
                bool open = Find.WindowStack.Windows.Any(w => w is Dialog_MessageBox mb && mb.text.ToString().Contains("cannot build"));
                Kit.Check(!open, "steward closes the 'unlocks things you cannot build (memes)' box while the game is paused");
                windowCase = false;
                Next();
            }
            var tm = Find.TickManager;
            if (!runCase && tm.CurTimeSpeed == TimeSpeed.Paused && !Find.WindowStack.WindowsForcePause) tm.CurTimeSpeed = TimeSpeed.Superfast;
        }

        public override void GameComponentTick()
        {
            if (!armed || done) return;
            ticks++;
            try
            {
                if (!started)
                {
                    if (!GovHarness.Finished && ticks < 30000) return;
                    started = true;
                    Setup();
                    return;
                }
                Step();
                if (ticks - startTick > 9000)
                {
                    Kit.Check(false, $"steward harness timed out in case '{Cases[Math.Min(caseIdx, Cases.Length - 1)]}'");
                    Finish();
                }
            }
            catch (Exception ex)
            {
                Kit.Check(false, "steward harness threw: " + ex);
                Finish();
            }
        }

        void Setup()
        {
            map = Find.CurrentMap;
            startTick = ticks;
            // the last governor fight can leave raiders standing: peacetime reflexes are off during a threat
            int removed = 0;
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToList())
                if (p.HostileTo(Faction.OfPlayer) && !p.Dead) { p.Destroy(); removed++; }
            foreach (var p in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                p.drafter.Drafted = false;
                foreach (var h in p.health.hediffSet.hediffs.Where(h => h is Hediff_Injury || h.def == HediffDefOf.Anesthetic).ToList())
                    p.health.RemoveHediff(h);
            }
            int spawned = 0;
            while (map.mapPawns.FreeColonistsSpawned.Count(p => !p.Downed) < 3 && spawned < 5)
            {
                var np = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                var anyone = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                GenSpawn.Spawn(np, CellFinder.StandableCellNear(anyone?.Position ?? map.Center, map, 6f), map);
                spawned++;
            }
            Kit.Info($"steward setup: removed {removed} hostiles left from the fights, healed colonists, spawned {spawned}");
            originalPolicy = File.Exists(GovernorPolicy.FilePath) ? File.ReadAllText(GovernorPolicy.FilePath) : null;
            File.WriteAllText(GovernorPolicy.FilePath, "{\"enabled\": true, \"steward\": {\"enabled\": true}}\n");
            Tools.Run("governor", new Dictionary<string, object> { ["action"] = "reload" });
            Kit.Check(GovernorPolicy.Current.enabled && GovernorPolicy.Current.steward.enabled, "governor + steward enabled from governor.json");
            cursor = GovernorEvents.Seq;
            var cols = map.mapPawns.FreeColonistsSpawned.ToList();
            Kit.Info("steward harness: colonists " + string.Join(", ", cols.Select(p => p.LabelShort)));
            foreach (var p in cols)
            {
                p.drafter.Drafted = false;
                if (p.workSettings != null) { p.workSettings.EnableAndInitialize(); p.workSettings.SetPriority(WorkTypeDefOf.Doctor, 1); }
                p.needs.food.CurLevelPercentage = 1f;
            }
            caseIdx = -1;
            Next();
        }

        void Next()
        {
            caseIdx++;
            caseStart = ticks;
            cursor = GovernorEvents.Seq;      // each case sees only its own events
            if (caseIdx >= Cases.Length) { Finish(); return; }
            Kit.Info("steward case: " + Cases[caseIdx]);
            var cols = map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed).ToList();
            if (cols.Count < 2) { Kit.Check(false, $"steward case '{Cases[caseIdx]}' needs 2 standing colonists, has {cols.Count}"); Finish(); return; }
            var at = cols[0].Position;
            switch (caseIdx)
            {
                case 0:
                    patient = cols.Last();
                    bed1 = SpawnBed(Free(at, 4));
                    bed2 = SpawnBed(Free(at, 6));
                    for (int i = 0; i < 4; i++) GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.MealSimple), Free(at, 3), map);
                    patient.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6, 0, -1, null, patient.health.hediffSet.GetNotMissingParts().First(p => p.def == BodyPartDefOf.Torso)));
                    patient.health.AddHediff(HediffMaker.MakeHediff(HediffDefOf.Anesthetic, patient));
                    patient.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Anesthetic).Severity = 1f;
                    patient.needs.food.CurLevelPercentage = 0.2f;
                    break;
                case 1:
                    forced = cols.First(p => p != patient);
                    forcedBed = SpawnBed(Free(at, 8));
                    var job = JobMaker.MakeJob(JobDefOf.LayDown, forcedBed);
                    job.playerForced = true;
                    forced.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    forced.needs.food.CurLevelPercentage = 0.35f;
                    break;
                case 2:
                    var flu = DefDatabase<HediffDef>.GetNamed("Flu");
                    var sick = cols.First(p => p != patient);
                    var h = HediffMaker.MakeHediff(flu, sick);
                    h.Severity = 0.7f * flu.lethalSeverity;
                    sick.health.AddHediff(h);
                    break;
                case 3:
                    var cur = Find.ResearchManager.GetProject();
                    if (cur != null) Find.ResearchManager.StopProject(cur);
                    queued = DefDatabase<ResearchProjectDef>.AllDefsListForReading.FirstOrDefault(r => !r.IsFinished && r.CanStartNow);
                    Tools.Run("governor", new Dictionary<string, object> { ["action"] = "steward", ["queue"] = new List<object> { "NoSuchProject", queued?.defName } });
                    break;
                case 4:
                    Find.LetterStack.ReceiveLetter("Steward test letter", "harmless", LetterDefOf.NeutralEvent);
                    break;
                case 5:
                    map.areaManager.TryMakeNewAllowed(out area);
                    foreach (var c in map.AllCells.Where(c => c.InHorDistOf(at, 25))) area[c] = true;
                    newbie = cols.Last();
                    foreach (var p in cols) p.playerSettings.AreaRestrictionInPawnCurrentMap = p == newbie ? null : area;
                    break;
                case 6:
                    cooler = BuildFreezer(Free(at, 12, 5));
                    break;
                case 7:
                    armedPawn = cols.First();
                    if (armedPawn.equipment.Primary == null)
                        armedPawn.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("Gun_Revolver")));
                    break;
                case 8:
                    windowCase = true;
                    windowAt = UnityEngine.Time.realtimeSinceStartup;
                    Find.WindowStack.Add(new Dialog_MessageBox("This research unlocks things which you cannot build (requires memes)."));
                    break;
                case 9:
                    runCase = true;
                    Tools.Run("run", new Dictionary<string, object> { ["hours"] = 6.0, ["speed"] = "superfast" });
                    break;
            }
        }

        void Step()
        {
            int age = ticks - caseStart;
            var evs = Events();
            switch (caseIdx)
            {
                case 0:
                    bool tended = !patient.health.HasHediffsNeedingTend();
                    if (age < 5000 && !(patient.InBed() && tended && Did(evs, "feedPatients"))) return;
                    // vanilla doctors often get there first; the outcome is what matters, the event says who did it
                    Kit.Check(patient.InBed(), "downed colonist outside a bed ends up in a bed" + (Did(evs, "rescue") ? " (steward rescue order)" : " (colonists' own AI)"));
                    Kit.Check(tended, "patient in bed gets tended" + (Did(evs, "tend") ? " (steward prioritized tend)" : " (colonists' own AI)"));
                    Kit.Check(Did(evs, "feedPatients"), "steward: patient in bed with food < 30% -> prioritized feeding (event)");
                    patient.health.RemoveHediff(patient.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Anesthetic));
                    Next();
                    break;
                case 1:
                    if (age < 2500 && !Did(evs, "clearForcedRest")) return;
                    Kit.Check(Did(evs, "clearForcedRest"), "steward: walking patient on a forced bed rest with food < 40% -> job cleared (event)");
                    Kit.Check(!(forced.CurJob?.playerForced ?? false) || forced.CurJob.def != JobDefOf.LayDown, "the forced LayDown job is gone");
                    Next();
                    break;
                case 2:
                    if (age < 600 && !Has(evs, "decision_needed")) return;
                    Kit.Check(Has(evs, "decision_needed"), "steward: disease severity >= immunity -> decision_needed");
                    Next();
                    break;
                case 3:
                    if (queued == null) { Kit.Info("no startable research project: research case skipped"); Next(); break; }
                    if (age < 600 && !Did(evs, "research")) return;
                    Kit.Check(Did(evs, "research") && Find.ResearchManager.GetProject() == queued, $"steward: no project -> first startable of the queue ({queued.defName}), unknown names skipped");
                    Next();
                    break;
                case 4:
                    if (age < 1200 && !Did(evs, "dismissRoutineLetters")) return;
                    Kit.Check(Did(evs, "dismissRoutineLetters") && !Find.LetterStack.LettersListForReading.Any(l => l.Label == "Steward test letter"),
                        "steward: neutral letter without choices dismissed");
                    Next();
                    break;
                case 5:
                    if (age < 600 && !Did(evs, "newColonistArea")) return;
                    Kit.Check(Did(evs, "newColonistArea") && newbie.playerSettings.AreaRestrictionInPawnCurrentMap == area,
                        "steward: colonist without area gets the one the others use");
                    foreach (var p in map.mapPawns.FreeColonistsSpawned) p.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                    Next();
                    break;
                case 6:
                    if (cooler == null) { Kit.Check(false, "could not build the test freezer"); Next(); break; }
                    if (age < 600 && !Did(evs, "thermostats")) return;
                    var tc = cooler.TryGetComp<CompTempControl>();
                    Kit.Check(Did(evs, "thermostats") && tc != null && Math.Abs(tc.TargetTemperature + 10f) < 0.1f,
                        $"steward: new cooler at 21C with a food stockpile on its cold side -> -10C (now {tc?.TargetTemperature})");
                    Next();
                    break;
                case 7:
                    if (age == 400)
                    {
                        // the steward has seen the pawn armed; now drop it like a downed colonist does
                        armedPawn.equipment.TryDropEquipment(armedPawn.equipment.Primary, out var dropped, armedPawn.Position);
                        dropped?.SetForbidden(true, false);
                        return;
                    }
                    if (age < 2000 && !(age > 400 && Did(evs, "unforbidWeapons"))) return;
                    Kit.Check(Did(evs, "unforbidWeapons"), "steward: a colonist's dropped forbidden weapon is unforbidden (and re-equip ordered)");
                    Next();
                    break;
                case 8:
                    return;   // checked per frame
                case 9:
                    if (age == 120)
                    {
                        GovernorEvents.Emit("decision_needed", map, new Dictionary<string, object> { ["reason"] = "steward harness stop test" });
                        runEmitAt = UnityEngine.Time.realtimeSinceStartup;
                    }
                    return;   // the stop pauses the game: checked per frame
            }
        }

        List<Dictionary<string, object>> Events()
        {
            var r = GovernorEvents.Since(cursor, null, 500);
            return ((List<object>)r["events"]).Cast<Dictionary<string, object>>().Where(e => (int)(double)e["tick"] >= 0).ToList();
        }

        bool Did(List<Dictionary<string, object>> evs, string reflex) =>
            evs.Any(e => e["type"] as string == "steward" && e.TryGetValue("reflex", out var r) && r as string == reflex);

        bool Has(List<Dictionary<string, object>> evs, string type) => evs.Any(e => e["type"] as string == type);

        IntVec3 Free(IntVec3 near, int radius, int clear = 1)
        {
            foreach (var c in GenRadial.RadialCellsAround(near, radius + 6, true))
            {
                if (c.DistanceTo(near) < radius) continue;
                var r = CellRect.CenteredOn(c, clear);
                if (r.InBounds(map) && r.Cells.All(x => x.Standable(map) && x.GetEdifice(map) == null && x.GetThingList(map).Count == 0)) return c;
            }
            return near;
        }

        Building_Bed SpawnBed(IntVec3 c)
        {
            var b = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
            b.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(b, c, map, Rot4.North);
            return b;
        }

        // a 3x3 room: granite walls, a stockpile inside, a cooler in the north wall facing in (rot north)
        Thing BuildFreezer(IntVec3 center)
        {
            var inner = CellRect.CenteredOn(center, 1);
            foreach (var c in inner.ExpandedBy(1).EdgeCells)
            {
                if (c == new IntVec3(center.x, 0, inner.maxZ + 1)) continue;
                var w = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.BlocksGranite);
                w.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(w, c, map);
            }
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            foreach (var c in inner) zone.AddCell(c);
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("Cooler");
            if (def == null) return null;
            var cool = ThingMaker.MakeThing(def);
            cool.SetFaction(Faction.OfPlayer);
            return GenSpawn.Spawn(cool, new IntVec3(center.x, 0, inner.maxZ + 1), map, Rot4.North);
        }

        void Finish()
        {
            done = true;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;   // let the probe reach TICKS DONE
            try
            {
                if (originalPolicy != null) File.WriteAllText(GovernorPolicy.FilePath, originalPolicy);
                Tools.Run("governor", new Dictionary<string, object> { ["action"] = "reload" });
            }
            catch (Exception e) { Kit.Info("could not restore governor.json: " + e.Message); }
            Kit.Summary(Mod);
        }
    }
}
