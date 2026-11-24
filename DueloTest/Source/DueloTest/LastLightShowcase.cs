using System.IO;
using System.Linq;
using Duelo;
using RimWorld;
using UnityEngine;
using Verse;

namespace DueloTest
{
    /// <summary>
    /// Visual check, armed by a file "showcase.flag" in this mod's folder (never by --ticks).
    /// Places a spouse ~10 cells away, frames the camera, kills a colonist at normal speed and
    /// captures screenshots of each phase (rise, travel, circle) into D:/rimworld/logs/showcase/duelo,
    /// pausing a moment for each capture and resuming on its own.
    /// </summary>
    public class LastLightShowcase : GameComponent
    {
        bool active, started;
        int ticks;
        Mote_LastLight light;
        string shotDir;

        // capture state: pause -> wait a few frames -> capture -> wait -> resume
        string pendingShot;
        int pendingFrames;
        bool shotRise, shotTravel, shotCircle;
        int phaseTicks;
        Mote_LastLight.Phase lastPhase;

        public LastLightShowcase(Game game) { }

        public override void FinalizeInit()
        {
            active = ShowcaseFlag.Wants("lastlight");
            if (active)
            {
                shotDir = ShowcaseFlag.ShotDir;   // test-only; RootDir resolves through the Mods junction into the game folder
                Directory.CreateDirectory(shotDir);
            }
        }

        public override void GameComponentTick()
        {
            if (!active) return;
            ticks++;
            if (!started && ticks == 120) Begin();
            if (!started || light == null || pendingShot != null) return;

            if (light.CurrentPhase != lastPhase) { lastPhase = light.CurrentPhase; phaseTicks = 0; }
            phaseTicks++;

            if (!shotRise && light.CurrentPhase == Mote_LastLight.Phase.Rise && phaseTicks == 60) { shotRise = true; Shoot("1-rise"); }
            else if (!shotTravel && light.CurrentPhase == Mote_LastLight.Phase.Travel && phaseTicks == 70) { shotTravel = true; Shoot("2-travel"); }
            else if (!shotCircle && light.CurrentPhase == Mote_LastLight.Phase.Circle && phaseTicks == 50) { shotCircle = true; Shoot("3-circle"); }
        }

        public override void GameComponentUpdate()
        {
            if (pendingShot == null) return;
            pendingFrames++;
            if (pendingFrames == 10)
            {
                string path = Path.Combine(shotDir, $"lastlight-{pendingShot}.png");
                ScreenCapture.CaptureScreenshot(path);
                Log.Message($"[Showcase] captured {path}");
            }
            else if (pendingFrames >= 40)
            {
                pendingShot = null;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            }
        }

        void Shoot(string name)
        {
            Find.TickManager.Pause();
            pendingShot = name;
            pendingFrames = 0;
        }

        void Begin()
        {
            started = true;
            Map map = Find.CurrentMap;
            var colonists = map.mapPawns.FreeColonistsSpawned.Where(p => p.story?.traits?.HasTrait(TraitDefOf.Psychopath) != true).ToList();
            if (colonists.Count < 2) { Log.Error("[Showcase] need 2 colonists"); return; }
            Pawn deceased = colonists[0], spouse = colonists[1];
            if (!deceased.relations.DirectRelationExists(PawnRelationDefOf.Spouse, spouse))
                deceased.relations.AddDirectRelation(PawnRelationDefOf.Spouse, spouse);

            IntVec3 far = CellFinder.StandableCellNear(deceased.Position + new IntVec3(9, 0, 3), map, 4f);
            if (far.IsValid)
            {
                spouse.Position = far;
                spouse.Notify_Teleported();
            }
            spouse.jobs.StopAll();

            Vector3 mid = (deceased.DrawPos + spouse.DrawPos) / 2f;
            Find.CameraDriver.SetRootPosAndSize(mid, 12f);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            Find.Selector.ClearSelection();

            deceased.Kill(null, null);
            light = LastLightUtility.LastEmitted;
            Log.Message($"[Showcase] {deceased.LabelShort} died; light -> {light?.target?.LabelShort ?? "nobody"}; hour {GenLocalDate.HourOfDay(map)}");
        }
    }
}
