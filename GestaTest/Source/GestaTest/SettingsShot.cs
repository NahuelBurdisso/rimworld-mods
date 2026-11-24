using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace GestaTest
{
    /// <summary>Showcase helper: opens Gesta's settings window early in a showcase game and captures it.</summary>
    public class SettingsShot : GameComponent
    {
        bool active;
        int ticks, frames = -1;
        Window window;

        public SettingsShot(Game game) { }

        public override void FinalizeInit()
        {
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.gestatest");
            active = me != null && File.Exists(Path.Combine(me.RootDir, "showcase.flag")) && !Kit.HarnessArmed;
        }

        public override void GameComponentTick()
        {
            if (!active || ++ticks != 30) return;
            Mod gesta = LoadedModManager.ModHandles.FirstOrDefault(m => m.Content.PackageId == "nahuel.gesta");
            if (gesta == null) return;
            window = new Dialog_ModSettings(gesta);
            Find.WindowStack.Add(window);
            frames = 0;
        }

        public override void GameComponentUpdate()
        {
            if (frames < 0) return;
            frames++;
            if (frames == 30)
            {
                string dir = "D:/rimworld/logs/showcase/gesta";
                Directory.CreateDirectory(dir);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "settings.png"));
                Log.Message("[Showcase] captured settings");
            }
            if (frames == 60)
            {
                window?.Close();
                frames = -1;
            }
        }
    }
}
