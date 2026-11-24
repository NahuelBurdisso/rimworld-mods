using System.Collections.Generic;
using System.Linq;
using Omens;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace OmensTest
{
    /// <summary>
    /// The Crows Know + The Cold Camp (DESIGN.md test plan 10-20) with real raids: a StageThenAttack raid
    /// (crows circle, settings toggle, forced assault -> flock + camp, two raiders exit -> "2 went home"),
    /// a second staging raid moved next to the camp (same camp, two entries), an ImmediateAttack raid (no
    /// staging, no crows, no camp), then every raider killed (carrion omen) and the corpses removed (omen ends).
    /// </summary>
    public class RaidHarness : HarnessBase
    {
        public RaidHarness(Game game) { }

        protected override int StartTick => 800;
        protected override string Name => "Raid";
        protected override string After => "Rain";

        StagingWatcher sw;
        CrowWatcher cw;
        Faction enemy;
        List<Pawn> raiders, raiders2, raiders3;
        Lord lord, lord2, lord3;
        StageRecord rec, rec2;
        Building_ColdCamp camp;
        int want, stat0, stat1, camps0, minWaitSaved;
        IntVec3 stage2;

        protected override string Facts() =>
            $"lord {lord?.loadID} toil {lord?.CurLordToil?.GetType().Name} ticksInToil {lord?.ticksInToil} rec {rec} rec2 {rec2} " +
            $"crows live {CrowMote.Live.Count} circling@stage {(rec != null ? TestUtil.CirclingNear(map, rec.cell, 8f) : -1)} departing {TestUtil.Departing(map)} " +
            $"stats crows {OmensStats.CrowsSpawned} flock {OmensStats.FlockCrows} camps {OmensStats.CampsSpawned} carrion {cw?.CarrionOpen} battleStart {cw?.BattleStart}";

        void MakeOpen(IntVec3 c)
        {
            if (c.Fogged(map)) map.fogGrid.Unfog(c);
            if (c.Roofed(map)) map.roofGrid.SetRoof(c, null);
        }

        protected override void Step()
        {
            switch (phase)
            {
                // ------------------------------------------------ raid 1: staging, crows
                case 0:
                    sw = StagingWatcher.For(map);
                    cw = map.GetComponent<CrowWatcher>();
                    Kit.Check(sw != null && cw != null, "Raid: StagingWatcher and CrowWatcher map components exist");
                    OmensMod.Settings.crows = true;
                    OmensMod.Settings.coldCamp = true;
                    enemy = TestUtil.StagingFaction();
                    if (enemy == null) { Kit.Check(false, "Raid: no hostile faction that can stage attacks"); Finish(); return; }
                    raiders = TestUtil.Raid(map, enemy, 700f, TestUtil.StageThenAttack);
                    lord = raiders.FirstOrDefault()?.GetLord();
                    Kit.Check(raiders.Count >= 3 && lord?.CurLordToil is LordToil_Stage,
                        $"Raid: a real StageThenAttack raid of {enemy.Name}: {raiders.Count} raiders, lord toil {lord?.CurLordToil?.GetType().Name}");
                    if (lord == null) { Finish(); return; }
                    MakeOpen(lord.CurLordToil.FlagLoc);
                    want = 2 + lord.loadID % 2;
                    Next();
                    break;
                case 1:
                    rec = sw.RecordFor(lord.loadID);
                    if (!WaitFor(rec != null, 200, "a staging record")) return;
                    if (rec == null) { Finish(); return; }
                    Kit.Check(rec.cell == lord.CurLordToil.FlagLoc && rec.Came == raiders.Count && rec.factionName == enemy.Name,
                        $"Raid: StagingWatcher recorded the staging: cell {rec.cell}, came {rec.Came}/{raiders.Count}, faction {rec.factionName}");
                    Next();
                    break;
                case 2:
                {
                    int n = TestUtil.CirclingNear(map, rec.cell, 7.5f);
                    if (!WaitFor(n >= want, 700, $"{want} crows circling the stage point")) return;
                    int far = CrowMote.Live.Count(m => m.Map == map && !m.Departing && (m.exactPosition - rec.cell.ToVector3Shifted()).MagnitudeHorizontal() > 7.5f);
                    Kit.Check(n == want && far == 0, $"Raid: {n} crows circle the staging point (want {want}, 2-3), none elsewhere ({far})");
                    OmensMod.Settings.crows = false;
                    cw.CrowPoll();
                    Kit.Check(TestUtil.CirclingNear(map, rec.cell, 10f) == 0, "Raid: crows setting off -> the circling crows leave");
                    OmensMod.Settings.crows = true;
                    Next();
                    break;
                }
                case 3:
                {
                    int n = TestUtil.CirclingNear(map, rec.cell, 7.5f);
                    if (!WaitFor(n >= want, 400, "crows coming back after the setting is on again")) return;
                    Kit.Check(n >= 2, $"Raid: setting on again -> {n} crows circle again");
                    Next();
                    break;
                }
                // ------------------------------------------------ assault: flock + cold camp
                case 4:
                    if (!WaitFor(lord.ticksInToil >= OmensTuning.MinWaitTicks + 150, 2000, "the raid to have waited long enough for a camp")) return;
                    stat0 = OmensStats.FlockCrows;
                    camps0 = OmensStats.CampsSpawned;
                    Kit.Check(TestUtil.ForceAssault(lord), "Raid: forced the assault (as the ForceEnemyAssault debug action)");
                    Next();
                    break;
                case 5:
                    if (!WaitFor(rec.ended, 130, "the watcher to see the assault start")) return;
                    int flock = OmensStats.FlockCrows - stat0;
                    Kit.Check(rec.ended && rec.assault, $"Raid: stage ended as an assault after waiting {rec.WaitTicks} ticks");
                    Kit.Check(flock >= 6 && flock <= 8 && TestUtil.Departing(map) >= 6, $"Raid: a flock of {flock} crows leaves toward the colony (6-8); {TestUtil.Departing(map)} in flight");
                    Kit.Check(cw.SiteFor(lord.loadID) == null && TestUtil.CirclingNear(map, rec.cell, 10f) == 0, "Raid: no crow circles the stage point any more");
                    camp = rec.camp;
                    Kit.Check(camp != null && camp.Spawned && camp.Position.DistanceTo(rec.cell) <= 4.5f && OmensStats.CampsSpawned == camps0 + 1,
                        $"Raid: a cold campfire at {camp?.Position}, {camp?.Position.DistanceTo(rec.cell):F1} cells from the stage point {rec.cell}");
                    if (camp == null) { Finish(); return; }
                    string text = camp.GetInspectString();
                    Log.Message("[Test] Raid: camp inspect text: " + text.Replace("\n", " | "));
                    Kit.Check(text.Contains(enemy.Name) && text.Contains($"{rec.Came} came") && text.Contains("waited here"),
                        "Raid: the camp names the faction, how long they waited and how many came");
                    int ash = GenRadial.RadialCellsAround(camp.Position, 1.5f, true).Count(c => c.InBounds(map) && c.GetThingList(map).Any(t => t.def == ThingDefOf.Filth_Ash));
                    Kit.Check(ash >= 1, $"Raid: ash around the camp ({ash} cells)");
                    Kit.Check(camp.DeconstructibleBy(Faction.OfPlayer).Accepted && new Designator_Deconstruct().CanDesignateThing(camp).Accepted
                        && camp.def.costList.NullOrEmpty() && camp.Faction == null,
                        "Raid: the camp can be deconstructed by the player and gives nothing back");
                    Next();
                    break;
                case 6:
                {
                    // two raiders go home
                    List<Pawn> leave = raiders.Where(p => p.Spawned && !p.Dead && !p.Downed).Take(2).ToList();
                    foreach (Pawn p in leave) p.ExitMap(false, Rot4.North);
                    Kit.Check(leave.Count == 2, $"Raid: {leave.Count} raiders exit the map");
                    Next();
                    break;
                }
                case 7:
                {
                    if (!WaitFor(rec.returned >= 2, 130, "the watcher to count the two raiders who went home")) return;
                    CampEntry e = camp.Entries.LastOrDefault();
                    string after = camp.GetInspectString();
                    Kit.Check(rec.returned == 2 && e?.returned == 2 && after.Contains("2 went home"),
                        $"Raid: 2 went home (record {rec.returned}, entry {e?.returned}): \"{after.Replace("\n", " | ")}\"");
                    Next();
                    break;
                }
                // ------------------------------------------------ raid 2: stages next to the camp
                case 8:
                    minWaitSaved = OmensTuning.MinWaitTicks;
                    OmensTuning.MinWaitTicks = 200;           // the 1000-tick rule was checked with raid 1
                    camps0 = OmensStats.CampsSpawned;
                    raiders2 = TestUtil.Raid(map, enemy, 400f, TestUtil.StageThenAttack);
                    lord2 = raiders2.FirstOrDefault()?.GetLord();
                    if (!(lord2?.CurLordToil is LordToil_Stage)) { Kit.Check(false, "Raid: second staging raid"); Next(); Next(); Next(); return; }
                    stage2 = CellFinder.StandableCellNear(camp.Position + new IntVec3(2, 0, 2), map, 2f, c => c != camp.Position);
                    MakeOpen(stage2);
                    ((LordToilData_Stage)lord2.CurLordToil.data).stagingPoint = stage2;
                    lord2.CurLordToil.UpdateAllDuties();
                    Next();
                    break;
                case 9:
                    rec2 = sw.RecordFor(lord2.loadID);
                    if (!WaitFor(rec2 != null && lord2.ticksInToil >= 300, 600, "the second staging to be recorded and wait 300 ticks")) return;
                    if (rec2 == null) { Next(); Next(); return; }
                    Kit.Check(rec2.cell == stage2, $"Raid: second raid stages at {rec2.cell}, {stage2.DistanceTo(camp.Position):F1} cells from the camp");
                    TestUtil.ForceAssault(lord2);
                    Next();
                    break;
                case 10:
                    if (!WaitFor(rec2.ended, 130, "the second assault")) return;
                    Kit.Check(rec2.camp == camp && camp.Entries.Count == 2 && OmensStats.CampsSpawned == camps0,
                        $"Raid: the second raid is added to the same camp ({camp.Entries.Count} entries, {OmensStats.CampsSpawned - camps0} new camps)");
                    Log.Message("[Test] Raid: camp inspect text now: " + camp.GetInspectString().Replace("\n", " | "));
                    OmensTuning.MinWaitTicks = minWaitSaved;
                    Next();
                    break;
                // ------------------------------------------------ raid 3: immediate attack, the negative case
                case 11:
                    stat1 = OmensStats.CrowsSpawned;
                    camps0 = OmensStats.CampsSpawned;
                    raiders3 = TestUtil.Raid(map, enemy, 300f, RaidStrategyDefOf.ImmediateAttack);
                    lord3 = raiders3.FirstOrDefault()?.GetLord();
                    Kit.Check(lord3 != null && !(lord3.CurLordToil is LordToil_Stage), $"Raid: an ImmediateAttack raid ({raiders3.Count} raiders, toil {lord3?.CurLordToil?.GetType().Name})");
                    Next();
                    break;
                case 12:
                    if (InPhase < 400) return;
                    Kit.Check(lord3 == null || sw.RecordFor(lord3.loadID) == null, "Raid: immediate attack -> no staging record");
                    Kit.Check(OmensStats.CrowsSpawned == stat1, $"Raid: immediate attack -> no crows in 400 ticks ({OmensStats.CrowsSpawned - stat1})");
                    Kit.Check(OmensStats.CampsSpawned == camps0, "Raid: immediate attack -> no camp");
                    Next();
                    break;
                // ------------------------------------------------ carrion
                case 13:
                {
                    Kit.Check(cw.BattleStart >= 0, $"Raid: the battle was noticed (started at tick {cw.BattleStart})");
                    List<Pawn> hostiles = map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer) && !p.Dead).ToList();
                    int open = 0;
                    foreach (Pawn p in hostiles)
                    {
                        if (OmensUtil.OpenSky(p.Position, map)) open++;
                        p.Kill(null);
                    }
                    Log.Message($"[Test] Raid: killed {hostiles.Count} hostiles, {open} under open sky");
                    cw.CarrionPoll();
                    Kit.Check(cw.CarrionOpen && cw.CarrionCorpses >= 3, $"Raid: battle over with {cw.CarrionCorpses} dead in the open -> carrion omen at {cw.CarrionCenter}");
                    Next();
                    break;
                }
                case 14:
                {
                    if (!cw.CarrionOpen) { Kit.Check(false, "Raid: carrion omen closed too early"); Next(); return; }
                    int n = TestUtil.CirclingNear(map, cw.CarrionCenter, 7.5f);
                    if (!WaitFor(n >= 2, 400, "crows over the dead")) return;
                    Kit.Check(n >= 2, $"Raid: {n} crows circle over the dead at {cw.CarrionCenter}");
                    foreach (Thing c in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).ToList()) c.Destroy();
                    cw.CarrionPoll();
                    cw.CrowPoll();
                    Kit.Check(!cw.CarrionOpen && cw.SiteFor(CrowWatcher.CarrionKey) == null && TestUtil.CirclingNear(map, cw.CarrionCenter.IsValid ? cw.CarrionCenter : map.Center, 999f) == 0,
                        "Raid: corpses gone -> the omen ends and its crows leave");
                    Next();
                    break;
                }
                // ------------------------------------------------ save data
                case 15:
                {
                    var holder = new EntryHolder { entries = camp.Entries.ToList() };
                    EntryHolder back = TestUtil.Roundtrip(holder);
                    bool same = back?.entries != null && back.entries.Count == holder.entries.Count
                        && back.entries.Zip(holder.entries, (a, b) => a.factionName == b.factionName && a.came == b.came && a.returned == b.returned
                            && a.startTick == b.startTick && a.waitTicks == b.waitTicks && a.id == b.id).All(x => x);
                    Kit.Check(same, $"Raid: camp entries survive a Scribe roundtrip ({back?.entries?.Count} entries)");
                    var r = new StageRecord { lordId = rec.lordId, factionName = rec.factionName, cell = rec.cell, startTick = rec.startTick, endTick = rec.endTick,
                        ended = true, assault = true, seenIds = rec.seenIds.ToList(), returned = rec.returned, died = rec.died, captured = rec.captured, campEntryId = rec.campEntryId };
                    StageRecord r2 = TestUtil.Roundtrip(r);
                    Kit.Check(r2 != null && r2.lordId == r.lordId && r2.cell == r.cell && r2.Came == r.Came && r2.returned == r.returned
                        && r2.startTick == r.startTick && r2.endTick == r.endTick && r2.factionName == r.factionName && r2.ended,
                        $"Raid: a stage record survives a Scribe roundtrip ({r2})");
                    Finish();
                    break;
                }
            }
        }

        protected override void Cleanup()
        {
            if (minWaitSaved > 0) OmensTuning.MinWaitTicks = minWaitSaved;
            OmensMod.Settings.crows = true;
            OmensMod.Settings.coldCamp = true;
        }

        public class EntryHolder : IExposable
        {
            public List<CampEntry> entries;
            public void ExposeData() { Scribe_Collections.Look(ref entries, "entries", LookMode.Deep); }
        }
    }
}
