using System.Collections.Generic;
using System.IO;
using System.Linq;
using LightForTheAbsent;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LightForTheAbsentTest
{
    /// <summary>
    /// Visual check, armed by mods/LightForTheAbsentTest/showcase.flag (never under --ticks). Builds three roofed
    /// rooms, moves the clock to the middle of the night and shows the three states side by side: away (steady),
    /// returning (half faded), died far away (flickering). Captures to D:/rimworld/logs/showcase/lightfortheabsent.
    /// </summary>
    public class LightShowcase : GameComponent
    {
        public const string ShotDir = "D:/rimworld/logs/showcase/lightfortheabsent";
        bool active, started;
        int ticks, readyTick = -1;
        readonly List<(int at, string name)> shots = new List<(int, string)> { (60, "1-three-rooms-normal-zoom"), (75, "2-three-rooms-close"), (100, "3-closeup-flicker") };
        int shotIndex;
        string pending;
        int frames;
        Vector3 wide, close;

        public LightShowcase(Game game) { }

        public override void FinalizeInit()
        {
            if (Kit.HarnessArmed) return;
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.lightfortheabsenttest");
            active = me != null && File.Exists(Path.Combine(me.RootDir, "showcase.flag"));
            if (active) Directory.CreateDirectory(ShotDir);
        }

        public override void GameComponentTick()
        {
            if (!active || pending != null) return;
            ticks++;
            if (!started && ticks == 180) Begin();
            if (readyTick < 0 || shotIndex >= shots.Count) return;
            if (ticks - readyTick == shots[shotIndex].at)
            {
                string name = shots[shotIndex].name;
                if (name.StartsWith("2")) Find.CameraDriver.SetRootPosAndSize(wide, 13f);
                if (name.StartsWith("3")) Find.CameraDriver.SetRootPosAndSize(close, 11f);   // closest zoom the game allows
                Find.TickManager.Pause();
                pending = name;
                frames = 0;
                shotIndex++;
            }
        }

        public override void GameComponentUpdate()
        {
            if (pending == null) return;
            frames++;
            if (frames == 12)
            {
                string path = Path.Combine(ShotDir, $"lightfortheabsent-{pending}.png");
                ScreenCapture.CaptureScreenshot(path);
                Log.Message($"[Showcase] captured {path}");
            }
            else if (frames >= 40)
            {
                pending = null;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            }
        }

        void Begin()
        {
            started = true;
            Map map = Find.CurrentMap;
            AbsenceTracker tracker = AbsenceTracker.Instance;
            AbsenceTracker.AutoPoll = false;

            // night: move the clock forward until the sun is well down
            for (int i = 0; i < 24 && GenCelestial.CurCelestialSunGlow(map) > 0.05f; i++)
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 2500);
            Log.Message($"[Showcase] sun glow {GenCelestial.CurCelestialSunGlow(map):0.00}, hour {GenLocalDate.HourOfDay(map)}");

            IntVec3 o = FindArea(map);
            if (!o.IsValid) { Log.Error("[Showcase] no area"); return; }
            List<Pawn> cols = map.mapPawns.FreeColonistsSpawned.ToList();
            while (cols.Count < 3)
            {
                Pawn np = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true));
                GenSpawn.Spawn(np, CellFinder.StandableCellNear(o + new IntVec3(9, 0, -3), map, 8f), map);
                cols.Add(np);
            }
            var beds = new Building_Bed[3];
            for (int i = 0; i < 3; i++)
            {
                beds[i] = Room(map, o + new IntVec3(7 * i, 0, 0));
                cols[i].ownership.ClaimBedIfNonMedical(beds[i]);
            }
            foreach (Pawn p in cols.Take(3)) p.jobs.StopAll();
            Caravan away = CaravanExitMapUtility.ExitMapAndCreateCaravan(new List<Pawn> { cols[0] }, Faction.OfPlayer, map.Tile, map.Tile, PlanetTile.Invalid, false);
            Caravan back = CaravanExitMapUtility.ExitMapAndCreateCaravan(new List<Pawn> { cols[1] }, Faction.OfPlayer, map.Tile, map.Tile, PlanetTile.Invalid, false);
            Caravan lost = CaravanExitMapUtility.ExitMapAndCreateCaravan(new List<Pawn> { cols[2] }, Faction.OfPlayer, map.Tile, map.Tile, PlanetTile.Invalid, false);
            tracker.Poll();
            CaravanEnterMapUtility.Enter(back, map, CaravanEnterMode.Edge);
            tracker.Poll();
            for (int i = 0; i < 12; i++) tracker.Poll();   // half way through the fade
            cols[2].Kill(null);
            tracker.Poll();
            Log.Message("[Showcase] records: " + string.Join("; ", tracker.RecordsForReading.Select(r => r.ToString())));

            wide = new Vector3(o.x + 10f, 0f, o.z + 3f);
            close = beds[2].TrueCenter();
            Find.CameraDriver.SetRootPosAndSize(wide, 20f);   // a normal playing zoom
            Find.Selector.ClearSelection();
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            readyTick = ticks;
        }

        static Building_Bed Room(Map map, IntVec3 o)
        {
            var outer = new CellRect(o.x, o.z, 6, 6);
            foreach (IntVec3 c in outer.EdgeCells)
            {
                Thing wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog);
                wall.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(wall, c, map);
            }
            foreach (IntVec3 c in outer) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            foreach (IntVec3 c in outer.ContractedBy(1)) map.terrainGrid.SetTerrain(c, TerrainDefOf.WoodPlankFloor);
            var bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
            bed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(bed, o + new IntVec3(2, 0, 3), map, Rot4.South);
            return bed;
        }

        /// <summary>A clear 22x12 patch near the centre: strict first, then allowing chunks and other items (cleared).</summary>
        static IntVec3 FindArea(Map map)
        {
            for (int pass = 0; pass < 2; pass++)
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(map.Center, Mathf.Min(GenRadial.MaxRadialPatternRadius - 1f, 90f), true))
                {
                    var r = new CellRect(cell.x, cell.z - 5, 22, 12);
                    if (!r.InBounds(map)) continue;
                    bool ok = true;
                    foreach (IntVec3 x in r)
                        if (!x.Walkable(map) || x.GetTerrain(map).IsWater || x.GetEdifice(map) != null || x.Fogged(map) || x.Roofed(map)
                            || x.GetFirstPawn(map) != null || (pass == 0 && x.GetFirstItem(map) != null))
                        { ok = false; break; }
                    if (!ok) continue;
                    foreach (IntVec3 x in r)
                        foreach (Thing t in x.GetThingList(map).ToList())
                            if (t is Plant || t.def.category == ThingCategory.Item || t.def.category == ThingCategory.Filth) t.Destroy();
                    Log.Message($"[Showcase] area at {cell} (pass {pass})");
                    return cell;
                }
            return IntVec3.Invalid;
        }
    }
}
