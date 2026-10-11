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
        readonly List<(int frame, string name)> shots = new List<(int, string)> { (45, "laststand-1-caption"), (260, "laststand-2-vignette"), (440, "feat-1-beam"), (500, "feat-2-fading"), (660, "poppies"), (800, "ash"), (875, "tide-0-before"), (1700, "tide-1-cold") };
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

        readonly List<Pawn> mechs = new List<Pawn>();
        int goldFrame = -1, bloodFrame = -1;

        void StageBrothers()
        {
            Pawn anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Downed) ?? map.mapPawns.FreeColonistsSpawned.First();
            IntVec3 spot = CellFinder.StandableCellNear(anchor.Position + new IntVec3(3, 0, 3), map, 4f);
            Pawn a = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            Pawn b = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(a, spot, map);
            GenSpawn.Spawn(b, spot + IntVec3.East, map);
            if (!BrothersTracker.AreBrothers(a, b)) BrothersTracker.Instance.Bond(a, b);
            a.Rotation = Rot4.South; b.Rotation = Rot4.East;
            Find.CameraDriver.SetRootPosAndSize(a.DrawPos, 5f);
            Log.Message($"[Showcase] brothers: {a.LabelShort} & {b.LabelShort}");
        }

        void StageBlood()
        {
            var standing = map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed).Take(3).ToList();
            float[] amounts = { 0.2f, 0.5f, 0.95f };
            for (int i = 0; i < standing.Count; i++)
            {
                BloodStains.Add(standing[i], amounts[i]);
                standing[i].jobs.StopAll();
            }
            if (standing.Count > 0) Find.CameraDriver.SetRootPosAndSize(standing[0].DrawPos, 5f);
            Log.Message($"[Showcase] blood on {standing.Count} colonists");
        }

        void StageTide()
        {
            Pawn witness = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Downed);
            if (witness == null) return;
            Faction f = Faction.OfMechanoids ?? Faction.OfPirates;
            PawnKindDef kind = Faction.OfMechanoids != null ? PawnKindDef.Named("Mech_CentipedeGunner") : PawnKindDefOf.PirateBoss;
            for (int i = 0; i < 8; i++)
            {
                Pawn m = Kit.SpawnHostile(map, witness.Position + new IntVec3(45, 0, i * 2 - 8), kind, f);
                m.equipment?.DestroyAllEquipment();
                mechs.Add(m);
            }
            var standing = map.mapPawns.FreeColonistsSpawned.Where(CombatUtil.IsFighter).ToList();
            if (standing.Count >= 2) Kit.Down(standing.Last());   // the colony has to be losing people
            Find.CameraDriver.SetRootPosAndSize(witness.DrawPos, 16f);
            Log.Message("[Showcase] tide: enemies spawned");
        }

        void EndTide()
        {
            foreach (Pawn m in mechs) if (!m.Dead) m.Kill(null);
            Log.Message($"[Showcase] tide: losing intensity {map.GetComponent<TideWatcher>().losingIntensity:0.00}, killed the enemy");
        }

        void StageAsh()
        {
            Pawn witness = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Downed);
            if (witness == null) return;
            IntVec3 centre = witness.Position + new IntVec3(0, 0, 6);
            var offsets = new[] { new IntVec3(-2, 0, 0), new IntVec3(1, 0, 1), new IntVec3(3, 0, -1) };
            foreach (IntVec3 o in offsets)
            {
                IntVec3 c = CellFinder.StandableCellNear(centre + o, map, 2f);
                if (!c.IsValid) continue;
                Pawn h = Kit.SpawnHostile(map, c);
                Kit.Teleport(h, c);
                Kit.KillBy(h, witness, DamageDefOf.Bomb);
            }
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).ToList()) t.Destroy();
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).ToList()) if (t.Position.InHorDistOf(centre, 6f)) t.Destroy();
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel).ToList()) if (t.Position.InHorDistOf(centre, 6f)) t.Destroy();
            Find.CameraDriver.SetRootPosAndSize(centre.ToVector3Shifted(), 8f);
            Log.Message($"[Showcase] ash marks: {map.listerThings.ThingsOfDef(GestaDefOf.Gesta_AshSilhouette).Count}");
        }

        void StagePoppies()
        {
            var field = map.GetComponent<PoppyField>();
            Pawn witness = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Downed);
            if (witness == null) return;
            IntVec3 centre = IntVec3.Invalid;
            int made = 0;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(witness.Position, 45f, false))
            {
                if (made >= 7) break;
                if (!c.InBounds(map) || !c.Standable(map) || c.Roofed(map) || map.fertilityGrid.FertilityAt(c) < PoppyField.MinFertility || c.GetZone(map) != null || c.GetEdifice(map) != null) continue;
                if (centre.IsValid && !c.InHorDistOf(centre, 6f)) continue;
                if (!centre.IsValid) centre = c;
                bool enemy = made % 3 == 2;
                Pawn victim = enemy ? Kit.SpawnHostile(map, c) : PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (!victim.Spawned) GenSpawn.Spawn(victim, c, map);
                Kit.Teleport(victim, c);
                Pawn killer = Kit.SpawnHostile(map, c + new IntVec3(10, 0, 0));
                if (enemy) Kit.KillBy(victim, witness); else Kit.KillBy(victim, killer);
                killer.Kill(null);
                made++;
            }
            field.TryBloomAll(force: true);
            foreach (Thing t in map.listerThings.AllThings.ToList())
            {
                if (!(t is Plant_Poppy p)) continue;
                p.Growth = 1f;
                map.mapDrawer.MapMeshDirty(p.Position, MapMeshFlagDefOf.Things);
            }
            // clear the bodies so the flowers read
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).ToList()) t.Destroy();
            if (centre.IsValid) Find.CameraDriver.SetRootPosAndSize(centre.ToVector3Shifted(), 9f);
            Log.Message($"[Showcase] staged {made} poppies");
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
                Pawn hero = LastStandTracker.ActiveHero ?? map.mapPawns.FreeColonistsSpawned.FirstOrDefault(CombatUtil.IsFighter);
                if (hero != null)
                {
                    Find.CameraDriver.SetRootPosAndSize(hero.DrawPos + new Vector3(0f, 0f, 2f), 12f);
                    FeatTracker.Instance.TryAward(hero, GestaDefOf.Gesta_Feat_Kingslayer);
                }
            }
            if (framesSinceTrigger == 560) StagePoppies();
            if (framesSinceTrigger == 720) StageAsh();
            if (framesSinceTrigger == 880) StageTide();
            if (framesSinceTrigger == 1720) EndTide();
            if (framesSinceTrigger > 1720 && goldFrame < 0 && map.GetComponent<TideWatcher>().Gold) goldFrame = framesSinceTrigger;
            if (goldFrame > 0 && framesSinceTrigger == goldFrame + 150)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, "tide-2-gold.png"));
                Log.Message("[Showcase] captured tide-2-gold");
            }
            if ((goldFrame > 0 && framesSinceTrigger == goldFrame + 200) || framesSinceTrigger == 3500) bloodFrame = framesSinceTrigger;
            if (bloodFrame > 0 && framesSinceTrigger == bloodFrame + 1) StageBlood();
            if (bloodFrame > 0 && framesSinceTrigger == bloodFrame + 60)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, "blood.png"));
                Log.Message("[Showcase] captured blood");
            }
            if (bloodFrame > 0 && framesSinceTrigger == bloodFrame + 100) StageBrothers();
            if (bloodFrame > 0 && framesSinceTrigger == bloodFrame + 170)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, "brothers.png"));
                Log.Message("[Showcase] captured brothers");
            }
            if (bloodFrame > 0 && framesSinceTrigger == bloodFrame + 220)
            {
                Log.Message("[Showcase] done");
                framesSinceTrigger = -1;
            }
        }
    }
}
