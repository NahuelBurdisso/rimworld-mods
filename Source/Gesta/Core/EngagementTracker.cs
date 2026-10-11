using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Gesta
{
    /// <summary>
    /// Tells features when a fight starts and ends on a player home map. Vanilla has no
    /// "battle ended" event, so this polls GenHostility every 250 ticks. Not saved: after a load
    /// an ongoing fight simply starts again.
    /// </summary>
    public class EngagementTracker : GameComponent
    {
        public class Engagement
        {
            public bool active;
            public int startTick;
            public int maxFighters;
            /// <summary>A last stand already happened in this engagement (one per fight).</summary>
            public bool standHappened;
        }

        const int CheckInterval = 250;
        const int MaxEngagementTicks = GenDate.TicksPerDay;

        readonly Dictionary<int, Engagement> byMap = new Dictionary<int, Engagement>();
        readonly HashSet<Map> pendingChecks = new HashSet<Map>();
        static readonly List<Map> tmpMaps = new List<Map>();

        static EngagementTracker instance;
        public static EngagementTracker Instance => instance;

        public EngagementTracker(Game game) { instance = this; LastAttackers.Clear(); }

        public Engagement Get(Map map)
        {
            if (map == null) return null;
            if (!byMap.TryGetValue(map.uniqueID, out Engagement e))
            {
                e = new Engagement();
                byMap[map.uniqueID] = e;
            }
            return e;
        }

        public bool IsActive(Map map) => map != null && byMap.TryGetValue(map.uniqueID, out Engagement e) && e.active;

        /// <summary>Ask features to re-evaluate this map on the next tick (after a down or a death).</summary>
        public void QueueCheck(Map map)
        {
            if (map != null) pendingChecks.Add(map);
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % CheckInterval == 0)
            {
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++) Poll(maps[i], now);
            }

            if (pendingChecks.Count > 0)
            {
                tmpMaps.Clear();
                tmpMaps.AddRange(pendingChecks);
                pendingChecks.Clear();
                foreach (Map map in tmpMaps)
                {
                    if (map.Disposed || !IsActive(map)) continue;
                    UpdateFighters(map);
                    LastStandTracker.Instance?.Evaluate(map);
                }
                tmpMaps.Clear();
            }
        }

        void Poll(Map map, int now)
        {
            if (!CombatUtil.IsPlayerHome(map)) return;
            Engagement e = Get(map);
            bool threat = GenHostility.AnyHostileActiveThreatToPlayer(map);

            if (threat && !e.active)
            {
                e.active = true;
                e.startTick = now;
                e.maxFighters = 0;
                e.standHappened = false;
                UpdateFighters(map);
                LastStandTracker.Instance?.Notify_EngagementStarted(map);
                BrothersTracker.Instance?.Notify_EngagementStarted(map);
                Chronicler.Instance?.Notify_EngagementStarted(map);
            }
            else if (e.active && (!threat || now - e.startTick > MaxEngagementTicks))
            {
                e.active = false;
                LastStandTracker.Instance?.Notify_EngagementEnded(map, forced: threat);
                BrothersTracker.Instance?.Notify_EngagementEnded(map, forced: threat);
                Chronicler.Instance?.Notify_EngagementEnded(map, forced: threat);
                return;
            }
            else if (!threat && !e.active)
            {
                // e.g. a save taken in the last seconds of a fight: there is no transition to see
                LastStandTracker.Instance?.Notify_EngagementEnded(map, forced: false);
            }

            if (e.active)
            {
                UpdateFighters(map);
                LastStandTracker.Instance?.Evaluate(map);
            }
        }

        void UpdateFighters(Map map)
        {
            Engagement e = Get(map);
            int n = CombatUtil.CountFighters(map);
            if (n > e.maxFighters) e.maxFighters = n;
        }

        /// <summary>For tests: poll every map right now instead of waiting for the interval.</summary>
        public void PollNow()
        {
            int now = Find.TickManager.TicksGame;
            foreach (Map map in Find.Maps) Poll(map, now);
        }
    }
}
