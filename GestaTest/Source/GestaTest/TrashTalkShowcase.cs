using System.IO;
using System.Linq;
using Gesta;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace GestaTest
{
    /// <summary>
    /// Visual check for Trash Talk, armed by "showcase-trashtalk.flag" in this mod's folder on a live
    /// `--showcase` game (never with --ticks; the main showcase stands down while this flag exists).
    /// A raid stands 12 cells from a sharp-tongued colonist; the taunt, the reply, the break and the
    /// log tab are captured into D:/rimworld/logs/showcase/gesta/.
    /// </summary>
    public class TrashTalkShowcase : GameComponent
    {
        public const string Flag = "showcase-trashtalk.flag";
        bool active, staged;
        int ticks, frame = -1, replyFrame = -1, breakFrame = -1;
        int repliesAtStart;
        string shotDir;
        Map map;
        Pawn talker, target;
        Lord raid;
        IntVec3 a, b;

        public TrashTalkShowcase(Game game) { }

        public static bool Armed()
        {
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.gestatest");
            return me != null && File.Exists(Path.Combine(me.RootDir, Flag)) && !Kit.HarnessArmed;
        }

        public override void FinalizeInit()
        {
            active = Armed();
            if (!active) return;
            shotDir = "D:/rimworld/logs/showcase/gesta";
            Directory.CreateDirectory(shotDir);
        }

        public override void GameComponentTick()
        {
            if (!active) return;
            ticks++;
            if (raid != null) raid.lastPawnHarmTick = Find.TickManager.TicksGame;
            if (ticks == 120 && !staged)
            {
                staged = true;
                try { Stage(); } catch (System.Exception ex) { Log.Error("[Showcase] trash talk stage threw: " + ex); active = false; }
            }
        }

        bool Open(IntVec3 c) => c.InBounds(map) && c.Standable(map) && !c.Fogged(map) && c.GetFirstPawn(map) == null && !c.Roofed(map);

        void Stage()
        {
            map = Find.CurrentMap;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer)).ToList()) p.Kill(null);
            IntVec3 root = map.mapPawns.FreeColonistsSpawned.First().Position;
            bool found = false;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(root, 40f, true))
            {
                if (!Open(c) || !Open(c + IntVec3.East * 12) || !GenSight.LineOfSight(c, c + IntVec3.East * 12, map)) continue;
                if (GenRadial.RadialCellsAround(c + IntVec3.East * 12, 2f, true).Count(Open) < 9) continue;
                a = c; b = c + IntVec3.East * 12; found = true;
                break;
            }
            if (!found) { Log.Error("[Showcase] trash talk: no arena"); return; }

            var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = Faction.OfPirates ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            parms.points = 300f;
            parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            var before = map.mapPawns.AllPawnsSpawned.ToList();
            IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            raid = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => !before.Contains(p) && p.GetLord() != null && p.HostileTo(Faction.OfPlayer))?.GetLord();
            if (raid == null) { Log.Error("[Showcase] trash talk: no raid"); return; }
            int n = 0;
            foreach (Pawn p in raid.ownedPawns.ToList())
            {
                IntVec3 c = n++ == 0 ? b : CellFinder.StandableCellNear(b + new IntVec3(2, 0, 0), map, 2.5f, Open);
                if (!c.IsValid) { p.Destroy(); continue; }
                Kit.Teleport(p, c);
                p.equipment?.DestroyAllEquipment();
                Hold(p);
            }
            target = raid.ownedPawns.OrderBy(p => p.Position.DistanceToSquared(a)).First();

            for (int i = 0; i < 30; i++)
            {
                talker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                SkillRecord social = talker.skills?.GetSkill(SkillDefOf.Social);
                if (social != null && !social.TotallyDisabled && talker.DevelopmentalStage.Adult()) break;
                Find.WorldPawns.PassToWorld(talker, PawnDiscardDecideMode.Discard);
                talker = null;
            }
            if (talker == null) { Log.Error("[Showcase] trash talk: no talker"); return; }
            talker.skills.GetSkill(SkillDefOf.Social).Level = 18;
            foreach (Trait t in talker.story.traits.allTraits.ToList()) talker.story.traits.RemoveTrait(t);
            talker.story.traits.GainTrait(new Trait(TraitDefOf.Abrasive));
            GenSpawn.Spawn(talker, a, map);
            Kit.Teleport(talker, a);
            talker.drafter.Drafted = true;
            talker.Rotation = Rot4.East;

            Find.CameraDriver.SetRootPosAndSize(((a + b).ToVector3Shifted() / 2f) + new Vector3(0f, 0f, 1.5f), 9f);
            Find.Selector.ClearSelection();
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            TrashTalk.DebugRoll = 0.99f;
            TrashTalk.Instance.DebugResetCooldowns();
            repliesAtStart = TrashTalk.Instance.RepliesDelivered;
            string why = TrashTalk.Instance.TryExchange(raid, talker, target);
            Log.Message($"[Showcase] trash talk: {talker.LabelShort} -> {target.LabelShort}: {why ?? "ok"} ({TrashTalkBubbles.TextOn(talker)})");
            frame = 0;
        }

        /// <summary>Stands the raider still for the photo (a raid would charge the colonist).</summary>
        static void Hold(Pawn p)
        {
            Job wait = JobMaker.MakeJob(JobDefOf.Wait, 1500);
            p.jobs.StartJob(wait, JobCondition.InterruptForced);
            p.Rotation = Rot4.West;
        }

        void Shot(string name)
        {
            string path = Path.Combine(shotDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log.Message("[Showcase] captured " + path);
        }

        public override void GameComponentUpdate()
        {
            if (frame < 0) return;
            frame++;
            if (frame == 40) Shot("trashtalk-1-taunt");
            if (replyFrame < 0 && TrashTalk.Instance.RepliesDelivered > repliesAtStart)
            {
                replyFrame = frame;
                Log.Message($"[Showcase] reply: {TrashTalkBubbles.TextOn(target)}");
            }
            if (replyFrame > 0 && frame == replyFrame + 30)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;   // both boxes up: hold the moment
                Shot("trashtalk-2-reply");
            }
            if (replyFrame > 0 && frame == replyFrame + 60)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                TrashTalk.Instance.DebugResetCooldowns();
                foreach (Pawn p in raid.ownedPawns.ToList()) if (p.Spawned && !p.Downed) Hold(p);
                string why = TrashTalk.Instance.TryExchange(raid, talker, target);
                TauntRecord r = TrashTalk.Instance.LastExchange;
                Log.Message($"[Showcase] second taunt: {why ?? r.outcome.ToString()}, fled {r.fled?.LabelShort}");
                breakFrame = frame;
            }
            if (breakFrame > 0 && frame == breakFrame + 50) Shot("trashtalk-3-break");
            if (breakFrame > 0 && frame == breakFrame + 80)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Find.Selector.ClearSelection();
                Find.Selector.Select(talker);
                InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Log));
            }
            if (breakFrame > 0 && frame == breakFrame + 110) Shot("trashtalk-4-log");
            if (breakFrame > 0 && frame == breakFrame + 120)
            {
                TrashTalk.DebugRoll = null;
                Log.Message("[Showcase] trash talk done");
                frame = -1;
            }
        }
    }
}
