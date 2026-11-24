using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace OmensTest
{
    /// <summary>
    /// A harness armed by `rw launch --ticks N`: waits for its start tick, then runs Step() every tick until
    /// Finish(). Exceptions and the deadline become FAILs, never hangs.
    /// </summary>
    public abstract class HarnessBase : GameComponent
    {
        protected bool active, done;
        protected int ticks, phase, phaseTick;
        protected Map map;
        int passedAtStart, failedAtStart;

        protected abstract int StartTick { get; }
        protected abstract string Name { get; }
        /// <summary>Another harness that must finish first (null = none).</summary>
        protected virtual string After => null;

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (ticks < StartTick) return;
            if (map == null && After != null && !TestUtil.Finished(After) && ticks < TestUtil.Deadline - 1500) return;
            if (ticks > TestUtil.Deadline)
            {
                Kit.Check(false, $"{Name}: still waiting in phase {phase} at the deadline ({Facts()})");
                Finish();
                return;
            }
            try
            {
                if (map == null)
                {
                    map = Find.CurrentMap;
                    passedAtStart = Kit.Passed;
                    failedAtStart = Kit.Failed;
                    phaseTick = ticks;
                    Log.Message($"[Test] {Name} harness starting at tick {ticks}");
                }
                Step();
            }
            catch (System.Exception ex)
            {
                Kit.Check(false, $"{Name} harness threw in phase {phase}: {ex}");
                Finish();
            }
        }

        protected void Next()
        {
            phase++;
            phaseTick = ticks;
        }

        protected int InPhase => ticks - phaseTick;

        protected abstract void Step();

        protected virtual string Facts() => "";

        protected virtual void Cleanup() { }

        /// <summary>Waits in a phase: true when <paramref name="cond"/> holds; FAIL + skip ahead after <paramref name="timeout"/>.</summary>
        protected bool WaitFor(bool cond, int timeout, string what)
        {
            if (cond) return true;
            if (InPhase < timeout) return false;
            Kit.Check(false, $"{Name}: timed out after {timeout} ticks waiting for {what} ({Facts()})");
            return true;
        }

        protected void Finish()
        {
            if (done) return;
            done = true;
            try { Cleanup(); }
            catch (System.Exception ex) { Kit.Check(false, $"{Name} cleanup threw: {ex}"); }
            int p = Kit.Passed - passedAtStart, f = Kit.Failed - failedAtStart;
            if (f == 0) Log.Message($"[Test] DONE Omens/{Name}: {p} passed");
            else Log.Error($"[Test] DONE Omens/{Name}: {p} passed, {f} FAILED");
            TestUtil.MarkFinished(Name);
            if (TestUtil.AllFinished) Kit.Summary("Omens (all harnesses)");
        }
    }

    public static class TestUtil
    {
        public const int Deadline = 5600;
        public const int HarnessCount = 2;
        static readonly HashSet<string> finished = new HashSet<string>();
        public static void MarkFinished(string name) => finished.Add(name);
        public static bool Finished(string name) => finished.Contains(name);
        public static bool AllFinished => finished.Count >= HarnessCount;

        public static int Now => Find.TickManager.TicksGame;

        public static void Teleport(Pawn p, IntVec3 cell)
        {
            p.Position = cell;
            p.Notify_Teleported();
            p.jobs?.StopAll();
        }

        /// <summary>A hostile humanlike faction that can stage attacks and raid; pirates first.</summary>
        public static Faction StagingFaction()
        {
            return Find.FactionManager.AllFactionsListForReading.Where(f =>
                    !f.IsPlayer && !f.Hidden && !f.defeated && f.def.humanlikeFaction && f.def.canStageAttacks
                    && f.HostileTo(Faction.OfPlayer)
                    && f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Any(g => g.kindDef == PawnGroupKindDefOf.Combat))
                .OrderByDescending(f => f == Faction.OfPirates).FirstOrDefault();
        }

        public static RaidStrategyDef StageThenAttack => DefDatabase<RaidStrategyDef>.GetNamed("StageThenAttack");

        /// <summary>A real raid (IncidentWorker_RaidEnemy); returns the raiders it spawned.</summary>
        public static List<Pawn> Raid(Map map, Faction faction, float points, RaidStrategyDef strategy)
        {
            List<Pawn> before = map.mapPawns.SpawnedPawnsInFaction(faction).ToList();
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = faction;
            parms.raidStrategy = strategy;
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            parms.points = points;
            parms.forced = true;
            TimeSpeed speed = Find.TickManager.CurTimeSpeed;
            bool ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            if (Find.TickManager.CurTimeSpeed == TimeSpeed.Paused && speed != TimeSpeed.Paused) Find.TickManager.CurTimeSpeed = speed;
            if (!ok) return new List<Pawn>();
            return map.mapPawns.SpawnedPawnsInFaction(faction).Where(p => !before.Contains(p)).ToList();
        }

        /// <summary>As DebugActionsMisc.ForceEnemyAssault (Verse/DebugActionsMisc.cs:390), for one lord.</summary>
        public static bool ForceAssault(Lord lord)
        {
            if (!(lord?.CurLordToil is LordToil_Stage stage)) return false;
            foreach (Transition t in lord.Graph.transitions)
                if (t.sources.Contains(stage) && (t.target is LordToil_AssaultColony || t.target is LordToil_AssaultColonyBreaching
                    || t.target is LordToil_AssaultColonyPrisoners || t.target is LordToil_AssaultColonySappers))
                {
                    lord.GotoToil(t.target);
                    return true;
                }
            return false;
        }

        public static int CirclingNear(Map map, IntVec3 c, float radius)
        {
            int n = 0;
            foreach (Omens.CrowMote m in Omens.CrowMote.Live)
                if (m.Spawned && m.Map == map && !m.Departing && (m.exactPosition - c.ToVector3Shifted()).MagnitudeHorizontal() <= radius) n++;
            return n;
        }

        public static int Departing(Map map)
        {
            int n = 0;
            foreach (Omens.CrowMote m in Omens.CrowMote.Live)
                if (m.Spawned && m.Map == map && m.Departing) n++;
            return n;
        }

        /// <summary>Saves an IExposable with Scribe to a temp file and loads it back (values and deep lists only).</summary>
        public static T Roundtrip<T>(T obj) where T : class, IExposable
        {
            string path = Path.Combine(GenFilePaths.TempFolderPath, "omens_roundtrip.xml");
            Scribe.saver.InitSaving(path, "OmensTest");
            Scribe_Deep.Look(ref obj, "rec");
            Scribe.saver.FinalizeSaving();
            T copy = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref copy, "rec");
            Scribe.loader.FinalizeLoading();
            try { File.Delete(path); } catch { }
            return copy;
        }
    }

    /// <summary>Raid letters pause the game under default auto-pause: resume while harnesses run.</summary>
    public class HarnessWatchdog : GameComponent
    {
        bool active;
        int resumes;

        public HarnessWatchdog(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentUpdate()
        {
            if (!active || TestUtil.AllFinished || Find.TickManager == null) return;
            if (Find.TickManager.CurTimeSpeed != TimeSpeed.Paused) return;
            TimeSpeed back = Find.TickManager.prePauseTimeSpeed;
            Find.TickManager.CurTimeSpeed = back == TimeSpeed.Paused ? TimeSpeed.Normal : back;
            if (resumes++ < 20) Log.Message($"[Test] (watchdog: paused at tick {Find.TickManager.TicksGame}; resumed)");
        }
    }
}
