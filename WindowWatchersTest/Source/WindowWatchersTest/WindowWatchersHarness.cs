using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using WindowWatchers;

namespace WindowWatchersTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N`. Builds a 7x7 roofed wooden room with one door on the east wall in an open
    /// area. Inside: child C (parent: M), unrelated child N, healthy adult H (lover of M), elder E (spouse of M2).
    /// Outside, east of the door: M drafted. A disarmed raider R held far away makes the threat.
    /// Rules checked: no threat -> nothing; threat -> C watches M from the inside cell by the door, door ajar, bubble with
    /// arrow; N / H do not; hungry -> no job; M hurt -> memory + trembling; enemy close -> stops; no line of sight ->
    /// no spot; M down -> memory; E watches M2, threat ends -> relief + embrace (C gets no relief); new threat, M2 dies
    /// while watched -> memory.
    /// </summary>
    public class WindowWatchersHarness : GameComponent
    {
        bool active;
        int ticks;
        int step = -1;
        int stepStart;

        Map map;
        IntVec3 o, spot, mEast, mNorth, rFar;
        Building_Door door;
        Pawn m, c, n, h, e, m2, r, r2;
        bool nEverWatched, hEverWatched, eSawReunite;

        public WindowWatchersHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        MapComponent_WindowWatchers Comp => MapComponent_WindowWatchers.For(map);

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
            catch (Exception ex)
            {
                Kit.Check(false, "harness exception at step " + step + ": " + ex);
                Finish();
            }
        }

        // ---------- setup ----------

        bool RectClear(IntVec3 origin)
        {
            var rect = new CellRect(origin.x - 3, origin.z - 3, 20, 20);
            foreach (IntVec3 x in rect)
            {
                if (!x.InBounds(map) || !x.Standable(map) || x.Fogged(map) || x.GetEdifice(map) != null || x.Roofed(map)
                    || x.GetFirstPawn(map) != null) return false;
            }
            return true;
        }

        /// <summary>Nearest clear area to the map center, scanning the whole map on a 4-cell grid.</summary>
        bool FindArea(out IntVec3 origin)
        {
            origin = IntVec3.Invalid;
            float best = float.MaxValue;
            IntVec3 center = map.Center;
            for (int x = 4; x < map.Size.x - 20; x += 4)
                for (int z = 4; z < map.Size.z - 20; z += 4)
                {
                    var c = new IntVec3(x, 0, z);
                    float d = (c - center).LengthHorizontalSquared;
                    if (d >= best || !RectClear(c)) continue;
                    best = d;
                    origin = c;
                }
            if (origin.IsValid) return true;

            // no naturally clear area on this map: take the least cluttered one with no pawns and clear it by hand
            int bestBlocked = int.MaxValue;
            for (int x = 4; x < map.Size.x - 24; x += 4)
                for (int z = 4; z < map.Size.z - 24; z += 4)
                {
                    var rect = new CellRect(x - 3, z - 3, 20, 20);
                    int blocked = 0;
                    bool pawn = false;
                    foreach (IntVec3 cell in rect)
                    {
                        if (cell.GetFirstPawn(map) != null) { pawn = true; break; }
                        if (!cell.Standable(map) || cell.GetEdifice(map) != null || cell.Roofed(map) || cell.Fogged(map)) blocked++;
                    }
                    if (pawn || blocked >= bestBlocked) continue;
                    bestBlocked = blocked;
                    origin = new IntVec3(x, 0, z);
                }
            if (!origin.IsValid) return false;
            Log.Message($"[Test] no clear area: clearing {bestBlocked} blocked cells by hand at {origin}");
            foreach (IntVec3 cell in new CellRect(origin.x - 3, origin.z - 3, 20, 20))
            {
                foreach (Thing t in cell.GetThingList(map).ToList())
                    if (!(t is Pawn)) t.Destroy();
                if (!cell.Standable(map)) map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                map.roofGrid.SetRoof(cell, null);
                if (cell.Fogged(map)) map.fogGrid.Unfog(cell);
            }
            return RectClear(origin);
        }

        Pawn Gen(float age, DevelopmentalStage stage, bool violent, Gender g, IntVec3 cell)
        {
            var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: violent,
                prohibitedTraits: new[] { TraitDefOf.Psychopath }, fixedBiologicalAge: age, fixedChronologicalAge: age,
                fixedGender: g, developmentalStages: stage, forceNoGear: true);
            Pawn p = PawnGenerator.GeneratePawn(req);
            GenSpawn.Spawn(p, cell, map);
            p.equipment?.DestroyAllEquipment();
            if (p.needs?.food != null) p.needs.food.CurLevelPercentage = 1f;
            if (p.needs?.rest != null) p.needs.rest.CurLevel = 1f;
            if (p.timetable != null) for (int hr = 0; hr < 24; hr++) p.timetable.SetAssignment(hr, TimeAssignmentDefOf.Anything);
            return p;
        }

        void Setup()
        {
            map = Find.CurrentMap;
            if (!ModsConfig.BiotechActive) { Kit.Check(false, "Biotech needed for child pawns"); Finish(); return; }
            Log.Message($"[Test] map: biome {map.Biome.defName}, size {map.Size}");
            if (!FindArea(out o))
            { Kit.Check(false, "found a clear 20x20 area for the test room"); Finish(); return; }
            // items in the way of the walls would block them
            foreach (IntVec3 x in new CellRect(o.x, o.z, 7, 7))
                foreach (Thing t in x.GetThingList(map).ToList())
                    if (t.def.category == ThingCategory.Item || t.def.category == ThingCategory.Plant) t.Destroy();

            for (int x = 0; x <= 6; x++)
                for (int z = 0; z <= 6; z++)
                {
                    IntVec3 cell = o + new IntVec3(x, 0, z);
                    bool border = x == 0 || z == 0 || x == 6 || z == 6;
                    if (!border) { map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed); continue; }
                    Thing b = ThingMaker.MakeThing(x == 6 && z == 3 ? ThingDefOf.Door : ThingDefOf.Wall, ThingDefOf.WoodLog);
                    b.SetFaction(Faction.OfPlayer);
                    GenSpawn.Spawn(b, cell, map);
                    if (b is Building_Door d) door = d;
                }
            spot = o + new IntVec3(5, 0, 3);
            mEast = o + new IntVec3(14, 0, 3);
            mNorth = o + new IntVec3(3, 0, 15);
            Room room = spot.GetRoom(map);
            Kit.Check(door != null && WatchSight.IsIndoorRoom(room) && spot.Roofed(map), "setup: roofed indoor room with a door built");

            m = Gen(35f, DevelopmentalStage.Adult, true, Gender.Female, mEast);
            c = Gen(8f, DevelopmentalStage.Child, false, Gender.Female, o + new IntVec3(2, 0, 2));
            n = Gen(9f, DevelopmentalStage.Child, false, Gender.Male, o + new IntVec3(2, 0, 4));
            h = Gen(30f, DevelopmentalStage.Adult, true, Gender.Male, o + new IntVec3(3, 0, 3));
            e = Gen(76f, DevelopmentalStage.Adult, false, Gender.Male, o + new IntVec3(4, 0, 5));
            m2 = Gen(40f, DevelopmentalStage.Adult, true, Gender.Female, o + new IntVec3(10, 0, 8));
            c.relations.AddDirectRelation(PawnRelationDefOf.Parent, m);
            h.relations.AddDirectRelation(PawnRelationDefOf.Lover, m);
            e.relations.AddDirectRelation(PawnRelationDefOf.Spouse, m2);
            e.workSettings?.EnableAndInitializeIfNotAlreadyInitialized();
            e.workSettings?.Disable(WorkTypeDefOf.Doctor);
            m.drafter.Drafted = true;

            Kit.Check(WatcherUtility.IsNonCombatant(c) && WatcherUtility.IsNonCombatant(n) && WatcherUtility.IsNonCombatant(e),
                "setup: child C, child N and elder E (76) count as non-combatants");
            Kit.Check(!WatcherUtility.IsNonCombatant(h), "setup: healthy violent adult H (30) is not a non-combatant");
            Kit.Check(WatcherUtility.BondScore(c, m) == WatcherUtility.FamilyScore && WatcherUtility.BondScore(n, m) == 0
                && WatcherUtility.BondScore(e, m2) == WatcherUtility.PartnerScore, "setup: bonds C->M family, E->M2 partner, N->M none");

            if (!CellFinder.TryFindRandomCellNear(o, map, 90, x => x.Standable(map) && !x.Fogged(map) && x.GetFirstPawn(map) == null
                    && x.DistanceTo(o) >= 45f, out rFar))
            { Kit.Check(false, "found a far cell for the raider"); Finish(); return; }
            Go(1);
        }

        // ---------- helpers ----------

        void Go(int s) { step = s; stepStart = ticks; }
        int InStep => ticks - stepStart;

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
                wait.expiryInterval = 3000;
                p.jobs.StartJob(wait, JobCondition.InterruptForced);
            }
        }

        static JobDriver_WatchLovedOne Watch(Pawn p) => p.jobs?.curDriver as JobDriver_WatchLovedOne;
        static bool WatchingAt(Pawn p) => Watch(p) is JobDriver_WatchLovedOne d && d.Watching && p.Position == d.Spot;

        static bool HasMem(Pawn p, ThoughtDef def, Pawn other) =>
            p.needs?.mood?.thoughts?.memories?.Memories.Any(x => x.def == def && x.otherPawn == other) == true;

        string Diag(Pawn p)
        {
            string s = $"{p.LabelShort}: job={p.CurJobDef?.defName} pos={p.Position - o} threat={Comp?.ThreatActive} plan={Comp?.TryGetPlan(p, out _)}"
                + $" watcher={WatcherUtility.CanBeWatcher(p)} needsOk={WatcherUtility.NeedsAllowWatching(p, Comp?.PatientsWaiting ?? false)}"
                + $" patients={Comp?.PatientsWaiting} region={p.GetRegion()?.id} room={p.GetRoom()?.ID} indoor={WatchSight.IsIndoorRoom(p.GetRoom())}"
                + $" doorForbidden={door?.IsForbiddenToPass(p)}";
            Pawn t = p == c ? m : p == e ? m2 : null;
            if (t != null && t.Spawned)
            {
                WatchSpot sp = WatchSight.FindSpot(p, t, null);
                s += $" fighter={WatcherUtility.IsFighter(t, map)} bond={WatcherUtility.BondScore(p, t)} findSpot={(sp.IsValid ? (sp.cell - o).ToString() : "none")}"
                    + $" seeFromSpot={WatchSight.CanSee(spot, t.Position, map, door)} safe={WatchSight.IsSafe(spot, map, door)} reach={p.CanReach(spot, PathEndMode.OnCell, Danger.Some)}";
            }
            return s;
        }

        void Timeout(int limit, string what)
        {
            if (InStep <= limit) return;
            Kit.Check(false, what + " (timed out) - " + Diag(c) + " / " + Diag(e));
            Finish();
        }

        void KeepFed()
        {
            foreach (Pawn p in new[] { c, n, h, e })
            {
                if (p == null || p.Dead) continue;
                if (p.needs?.food != null && p.needs.food.CurLevelPercentage < 0.6f) p.needs.food.CurLevelPercentage = 1f;
                if (p.needs?.rest != null && p.needs.rest.CurLevel < 0.6f) p.needs.rest.CurLevel = 1f;
            }
        }

        // ---------- steps ----------

        void Poll()
        {
            KeepFed();
            if (r != null && step < 10) Hold(r, step == 4 ? r.Position : rFar);
            if (r2 != null && step >= 11) Hold(r2, r2.Position);
            if (n.CurJobDef == WWDefOf.WW_Watch) nEverWatched = true;
            if (h.CurJobDef == WWDefOf.WW_Watch) hEverWatched = true;

            switch (step)
            {
                case 1:
                    if (InStep < 300) return;
                    Kit.Check(!Comp.ThreatActive && c.CurJobDef != WWDefOf.WW_Watch && JobGiver_WatchLovedOne.TryMakeJob(c) == null,
                        "no threat: no threat flag, C does not watch, giver gives nothing - " + Diag(c));
                    // start C outside, just north of the house (run 5 failed from there): she has to walk around to the door
                    Kit.Teleport(c, CellFinder.StandableCellNear(o + new IntVec3(4, 0, 8), map, 1f));
                    r = Hostile(rFar);
                    Go(2);
                    return;

                case 2:
                    if (WatchingAt(c))
                    {
                        JobDriver_WatchLovedOne d = Watch(c);
                        Kit.Check(Comp.ThreatActive, "threat: raider makes the threat flag active");
                        Kit.Check(d.Target == m, $"C, who was outside north of the house, comes in and watches her mother M (after {InStep} ticks)");
                        Kit.Check(d.Spot == spot && d.Door == door, $"C stands on the inside cell next to the door and sees M through it (spot {d.Spot - o})");
                        Kit.Check(d.Spot.Roofed(map) && WatchSight.IsIndoorRoom(d.Spot.GetRoom(map)), "watch spot is roofed and indoors");
                        Kit.Check(door.Open, "door is kept ajar while watching");
                        Kit.Check(d.Mote != null && d.Mote.Spawned && d.Mote.arrowTarget == m, "worry bubble over C with the arrow pointing at M");
                        Kit.Check(c.Position.DistanceTo(rFar) > WatchSight.HostileSightRadius, "watch spot is far from the enemy");
                        Kit.Check(!nEverWatched && n.CurJobDef != WWDefOf.WW_Watch && JobGiver_WatchLovedOne.TryMakeJob(n) == null,
                            "negative: unrelated child N does not watch M");
                        Kit.Check(!hEverWatched && h.CurJobDef != WWDefOf.WW_Watch && JobGiver_WatchLovedOne.TryMakeJob(h) == null,
                            "negative: M's lover H, a healthy fighter-capable adult, does not watch");
                        float food = c.needs.food.CurLevelPercentage;
                        c.needs.food.CurLevelPercentage = 0.05f;
                        Kit.Check(JobGiver_WatchLovedOne.TryMakeJob(c) == null, "negative: hungry C gets no watch job (food first)");
                        c.needs.food.CurLevelPercentage = food;
                        Kit.Check(JobGiver_WatchLovedOne.TryMakeJob(c) != null, "fed again: giver hands the watch job (continuation)");
                        m.TakeDamage(new DamageInfo(DamageDefOf.Cut, 5f, 2f, -1f, r));
                        Go(3);
                        return;
                    }
                    Timeout(2000, "C starts watching M");
                    return;

                case 3:
                    if (HasMem(c, WWDefOf.WW_WatchedHurt, m))
                    {
                        JobDriver_WatchLovedOne d = Watch(c);
                        Kit.Check(true, "M hurt: C gains 'watched M get hurt'");
                        Kit.Check(d?.Mote != null && d.Mote.Shaking && d.Mote.instanceColor.g < 0.9f, "M hurt: the bubble trembles and turns red");
                        Kit.Check(c.CurJobDef == WWDefOf.WW_Watch, "M hurt but standing: C keeps watching");
                        Kit.Teleport(r, CellFinder.StandableCellNear(o + new IntVec3(-5, 0, 3), map, 2f));
                        Hold(r, r.Position);
                        Go(4);
                        return;
                    }
                    Timeout(240, "C notices M getting hurt");
                    return;

                case 4:
                    if (c.CurJobDef != WWDefOf.WW_Watch)
                    {
                        Kit.Check(true, $"enemy {r.Position.DistanceTo(spot):F0} cells from the spot: C stops watching");
                        Kit.Teleport(r, rFar);
                        Hold(r, rFar);
                        Go(5);
                        return;
                    }
                    Timeout(240, "C stops watching when an enemy comes close");
                    return;

                case 5:
                    if (WatchingAt(c))
                    {
                        Kit.Check(Watch(c).Target == m, "enemy gone: C goes back to watching M");
                        Kit.Teleport(m, mNorth);
                        Go(6);
                        return;
                    }
                    Timeout(2000, "C resumes watching after the enemy left");
                    return;

                case 6:
                    if (InStep < 600) return;
                    Kit.Check(c.CurJobDef != WWDefOf.WW_Watch && !Comp.TryGetPlan(c, out _),
                        "negative: M behind the north wall (no line of sight from any safe cell): C does not watch - " + Diag(c));
                    Kit.Teleport(m, mEast);
                    Go(7);
                    return;

                case 7:
                    if (WatchingAt(c))
                    {
                        m.health.AddHediff(HediffDefOf.Anesthetic);
                        Kit.Check(m.Downed, "M goes down (anesthetic)");
                        Go(8);
                        return;
                    }
                    Timeout(2000, "C watches M again from the door");
                    return;

                case 8:
                    if (HasMem(c, WWDefOf.WW_WatchedFall, m) && c.CurJobDef != WWDefOf.WW_Watch)
                    {
                        Kit.Check(true, "M down: C gains 'watched M go down' and stops watching");
                        Kit.Check(Comp.Records.Any(x => x.watcher == c && x.target == m && x.sawFall), "record: C saw M fall");
                        Kit.Teleport(e, CellFinder.StandableCellNear(o + new IntVec3(-3, 0, 9), map, 2f));
                        Kit.Teleport(m2, o + new IntVec3(13, 0, 1));
                        m2.drafter.Drafted = true;
                        Go(9);
                        return;
                    }
                    Timeout(300, "C reacts to M going down");
                    return;

                case 9:
                    if (WatchingAt(e))
                    {
                        Kit.Check(Watch(e).Target == m2, $"elder E, who was outside behind the house, comes in and watches his drafted wife M2 (after {InStep} ticks, moving {e.health.capacities.GetLevel(PawnCapacityDefOf.Moving):P0})");
                        Kit.KillBy(r, m2);
                        Go(10);
                        return;
                    }
                    Timeout(4000, "E starts watching M2");
                    return;

                case 10:
                    if (e.CurJobDef == WWDefOf.WW_Reunite) eSawReunite = true;
                    if (HasMem(m2, WWDefOf.WW_Embraced, e))
                    {
                        Kit.Check(!Comp.ThreatActive, "raider dead: threat over");
                        Kit.Check(HasMem(e, WWDefOf.WW_Relief, m2), "threat over: E gains 'M2 made it back'");
                        Kit.Check(eSawReunite || e.Position.AdjacentTo8WayOrInside(m2.Position), "E went to M2 (reunite job)");
                        Kit.Check(true, "E embraced M2: M2 gains 'E hugged me after the fight'");
                        Kit.Check(!HasMem(c, WWDefOf.WW_Relief, m), "negative: C, who saw M fall, gets no relief for M");
                        r2 = Hostile(rFar);
                        Go(11);
                        return;
                    }
                    Timeout(3000, "threat over: relief and embrace");
                    return;

                case 11:
                    if (WatchingAt(e) && Watch(e).Target == m2)
                    {
                        Kit.Check(true, "new threat: E watches M2 again");
                        Kit.KillBy(m2, r2);
                        Go(12);
                        return;
                    }
                    Timeout(2500, "E watches M2 in the second threat");
                    return;

                case 12:
                    if (HasMem(e, WWDefOf.WW_WatchedDie, m2))
                    {
                        Kit.Check(e.CurJobDef != WWDefOf.WW_Watch, "M2 killed: E gains 'watched M2 die' and stops watching");
                        Finish();
                        return;
                    }
                    Timeout(300, "E reacts to M2 dying");
                    return;
            }
        }

        void Finish()
        {
            step = 99;
            Kit.Summary("WindowWatchers");
        }
    }
}
