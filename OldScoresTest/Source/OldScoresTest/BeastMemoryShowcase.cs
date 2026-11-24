using System.IO;
using System.Linq;
using OldScores;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace OldScoresTest
{
    /// <summary>
    /// The Beast Remembers, visual check (same "showcase.flag", never with --ticks), after the Raid Echoes
    /// showcase: a colony husky hurt by a raider growls at him once he is a prisoner 4 cells away (red bubble),
    /// then a raider it dragged down sees it from his spot and is afraid. Shots in logs/showcase/oldscores/.
    /// </summary>
    public class BeastMemoryShowcase : GameComponent
    {
        const string ShotDir = "D:/rimworld/logs/showcase/oldscores";

        bool active, finished;
        int stage = -1, stageTick, frame = -1;
        Map map;
        IntVec3 origin;
        Pawn husky, man, downed;

        public BeastMemoryShowcase(Game game) { }

        public override void FinalizeInit()
        {
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.oldscorestest");
            active = me != null && File.Exists(Path.Combine(me.RootDir, "showcase.flag")) && !Kit.HarnessArmed;
            if (active) Log.Message("[Showcase] The Beast Remembers showcase armed; starts after the Raid Echoes showcase");
        }

        int Now => Find.TickManager.TicksGame;
        OldScoresTracker T => OldScoresTracker.Instance;

        public override void GameComponentTick()
        {
            if (!active || finished) return;
            try { Tick(); }
            catch (System.Exception ex) { Log.Error("[Showcase] The Beast Remembers showcase failed: " + ex); finished = true; }
        }

        void Tick()
        {
            if (stage == -1)
            {
                EchoShowcase echoes = Current.Game.GetComponent<EchoShowcase>();
                OldScoresShowcase first = Current.Game.GetComponent<OldScoresShowcase>();
                if ((echoes != null && !echoes.Finished) || (first != null && !first.Finished)) return;
                if (OldScoresTracker.Instance != null) OldScoresTracker.Instance.showEchoes = false;
                Setup();
                return;
            }
            if (stage == 0 && OldScoresTracker.LastGrowlBubble != null && frame < 0)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;   // hold the bubble for the shot
                frame = 0;
                Log.Message($"[Showcase] growl after {Now - stageTick} ticks");
            }
            if (stage == 0 && frame < 0 && Now - stageTick > 600) { Log.Message("[Showcase] no growl within 600 ticks"); StartFear(); }
            if (stage == 1 && frame < 0 && downed.needs?.mood?.thoughts.memories.GetFirstMemoryOfDef(OldScoresDefOf.OldScores_FearOfBeast) != null)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                frame = 0;
                Log.Message($"[Showcase] fear after {Now - stageTick} ticks; resistance {downed.guest?.resistance:0.0}");
            }
            if (stage == 1 && frame < 0 && Now - stageTick > 600) { Log.Message("[Showcase] no fear within 600 ticks"); Done(); }
        }

        void Setup()
        {
            map = Find.CurrentMap;
            Faction enemy = TestUtil.HostileFaction();
            Pawn anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            IntVec3 near = anchor?.Position ?? map.Center;
            near = new IntVec3(Mathf.Clamp(near.x + (near.x < map.Size.x / 2 ? 25 : -25), 20, map.Size.x - 20), 0, Mathf.Clamp(near.z, 20, map.Size.z - 20));
            origin = TestUtil.PrepareStrip(map, near, 4, 10, 3);
            if (!origin.IsValid || enemy == null) { Log.Error("[Showcase] beast memory: no strip/enemy"); finished = true; return; }
            husky = PawnGenerator.GeneratePawn(PawnKindDef.Named("Husky"), Faction.OfPlayer);
            husky.Name = new NameSingle("Grimm");
            GenSpawn.Spawn(husky, origin, map);
            man = Kit.SpawnHostile(map, origin + new IntVec3(6, 0, 0), null, enemy);
            man.equipment?.DestroyAllEquipment();
            husky.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 4f, 0f, -1f, man));
            man.guest.CapturedBy(Faction.OfPlayer, anchor);
            Place(husky, origin);
            Place(man, origin + new IntVec3(4, 0, 0));
            OldScoresTracker.LastGrowlBubble = null;
            Find.Selector.ClearSelection();
            Find.CameraDriver.SetRootPosAndSize(origin.ToVector3Shifted() + new Vector3(2f, 0f, 0f), 6f);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            stage = 0;
            stageTick = Now;
            Log.Message($"[Showcase] {husky.LabelShort} remembers {man.LabelShort}: {T.GetGrudge(husky.thingIDNumber, man.thingIDNumber) != null}");
        }

        void StartFear()
        {
            frame = -1;
            if (man != null && !man.Dead) { man.Kill(null); man.Corpse?.Destroy(); }
            Faction enemy = TestUtil.HostileFaction();
            downed = Kit.SpawnHostile(map, origin + new IntVec3(5, 0, 1), null, enemy);
            downed.equipment?.DestroyAllEquipment();
            downed.health.overrideDeathOnDownedChance = 0f;
            for (int i = 0; i < 60 && !downed.Downed && !downed.Dead; i++)
            {
                BodyPartRecord leg = downed.health.hediffSet.GetNotMissingParts().FirstOrDefault(p => p.def == BodyPartDefOf.Leg);
                if (leg == null) break;
                downed.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 5f, 0f, -1f, husky, leg));
            }
            downed.guest.CapturedBy(Faction.OfPlayer, map.mapPawns.FreeColonistsSpawned.FirstOrDefault());
            Place(husky, downed.Position + new IntVec3(-3, 0, 0));
            Find.Selector.ClearSelection();
            Find.Selector.Select(downed, playSound: false);   // the needs tab is not open, but the inspect pane names him
            Find.CameraDriver.SetRootPosAndSize(downed.DrawPos + new Vector3(-1.5f, 0f, 0f), 6f);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            stage = 1;
            stageTick = Now;
            Log.Message($"[Showcase] {downed.LabelShort} downed {downed.Downed}, fear record {T.GetFear(downed.thingIDNumber) != null}");
        }

        public override void GameComponentUpdate()
        {
            if (!active || finished || frame < 0) return;
            frame++;
            if (stage == 0)
            {
                if (frame == 12) Shot("8-growl-bubble");
                if (frame == 16) { Find.TickManager.CurTimeSpeed = TimeSpeed.Normal; StartFear(); }
            }
            else if (stage == 1)
            {
                if (frame == 12) Shot("9-fear-of-the-beast");
                if (frame == 16) Done();
            }
        }

        void Place(Pawn p, IntVec3 c)
        {
            TestUtil.Teleport(p, c);
            if (!p.Downed && p.jobs != null)
            {
                Job j = JobMaker.MakeJob(JobDefOf.Wait);
                j.expiryInterval = 3000;
                p.jobs.StartJob(j, JobCondition.InterruptForced);
            }
        }

        void Shot(string name)
        {
            string path = Path.Combine(ShotDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log.Message("[Showcase] captured " + path);
        }

        void Done()
        {
            finished = true;
            frame = -1;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            Log.Message("[Showcase] The Beast Remembers showcase done");
        }
    }
}
