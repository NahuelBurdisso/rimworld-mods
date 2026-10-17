using System.IO;
using System.Linq;
using AfterTheRain;
using RimWorld;
using UnityEngine;
using Verse;

namespace AfterTheRainTest
{
    /// <summary>
    /// Visual check, armed by mods/AfterTheRainTest/showcase.flag (never under --ticks). Two real rainbows through
    /// the watcher (Log.Error if it refuses): one early in the morning (low light, left side) and one in the late
    /// afternoon (right side). Captures before, fading in, full, zoomed out, zoomed in, afternoon and fading out into
    /// D:/rimworld/logs/showcase/aftertherain.
    /// </summary>
    public class RainShowcase : GameComponent
    {
        public const string ShotDir = "D:/rimworld/logs/showcase/aftertherain";
        bool active;
        int ticks;
        GameCondition_Rainbow rainbow;
        string pending;
        int frames;
        Vector3 home;

        public RainShowcase(Game game) { }

        public override void FinalizeInit()
        {
            if (Kit.HarnessArmed) return;
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.aftertheraintest");
            active = me != null && File.Exists(Path.Combine(me.RootDir, "showcase.flag"));
            if (active) Directory.CreateDirectory(ShotDir);
        }

        public override void GameComponentTick()
        {
            if (!active || pending != null) return;
            ticks++;
            Map map = Find.CurrentMap;
            switch (ticks)
            {
                case 120: Prepare(map, 6.3f, 24f); break;
                case 240: Shoot("0-before"); break;
                case 241: Begin(map); break;
                case 241 + 250: Shoot("1-fading-in"); break;
                case 241 + 700: Shoot("2-full-morning"); break;
                case 241 + 760: Find.CameraDriver.SetRootPosAndSize(home, 45f); break;
                case 241 + 820: Shoot("3-zoomed-out"); break;
                case 241 + 880: Find.CameraDriver.SetRootPosAndSize(home, 10f); break;
                case 241 + 940: Shoot("4-zoomed-in"); break;
                case 241 + 1000: rainbow?.End(); break;
                case 1300: Prepare(map, 17.4f, 24f); break;
                case 1301: Begin(map); break;
                case 1301 + 700: Shoot("5-full-afternoon"); break;
                case 1301 + 760: if (rainbow != null) rainbow.TicksLeft = 300; break;
                case 1301 + 910: Shoot("6-fading-out"); break;
                case 1301 + 1000: Log.Message("[Showcase] done"); active = false; break;
            }
        }

        public override void GameComponentUpdate()
        {
            if (pending == null) return;
            frames++;
            if (frames == 10)
            {
                string path = Path.Combine(ShotDir, $"aftertherain-{pending}.png");
                ScreenCapture.CaptureScreenshot(path);
                Map map = Find.CurrentMap;
                Log.Message($"[Showcase] captured {path} (strength {rainbow?.Strength ?? 0f:0.00}, hour {GenLocalDate.HourFloat(map):0.0}, glow {GenCelestial.CurCelestialSunGlow(map):0.00})");
            }
            else if (frames >= 40)
            {
                pending = null;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            }
        }

        void Shoot(string name)
        {
            Find.TickManager.Pause();
            pending = name;
            frames = 0;
        }

        /// <summary>Clock to the given hour (forward only), rain settled into clear, camera on the colonists.</summary>
        void Prepare(Map map, float hour, float zoom)
        {
            float now = GenLocalDate.HourFloat(map);
            float ahead = hour - now;
            if (ahead < 0f) ahead += 24f;
            int skip = (int)(ahead * 2500f);
            if (skip > 0) Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + skip);
            WeatherManager wm = map.weatherManager;
            wm.TransitionTo(DefDatabase<WeatherDef>.GetNamed("Rain"));
            wm.curWeatherAge = 5000;
            wm.TransitionTo(WeatherDefOf.Clear);
            wm.curWeatherAge = RainbowWatcher.MaxAge + 500;   // settled clear on screen; the watcher ignores it until Begin
            Pawn p = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            home = p?.DrawPos ?? map.Center.ToVector3Shifted();
            Find.CameraDriver.SetRootPosAndSize(home, zoom);
            Find.Selector.ClearSelection();
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            Log.Message($"[Showcase] clock {now:0.0} -> {GenLocalDate.HourFloat(map):0.0}, dayPercent {GenLocalDate.DayPercent(map):0.000}, sun glow {GenCelestial.CurCelestialSunGlow(map):0.00}");
        }

        void Begin(Map map)
        {
            map.weatherManager.curWeatherAge = 4000;   // the rain just cleared; rolled by the watcher in this same tick
            RainbowWatcher w = map.GetComponent<RainbowWatcher>();
            RainbowResult r = w.Evaluate(GenLocalDate.DayPercent(map), GenCelestial.CurCelestialSunGlow(map), 1f);
            rainbow = RainbowWatcher.ActiveRainbow(map);
            if (r != RainbowResult.Started || rainbow == null)
            {
                Log.Error($"[Showcase] the watcher refused a real rainbow at {GenLocalDate.HourFloat(map):0.0}h: {r}");
                return;
            }
            Log.Message($"[Showcase] rainbow started by the watcher, side {rainbow.side}, duration {rainbow.Duration}");
        }
    }
}
