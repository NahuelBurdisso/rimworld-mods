using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aftermath;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AftermathTest
{
    /// <summary>
    /// Visual check, armed by "showcase.flag" in this mod's folder on a live `rw launch --quicktest --wait`
    /// (never with --ticks). Stages the five features on cleared strips and captures screenshots into
    /// D:/rimworld/logs/showcase/aftermath/. Starts after game tick 6000 and once Old Scores' showcase
    /// (same game, same camera) has finished.
    /// </summary>
    public class AftermathShowcase : GameComponent
    {
        const string ShotDir = "D:/rimworld/logs/showcase/aftermath";   // test-only, hardcoded on purpose
        const int StartTick = 6000, StartAnywayTick = 30000;

        bool active, started, finished;
        int frame = -1;
        Map map;
        Faction enemy;
        readonly List<Pawn> extras = new List<Pawn>();

        public AftermathShowcase(Game game) { }

        public override void FinalizeInit()
        {
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.aftermathtest");
            active = me != null && File.Exists(Path.Combine(me.RootDir, "showcase.flag")) && !Kit.HarnessArmed;
            if (!active) return;
            Directory.CreateDirectory(ShotDir);
            Log.Message("[Showcase] Aftermath showcase armed; starts after tick " + StartTick + " once Old Scores' showcase is done");
        }

        public override void GameComponentTick()
        {
            if (!active || started) return;
            int now = Find.TickManager.TicksGame;
            if (now < StartTick) return;
            if (now < StartAnywayTick && !OldScoresShowcaseDone()) return;
            started = true;
            frame = 0;
            Log.Message($"[Showcase] Aftermath showcase starting at tick {now}");
        }

        /// <summary>Old Scores' showcase (if loaded and armed) moves the camera too: wait until it finished.</summary>
        static bool OldScoresShowcaseDone()
        {
            GameComponent g = Current.Game.components.FirstOrDefault(c => c.GetType().FullName == "OldScoresTest.OldScoresShowcase");
            if (g == null) return true;
            var t = Traverse.Create(g);
            if (!t.Field("active").GetValue<bool>()) return true;
            return t.Field("finished").GetValue<bool>();
        }

        public override void GameComponentUpdate()
        {
            if (frame < 0 || finished) return;
            frame++;
            try { Run(); }
            catch (System.Exception ex)
            {
                Log.Error("[Showcase] Aftermath showcase failed: " + ex);
                Done();
            }
        }

        void Run()
        {
            switch (frame)
            {
                case 1: Begin(); Stage("door", StageDoor); break;
                case 70: Shot("1-braced-door"); break;
                case 80: Stage("standard", StageStandard); break;
                case 150: Shot("2-trophy-standard"); break;
                case 160: Stage("foe", StageFoe); break;
                case 230: Shot("3-honored-foe"); break;
                case 240: Stage("vigil", StageVigil); break;
                case 330: Shot("4-vigil-lamp"); break;
                case 340: Stage("offerings", StageOfferings); break;
                case 410: Shot("5-bedside-offerings"); break;
                case 420: Done(); break;
            }
        }

        void Begin()
        {
            map = Find.CurrentMap;
            enemy = TestUtil.HostileFaction();
            Log.Message($"[Showcase] enemy faction: {enemy?.Name ?? "none"} (member kind {enemy?.def?.basicMemberKind?.defName ?? "none"})");
        }

        /// <summary>Runs one stage; a failure is logged and skipped so the next stages still run and get captured.</summary>
        void Stage(string name, System.Action stage)
        {
            skipped = false;
            try { stage(); }
            catch (System.Exception ex)
            {
                skipped = true;
                Log.Error($"[Showcase] FAIL stage {name} skipped: {ex}");
            }
        }

        bool skipped;

        /// <summary>A raider-type kind for the enemy faction, never null.</summary>
        PawnKindDef EnemyKind => enemy?.def?.basicMemberKind ?? PawnKindDefOf.Pirate;

        /// <summary>The enemy faction, or a FAIL line and false (the stage is skipped).</summary>
        bool HaveEnemy(string stage)
        {
            if (enemy != null) return true;
            Log.Error($"[Showcase] FAIL stage {stage} skipped: no hostile humanlike faction in this world");
            skipped = true;
            return false;
        }

        Pawn AnyColonist => map.mapPawns.FreeColonistsSpawned.FirstOrDefault();

        void Shot(string name)
        {
            if (skipped) { Log.Message("[Showcase] (no capture for " + name + ": its stage was skipped)"); return; }
            string path = Path.Combine(ShotDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log.Message("[Showcase] captured " + path);
        }

        void Look(Thing select, Vector3 at, float size)
        {
            Find.Selector.ClearSelection();
            if (select != null) Find.Selector.Select(select, playSound: false);
            Find.CameraDriver.SetRootPosAndSize(at, size);
            if (Find.TickManager.CurTimeSpeed != TimeSpeed.Normal) Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        }

        IntVec3 Strip(int dx, int dz, int w, int h, string who)
        {
            IntVec3 r = TestUtil.PrepareStrip(map, TestUtil.Anchor(map, dx, dz), w, h, "showcase " + who);
            if (!r.IsValid) Log.Error("[Showcase] no strip for " + who);
            return r;
        }

        // 1. a wall with a door that raiders broke twice (two raids), rebuilt: two crossed planks
        void StageDoor()
        {
            if (finished) return;
            if (!HaveEnemy("door")) return;
            IntVec3 r = Strip(-20, 18, 7, 4, "door");
            if (!r.IsValid) { skipped = true; return; }
            for (int x = 0; x < 7; x++)
                if (x != 3) TestUtil.SpawnBuilding<Thing>(ThingDefOf.Wall, ThingDefOf.WoodLog, r + new IntVec3(x, 0, 2), map);
            IntVec3 cell = r + new IntVec3(3, 0, 2);
            Pawn raider = TestUtil.SpawnAt(map, r + new IntVec3(6, 0, 0), enemy, EnemyKind);
            raider.equipment?.DestroyAllEquipment();
            Thing door = TestUtil.SpawnBuilding<Thing>(ThingDefOf.Door, ThingDefOf.WoodLog, cell, map);
            door.Kill(new DamageInfo(DamageDefOf.Blunt, 99999f, 0f, -1f, raider));
            BreachTracker bt = BreachTracker.For(map);
            if (bt != null) foreach (BreachRecord b in bt.At(cell)) b.tick -= 20 * GenDate.TicksPerDay;
            door = TestUtil.SpawnBuilding<Thing>(ThingDefOf.Door, ThingDefOf.WoodLog, cell, map);
            door.Kill(new DamageInfo(DamageDefOf.Blunt, 99999f, 0f, -1f, raider));
            door = TestUtil.SpawnBuilding<Thing>(ThingDefOf.Door, ThingDefOf.WoodLog, cell, map);
            raider.Kill(null);
            raider.Corpse?.Destroy();
            foreach (Thing t in map.thingGrid.ThingsListAt(cell).Where(t => t.def.category == ThingCategory.Item || t.def.IsFilth).ToList()) t.Destroy();
            Look(door, cell.ToVector3Shifted(), 5f);
            Log.Message($"[Showcase] door: {door.TryGetComp<CompBracedDoor>()?.VisiblePlanks()} planks");
        }

        // 2. a captured standard, installed
        void StageStandard()
        {
            if (finished) return;
            if (!HaveEnemy("standard")) return;
            IntVec3 r = Strip(20, 18, 5, 4, "standard");
            if (!r.IsValid) { skipped = true; return; }
            Pawn leader = PawnGenerator.GeneratePawn(EnemyKind, enemy);
            Pawn hero = AnyColonist ?? leader;
            var s = (Building_CapturedStandard)ThingMaker.MakeThing(AftermathDefOf.Aftermath_CapturedStandard);
            s.Init(leader, hero, new List<int> { hero.thingIDNumber }, map);
            if (!leader.Spawned && !leader.Destroyed) Find.WorldPawns.PassToWorld(leader, RimWorld.Planet.PawnDiscardDecideMode.Discard);
            IntVec3 at = r + new IntVec3(2, 0, 1);
            GenSpawn.Spawn(s, at, map);
            s.SetFaction(Faction.OfPlayer);
            Look(s, at.ToVector3Shifted() + new Vector3(0f, 0f, 0.4f), 4f);
            Log.Message($"[Showcase] standard of {s.factionName}, colour {s.clothColor}, icon {enemy.def.factionIconPath}");
        }

        // 3. a worthy foe in his grave under his rifle and helmet
        void StageFoe()
        {
            if (finished) return;
            if (!HaveEnemy("foe")) return;
            IntVec3 r = Strip(-20, -14, 6, 5, "foe");
            if (!r.IsValid) { skipped = true; return; }
            Pawn foe = TestUtil.SpawnAt(map, r + new IntVec3(5, 0, 4), enemy, EnemyKind);
            foe.equipment?.DestroyAllEquipment();
            ThingDef rifle = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_AssaultRifle");
            ThingDef helmet = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_SimpleHelmet");
            if (rifle != null && foe.equipment != null) foe.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(rifle));
            if (helmet != null && foe.apparel != null) foe.apparel.Wear((Apparel)ThingMaker.MakeThing(helmet, ThingDefOf.Steel), dropReplacedApparel: false);
            var victims = new List<Pawn>();
            for (int i = 0; i < 2; i++)
            {
                Pawn v = TestUtil.NewColonist(map, r + new IntVec3(i, 0, 4));
                extras.Add(v);
                victims.Add(v);
                TestUtil.DownBy(v, foe);
            }
            Kit.KillBy(foe, victims.FirstOrDefault() ?? AnyColonist);
            foreach (Pawn v in victims) TestUtil.Heal(v);
            var grave = TestUtil.SpawnBuilding<Building_Grave>(ThingDefOf.Grave, null, r + new IntVec3(2, 0, 1), map, Rot4.South);
            Corpse c = foe.Corpse;
            if (c != null)
            {
                if (c.Spawned) c.DeSpawn();
                grave.TryAcceptThing(c);
            }
            Look(grave, grave.TrueCenter(), 4f);
            Log.Message($"[Showcase] grave holds {c?.InnerPawn?.LabelShort}: marker {grave.GetComp<CompHonoredFoe>()?.Marker(out _, out _)}");
        }

        // 4. a dying patient under a roof (dark): the faint amber light on the bed
        void StageVigil()
        {
            if (finished) return;
            IntVec3 r = Strip(20, -14, 7, 7, "vigil");
            if (!r.IsValid) { skipped = true; return; }
            for (int x = 0; x < 7; x++)
                for (int z = 0; z < 7; z++)
                    map.roofGrid.SetRoof(r + new IntVec3(x, 0, z), RoofDefOf.RoofConstructed);
            var bed = TestUtil.SpawnBuilding<Building_Bed>(ThingDefOf.Bed, ThingDefOf.WoodLog, r + new IntVec3(3, 0, 4), map, Rot4.South);
            Pawn p = TestUtil.NewColonist(map, r + new IntVec3(1, 0, 1));
            extras.Add(p);
            Hediff h = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, p);
            h.Severity = 0.65f;
            p.health.AddHediff(h);
            p.jobs.Notify_TuckedIntoBed(bed);
            VigilTracker vt = VigilTracker.For(map);
            vt?.Poll();
            Look(null, bed.TrueCenter(), 6f);
            Log.Message($"[Showcase] vigil: light {(vt?.GlowerAt(bed) != null)} on {bed.Position}");
        }

        // 5. a wounded colonist surrounded by gifts
        void StageOfferings()
        {
            if (finished) return;
            IntVec3 r = Strip(0, 22, 5, 5, "offerings");
            if (!r.IsValid) { skipped = true; return; }
            var bed = TestUtil.SpawnBuilding<Building_Bed>(ThingDefOf.Bed, ThingDefOf.WoodLog, r + new IntVec3(2, 0, 3), map, Rot4.South);
            Pawn p = TestUtil.NewColonist(map, r + new IntVec3(0, 0, 0));
            extras.Add(p);
            p.jobs.Notify_TuckedIntoBed(bed);
            ThingDef[] defs = { AftermathDefOf.Aftermath_Offering_Flowers, AftermathDefOf.Aftermath_Offering_Letter, AftermathDefOf.Aftermath_Offering_Figure, AftermathDefOf.Aftermath_Offering_Candle };
            string[] labels = { "lover", "sister", "friend", "" };
            List<Pawn> visitors = map.mapPawns.FreeColonistsSpawned.Where(x => x != p).Take(4).ToList();
            List<IntVec3> cells = GenAdj.CellsAdjacent8Way(bed).Where(c => c.Standable(map) && c.GetFirstItem(map) == null).ToList();
            Offering first = null;
            if (visitors.Count == 0) Log.Message("[Showcase] offerings: no other colonists to name as visitors");
            for (int i = 0; i < defs.Length && i < cells.Count && i < visitors.Count; i++)
            {
                var o = (Offering)ThingMaker.MakeThing(defs[i]);
                o.Init(p, visitors[i], labels[i], map);
                GenSpawn.Spawn(o, cells[(i * 2) % cells.Count], map);
                first ??= o;
            }
            Look(first, bed.TrueCenter(), 4f);
        }

        void Done()
        {
            finished = true;
            frame = -1;
            Log.Message("[Showcase] done");
        }
    }
}
