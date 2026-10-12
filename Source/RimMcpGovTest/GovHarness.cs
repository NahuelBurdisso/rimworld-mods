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
    /// Drives the RimMcp governor through real fights during `rw launch --ticks 12000` (quicktest):
    /// A raid, B manhunters, C retreat + rescue, D capture, E manual-draft respect.
    /// Test setup uses dev-style spawning; the governor itself only drafts and gives orders.
    /// </summary>
    public class GovHarness : GameComponent
    {
        const string Mod = "RimMcp/Governor";
        bool armed, done;
        int ticks, step, phase;
        Map map;
        Governor gov;
        IntVec3 origin;
        Building_Bed prisonBed;
        readonly List<IntVec3> standCells = new List<IntVec3>();
        string originalPolicy;
        Run run;

        class Run
        {
            public string name;
            public long cursor;
            public int spawnTick, seenTick = -1, endTick = -1, killAt, guardAt, postMin, postMax;
            public readonly List<Pawn> enemies = new List<Pawn>();
            public readonly HashSet<Pawn> assigned = new HashSet<Pawn>(), reached = new HashSet<Pawn>(), excluded = new HashSet<Pawn>();
            public readonly List<Pawn> lastRanged = new List<Pawn>(); // shooters assigned at the last tick of the fight
            public readonly List<Pawn> startColonists = new List<Pawn>();
            public bool draftChecked, forced, hookDone, hook2Done;
            public int hookTick;
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
                else if (step >= 1 && step <= 5) Scenario();
                if (!done && ticks >= 11900)
                {
                    Kit.Check(false, $"harness finished before 11900 ticks (stuck in scenario {step}, phase {phase})");
                    Finish();
                }
            }
            catch (Exception ex)
            {
                Kit.Check(false, "harness threw: " + ex);
                Finish();
            }
        }

        void Finish()
        {
            done = true;
            try
            {
                if (originalPolicy != null) File.WriteAllText(GovernorPolicy.FilePath, originalPolicy);
                Tools.Run("governor", new Dictionary<string, object> { ["action"] = "reload" });
            }
            catch (Exception e) { Kit.Info("could not restore governor.json: " + e.Message); }
            Kit.Info("events file: " + GovernorEvents.FilePath);
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
            foreach (var p in map.mapPawns.FreeColonistsSpawned.ToList()) Arm(p);
            originalPolicy = File.Exists(GovernorPolicy.FilePath) ? File.ReadAllText(GovernorPolicy.FilePath) : null;
            WritePolicy(0.5f);
            Kit.Check(GovernorPolicy.Current.enabled, "governor enabled through governor.json + the governor tool (reload)");
            var status = Tools.Run("governor", new Dictionary<string, object> { ["action"] = "status" }) as Dictionary<string, object>;
            Kit.Check(status != null && status["policyPath"] as string == GovernorPolicy.FilePath, "governor status prints the policy path: " + GovernorPolicy.FilePath);
            var home = GovernorPositions.HomeCenter(map, GovernorPolicy.Current);
            Kit.Info($"base at {origin}, home center {home}, map {map.Size}, colonists: " +
                     string.Join(", ", map.mapPawns.FreeColonistsSpawned.Select(p => $"{p.LabelShort}({p.equipment?.Primary?.def.defName ?? "unarmed"}{(p.WorkTagIsDisabled(WorkTags.Violent) ? ", non-violent" : "")})")));
            step = 1;
            phase = 0;
        }

        void WritePolicy(float retreatPct)
        {
            File.WriteAllText(GovernorPolicy.FilePath,
                "{\"enabled\": true, \"combat\": {\"autoDraft\": true, \"retreatHealthPct\": " + retreatPct.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ", \"retreatBleedRate\": 0.8, \"undraftAfterQuietTicks\": 300, \"engageMeleeRadius\": 5, \"defendIndoorsIfRatioAbove\": 2.5}," +
                " \"rescue\": {\"downedColonists\": true, \"captureDowned\": true}, \"nonCombatants\": {\"shelter\": true}, \"events\": {\"maxFileKb\": 512}}\n");
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

        bool FindBaseSpot(out IntVec3 result)
        {
            var cols = map.mapPawns.FreeColonistsSpawned;
            var center = cols.Count > 0 ? new IntVec3((int)cols.Average(p => p.Position.x), 0, (int)cols.Average(p => p.Position.z)) : map.Center;
            foreach (var c in GenRadial.RadialCellsAround(center, 40f, true))
            {
                var o = c - new IntVec3(8, 0, 6);
                var rect = new CellRect(o.x, o.z, 16, 12);
                if (!rect.InBounds(map) || rect.minX < 5 || rect.minZ < 5 || rect.maxX > map.Size.x - 6 || rect.maxZ > map.Size.z - 6) continue;
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
            Kit.Check(beds >= 4 && prisonBed != null && prisonBed.ForPrisoners && prisonBed.Position.IsInPrisonCell(map), $"test base built: {beds} beds, prison bed {(prisonBed != null ? prisonBed.Position.ToString() : "missing")}");
        }

        Thing Place(ThingDef def, IntVec3 c, Rot4 rot)
        {
            var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            thing.SetFaction(Faction.OfPlayer);
            return GenSpawn.Spawn(thing, c, map, rot);
        }

        void Arm(Pawn p)
        {
            if (p.equipment == null || !Violent(p) || Governor.IsRanged(p)) return;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(p.skills?.GetSkill(SkillDefOf.Shooting)?.Level >= 6 ? "Gun_BoltActionRifle" : "Gun_Revolver")
                      ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
            if (def == null) return;
            p.equipment.DestroyAllEquipment();
            p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(def));
        }

        // ------------------------------------------------------------------ scenarios

        static readonly string[] Names = { "", "A raid", "B manhunters", "C retreat+rescue", "D capture", "E manual draft" };

        void Scenario()
        {
            int now = Find.TickManager.TicksGame;
            var st = gov.StateFor(map);
            switch (phase)
            {
                case 0:
                    run = new Run { name = Names[step], cursor = GovernorEvents.Seq, spawnTick = now };
                    run.startColonists.AddRange(map.mapPawns.FreeColonistsSpawned);
                    switch (step)
                    {
                        case 1: SpawnRaiders(4, 1, 34); run.killAt = now + 1300; run.postMin = 480; run.postMax = 480; break;
                        case 2: SpawnManhunters(3, 34); run.killAt = now + 1200; run.postMin = 480; run.postMax = 480; break;
                        case 3: SpawnRaiders(3, 0, 34); run.killAt = now + 1300; run.postMin = 480; run.postMax = 1200; break;
                        case 4: SpawnRaiders(2, 0, 25); run.killAt = now + 1000; run.postMin = 480; run.postMax = 1500; break;
                        case 5: SpawnRaiders(2, 0, 30); run.killAt = now + 1000; run.postMin = 480; run.postMax = 480; break;
                    }
                    run.guardAt = run.killAt + 900;
                    Kit.Info($"{run.name}: spawned {run.enemies.Count} {string.Join(",", run.enemies.Select(e => e.KindLabel).Distinct())} at {run.enemies.FirstOrDefault()?.Position}");
                    if (run.enemies.Count == 0) { Kit.Check(false, run.name + ": could not spawn enemies"); NextScenario(); return; }
                    phase = 1;
                    break;
                case 1:
                    Monitor(st, now);
                    Hooks(st, now);
                    if (run.seenTick >= 0 && !st.active)
                    {
                        run.endTick = now;
                        phase = 2;
                    }
                    else if (now >= run.guardAt)
                    {
                        Kit.Check(false, $"{run.name}: threat ended (seen={run.seenTick >= 0}, active={st.active})");
                        KillEnemies();
                        run.endTick = now;
                        phase = 2;
                    }
                    break;
                case 2:
                    Hooks(st, now);
                    int post = now - run.endTick;
                    if (post >= run.postMax || post >= run.postMin && post % 30 == 0 && PostDone()) { EndChecks(st); NextScenario(); }
                    break;
            }
        }

        void NextScenario()
        {
            Restore();
            step++;
            phase = 0;
            if (step > 5) Finish();
        }

        void Monitor(GovMapState st, int now)
        {
            if (run.seenTick < 0 && st.active)
            {
                run.seenTick = now;
                Kit.Info($"{run.name}: threat seen after {now - run.spawnTick} ticks");
            }
            if (run.seenTick < 0) return;
            if (st.active && st.assign.Count > 0) run.lastRanged.Clear();
            foreach (var kv in st.assign)
            {
                run.assigned.Add(kv.Key);
                if (kv.Value.role == GovRole.Ranged) run.lastRanged.Add(kv.Key);
                if (kv.Key.Position == kv.Value.cell) run.reached.Add(kv.Key);
            }
            foreach (var p in run.assigned)
                if (p.Downed || p.Dead || st.retreated.Contains(p) || gov.Manual.Contains(p)) run.excluded.Add(p);
            if (!run.draftChecked && now - run.seenTick >= 120)
            {
                run.draftChecked = true;
                var exp = ExpectedFighters();
                var drafted = exp.Count(p => p.Drafted && gov.Drafted.Contains(p));
                int delay = st.firstDeployTick >= 0 ? st.firstDeployTick - st.startTick : -1;
                Kit.Check(exp.Count > 0 && drafted == exp.Count && delay >= 0 && delay <= 120,
                    $"{run.name}: governor drafted {drafted}/{exp.Count} fighters within 120 ticks of seeing the threat (deploy after {delay} ticks, mode {st.planMode})");
            }
            if (!run.forced && now >= run.killAt)
            {
                int n = KillEnemies();
                run.forced = true;
                if (n > 0) Kit.Info($"{run.name}: harness killed {n} enemies still standing at the time limit");
            }
        }

        List<Pawn> ExpectedFighters() => map.mapPawns.FreeColonistsSpawned.Where(p =>
            !p.Downed && Violent(p) && p.IsColonistPlayerControlled && !p.IsSlave && !gov.Manual.Contains(p) &&
            p.health.summaryHealth.SummaryHealthPercent >= GovernorPolicy.Current.retreatHealthPct &&
            p.health.hediffSet.BleedRateTotal < GovernorPolicy.Current.retreatBleedRate).ToList();

        void Hooks(GovMapState st, int now)
        {
            if (run.seenTick < 0) return;
            switch (step)
            {
                case 3: // C: wound one fighter past the retreat threshold, down another
                    if (!run.hookDone && phase == 1 && (now - run.seenTick >= 300 || run.assigned.Count >= 2 && run.assigned.All(run.reached.Contains)))
                    {
                        var ranked = st.assign.Keys.Where(p => !p.Downed).ToList();
                        if (ranked.Count < 2) { Kit.Check(false, "C: two assigned fighters to wound/down"); run.hookDone = run.hook2Done = true; return; }
                        run.hookDone = true;
                        run.hookTick = now;
                        run.x = ranked[0];
                        run.y = ranked[1];
                        WritePolicy(0.9f); // a high threshold so one or two cuts are enough (no risk of downing him)
                        var legs = run.x.health.hediffSet.GetNotMissingParts().Where(bp => bp.def == BodyPartDefOf.Leg).ToList();
                        for (int i = 0; i < 12 && !run.x.Downed && run.x.health.summaryHealth.SummaryHealthPercent >= 0.86f; i++)
                            run.x.TakeDamage(new DamageInfo(DamageDefOf.Cut, 4f, 0f, -1f, null, legs.Count > 0 ? legs[i % legs.Count] : null));
                        Kit.Down(run.y);
                        Kit.Info($"C: wounded {run.x.LabelShort} to {run.x.health.summaryHealth.SummaryHealthPercent:P0} (downed={run.x.Downed}), downed {run.y.LabelShort}");
                    }
                    if (run.hookDone && !run.hook2Done && now - run.hookTick >= 250)
                    {
                        run.hook2Done = true;
                        Kit.Check(!run.x.Drafted && st.retreated.Contains(run.x) && !st.assign.ContainsKey(run.x),
                            $"C: wounded {run.x.LabelShort} retreated: undrafted={!run.x.Drafted}, retreated={st.retreated.Contains(run.x)}, job={run.x.CurJobDef?.defName}");
                        WritePolicy(0.5f);
                    }
                    break;
                case 4: // D: down one raider once the threat is seen, so the governor captures him after the fight
                    if (!run.hookDone)
                    {
                        run.hookDone = true;
                        run.x = run.enemies.FirstOrDefault(e => e.Spawned && !e.Dead && st.seen.Contains(e));
                        if (run.x == null) { Kit.Check(false, "D: a seen raider to down"); return; }
                        Kit.Down(run.x);
                        Kit.Info($"D: downed raider {run.x.LabelShort}; prison bed free: {prisonBed != null && !prisonBed.AnyOccupants}");
                    }
                    break;
                case 5: // E: Claude drafts a fighter through the bridge mid-fight
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
                case 3: return run.y == null || run.y.InBed() && Has("rescued");
                case 4: return run.x == null || run.x.IsPrisonerOfColony && Has("captured");
                default: return true;
            }
        }

        void EndChecks(GovMapState st)
        {
            var name = run.name;
            int died = run.startColonists.Count(p => p.Dead);
            Kit.Check(died == 0, $"{name}: no colonist died ({died})");
            // shooters still on station duty at the end (helpers pulled off for a rescue and melee
            // pawns that went to fight are not expected to stand on a cell)
            var required = run.lastRanged.Where(p => !run.excluded.Contains(p)).ToList();
            Kit.Check(required.Count > 0 && required.All(run.reached.Contains),
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
            Kit.Info($"{name}: outcome {(end != null ? Json.Write(end) : "(no threat_end)")}");
            Kit.Info($"{name}: event types [{string.Join(",", types)}]");
            Kit.Info($"{name}: colonists " + string.Join(", ", run.startColonists.Select(p =>
                $"{p.LabelShort}={(p.Dead ? "dead" : p.health.summaryHealth.SummaryHealthPercent.ToString("P0"))}{(p.Downed ? " downed" : "")}{(p.InBed() ? " in bed" : "")}")));
            switch (step)
            {
                case 3:
                    if (run.y != null) Kit.Check(run.y.InBed(), $"C: downed {run.y.LabelShort} was rescued to a bed (in bed={run.y.InBed()}, downed={run.y.Downed})");
                    Kit.Check(types.Contains("retreat") && types.Contains("rescued"), "C: events have retreat and rescued");
                    break;
                case 4:
                    if (run.x != null)
                        Kit.Check(run.x.IsPrisonerOfColony, $"D: downed raider {run.x.LabelShort} was captured (prisoner={run.x.IsPrisonerOfColony}, in bed={run.x.InBed()}, dead={run.x.Dead})");
                    Kit.Check(types.Contains("captured"), "D: events have captured");
                    break;
                case 5:
                    if (run.z != null) Kit.Check(run.z.Drafted, $"E: {run.z.LabelShort}, drafted by Claude mid-fight, is still drafted after the fight");
                    break;
            }
        }

        // ------------------------------------------------------------------ spawning and cleanup

        IntVec3 FindSpawnCell(int dist)
        {
            var home = GovernorPositions.HomeCenter(map, GovernorPolicy.Current);
            var dirs = new[] { new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, -1), new Vector2(0, 1), new Vector2(0, -1), new Vector2(-1, 1), new Vector2(-1, -1), new Vector2(-1, 0) };
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

        void SpawnRaiders(int n, int armedCount, int dist)
        {
            var at = FindSpawnCell(dist);
            if (!at.IsValid) return;
            var faction = Find.FactionManager.RandomEnemyFaction(allowHidden: false, allowDefeated: false, allowNonHumanlike: false) ?? Faction.OfPirates;
            if (faction == null) return;
            var kind = faction.def.basicMemberKind ?? PawnKindDefOf.Pirate;
            var revolver = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
            for (int i = 0; i < n; i++)
            {
                var p = PawnGenerator.GeneratePawn(kind, faction);
                p.equipment?.DestroyAllEquipment();
                p.inventory?.DestroyAll();
                if (i < armedCount && revolver != null) p.equipment?.AddEquipment((ThingWithComps)ThingMaker.MakeThing(revolver));
                var cell = CellFinder.StandableCellNear(at, map, 4f, x => !x.Fogged(map) && x.GetFirstPawn(map) == null);
                GenSpawn.Spawn(p, cell.IsValid ? cell : at, map);
                run.enemies.Add(p);
            }
            LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false,
                useAvoidGridSmart: false, canSteal: false), map, run.enemies);
        }

        void SpawnManhunters(int n, int dist)
        {
            var at = FindSpawnCell(dist);
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

        int KillEnemies()
        {
            int n = 0;
            foreach (var e in run.enemies)
                if (e.Spawned && !e.Dead && !e.Downed) { e.Kill(null); n++; }
            return n;
        }

        // Between scenarios: remove the enemies and prisoners, heal and undraft colonists, put them back.
        void Restore()
        {
            if (run != null)
                foreach (var e in run.enemies)
                {
                    if (e.Dead) { if (e.Corpse != null && e.Corpse.Spawned) e.Corpse.Destroy(); }
                    else if (!e.Destroyed) e.Destroy();
                }
            foreach (var p in map.mapPawns.PrisonersOfColonySpawned.ToList()) p.Destroy();
            gov.Drafted.Clear(); // the harness undrafts below; do not let the governor read that as a player override
            gov.Manual.Clear();
            int k = 0;
            foreach (var p in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                foreach (var h in p.health.hediffSet.hediffs.ToList())
                    if (h is Hediff_Injury || h is Hediff_MissingPart || h.def == HediffDefOf.BloodLoss) p.health.RemoveHediff(h);
                if (p.Drafted) p.drafter.Drafted = false;
                Kit.Teleport(p, standCells[k++ % standCells.Count]);
                Arm(p);
            }
        }
    }
}
