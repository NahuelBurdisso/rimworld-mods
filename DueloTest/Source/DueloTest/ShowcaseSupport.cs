using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace DueloTest
{
    /// <summary>
    /// Showcases are armed by mods/DueloTest/showcase.flag (never under --ticks). The flag's text picks
    /// which ones run: "lastlight", "eulogy", "moss" (an empty flag means lastlight, as before).
    /// Screenshots go to D:/rimworld/logs/showcase/duelo.
    /// </summary>
    public static class ShowcaseFlag
    {
        public const string ShotDir = "D:/rimworld/logs/showcase/duelo";

        public static bool Wants(string name)
        {
            if (GenCommandLine.TryGetCommandLineArg("rwtools-ticks", out _)) return false;
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.duelotest");
            if (me == null) return false;
            string path = Path.Combine(me.RootDir, "showcase.flag");
            if (!File.Exists(path)) return false;
            string text = File.ReadAllText(path).Trim().ToLowerInvariant();
            if (text.Length == 0) return name == "lastlight";
            return text.Contains(name);
        }
    }

    /// <summary>Pause, wait a few frames, capture, wait, resume at the given speed.</summary>
    public class ShowcaseCamera
    {
        string pending;
        int frames;
        TimeSpeed resume = TimeSpeed.Normal;
        public int Shots;

        public bool Busy => pending != null;

        public void Shoot(string name, TimeSpeed resumeAt)
        {
            Directory.CreateDirectory(ShowcaseFlag.ShotDir);
            Find.TickManager.Pause();
            pending = name;
            frames = 0;
            resume = resumeAt;
        }

        /// <summary>Call from GameComponentUpdate.</summary>
        public void Update()
        {
            if (pending == null) return;
            frames++;
            if (frames == 10)
            {
                string path = Path.Combine(ShowcaseFlag.ShotDir, $"{pending}.png");
                ScreenCapture.CaptureScreenshot(path);
                Shots++;
                Log.Message($"[Showcase] captured {path}");
            }
            else if (frames >= 40)
            {
                pending = null;
                if (resume != TimeSpeed.Paused) Find.TickManager.CurTimeSpeed = resume;
            }
        }
    }
}
