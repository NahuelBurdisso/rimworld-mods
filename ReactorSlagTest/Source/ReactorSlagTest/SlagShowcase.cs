using System.IO;
using System.Linq;
using ReactorSlag;
using RimWorld;
using UnityEngine;
using Verse;

namespace ReactorSlagTest
{
    /// <summary>
    /// Visual check, armed by mods/ReactorSlagTest/showcase.flag (never under --ticks). Kills a centipede and an
    /// apocriton side by side and captures: hot by day, hot by night, half-cooled by night, cold by day, cold by
    /// night. The hour is moved with gameStartAbsTick, so the cores' heat (TicksGame) is not touched by it.
    /// Screenshots go to D:/rimworld/logs/showcase/reactorslag.
    /// </summary>
    public class SlagShowcase : GameComponent
    {
        public const string ShotDir = "D:/rimworld/logs/showcase/reactorslag";

        bool active, begun;
        int ticks, step, wait;
        Map map;
        ReactorSlagCore small, large;
        Vector3 frame;

        string pending;
        int frames;

        public SlagShowcase(Game game) { }

        public override void FinalizeInit()
        {
            active = false;
            if (Kit.HarnessArmed) return;
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.reactorslagtest");
            active = me != null && File.Exists(Path.Combine(me.RootDir, "showcase.flag"));
            if (active) Directory.CreateDirectory(ShotDir);
        }

        public override void GameComponentTick()
        {
            if (!active || begun) return;
            if (++ticks == 120) Begin();
        }

        void SetHour(int hour)
        {
            int now = GenLocalDate.HourOfDay(map);
            int delta = ((hour - now) % 24 + 24) % 24;
            Find.TickManager.gameStartAbsTick += delta * GenDate.TicksPerHour;
        }

        void Shoot(string name)
        {
            Find.TickManager.Pause();
            Find.CameraDriver.SetRootPosAndSize(frame, 13f);
            pending = name;
            frames = 0;
        }

        public override void GameComponentUpdate()
        {
            if (!active || !begun) return;
            if (pending != null)
            {
                frames++;
                if (frames == 15)
                {
                    string path = Path.Combine(ShotDir, $"slag-{pending}.png");
                    ScreenCapture.CaptureScreenshot(path);
                    Log.Message($"[Showcase] captured {path} (hour {GenLocalDate.HourOfDay(map)}, heat {small?.Heat:0.##}/{large?.Heat:0.##})");
                }
                else if (frames >= 45) pending = null;
                return;
            }
            if (wait > 0) { wait--; return; }
            int now = Find.TickManager.TicksGame;
            switch (step++)
            {
                case 0: Shoot("0-hot-day"); break;
                case 1: SetHour(23); Find.TickManager.CurTimeSpeed = TimeSpeed.Normal; wait = 420; break;
                case 2: Shoot("1-hot-night"); break;
                case 3:
                    foreach (ReactorSlagCore c in new[] { small, large })
                        if (c != null && c.Spawned) { c.spawnTick = now - ReactorSlagCore.CoolingTicks / 2; c.ApplyHeat(); }
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal; wait = 300; break;
                case 4: Shoot("2-half-cooled-night"); break;
                case 5:
                    foreach (ReactorSlagCore c in new[] { small, large })
                        if (c != null && c.Spawned) { c.spawnTick = now - ReactorSlagCore.CoolingTicks - 1; c.TickRare(); }
                    SetHour(13); wait = 60; break;
                case 6: Shoot("3-cold-day"); break;
                case 7: SetHour(23); wait = 60; break;
                case 8: Shoot("4-cold-night"); break;
                case 9: Log.Message("[Showcase] done"); active = false; break;
            }
        }

        void Begin()
        {
            begun = true;
            map = Find.CurrentMap;
            bool Open(IntVec3 c) => GenRadial.RadialCellsAround(c, 5f, true).All(x =>
                x.InBounds(map) && x.Standable(map) && !x.Fogged(map) && x.GetEdifice(map) == null &&
                x.GetTerrain(map).natural && !x.GetTerrain(map).IsWater && map.zoneManager.ZoneAt(x) == null);
            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 110, Open, out IntVec3 center))
            { Log.Error("[Showcase] no open area"); active = false; return; }

            IntVec3 a = center + new IntVec3(-3, 0, 0), b = center + new IntVec3(3, 0, 0);
            small = KillAt("Mech_CentipedeBlaster", a);
            large = KillAt("Mech_Apocriton", b);
            frame = center.ToVector3Shifted();
            SetHour(13);
            Find.Selector.ClearSelection();
            Log.Message($"[Showcase] cores: {small?.def.defName} {large?.def.defName}");
        }

        ReactorSlagCore KillAt(string kind, IntVec3 cell)
        {
            PawnKindDef k = DefDatabase<PawnKindDef>.GetNamedSilentFail(kind);
            if (k == null) return null;
            Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(k, Faction.OfMechanoids, forceGenerateNewPawn: true));
            GenSpawn.Spawn(p, cell, map);
            p.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f));
            return p.Corpse != null && p.Corpse.Spawned ? p.Corpse.Position.GetThingList(map).OfType<ReactorSlagCore>().FirstOrDefault() : null;
        }
    }
}
