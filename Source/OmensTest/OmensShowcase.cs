using System.IO;
using System.Linq;
using Omens;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace OmensTest
{
    /// <summary>
    /// Visual check, armed by "showcase.flag" in this mod's folder on a live quicktest (never with --ticks):
    /// a revenant outlined by rain and by snow, crows over a staging raid, the flock leaving at the assault,
    /// and the cold camp with its inspect pane. Screenshots go to D:/rimworld/logs/showcase/omens/.
    /// </summary>
    public class OmensShowcase : GameComponent
    {
        const string ShotDir = "D:/rimworld/logs/showcase/omens";   // test-only, hardcoded on purpose
        const int StartTick = 400;

        bool active, finished;
        int step, stepTick, waitShot = -1;
        string pendingShot;
        Map map;
        Pawn revenant;
        IntVec3 revCell = IntVec3.Invalid;
        Lord lord;
        StageRecord rec;

        public OmensShowcase(Game game) { }

        public override void FinalizeInit()
        {
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.omenstest");
            active = me != null && File.Exists(Path.Combine(me.RootDir, "showcase.flag")) && !Kit.HarnessArmed;
            if (!active) return;
            Directory.CreateDirectory(ShotDir);
            Log.Message("[Showcase] Omens showcase armed; starts at tick " + StartTick);
        }

        int Now => Find.TickManager.TicksGame;
        int InStep => Now - stepTick;

        void Go(int s) { step = s; stepTick = Now; }

        public override void GameComponentTick()
        {
            if (!active || finished) return;
            // the revenant would walk away: keep it on its cell while it poses
            if (revenant != null && revenant.Spawned && revCell.IsValid && revenant.Position != revCell) TestUtil.Teleport(revenant, revCell);
        }

        public override void GameComponentUpdate()
        {
            if (!active || finished || Current.ProgramState != ProgramState.Playing || Find.TickManager == null) return;
            if (Find.TickManager.CurTimeSpeed == TimeSpeed.Paused) Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            if (pendingShot != null)
            {
                // capture one frame after the camera moved
                if (waitShot-- > 0) return;
                string path = Path.Combine(ShotDir, pendingShot + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Log.Message("[Showcase] captured " + path);
                pendingShot = null;
                return;
            }
            if (Now < StartTick) return;
            try { Run(); }
            catch (System.Exception ex)
            {
                Log.Error("[Showcase] Omens showcase failed in step " + step + ": " + ex);
                finished = true;
            }
        }

        void Shot(string name, int frames = 3)
        {
            pendingShot = name;
            waitShot = frames;
            string rev = revenant == null ? "none" : $"spawned {revenant.Spawned} at {revenant.Position} (pose cell {revCell}) hidden {revenant.IsHiddenFromPlayer()} hostile {revenant.HostileTo(Faction.OfPlayer)} roofed {revenant.Spawned && revenant.Position.Roofed(map)}";
            Log.Message($"[Showcase] {name}: weather {map.weatherManager.curWeather.defName} rain {map.weatherManager.RainRate:F2} snow {map.weatherManager.SnowRate:F2}; revenant {rev}; " +
                $"splashes {OmensStats.RainSplashes} puffs {OmensStats.SnowPuffs} crows {OmensStats.CrowsSpawned} flock {OmensStats.FlockCrows} circling {CrowMote.Live.Count(m => m.Spawned && !m.Departing)} departing {CrowMote.Live.Count(m => m.Spawned && m.Departing)}");
        }

        void Look(Thing select, Vector3 at, float size)
        {
            Find.Selector.ClearSelection();
            if (select != null) Find.Selector.Select(select, playSound: false);
            Find.CameraDriver.SetRootPosAndSize(at, size);
        }

        void Weather(string defName)
        {
            WeatherDef w = DefDatabase<WeatherDef>.GetNamed(defName);
            map.weatherManager.curWeather = w;
            map.weatherManager.lastWeather = w;
            map.weatherManager.curWeatherAge = 5000;
        }

        void Run()
        {
            switch (step)
            {
                case 0:
                    map = Find.CurrentMap;
                    // daylight for readable captures
                    int hour = GenLocalDate.HourOfDay(map);
                    if (hour < 8 || hour > 16) Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + ((24 + 10 - hour) % 24) * GenDate.TicksPerHour);
                    Log.Message($"[Showcase] start at tick {Now}, hour {GenLocalDate.HourOfDay(map)}");
                    if (ModsConfig.AnomalyActive)
                    {
                        IntVec3 anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center;
                        revCell = CellFinder.RandomClosewalkCellNear(anchor + new IntVec3(8, 0, 4), map, 8,
                            c => c.Standable(map) && !c.Roofed(map) && !c.Fogged(map) && c.GetFirstPawn(map) == null && !c.GetTerrain(map).IsWater);
                        revenant = PawnGenerator.GeneratePawn(PawnKindDefOf.Revenant, Faction.OfEntities);
                        GenSpawn.Spawn(revenant, revCell, map);
                        Weather("Rain");
                        Look(null, revCell.ToVector3Shifted(), 9f);
                    }
                    Go(1);
                    break;
                case 1:
                    if (InStep < 240) return;
                    if (revenant != null) { Look(null, revCell.ToVector3Shifted(), 9f); Shot("1-rain-outline"); }
                    Go(2);
                    break;
                case 2:
                    if (InStep < 30) return;
                    if (revenant != null) { Look(null, revCell.ToVector3Shifted(), 18f); Shot("2-rain-outline-normal-zoom"); }
                    Go(3);
                    break;
                case 3:
                    if (InStep < 10) return;
                    if (revenant != null) { Weather("SnowGentle"); Look(null, revCell.ToVector3Shifted(), 9f); }
                    Go(4);
                    break;
                case 4:
                    if (InStep < 240) return;
                    if (revenant != null) Shot("3-snow-outline");
                    Go(5);
                    break;
                case 5:
                {
                    if (InStep < 10) return;
                    if (revenant != null) { revenant.Destroy(); revenant = null; }
                    Weather("Clear");
                    Faction enemy = TestUtil.StagingFaction();
                    var raiders = TestUtil.Raid(map, enemy, 600f, TestUtil.StageThenAttack);
                    lord = raiders.FirstOrDefault()?.GetLord();
                    if (lord == null) { Log.Error("[Showcase] no staging raid"); finished = true; return; }
                    IntVec3 c = lord.CurLordToil.FlagLoc;
                    if (c.Fogged(map)) map.fogGrid.Unfog(c);
                    if (c.Roofed(map)) map.roofGrid.SetRoof(c, null);
                    Log.Message($"[Showcase] staging raid of {enemy.Name} at {c}");
                    Look(null, c.ToVector3Shifted(), 12f);
                    Go(6);
                    break;
                }
                case 6:
                    rec = StagingWatcher.For(map)?.RecordFor(lord.loadID);
                    if (InStep < 700 && TestUtil.CirclingNear(map, lord.CurLordToil.FlagLoc, 8f) < 3) return;
                    Look(null, lord.CurLordToil.FlagLoc.ToVector3Shifted(), 12f);
                    Shot("4-crows-over-staging");
                    Go(7);
                    break;
                case 7:
                    if (InStep < 60) return;
                    Look(null, lord.CurLordToil.FlagLoc.ToVector3Shifted(), 22f);
                    Shot("5-crows-over-staging-normal-zoom");
                    Go(8);
                    break;
                case 8:
                    if (InStep < 20) return;
                    if (lord.ticksInToil < OmensTuning.MinWaitTicks + 100) return;
                    rec = StagingWatcher.For(map)?.RecordFor(lord.loadID);
                    TestUtil.ForceAssault(lord);
                    Go(9);
                    break;
                case 9:
                    if (rec == null || !rec.ended) { if (InStep > 300) { Log.Error("[Showcase] stage never ended"); finished = true; } return; }
                    if (InStep < 75) return;
                    Look(null, rec.cell.ToVector3Shifted() + (OmensUtil.ColonyCenter(map) - rec.cell.ToVector3Shifted()).normalized * 4f, 14f);
                    Shot("6-flock-leaves", 1);
                    Go(10);
                    break;
                case 10:
                    if (InStep < 20) return;
                    if (rec.camp == null) { Log.Error("[Showcase] no camp"); finished = true; return; }
                    Look(rec.camp, rec.camp.TrueCenter(), 6f);
                    Log.Message("[Showcase] camp text: " + rec.camp.GetInspectString().Replace("\n", " | "));
                    Go(11);
                    break;
                case 11:
                    if (InStep < 30) return;
                    Shot("7-cold-camp");
                    Go(12);
                    break;
                case 12:
                    if (InStep < 10) return;
                    finished = true;
                    Log.Message("[Showcase] done");
                    break;
            }
        }
    }
}
