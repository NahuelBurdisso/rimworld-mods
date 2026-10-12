using System.Collections.Generic;
using System.IO;
using System.Linq;
using OldScores;
using RimWorld;
using UnityEngine;
using Verse;

namespace OldScoresTest
{
    /// <summary>
    /// Raid Echoes visual check, armed by the same "showcase.flag" (never with --ticks). Runs after
    /// <see cref="OldScoresShowcase"/>: three real raids walk in at ultrafast speed (two of one faction from
    /// the same place, one of another faction if there is one), fall to the colony or to the showcase, and
    /// then the overlay is switched on and captured into D:/rimworld/logs/showcase/oldscores/.
    /// </summary>
    public class EchoShowcase : GameComponent
    {
        const string ShotDir = "D:/rimworld/logs/showcase/oldscores";
        const int MaxWalkTicks = 2200, AfterBreachTicks = 240;

        bool active, finished;
        int stage = -1, stageTick, frame = -1;
        Map map;
        Faction first, second;
        IntVec3 firstArrival = IntVec3.Invalid;
        readonly List<Pawn> raiders = new List<Pawn>();
        OldScoresMapComponent comp;

        public EchoShowcase(Game game) { }

        public override void FinalizeInit()
        {
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.oldscorestest");
            active = me != null && File.Exists(Path.Combine(me.RootDir, "showcase.flag")) && !Kit.HarnessArmed;
            if (active) Log.Message("[Showcase] Raid Echoes showcase armed; starts after the Old Scores showcase");
        }

        int Now => Find.TickManager.TicksGame;

        public override void GameComponentTick()
        {
            if (!active || finished) return;
            try { Tick(); }
            catch (System.Exception ex) { Log.Error("[Showcase] Raid Echoes showcase failed: " + ex); finished = true; }
        }

        void Tick()
        {
            if (stage == -1)
            {
                OldScoresShowcase other = Current.Game.GetComponent<OldScoresShowcase>();
                if (other != null && !other.Finished) return;
                map = Find.CurrentMap;
                comp = OldScoresMapComponent.For(map);
                comp.ClearEchoes();
                first = TestUtil.HostileFaction();
                second = TestUtil.OtherHostileFaction(first);
                Log.Message($"[Showcase] Raid Echoes: {first?.Name} twice, then {second?.Name ?? "nobody"}");
                if (first == null) { finished = true; return; }
                StartRaid(first, IntVec3.Invalid);
                firstArrival = comp.ActiveRaids.LastOrDefault()?.arrival ?? IntVec3.Invalid;
                Go(0);
                return;
            }
            switch (stage)
            {
                case 0: if (RaidDone()) { StartRaid(first, firstArrival); Go(1); frame = 0; } break;   // frame: the old-road shot
                case 1: if (RaidDone()) { if (second != null) { StartRaid(second, IntVec3.Invalid); Go(2); } else Go(3); } break;
                case 2: if (RaidDone()) Go(3); break;
                case 3:
                    if (comp.ActiveRaids.Count > 0 && Now - stageTick < 400) return;   // let the last echo finish
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    OldScoresTracker.Instance.showEchoes = true;
                    Log.Message($"[Showcase] echoes on the map: {comp.Echoes.Count}; labels: {string.Join(" | ", comp.EchoLabelsForTest())}");
                    Go(4);
                    frame = 0;
                    break;
            }
        }

        void Go(int s) { stage = s; stageTick = Now; killTick = -1; breachTick = -1; }

        void StartRaid(Faction f, IntVec3 center)
        {
            raiders.Clear();
            var before = new HashSet<Pawn>(map.mapPawns.AllPawns);
            bool ok = false;
            for (int attempt = 0; attempt < 4 && !ok; attempt++)   // vanilla generation can come back empty at random
            {
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
                parms.faction = f;
                parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
                parms.points = 220f;
                parms.forced = true;
                if (center.IsValid) parms.spawnCenter = center;
                ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                if (!ok) Log.Message($"[Showcase] raid attempt {attempt + 1} of {f.Name} did not fire");
            }
            raiders.AddRange(map.mapPawns.AllPawns.Where(p => !before.Contains(p) && p.Faction == f));
            Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
            Log.Message($"[Showcase] raid of {f.Name}: {raiders.Count} raiders, arrival {comp.ActiveRaids.LastOrDefault()?.arrival}; message: {comp.lastOldRoadText ?? "-"}");
        }

        /// <summary>
        /// Lets the raid walk until one of them breaks in (plus a little) or time runs out, then kills the rest;
        /// true once the raid has become an echo (the next raid must see it).
        /// </summary>
        bool RaidDone()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
            if (killTick >= 0) return comp.ActiveRaids.Count == 0 || Now - killTick > 300;
            ActiveRaid raid = comp.ActiveRaids.LastOrDefault();
            int walked = Now - stageTick;
            if (raid != null)
            {
                bool breached = raid.tracks.Any(t => t.breach.IsValid);
                if (breached)
                {
                    if (breachTick < 0) breachTick = Now;
                    if (Now - breachTick < AfterBreachTicks && walked < MaxWalkTicks) return false;
                }
                else if (walked < MaxWalkTicks)
                {
                    // the quicktest colony's Home area is tiny: after 900 ticks paint it under the lead raider,
                    // as if he had reached the walls (the same rule a real breach goes through)
                    if (walked == 900 && stage == 0) PaintHomeUnderLead(raid);
                    return false;
                }
            }
            breachTick = -1;
            killTick = Now;
            Pawn shooter = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            foreach (Pawn p in raiders)
                if (!p.Dead && p.Spawned) p.Kill(new DamageInfo(DamageDefOf.Bullet, 40f, 0f, -1f, shooter));
            return false;
        }

        int breachTick = -1, killTick = -1;

        void PaintHomeUnderLead(ActiveRaid raid)
        {
            Pawn lead = raid.tracks.Where(t => t.pawn != null && t.pawn.Spawned && t.recording).OrderByDescending(t => t.maxDist).Select(t => t.pawn).FirstOrDefault();
            if (lead == null) return;
            foreach (IntVec3 c in CellRect.CenteredOn(lead.Position, 5).ClipInsideMap(map)) map.areaManager.Home[c] = true;
            Log.Message($"[Showcase] painted Home area under the lead raider at {lead.Position}");
        }

        public override void GameComponentUpdate()
        {
            if (!active || finished || frame < 0) return;
            frame++;
            if (stage == 1)
            {
                // the second raid of the same faction: "They come by the old road again" on screen
                if (frame == 2)
                {
                    Pawn p = raiders.FirstOrDefault(r => r.Spawned);
                    if (p != null) Find.CameraDriver.SetRootPosAndSize(p.DrawPos, 16f);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                }
                if (frame == 20) Shot("4-old-road-message");
                if (frame == 22) { Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast; frame = -1; }
                return;
            }
            if (stage != 4) return;
            if (frame == 2)
            {
                // overview: frame every lane point
                List<IntVec3> cells = comp.Echoes.SelectMany(e => e.points.Concat(e.breaches)).ToList();
                if (cells.Count > 0)
                {
                    CellRect r = CellRect.FromLimits(cells.Min(c => c.x), cells.Min(c => c.z), cells.Max(c => c.x), cells.Max(c => c.z));
                    float size = Mathf.Clamp(Mathf.Max(r.Height, r.Width / 1.8f) * 0.55f + 4f, 14f, 60f);
                    Find.CameraDriver.SetRootPosAndSize(r.CenterVector3, size);
                }
                Find.Selector.ClearSelection();
            }
            if (frame == 30) Shot("5-echoes-overview");
            if (frame == 34)
            {
                // normal zoom on the main breach label
                RaidEcho e = comp.Echoes.FirstOrDefault(x => x.breaches.Count > 0) ?? comp.Echoes.FirstOrDefault();
                if (e != null)
                {
                    IntVec3 at = e.breaches.Count > 0 ? e.breaches[0] : e.points[e.LaneEnd(0) - 1];
                    Find.CameraDriver.SetRootPosAndSize(at.ToVector3Shifted() + new Vector3(0f, 0f, -3f), 12f);
                }
            }
            if (frame == 62) Shot("6-echoes-normal-zoom");
            if (frame == 66)
            {
                OldScoresTracker.Instance.showEchoes = false;
            }
            if (frame == 90) Shot("7-echoes-overlay-off");
            if (frame == 94)
            {
                OldScoresTracker.Instance.showEchoes = true;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                finished = true;
                Log.Message("[Showcase] Raid Echoes showcase done");
            }
        }

        void Shot(string name)
        {
            string path = Path.Combine(ShotDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log.Message("[Showcase] captured " + path);
        }
    }
}
