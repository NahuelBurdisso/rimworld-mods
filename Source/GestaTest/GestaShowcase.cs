using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gesta;
using RimWorld;
using UnityEngine;
using Verse;

namespace GestaTest
{
    /// <summary>
    /// Visual check, armed by "showcase.flag" in this mod's folder on a live `rw launch --quicktest`
    /// (never with --ticks). Stages a last stand next to the camera and captures screenshots into
    /// D:/rimworld/logs/showcase/gesta/.
    /// </summary>
    public class GestaShowcase : GameComponent
    {
        bool active, started;
        int ticks;
        int framesSinceTrigger = -1;
        readonly List<(int frame, string name)> shots = new List<(int, string)> { (45, "laststand-1-caption"), (260, "laststand-2-vignette") };
        string shotDir;
        Map map;
        readonly List<Pawn> hostiles = new List<Pawn>();

        public GestaShowcase(Game game) { }

        public override void FinalizeInit()
        {
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.gestatest");
            active = me != null && File.Exists(Path.Combine(me.RootDir, "showcase.flag")) && !Kit.HarnessArmed;
            if (!active) return;
            shotDir = "D:/rimworld/logs/showcase/gesta";   // test-only; RootDir resolves through the Mods junction
            Directory.CreateDirectory(shotDir);
        }

        public override void GameComponentTick()
        {
            if (!active || started) return;
            if (++ticks != 120) return;
            started = true;
            map = Find.CurrentMap;
            var fighters = map.mapPawns.FreeColonistsSpawned.Where(CombatUtil.IsFighter).ToList();
            if (fighters.Count < 2) { Log.Error("[Showcase] need 2 fighters"); return; }
            Pawn hero = fighters[0];
            EngagementTracker.Instance.PollNow();
            for (int i = 0; i < 3; i++)
            {
                Pawn h = Kit.SpawnHostile(map, hero.Position + new IntVec3(6, 0, 2));
                h.equipment?.DestroyAllEquipment();
                hostiles.Add(h);
            }
            EngagementTracker.Instance.PollNow();
            foreach (Pawn p in fighters.Skip(1)) Kit.Down(p);
            Find.CameraDriver.SetRootPosAndSize(hero.DrawPos, 14f);
            Find.Selector.ClearSelection();
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            EngagementTracker.Instance.PollNow();
            framesSinceTrigger = 0;
            Log.Message($"[Showcase] hero {hero.LabelShort}, last stand active: {LastStandTracker.ActiveHero != null}");
        }

        public override void GameComponentUpdate()
        {
            if (framesSinceTrigger < 0) return;
            framesSinceTrigger++;
            foreach (var (frame, name) in shots)
            {
                if (framesSinceTrigger != frame) continue;
                string path = Path.Combine(shotDir, name + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Log.Message("[Showcase] captured " + path);
            }
            if (framesSinceTrigger == 400)
            {
                foreach (Pawn h in hostiles) if (!h.Dead) h.Kill(null);
                Log.Message("[Showcase] done");
                framesSinceTrigger = -1;
            }
        }
    }
}
