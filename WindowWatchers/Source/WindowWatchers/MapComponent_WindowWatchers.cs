using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WindowWatchers
{
    /// <summary>One watcher who watched one loved one during the current threat.</summary>
    public class WatchRecord : IExposable
    {
        public Pawn watcher;
        public Pawn target;
        public bool sawFall;
        public int reunionUntil = -1;

        public void ExposeData()
        {
            Scribe_References.Look(ref watcher, "watcher");
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref sawFall, "sawFall");
            Scribe_Values.Look(ref reunionUntil, "reunionUntil", -1);
        }
    }

    public struct WatchPlan
    {
        public Pawn target;
        public WatchSpot spot;
    }

    /// <summary>
    /// Scans every 250 ticks, only on player home maps. Tracks whether a threat is active (ended = two quiet scans in a
    /// row), plans a loved one and a spot for each watcher, nudges idle/casual watchers to re-think, keeps the
    /// "who watched whom" records and hands out relief + a pending embrace when the threat ends.
    /// </summary>
    public class MapComponent_WindowWatchers : MapComponent
    {
        public const int ScanInterval = 250;
        public const int QuietScansToEnd = 2;
        public const int ReunionWindow = 2500;

        bool threatActive;
        int quietScans;
        List<WatchRecord> records = new List<WatchRecord>();

        readonly Dictionary<Pawn, WatchPlan> plans = new Dictionary<Pawn, WatchPlan>();
        readonly HashSet<IntVec3> claimed = new HashSet<IntVec3>();
        readonly List<Pawn> tmpPawns = new List<Pawn>();
        readonly List<Pawn> tmpFighters = new List<Pawn>();
        bool patientsWaiting;

        static readonly Dictionary<Map, MapComponent_WindowWatchers> byMap = new Dictionary<Map, MapComponent_WindowWatchers>();

        // per watcher: no spot found -> do not search again before this tick (unless the loved one changes)
        readonly Dictionary<Pawn, int> noSpotUntil = new Dictionary<Pawn, int>();
        readonly Dictionary<Pawn, Pawn> noSpotTarget = new Dictionary<Pawn, Pawn>();
        public const int NoSpotRetry = 500;

        public MapComponent_WindowWatchers(Map map) : base(map) { byMap[map] = this; }

        public override void MapRemoved()
        {
            base.MapRemoved();
            byMap.Remove(map);
        }

        public bool ThreatActive => threatActive;
        public bool PatientsWaiting => patientsWaiting;
        public IReadOnlyList<WatchRecord> Records => records;

        public static MapComponent_WindowWatchers For(Map map)
        {
            if (map == null) return null;
            if (byMap.TryGetValue(map, out var comp)) return comp;
            comp = map.GetComponent<MapComponent_WindowWatchers>();
            if (comp != null) byMap[map] = comp;
            return comp;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref threatActive, "threatActive");
            Scribe_Values.Look(ref quietScans, "quietScans");
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                records ??= new List<WatchRecord>();
                records.RemoveAll(r => r == null || r.watcher == null || r.target == null);
            }
        }

        public override void MapComponentTick()
        {
            if ((Find.TickManager.TicksGame + map.uniqueID * 37) % ScanInterval != 0) return;
            Scan();
        }

        public bool TryGetPlan(Pawn p, out WatchPlan plan) => plans.TryGetValue(p, out plan);

        /// <summary>The scan, public so the test harness can run it on demand.</summary>
        public void Scan()
        {
            plans.Clear();
            if (!map.IsPlayerHome)
            {
                threatActive = false;
                quietScans = 0;
                return;
            }

            bool now = GenHostility.AnyHostileActiveThreatToPlayer(map);
            if (now)
            {
                if (!threatActive)
                {
                    // a new threat: hugs still pending from the last one are dropped, records start clean
                    records.RemoveAll(r => r.reunionUntil >= 0 || r.sawFall);
                    noSpotUntil.Clear();
                    noSpotTarget.Clear();
                }
                threatActive = true;
                quietScans = 0;
            }
            else if (threatActive)
            {
                if (++quietScans >= QuietScansToEnd)
                {
                    threatActive = false;
                    quietScans = 0;
                    OnThreatOver();
                }
            }
            PruneRecords();
            if (threatActive || records.Count > 0) patientsWaiting = ComputePatientsWaiting();
            if (!threatActive) return;

            tmpFighters.Clear();
            tmpPawns.Clear();
            tmpPawns.AddRange(map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer));
            for (int i = 0; i < tmpPawns.Count; i++)
                if (WatcherUtility.IsFighter(tmpPawns[i], map)) tmpFighters.Add(tmpPawns[i]);
            if (tmpFighters.Count == 0) { tmpPawns.Clear(); return; }

            claimed.Clear();

            // watchers already in place keep their spot (no shuffling), unless an enemy got too close
            for (int i = 0; i < tmpPawns.Count; i++)
            {
                Pawn p = tmpPawns[i];
                if (p.CurJobDef != WWDefOf.WW_Watch) continue;
                var job = p.CurJob;
                var door = job.targetC.Thing as Building_Door;
                if (WatcherUtility.ThreatTooClose(map, job.targetB.Cell, door))
                {
                    p.jobs.EndCurrentJob(Verse.AI.JobCondition.Incompletable);
                    continue;
                }
                claimed.Add(job.targetB.Cell);
                plans[p] = new WatchPlan { target = job.targetA.Pawn, spot = new WatchSpot { cell = job.targetB.Cell, door = door } };
            }
            int tick = Find.TickManager.TicksGame;

            for (int i = 0; i < tmpPawns.Count; i++)
            {
                Pawn p = tmpPawns[i];
                if (plans.ContainsKey(p)) continue;
                if (!WatcherUtility.CanBeWatcher(p)) continue;
                if (!WatcherUtility.NeedsAllowWatching(p, patientsWaiting)) continue;
                Pawn target = ChooseLovedOne(p);
                if (target == null) continue;
                if (noSpotUntil.TryGetValue(p, out int until) && until > tick && noSpotTarget.TryGetValue(p, out Pawn was) && was == target) continue;
                WatchSpot spot = WatchSight.FindSpot(p, target, claimed);
                if (!spot.IsValid)
                {
                    noSpotUntil[p] = tick + NoSpotRetry;
                    noSpotTarget[p] = target;
                    continue;
                }
                noSpotUntil.Remove(p);
                claimed.Add(spot.cell);
                plans[p] = new WatchPlan { target = target, spot = spot };
                if (WatcherUtility.CurrentJobInterruptible(p)) p.jobs.CheckForJobOverride();
            }
            tmpPawns.Clear();
        }

        Pawn ChooseLovedOne(Pawn watcher)
        {
            Pawn best = null;
            int bestScore = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < tmpFighters.Count; i++)
            {
                Pawn f = tmpFighters[i];
                if (f == watcher) continue;
                int s = WatcherUtility.BondScore(watcher, f);
                if (s <= 0) continue;
                float d = (f.Position - watcher.Position).LengthHorizontalSquared;
                if (s > bestScore || (s == bestScore && d < bestDist))
                {
                    best = f;
                    bestScore = s;
                    bestDist = d;
                }
            }
            return best;
        }

        bool ComputePatientsWaiting()
        {
            IReadOnlyList<Pawn> all = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p.Dead || LifeStageUtility.AlwaysDowned(p)) continue;
                if (HealthAIUtility.ShouldBeTendedNowByPlayer(p)) return true;
                if (p.Downed && !p.InBed()) return true;
            }
            return false;
        }

        void PruneRecords()
        {
            int now = Find.TickManager.TicksGame;
            records.RemoveAll(r => r.watcher == null || r.target == null || r.watcher.Dead
                || (!threatActive && r.reunionUntil < now));
        }

        void OnThreatOver()
        {
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < records.Count; i++)
            {
                WatchRecord r = records[i];
                r.reunionUntil = -1;
                if (r.sawFall) continue;
                Pawn w = r.watcher, t = r.target;
                if (w == null || t == null || w.Dead || t.Dead || t.Downed || !w.Spawned || !t.Spawned || w.Map != map || t.Map != map) continue;
                WatcherUtility.GainMemory(w, WWDefOf.WW_Relief, t);
                r.reunionUntil = now + ReunionWindow;
                if (w.CurJobDef == WWDefOf.WW_Watch) w.jobs.EndCurrentJob(Verse.AI.JobCondition.Succeeded);
            }
            records.RemoveAll(r => r.reunionUntil < 0);
        }

        public void Notify_Watching(Pawn watcher, Pawn target)
        {
            WatchRecord r = FindRecord(watcher, target);
            if (r == null) records.Add(new WatchRecord { watcher = watcher, target = target });
        }

        public void Notify_SawFall(Pawn watcher, Pawn target)
        {
            WatchRecord r = FindRecord(watcher, target);
            if (r == null) { r = new WatchRecord { watcher = watcher, target = target }; records.Add(r); }
            r.sawFall = true;
        }

        WatchRecord FindRecord(Pawn watcher, Pawn target)
        {
            for (int i = 0; i < records.Count; i++)
                if (records[i].watcher == watcher && records[i].target == target) return records[i];
            return null;
        }

        public bool TryGetReunion(Pawn watcher, out Pawn target)
        {
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < records.Count; i++)
            {
                WatchRecord r = records[i];
                if (r.watcher == watcher && r.reunionUntil >= now && !r.sawFall)
                {
                    target = r.target;
                    return true;
                }
            }
            target = null;
            return false;
        }

        public void ConsumeReunion(Pawn watcher, Pawn target)
        {
            WatchRecord r = FindRecord(watcher, target);
            if (r != null) r.reunionUntil = -1;
        }
    }
}
