using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using OldScores;
using RimWorld;
using UnityEngine;
using Verse;

namespace OldScoresTest
{
    /// <summary>
    /// Visual check, armed by "showcase.flag" in this mod's folder on a live `rw launch --quicktest --wait`
    /// (never with --ticks). Stages the three features on a cleared strip away from the colony and
    /// captures screenshots into D:/rimworld/logs/showcase/oldscores/. Waits for game tick 3000 and for
    /// Gesta's showcase (same game, same camera) to finish.
    /// </summary>
    public class OldScoresShowcase : GameComponent
    {
        const string ShotDir = "D:/rimworld/logs/showcase/oldscores";   // test-only, hardcoded on purpose
        const int StartTick = 3000, StartAnywayTick = 20000;

        bool active, started, finished;
        int ticks;
        int frame = -1;
        Map map;
        Faction enemy;
        IntVec3 origin;
        Pawn hero, raider, wolf;
        Thing trophy;
        readonly List<Pawn> spawned = new List<Pawn>();

        public OldScoresShowcase(Game game) { }

        public override void FinalizeInit()
        {
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.oldscorestest");
            active = me != null && File.Exists(Path.Combine(me.RootDir, "showcase.flag")) && !Kit.HarnessArmed;
            if (!active) return;
            Directory.CreateDirectory(ShotDir);
            Log.Message("[Showcase] Old Scores showcase armed; starts at tick " + StartTick + " once Gesta's showcase is done");
        }

        public override void GameComponentTick()
        {
            if (!active || started) return;
            ticks++;
            if (ticks < StartTick) return;
            if (ticks < StartAnywayTick && !GestaShowcaseDone()) return;
            started = true;
            frame = 0;
            Log.Message($"[Showcase] Old Scores showcase starting at tick {ticks}");
        }

        /// <summary>Gesta's showcase (if loaded and armed) moves the camera too: wait until it logged done.</summary>
        static bool GestaShowcaseDone()
        {
            GameComponent g = Current.Game.components.FirstOrDefault(c => c.GetType().FullName == "GestaTest.GestaShowcase");
            if (g == null) return true;
            var t = Traverse.Create(g);
            if (!t.Field("active").GetValue<bool>()) return true;
            return t.Field("started").GetValue<bool>() && t.Field("framesSinceTrigger").GetValue<int>() < 0;
        }

        public override void GameComponentUpdate()
        {
            if (frame < 0 || finished) return;
            frame++;
            try { Run(); }
            catch (System.Exception ex)
            {
                Log.Error("[Showcase] Old Scores showcase failed: " + ex);
                Done();
            }
        }

        void Run()
        {
            switch (frame)
            {
                case 1: StageNickname(); break;
                case 240: StageDread(); break;
                case 262: Shot("1-dread-mote"); break;
                case 330: Shot("1-dread-fleeing"); break;
                case 360: StageBeast(); break;
                case 425: Shot("2-beast-label"); break;
                case 460: StageTrophy(); break;
                case 525: Shot("3-trophy"); break;
                case 540: Done(); break;
            }
        }

        void Shot(string name)
        {
            string path = Path.Combine(ShotDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log.Message("[Showcase] captured " + path);
        }

        void Normal()
        {
            if (Find.TickManager.CurTimeSpeed != TimeSpeed.Normal) Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        }

        // 1. a colonist the enemy has nicknamed (real kills), on a strip away from the colony centre
        void StageNickname()
        {
            map = Find.CurrentMap;
            OldScoresTracker T = OldScoresTracker.Instance;
            enemy = TestUtil.HostileFaction(f => !T.Nicknames.Any(r => r.faction == f && r.Named));
            if (enemy == null) { Log.Error("[Showcase] no hostile humanlike faction"); Done(); return; }
            Pawn anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            IntVec3 near = anchor?.Position ?? map.Center;
            // away from the colony: 30 cells towards the far side of the map, clamped into bounds
            int dx = near.x < map.Size.x / 2 ? 30 : -30;
            near = new IntVec3(Mathf.Clamp(near.x + dx, 20, map.Size.x - 20), 0, Mathf.Clamp(near.z, 20, map.Size.z - 20));
            origin = TestUtil.PrepareStrip(map, near, 4, 12, 3);
            if (!origin.IsValid) { Log.Error("[Showcase] no strip"); Done(); return; }

            hero = TestUtil.NewColonist(map, origin, p => p.GetStatValue(StatDefOf.IncomingDamageFactor) >= 0.99f);
            TestUtil.Teleport(hero, origin);
            hero.drafter.Drafted = true;
            // the five victims die out of frame (25 cells north or south, inside the map) and their bodies vanish
            IntVec3 killSpot = new IntVec3(origin.x, 0, origin.z > map.Size.z / 2 ? origin.z - 25 : origin.z + 25);
            for (int i = 0; i < OldScoresTuning.KillsForNickname; i++)
            {
                Pawn h = Kit.SpawnHostile(map, killSpot, null, enemy);
                Kit.KillBy(h, hero);
                h.Corpse?.Destroy();
            }
            NicknameRecord rec = T.GetNickname(hero, enemy);
            Log.Message($"[Showcase] {hero.LabelShort} is known to {enemy.Name} as {rec?.nickname ?? "(no nickname!)"}");
            Find.CameraDriver.SetRootPosAndSize(origin.ToVector3Shifted() + new Vector3(3f, 0f, 0f), 9f);
            Find.Selector.ClearSelection();
            Normal();
        }

        // 1b. a raider of that faction close by: dread, "!" mote, flee
        void StageDread()
        {
            float old = OldScoresTuning.DreadChance;
            OldScoresTuning.DreadChance = 1f;
            raider = TestUtil.SpawnAt(map, origin + new IntVec3(6, 0, 0), enemy);
            raider.equipment?.DestroyAllEquipment();
            spawned.Add(raider);
            int n = OldScoresTracker.Instance.DreadFrom(hero);
            OldScoresTuning.DreadChance = old;
            Find.Selector.ClearSelection();
            Find.Selector.Select(hero, playSound: false);   // inspect pane shows "Known as ..."
            Find.CameraDriver.SetRootPosAndSize(origin.ToVector3Shifted() + new Vector3(3f, 0f, 0f), 8f);
            Normal();
            Log.Message($"[Showcase] dread: {n} raider(s) frightened, state {raider.MentalStateDef?.defName ?? "none"}");
        }

        // 2. a wild wolf mauls her, escapes, and comes back (the real incident worker) with its red label
        void StageBeast()
        {
            if (raider != null && !raider.Dead) { raider.Kill(null); raider.Corpse?.Destroy(); }
            Find.Selector.ClearSelection();
            wolf = PawnGenerator.GeneratePawn(PawnKindDef.Named("Wolf_Timber"), null);
            GenSpawn.Spawn(wolf, CellFinder.StandableCellNear(origin + new IntVec3(8, 0, 0), map, 3f), map);
            float dealt = 0f, amount = 15f;
            for (int i = 0; i < 3 && dealt < OldScoresTuning.BeastMinDamage && !hero.Dead; i++, amount *= 1.6f)
                dealt = Bite(hero, wolf, amount, i);
            wolf.ExitMap(false, Rot4.North);
            OldScoresTracker.Instance.ForceBeastDue(wolf);
            IntVec3 spot = CellFinder.StandableCellNear(origin + new IntVec3(9, 0, 0), map, 3f);
            var parms = new IncidentParms { target = map, forced = true, spawnCenter = spot };
            TimeSpeed speed = Find.TickManager.CurTimeSpeed;
            bool fired = OldScoresDefOf.OldScores_BeastReturns.Worker.TryExecute(parms);
            if (Find.TickManager.CurTimeSpeed == TimeSpeed.Paused) Find.TickManager.CurTimeSpeed = speed == TimeSpeed.Paused ? TimeSpeed.Normal : speed;
            Find.CameraDriver.SetRootPosAndSize(wolf.Spawned ? wolf.DrawPos : spot.ToVector3Shifted(), 6f);
            Normal();
            Log.Message($"[Showcase] beast: bite {dealt:0.#}, returned {fired}, name {wolf.Name?.ToStringShort ?? "-"}, spawned {wolf.Spawned}");
        }

        // 3. she kills it: the trophy lies next to the corpse
        void StageTrophy()
        {
            if (wolf == null || !wolf.Spawned) { Log.Message("[Showcase] trophy: the wolf is not on the map"); return; }
            IntVec3 at = wolf.Position;
            Kit.KillBy(wolf, hero);
            trophy = map.listerThings.ThingsOfDef(OldScoresDefOf.OldScores_BeastTrophy).OrderBy(t => t.Position.DistanceToSquared(at)).FirstOrDefault();
            Find.Selector.ClearSelection();
            if (trophy != null) Find.Selector.Select(trophy, playSound: false);   // inspect pane tells its story
            Find.CameraDriver.SetRootPosAndSize(at.ToVector3Shifted(), 4.5f);
            Normal();
            Log.Message($"[Showcase] trophy: {trophy?.LabelCap ?? "none"} — {trophy?.TryGetComp<CompBeastTrophy>()?.History() ?? ""}");
        }

        static float Bite(Pawn target, Pawn animal, float amount, int legIndex)
        {
            List<BodyPartRecord> legs = target.health.hediffSet.GetNotMissingParts().Where(p => p.def == BodyPartDefOf.Leg).ToList();
            BodyPartRecord leg = legs.Count == 0 ? null : legs[legIndex % legs.Count];
            return target.TakeDamage(new DamageInfo(DamageDefOf.Bite, amount, 2f, -1f, animal, leg)).totalDamageDealt;
        }

        void Done()
        {
            finished = true;
            frame = -1;
            if (hero != null && hero.Spawned && hero.drafter != null) hero.drafter.Drafted = false;
            Log.Message("[Showcase] done");
        }
    }
}
