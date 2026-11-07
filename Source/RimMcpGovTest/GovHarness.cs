using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimMcp;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimMcpGovTest
{
    /// <summary>
    /// Drives the RimMcp governor through real fights during `rw launch --ticks 25000` (quicktest).
    /// 0 baseline raid (governor off, Claude-like late draft) / A same raid with the governor /
    /// B manhunters / C retreat + rescue / D capture / E manual-draft respect / F big raid /
    /// G attack from two directions. Headline: did the colonists win unaided (no harness kills)?
    /// Test setup uses dev-style spawning; the governor itself only drafts and gives orders.
    /// </summary>
    public class GovHarness : GameComponent
    {
        const string Mod = "RimMcp/Governor";
        const int LastStep = 8;
        const int RaidSeed = 7301;      // baseline and A get the same raiders
        const int ClaudeDelay = 600;    // baseline: Claude drafts everyone where they stand this late

        bool armed, done;
        int ticks, step, phase;
        Map map;
        Governor gov;
        IntVec3 origin;
        Building_Bed prisonBed;
        readonly List<IntVec3> standCells = new List<IntVec3>();
        string originalPolicy;
        Run run;
        string baselineSummary;

        static readonly string[] Names =
        {
            "", "0 baseline raid (governor off)", "A raid", "B manhunters", "C retreat+rescue", "D capture",
            "E manual draft", "F big raid", "G two directions",
        };

        class Run
        {
            public string name;
            public bool governor = true;
            public long cursor;
            public int spawnTick, seenTick = -1, endTick = -1, killAt, guardAt, postMin, postMax;
            public int firstShotTick = -1, nextTrace;
            public string firstShot;
            public readonly List<Pawn> enemies = new List<Pawn>();
            public readonly HashSet<Pawn> assigned = new HashSet<Pawn>(), reached = new HashSet<Pawn>(), excluded = new HashSet<Pawn>();
            public readonly List<Pawn> lastRanged = new List<Pawn>();
            public readonly HashSet<Pawn> everRanged = new HashSet<Pawn>();
            public readonly List<Pawn> startColonists = new List<Pawn>();
            public readonly List<Pawn> eligibleAtSeen = new List<Pawn>();
            public readonly Dictionary<Pawn, float[]> records = new Dictionary<Pawn, float[]>();
            public bool draftChecked, forced, hookDone, hook2Done;
            public int forcedCount, hookTick;
            public float oursAtLimit = -1f, theirsAtLimit = -1f;
            public Pawn x, y, z;
        }

        public GovHarness(Game game) { }

        public override void FinalizeInit() { armed = Kit.HarnessArmed; }

        // Letters or messages can pause the game; keep it running while the harness works.
        public override void GameComponentUpdate()
        {
            if (!armed || done || Current.ProgramState != ProgramState.Playing) return;
            var tm = Find.TickManager;
            if (tm.CurTimeSpeed == TimeSpeed.Paused && !Find.WindowStack.WindowsForcePause) tm.CurTimeSpeed = TimeSpeed.Ultrafast;
        }

        public override void GameComponentTick()
        {
            if (!armed || done) return;
            ticks++;
            try
            {
                if (ticks == 60) Setup();
                else if (step >= 1 && step <= LastStep) Scenario();
                // 8 scenarios at their time limits + post phases need ~31000 ticks: the old 24900 cap failed every run
                if (!done && ticks >= 40000)
                {
                    Kit.Check(false, $"harness finished before 40000 ticks (stuck in {Names[Math.Min(step, LastStep)]}, phase {phase})");
                    Finish();
                }
            }
            catch (Exception ex)
            {
                Kit.Check(false, "harness threw: " + ex);
                Finish();
            }
        }

        public static bool Finished;   // the steward harness starts after the fights

        void Finish()
        {
            done = true;
            Finished = true;
            try
            {
                if (originalPolicy != null) File.WriteAllText(GovernorPolicy.FilePath, originalPolicy);
                Tools.Run("governor", new Dictionary<string, object> { ["action"] = "reload" });
            }
            catch (Exception e) { Kit.Info("could not restore governor.json: " + e.Message); }
            Kit.Info($"events file: {GovernorEvents.FilePath}; harness ran {ticks} ticks");
            Kit.Summary(Mod);
        }

        // ------------------------------------------------------------------ setup

        void Setup()
        {
            map = Find.CurrentMap;
            gov = Governor.Instance;
            Kit.Check(gov != null, "the Governor game component exists");
            if (gov == null || map == null) { Finish(); return; }
            EnsureFighters(3);
            if (!FindBaseSpot(out origin)) { Kit.Check(false, "found a clear 16x12 spot for the test base"); Finish(); return; }
            for (int i = 0; i < 6; i++) standCells.Add(origin + new IntVec3(9, 0, 2 + i));
            int k = 0;
            foreach (var p in map.mapPawns.FreeColonistsSpawned.ToList()) Kit.Teleport(p, standCells[k++ % standCells.Count]);
            Build();
            k = 0;
            foreach (var p in map.mapPawns.FreeColonistsSpawned.ToList()) Arm(p, 500 + k++);
            // quicktest rosters are random (shooting 0-4 in sprint 2b): the outcome measured the roster, not the
            // governor. Fixed, ordinary skills make the runs comparable (baseline and governor get the same).
            foreach (var p in map.mapPawns.FreeColonistsSpawned.Where(Violent))
            {
                var sh = p.skills?.GetSkill(SkillDefOf.Shooting); if (sh != null && !sh.TotallyDisabled) sh.Level = 8;
                var me = p.skills?.GetSkill(SkillDefOf.Melee); if (me != null && !me.TotallyDisabled) me.Level = 6;
            }
            originalPolicy = File.Exists(GovernorPolicy.FilePath) ? File.ReadAllText(GovernorPolicy.FilePath) : null;
            WritePolicy(true, 0.5f);
            Kit.Check(GovernorPolicy.Current.enabled, "governor enabled through governor.json + the governor tool (reload)");
            var status = Tools.Run("governor", new Dictionary<string, object> { ["action"] = "status" }) as Dictionary<string, object>;
            Kit.Check(status != null && status["policyPath"] as string == GovernorPolicy.FilePath, "governor status prints the policy path: " + GovernorPolicy.FilePath);
            var home = GovernorPositions.HomeCenter(map, GovernorPolicy.Current);
            Kit.Info($"base at {origin}, home center {home}, sandbags x={origin.x + 12}, map {map.Size}, colonists: " +
                     string.Join(", ", map.mapPawns.FreeColonistsSpawned.Select(p =>
                         $"{p.LabelShort}({p.equipment?.Primary?.LabelCap ?? "unarmed"}, shoot {p.skills?.GetSkill(SkillDefOf.Shooting)?.Level}{(Violent(p) ? "" : ", non-violent")})")));
            step = 1;
            phase = 0;
        }

        void WritePolicy(bool enabled, float retreatPct)
        {
            File.WriteAllText(GovernorPolicy.FilePath,
                "{\"enabled\": " + (enabled ? "true" : "false") + ", \"combat\": {\"autoDraft\": true, \"retreatHealthPct\": " +
                retreatPct.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ", \"retreatBleedRate\": 3, \"undraftAfterQuietTicks\": 300, \"engageMeleeRadius\": 5, \"defendIndoorsIfRatioAbove\": 2.5}," +
                " \"rescue\": {\"downedColonists\": true, \"captureDowned\": true}, \"nonCombatants\": {\"shelter\": true}, \"events\": {\"maxFileKb\": 512}," +
                " \"steward\": {\"enabled\": false}}\n");
            Tools.Run("governor", new Dictionary<string, object> { ["action"] = "reload" });
        }

        void EnsureFighters(int n)
        {
            int guard = 0;
            while (map.mapPawns.FreeColonistsSpawned.Count(Violent) < n && guard++ < 20)
            {
                var p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (!Violent(p)) { Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard); continue; }
                var anyone = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                GenSpawn.Spawn(p, CellFinder.StandableCellNear(anyone?.Position ?? map.Center, map, 6f), map);
            }
        }

        static bool Violent(Pawn p) => !p.WorkTagIsDisabled(WorkTags.Violent);

        // 40 cells from the edges so raiders can start ~35 cells away on any side; smaller maps: 6.
        bool FindBaseSpot(out IntVec3 result) => FindBaseSpot(40, out result) || FindBaseSpot(6, out result);

        bool FindBaseSpot(int margin, out IntVec3 result)
        {
            var cols = map.mapPawns.FreeColonistsSpawned;
            var center = cols.Count > 0 ? new IntVec3((int)cols.Average(p => p.Position.x), 0, (int)cols.Average(p => p.Position.z)) : map.Center;
            foreach (var c in GenRadial.RadialCellsAround(center, 40f, true))
            {
                var o = c - new IntVec3(8, 0, 6);
                var rect = new CellRect(o.x, o.z, 16, 12);
                if (!rect.InBounds(map) || rect.minX < margin || rect.minZ < margin || rect.maxX > map.Size.x - margin - 1 || rect.maxZ > map.Size.z - margin - 1) continue;
                int standable = 0;
                bool ok = true;
                foreach (var cell in rect)
                {
                    var terr = cell.GetTerrain(map);
                    if (cell.Fogged(map) || cell.Roofed(map) || terr.IsWater || terr.passability != Traversability.Standable ||
                        !terr.affordances.Contains(TerrainAffordanceDefOf.Heavy)) { ok = false; break; }
                    if (cell.Standable(map)) standable++;
                }
                if (!ok || standable < rect.Area * 0.5f) continue; // trees and rocks get cleared
                result = o;
                return true;
            }
            result = IntVec3.Invalid;
            return false;
        }

        void Build()
        {
            var all = new CellRect(origin.x, origin.z, 16, 12);
            foreach (var c in all)
                foreach (var t in c.GetThingList(map).ToList())
                    if (!(t is Pawn) && t.def.destroyable && (t.def.category == ThingCategory.Building || t.def.category == ThingCategory.Plant || t.def.category == ThingCategory.Item))
                        t.Destroy(DestroyMode.Vanish);
            var roomA = new CellRect(origin.x, origin.z, 7, 6);       // bedroom, door north
            var roomB = new CellRect(origin.x, origin.z + 7, 5, 5);   // prison cell, door south
            var doorA = new IntVec3(origin.x + 3, 0, origin.z + 5);
            var doorB = new IntVec3(origin.x + 2, 0, origin.z + 7);
            foreach (var (room, door) in new[] { (roomA, doorA), (roomB, doorB) })
            {
                foreach (var c in room.EdgeCells) Place(c == door ? ThingDefOf.Door : ThingDefOf.Wall, c, Rot4.North);
                foreach (var c in room.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            }
            for (int i = 0; i < 3; i++) Place(ThingDefOf.Bed, new IntVec3(origin.x + 1 + 2 * i, 0, origin.z + 1), Rot4.North);
            prisonBed = Place(ThingDefOf.Bed, new IntVec3(origin.x + 2, 0, origin.z + 8), Rot4.North) as Building_Bed;
            if (prisonBed != null)
            {
                prisonBed.ForOwnerType = BedOwnerType.Prisoner;
                // as the bed gizmo does: "prison cell" is only recomputed on a room-shape refresh
                prisonBed.GetDistrict()?.Notify_RoomShapeOrContainedBedsChanged();
                prisonBed.GetRoom()?.Notify_RoomShapeChanged();
            }
            for (int i = 0; i < 5; i++) Place(ThingDefOf.Sandbags, new IntVec3(origin.x + 12, 0, origin.z + 1 + 2 * i), Rot4.North);
            foreach (var c in new CellRect(origin.x - 5, origin.z - 5, 26, 22).ClipInsideMap(map)) map.areaManager.Home[c] = true;
            var beds = map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>().Count();
            Kit.Check(beds >= 4 && prisonBed != null && prisonBed.ForPrisoners && prisonBed.Position.IsInPrisonCell(map),
                $"test base built: {beds} beds, prison bed {(prisonBed != null ? prisonBed.Position.ToString() : "missing")}");
        }

        Thing Place(ThingDef def, IntVec3 c, Rot4 rot)
        {
            var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            thing.SetFaction(Faction.OfPlayer);
            return GenSpawn.Spawn(thing, c, map, rot);
        }

        // Same loadout every scenario: a bolt-action rifle (seeded quality).
        void Arm(Pawn p, int seed)
        {
            if (p.equipment == null || !Violent(p)) return;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle");
            if (def == null) return;
            p.equipment.DestroyAllEquipment();
            Rand.PushState(seed);
            try { p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(def)); }
            finally { Rand.PopState(); }
        }

        // ------------------------------------------------------------------ scenarios

        void Scenario()
        {
            int now = Find.TickManager.TicksGame;
            var st = gov.StateFor(map);
            switch (phase)
            {
                case 0: Begin(now); break;
                case 1:
                    Monitor(st, now);
                    Hooks(st, now);
                    if (Ended(st))
                    {
                        run.endTick = now;
                        phase = 2;
                        if (!run.governor)
                        {
                            Tools.Run("draft", new Dictionary<string, object> { ["drafted"] = false });
                            WritePolicy(true, 0.5f);
                        }
                    }
                    else if (now >= run.guardAt)
                    {
                        Kit.Check(false, $"{run.name}: threat ended (seen={run.seenTick >= 0}, active={st.active})");
                        KillEnemies();
                        if (!run.governor) WritePolicy(true, 0.5f);
                        run.endTick = now;
                        phase = 2;
                    }
                    break;
                case 2:
                    Hooks(st, now);
                    int post = now - run.endTick;
                    if (post >= run.postMax || post >= run.postMin && post % 30 == 0 && PostDone()) { EndChecks(st, now); NextScenario(); }
                    break;
            }
        }

        void Begin(int now)
        {
            run = new Run { name = Names[step], cursor = GovernorEvents.Seq, spawnTick = now };
            run.startColonists.AddRange(map.mapPawns.FreeColonistsSpawned);
            foreach (var p in run.startColonists) run.records[p] = Records(p);
            var east = new Vector2(1, 0);
            int limit;
            switch (step)
            {
                case 1:
                    run.governor = false;
                    WritePolicy(false, 0.5f);
                    SpawnRaiders(4, 1, 34, east, RaidSeed); limit = 3000; run.postMin = run.postMax = 120; break;
                case 2: SpawnRaiders(4, 1, 34, east, RaidSeed); limit = 4000; run.postMin = run.postMax = 480; break;
                case 3: SpawnManhunters(3, 34, east, 7302); limit = 2500; run.postMin = run.postMax = 480; break;
                case 4: SpawnRaiders(2, 2, 34, east, 7303); limit = 3500; run.postMin = 480; run.postMax = 1200; break;
                case 5: SpawnRaiders(2, 0, 25, east, 7304); limit = 2500; run.postMin = 480; run.postMax = 1500; break;
                case 6: SpawnRaiders(2, 0, 30, east, 7305); limit = 2500; run.postMin = run.postMax = 480; break;
                case 7: SpawnRaiders(7, 2, 36, east, 7306); limit = 4000; run.postMin = run.postMax = 480; break;
                default:
                    SpawnRaiders(3, 0, 34, east, 7307);
                    SpawnRaiders(3, 1, 30, new Vector2(0, 1), 7308);
                    limit = 4000; run.postMin = run.postMax = 480; break;
            }
            run.killAt = now + limit;
            run.guardAt = run.killAt + 600;
            run.nextTrace = now + 240;
            Kit.Info($"{run.name}: spawned " + string.Join("; ", run.enemies.GroupBy(e => e.Position.x / 8 * 1000 + e.Position.z / 8)
                .Select(g => $"{g.Count()} at {g.First().Position} ({string.Join(", ", g.Select(e => e.equipment?.Primary?.def.label ?? "unarmed"))})")));
            if (run.enemies.Count == 0) { Kit.Check(false, run.name + ": could not spawn enemies"); NextScenario(); return; }
            phase = 1;
        }

        bool Ended(GovMapState st) => run.governor
            ? run.seenTick >= 0 && !st.active
            : run.seenTick >= 0 && run.enemies.All(e => e.Dead || e.Downed || !e.Spawned);

        void NextScenario()
        {
            Restore();
            step++;
            phase = 0;
            if (step > LastStep) Finish();
        }

        static float[] Records(Pawn p) => new[]
        {
            p.records.GetValue(RecordDefOf.ShotsFired), p.records.GetValue(RecordDefOf.DamageDealt), p.records.GetValue(RecordDefOf.Kills),
            p.records.GetValue(RecordDefOf.PawnsDowned), p.records.GetValue(RecordDefOf.DamageTaken),
        };

        float[] Delta(Pawn p)
        {
            var now = Records(p);
            if (run.records.TryGetValue(p, out var a)) for (int i = 0; i < now.Length; i++) now[i] -= a[i];
            return now;
        }

        void Monitor(GovMapState st, int now)
        {
            if (run.seenTick < 0 && (run.governor ? st.active : now - run.spawnTick >= 60))
            {
                run.seenTick = now;
                run.eligibleAtSeen.AddRange(ExpectedFighters());
                Kit.Info($"{run.name}: threat seen after {now - run.spawnTick} ticks; eligible fighters [{string.Join(",", run.eligibleAtSeen.Select(p => p.LabelShort))}]");
            }
            if (run.seenTick < 0) return;
            if (!run.governor && now - run.seenTick == ClaudeDelay)
            {
                Tools.Run("draft", new Dictionary<string, object> { ["drafted"] = true });
                Kit.Info($"{run.name}: 'Claude' drafted everyone where they stand, {ClaudeDelay} ticks after the threat appeared");
            }
            if (run.firstShotTick < 0)
                foreach (var p in run.startColonists)
                {
                    if (p.Dead || Delta(p)[0] <= 0) continue;
                    run.firstShotTick = now;
                    var near = Nearest(p.Position);
                    var home = GovernorPositions.HomeCenter(map, GovernorPolicy.Current);
                    run.firstShot = $"{p.LabelShort} fired first at t+{now - run.seenTick} (nearest enemy {(near == null ? "-" : ((int)(near.Position - p.Position).LengthHorizontal).ToString())} cells from him, " +
                                    $"enemy front {(int)Front(home)} cells from home)";
                    Kit.Info($"{run.name}: {run.firstShot}");
                    break;
                }
            if (run.governor)
            {
                if (st.active && st.assign.Count > 0) run.lastRanged.Clear();
                foreach (var kv in st.assign)
                {
                    run.assigned.Add(kv.Key);
                    if (kv.Value.role == GovRole.Ranged) { run.lastRanged.Add(kv.Key); run.everRanged.Add(kv.Key); }
                    if (kv.Key.Position == kv.Value.cell) run.reached.Add(kv.Key);
                }
                foreach (var p in run.assigned)
                    if (p.Downed || p.Dead || st.retreated.Contains(p) || gov.Manual.Contains(p)) run.excluded.Add(p);
                if (!run.draftChecked && now - run.seenTick >= 120)
                {
                    run.draftChecked = true;
                    var exp = run.eligibleAtSeen.Where(p => p.Spawned && !p.Downed && !p.Dead).ToList();
                    var drafted = exp.Count(p => p.Drafted && gov.Drafted.Contains(p));
                    int delay = st.firstDeployTick >= 0 ? st.firstDeployTick - st.startTick : -1;
                    Kit.Check(exp.Count > 0 && drafted == exp.Count && delay >= 0 && delay <= 120,
                        $"{run.name}: governor drafted {drafted}/{exp.Count} eligible fighters within 120 ticks of seeing the threat (deploy after {delay} ticks, mode {st.planMode})");
                }
            }
            if (now >= run.nextTrace)
            {
                run.nextTrace = now + 240;
                Trace(st, now);
            }
            if (!run.forced && now >= run.killAt)
            {
                // who was winning when the clock ran out (sprint 2b counted a slow win at the sandbags as a loss)
                run.oursAtLimit = run.startColonists.Where(p => p.Spawned && !p.Dead && !p.Downed && Violent(p)).Sum(p => 65f * p.health.summaryHealth.SummaryHealthPercent);
                run.theirsAtLimit = run.enemies.Where(e => e.Spawned && !e.Dead && !e.Downed).Sum(Governor.PowerOf);
                run.forcedCount = KillEnemies();
                run.forced = run.forcedCount > 0;
                if (run.forced) Kit.Info($"{run.name}: time limit reached, harness killed {run.forcedCount} enemies still standing");
            }
        }

        Pawn Nearest(IntVec3 c) => run.enemies.Where(e => e.Spawned && !e.Dead && !e.Downed)
            .OrderBy(e => (e.Position - c).LengthHorizontalSquared).FirstOrDefault();

        float Front(IntVec3 home)
        {
            var n = Nearest(home);
            return n == null ? -1f : (n.Position - home).LengthHorizontal;
        }

        // One compact line every 240 ticks: what each colonist is doing and where the enemy is.
        void Trace(GovMapState st, int now)
        {
            var home = GovernorPositions.HomeCenter(map, GovernorPolicy.Current);
            var cols = run.startColonists.Where(p => !p.Dead).Select(p =>
            {
                var near = Nearest(p.Position);
                string stn = st.assign.TryGetValue(p, out var a) ? (p.Position == a.cell ? "@stn" : $"->stn {(int)(a.cell - p.Position).LengthHorizontal}") : "-";
                string los = near == null ? "-" : GovernorPositions.HasLineOfFire(p.Position, near.Position, map) ? "los" : "noLOS";
                var d = Delta(p);
                return $"{p.LabelShort}{(p.Downed ? "(down)" : "")} {p.Position.x},{p.Position.z} {stn} {p.CurJobDef?.defName ?? "-"}" +
                       $"{(p.stances.curStance is Stance_Busy ? " busy" : "")}{(p.Drafted ? " D" : "")}{(p.drafter != null && p.Drafted && !p.drafter.FireAtWill ? " noFire" : "")}" +
                       $" near {(near == null ? "-" : ((int)(near.Position - p.Position).LengthHorizontal).ToString())} {los}" +
                       $" shots {d[0]:0} hp {p.health.summaryHealth.SummaryHealthPercent:P0}";
            });
            var alive = run.enemies.Where(e => e.Spawned && !e.Dead && !e.Downed).ToList();
            var jobs = string.Join(" ", alive.GroupBy(e => e.CurJobDef?.defName ?? "-").Select(g => $"{g.Key}:{g.Count()}"));
            Kit.Info($"TRACE {run.name} t+{now - run.seenTick}{(run.governor ? $" [{st.planMode}{(st.closeCombat ? ", close" : "")}{(st.overwhelmed ? ", overwhelmed" : "")}]" : "")}: " +
                     $"{string.Join(" | ", cols)} || enemies {alive.Count} standing, front {(int)Front(home)} from home, jobs {jobs}");
        }

        List<Pawn> ExpectedFighters() => map.mapPawns.FreeColonistsSpawned.Where(p =>
            !p.Downed && Violent(p) && p.IsColonistPlayerControlled && !p.IsSlave && !gov.Manual.Contains(p) && !p.InBed() &&
            // the governor lets a doctor finish tending a bleeding patient first (BusyCarrying)
            !(p.CurJobDef == JobDefOf.TendPatient && p.CurJob.targetA.Thing is Pawn pt && pt != p && pt.health.hediffSet.BleedRateTotal >= 0.1f) &&
            p.health.summaryHealth.SummaryHealthPercent >= GovernorPolicy.Current.retreatHealthPct &&
            p.health.hediffSet.BleedRateTotal < GovernorPolicy.Current.retreatBleedRate).ToList();

        void Hooks(GovMapState st, int now)
        {
            if (run.seenTick < 0) return;
            switch (step)
            {
                case 4: // C: wound one fighter past the retreat threshold, down another
                    if (!run.hookDone && phase == 1 && (now - run.seenTick >= 300 || run.assigned.Count >= 2 && run.assigned.All(run.reached.Contains)))
                    {
                        var ranked = st.assign.Keys.Where(p => !p.Downed).ToList();
                        if (ranked.Count < 2) { Kit.Check(false, "C: two assigned fighters to wound/down"); run.hookDone = run.hook2Done = true; return; }
                        run.hookDone = true;
                        run.hookTick = now;
                        run.x = ranked[0];
                        run.y = ranked[1];
                        WritePolicy(true, 0.9f); // a high threshold so one or two cuts are enough (no risk of downing him)
                        var legs = run.x.health.hediffSet.GetNotMissingParts().Where(bp => bp.def == BodyPartDefOf.Leg).ToList();
                        for (int i = 0; i < 12 && !run.x.Downed && run.x.health.summaryHealth.SummaryHealthPercent >= 0.86f; i++)
                            run.x.TakeDamage(new DamageInfo(DamageDefOf.Cut, 4f, 0f, -1f, null, legs.Count > 0 ? legs[i % legs.Count] : null));
                        Kit.Down(run.y);
                        Kit.Info($"C: wounded {run.x.LabelShort} to {run.x.health.summaryHealth.SummaryHealthPercent:P0} (downed={run.x.Downed}), downed {run.y.LabelShort}");
                    }
                    // back to 0.5 as soon as he retreated, so the last fighter is not pulled out by a scratch
                    if (run.hookDone && !run.hook2Done && (st.retreated.Contains(run.x) || now - run.hookTick >= 250))
                    {
                        run.hook2Done = true;
                        Kit.Check(!run.x.Drafted && st.retreated.Contains(run.x) && !st.assign.ContainsKey(run.x),
                            $"C: wounded {run.x.LabelShort} retreated after {now - run.hookTick} ticks: undrafted={!run.x.Drafted}, retreated={st.retreated.Contains(run.x)}, job={run.x.CurJobDef?.defName}");
                        WritePolicy(true, 0.5f);
                    }
                    break;
                case 5: // D: down one raider once the threat is seen, so the governor captures him after the fight
                    if (!run.hookDone)
                    {
                        run.hookDone = true;
                        run.x = run.enemies.FirstOrDefault(e => e.Spawned && !e.Dead && st.seen.Contains(e));
                        if (run.x == null) { Kit.Check(false, "D: a seen raider to down"); return; }
                        Kit.Down(run.x);
                        Kit.Info($"D: downed raider {run.x.LabelShort}; prison bed free: {prisonBed != null && !prisonBed.AnyOccupants}");
                    }
                    break;
                case 6: // E: Claude drafts a fighter through the bridge mid-fight
                    if (!run.hookDone && phase == 1 && now - run.seenTick >= 150 && st.assign.Count > 0)
                    {
                        run.hookDone = true;
                        run.z = st.assign.Keys.First();
                        Tools.Run("draft", new Dictionary<string, object> { ["names"] = new List<object> { run.z.LabelShort }, ["drafted"] = true });
                        Kit.Check(gov.Manual.Contains(run.z) && !gov.Drafted.Contains(run.z) && run.z.Drafted,
                            $"E: drafting {run.z.LabelShort} through the draft tool hands him to the player");
                    }
                    break;
            }
        }

        bool PostDone()
        {
            // the condition and its event (written by the governor's next 120-tick pass)
            bool Has(string type) => (GovernorEvents.Since(run.cursor, null, 500)["events"] as List<object>)
                .OfType<Dictionary<string, object>>().Any(e => e["type"] as string == type);
            switch (step)
            {
                case 4: return run.y == null || run.y.InBed() && Has("rescued");
                case 5: return run.x == null || run.x.IsPrisonerOfColony && Has("captured");
                default: return true;
            }
        }

        string Outcome(int now)
        {
            int killedE = run.enemies.Count(e => e.Dead) - run.forcedCount, downedE = run.enemies.Count(e => !e.Dead && e.Downed);
            var per = run.startColonists.Select(p =>
            {
                var d = Delta(p);
                return $"{p.LabelShort}: shots {d[0]:0}, dmg dealt {d[1]:0}, kills {d[2]:0}, downs {d[3]:0}, dmg taken {d[4]:0}, " +
                       $"hp {(p.Dead ? "dead" : p.health.summaryHealth.SummaryHealthPercent.ToString("P0"))}{(p.Downed ? " downed" : "")}";
            });
            float hp = run.startColonists.Sum(p => p.Dead ? 0f : p.health.summaryHealth.SummaryHealthPercent);
            float taken = run.startColonists.Sum(p => Delta(p)[4]);
            int fight = (run.endTick >= 0 ? run.endTick : now) - Math.Max(run.seenTick, run.spawnTick);
            return $"fight {fight} ticks, first shot {(run.firstShotTick >= 0 ? "t+" + (run.firstShotTick - run.seenTick) : "never")}, " +
                   $"enemies {run.enemies.Count}: {killedE} killed / {downedE} downed by colonists, {run.forcedCount} killed by the harness; " +
                   $"colonist hp sum {hp:0.00}/{run.startColonists.Count}, dmg taken {taken:0}, dead {run.startColonists.Count(p => p.Dead)} || {string.Join(" | ", per)}";
        }

        void EndChecks(GovMapState st, int now)
        {
            var name = run.name;
            var summary = Outcome(now);
            Kit.Info($"{name}: OUTCOME {summary}");
            if (!run.governor)
            {
                baselineSummary = summary;
                return;
            }
            bool winning = run.forced && run.oursAtLimit >= 2f * run.theirsAtLimit && run.startColonists.All(p => !p.Dead);
            Kit.Check(!run.forced || winning, $"{name}: colonists won unaided or were clearly winning at the time limit " +
                $"(harness killed {run.forcedCount}; standing power at the limit ours {run.oursAtLimit:0} vs theirs {run.theirsAtLimit:0}, needs 2x)");
            if (run.forced) Kit.Info($"{name}: {(winning ? "winning but slow" : "losing")} at the time limit (killed {run.forcedCount})");
            int died = run.startColonists.Count(p => p.Dead);
            Kit.Check(died == 0, $"{name}: no colonist died ({died})");
            // every pawn ever given a ranged station, unless it went down / retreated / was taken by the player
            // before getting there (the old check used the last assignment only: everyone downed -> "0/0" FAIL)
            var required = run.everRanged.Where(p => run.reached.Contains(p) || !run.excluded.Contains(p)).ToList();
            if (required.Count == 0) Kit.Info($"{name}: no ranged station to check (assigned {run.assigned.Count}, all excluded before arriving)");
            else Kit.Check(required.All(run.reached.Contains),
                $"{name}: fighters reached their stations ({required.Count(run.reached.Contains)}/{required.Count}; assigned {run.assigned.Count})");
            var stillDrafted = map.mapPawns.FreeColonistsSpawned.Where(p => p.Drafted && p != run.z).Select(p => p.LabelShort).ToList();
            var govDrafted = gov.Drafted.Where(p => p.Map == map).Select(p => p.LabelShort).ToList();
            Kit.Check(stillDrafted.Count == 0 && govDrafted.Count == 0,
                $"{name}: governor undrafted everyone afterwards (still drafted: [{string.Join(",", stillDrafted)}], governor-owned: [{string.Join(",", govDrafted)}])");
            var events = GovernorEvents.Since(run.cursor, null, 500)["events"] as List<object>;
            var types = events.OfType<Dictionary<string, object>>().Select(e => e["type"] as string).ToList();
            foreach (var t in new[] { "threat_start", "governor_deploy", "threat_end" })
                Kit.Check(types.Contains(t), $"{name}: events.jsonl has {t}");
            var end = events.OfType<Dictionary<string, object>>().LastOrDefault(e => e["type"] as string == "threat_end");
            Kit.Info($"{name}: threat_end {(end != null ? Json.Write(end) : "(none)")}");
            Kit.Info($"{name}: events [{string.Join(",", types)}]");
            foreach (var e in events.OfType<Dictionary<string, object>>().Where(e => e["type"] as string == "governor_deploy" || e["type"] as string == "decision_needed" || e["type"] as string == "retreat"))
                Kit.Info($"{name}: {Json.Write(e)}");
            switch (step)
            {
                case 2:
                    Kit.Info($"COMPARE raid A, same raiders/loadout/cover:\n  baseline (governor off, late draft): {baselineSummary ?? "(not run)"}\n  governor:                            {summary}");
                    break;
                case 4:
                    if (run.y != null) Kit.Check(run.y.InBed(), $"C: downed {run.y.LabelShort} was rescued to a bed (in bed={run.y.InBed()}, downed={run.y.Downed})");
                    Kit.Check(types.Contains("retreat") && types.Contains("rescued"), "C: events have retreat and rescued");
                    break;
                case 5:
                    if (run.x != null)
                        Kit.Check(run.x.IsPrisonerOfColony, $"D: downed raider {run.x.LabelShort} was captured (prisoner={run.x.IsPrisonerOfColony}, in bed={run.x.InBed()}, dead={run.x.Dead})");
                    Kit.Check(types.Contains("captured"), "D: events have captured");
                    break;
                case 6:
                    // a downed pawn is undrafted by the game itself: only a standing one tells whether the governor let go
                    if (run.z != null && (run.z.Downed || run.z.Dead)) Kit.Info($"E: {run.z.LabelShort} went down: drafted-after check not applicable");
                    else if (run.z != null) Kit.Check(run.z.Drafted, $"E: {run.z.LabelShort}, drafted by Claude mid-fight, is still drafted after the fight");
                    break;
            }
        }

        // ------------------------------------------------------------------ spawning and cleanup

        IntVec3 FindSpawnCell(int dist, Vector2 preferred)
        {
            var home = GovernorPositions.HomeCenter(map, GovernorPolicy.Current);
            var dirs = new List<Vector2> { preferred, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, -1), new Vector2(0, 1), new Vector2(0, -1), new Vector2(-1, 1), new Vector2(-1, -1), new Vector2(-1, 0) };
            var tp = TraverseParms.For(TraverseMode.PassDoors);
            for (int d = dist; d >= 15; d -= 5)
                foreach (var v in dirs)
                {
                    var n = v.normalized;
                    var c = new IntVec3(home.x + Mathf.RoundToInt(n.x * d), 0, home.z + Mathf.RoundToInt(n.y * d));
                    if (!c.InBounds(map)) continue;
                    var cell = CellFinder.StandableCellNear(c, map, 5f, x => !x.Fogged(map) && !x.Roofed(map) && map.reachability.CanReach(x, home, PathEndMode.Touch, tp));
                    if (cell.IsValid) return cell;
                }
            return IntVec3.Invalid;
        }

        // Baseliner raiders (no fire-spewing xenotypes), stripped, then revolvers / knives. Seeded so
        // the baseline and the governor run fight the same raiders.
        void SpawnRaiders(int n, int meleeCount, int dist, Vector2 dir, int seed)
        {
            Rand.PushState(seed);
            try
            {
                var at = FindSpawnCell(dist, dir);
                if (!at.IsValid) return;
                var faction = Find.FactionManager.RandomEnemyFaction(allowHidden: false, allowDefeated: false, allowNonHumanlike: false) ?? Faction.OfPirates;
                if (faction == null) return;
                var kind = faction.def.basicMemberKind ?? PawnKindDefOf.Pirate;
                var revolver = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
                var knife = DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Knife");
                var group = new List<Pawn>();
                for (int i = 0; i < n; i++)
                {
                    var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, mustBeCapableOfViolence: true,
                        forcedXenotype: ModsConfig.BiotechActive ? XenotypeDefOf.Baseliner : null));
                    p.equipment?.DestroyAllEquipment();
                    p.inventory?.DestroyAll();
                    var w = i < meleeCount ? knife : revolver;
                    if (w != null) p.equipment?.AddEquipment((ThingWithComps)ThingMaker.MakeThing(w, w.MadeFromStuff ? ThingDefOf.Steel : null));
                    var cell = CellFinder.StandableCellNear(at, map, 4f, x => !x.Fogged(map) && x.GetFirstPawn(map) == null);
                    GenSpawn.Spawn(p, cell.IsValid ? cell : at, map);
                    group.Add(p);
                }
                LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false,
                    useAvoidGridSmart: false, canSteal: false), map, group);
                run.enemies.AddRange(group);
            }
            finally { Rand.PopState(); }
        }

        void SpawnManhunters(int n, int dist, Vector2 dir, int seed)
        {
            Rand.PushState(seed);
            try
            {
                var at = FindSpawnCell(dist, dir);
                if (!at.IsValid) return;
                var kind = new[] { "Wolf_Timber", "WildBoar", "Warg", "Wolf_Arctic" }.Select(k => DefDatabase<PawnKindDef>.GetNamedSilentFail(k)).FirstOrDefault(k => k != null)
                           ?? PawnKindDefOf.Muffalo;
                for (int i = 0; i < n; i++)
                {
                    var p = PawnGenerator.GeneratePawn(kind, null);
                    var cell = CellFinder.StandableCellNear(at, map, 4f, x => !x.Fogged(map) && x.GetFirstPawn(map) == null);
                    GenSpawn.Spawn(p, cell.IsValid ? cell : at, map);
                    p.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter, forced: true, forceWake: true);
                    run.enemies.Add(p);
                }
            }
            finally { Rand.PopState(); }
        }

        int KillEnemies()
        {
            int n = 0;
            foreach (var e in run.enemies)
                if (e.Spawned && !e.Dead && !e.Downed) { e.Kill(null); n++; }
            return n;
        }

        // Between scenarios: remove enemies, prisoners and fires; heal, feed, rest and undraft the
        // colonists; same rifle again; back to the start cells; governor bookkeeping cleared.
        void Restore()
        {
            if (run != null)
                foreach (var e in run.enemies)
                {
                    if (e.Dead) { if (e.Corpse != null && e.Corpse.Spawned) e.Corpse.Destroy(); }
                    else if (!e.Destroyed) e.Destroy();
                }
            foreach (var p in map.mapPawns.PrisonersOfColonySpawned.ToList()) p.Destroy();
            foreach (var f in map.listerThings.ThingsOfDef(ThingDefOf.Fire).ToList()) if (!f.Destroyed) f.Destroy();
            gov.Drafted.Clear(); // the harness undrafts below; do not let the governor read that as a player override
            gov.Manual.Clear();
            int k = 0;
            foreach (var p in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                foreach (var h in p.health.hediffSet.hediffs.ToList())
                    if (h is Hediff_Injury || h is Hediff_MissingPart || h.def == HediffDefOf.BloodLoss ||
                        h.def == HediffDefOf.Hypothermia || h.def == HediffDefOf.Heatstroke) p.health.RemoveHediff(h);
                if (p.InMentalState) p.MentalState.RecoverFromState();
                if (p.needs?.food != null) p.needs.food.CurLevelPercentage = 1f;
                if (p.needs?.rest != null) p.needs.rest.CurLevelPercentage = 1f;
                if (p.needs?.joy != null) p.needs.joy.CurLevelPercentage = 1f;
                if (p.Drafted) p.drafter.Drafted = false;
                Kit.Teleport(p, standCells[k % standCells.Count]);
                Arm(p, 500 + k);
                k++;
            }
        }
    }
}
