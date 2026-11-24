using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace OldScores
{
    /// <summary>
    /// Feature 4 — Raid Echoes: recording. A raid (any IncidentWorker_Raid, see Patch_RaidGenerated) is
    /// tracked per map while it lasts; when it ends its approach roads, breaches and deaths become a
    /// <see cref="RaidEcho"/> kept by the map's component. Drawing lives in EchoesDraw.cs. See DESIGN.md.
    /// </summary>
    public partial class OldScoresMapComponent
    {
        List<RaidEcho> echoes = new List<RaidEcho>();
        List<ActiveRaid> activeRaids = new List<ActiveRaid>();

        public IReadOnlyList<RaidEcho> Echoes => echoes;
        public IReadOnlyList<ActiveRaid> ActiveRaids => activeRaids;

        /// <summary>Last "old road" message on this map (not saved; for the inspect of tests and logs).</summary>
        public string lastOldRoadText;
        public int lastOldRoadTick = -1;

        public static OldScoresMapComponent For(Map map) => map?.GetComponent<OldScoresMapComponent>();

        // --- entry points -------------------------------------------------------------------------

        /// <summary>From the TryGenerateRaidInfo postfix: a raid was generated and its pawns arrived (or are in pods).</summary>
        public static void Notify_RaidGenerated(IncidentParms parms, List<Pawn> pawns)
        {
            if (!OldScoresMod.Settings.echoes || parms == null || pawns == null || pawns.Count == 0) return;
            if (!(parms.target is Map map) || !map.IsPlayerHome) return;
            Faction f = parms.faction;
            if (f == null || f == Faction.OfPlayer || !f.HostileTo(Faction.OfPlayer)) return;
            For(map)?.StartRaid(parms, pawns, f);
        }

        /// <summary>From the Pawn.Kill postfix: where a raider of an active raid on this map fell.</summary>
        public void Notify_PawnDied(Pawn victim, IntVec3 cell)
        {
            for (int i = 0; i < activeRaids.Count; i++)
            {
                ActiveRaid raid = activeRaids[i];
                for (int j = 0; j < raid.tracks.Count; j++)
                {
                    RaidTrack t = raid.tracks[j];
                    if (t.pawn != victim) continue;
                    if (cell.IsValid && cell.InBounds(map) && raid.deaths.Count < OldScoresTuning.EchoMaxDeaths) raid.deaths.Add(cell);
                    t.pawn = null;
                    t.recording = false;
                    return;
                }
            }
        }

        void EchoTick(int now)
        {
            if (activeRaids.Count > 0 && now % OldScoresTuning.EchoSampleInterval == 0) SampleRaids(now);
            if (echoes.Count > 0 && now % OldScoresTuning.CleanupInterval == 0) ForgetOldEchoes(now);
        }

        // --- raid start ---------------------------------------------------------------------------

        void StartRaid(IncidentParms parms, List<Pawn> pawns, Faction f)
        {
            bool walkIn = parms.raidArrivalMode != null && parms.raidArrivalMode.walkIn;
            IntVec3 arrival = parms.spawnCenter.IsValid && parms.spawnCenter.InBounds(map) ? parms.spawnCenter : IntVec3.Invalid;
            if (walkIn && arrival.IsValid) TryOldRoad(f, arrival, pawns);

            var raid = new ActiveRaid
            {
                factionId = f.loadID,
                factionName = f.Name,
                startTick = Find.TickManager.TicksGame,
                arrival = arrival,
                walkIn = walkIn,
            };
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Dead || raid.Has(p)) continue;
                var t = new RaidTrack { pawn = p };
                if (p.Spawned && p.Map == map)
                {
                    t.seen = true;
                    t.points.Add(p.Position);   // the road starts where they stepped onto the map
                }
                raid.tracks.Add(t);
            }
            if (raid.tracks.Count > 0) activeRaids.Add(raid);
        }

        /// <summary>
        /// True when the faction already has a walk-in echo whose arrival or any lane start is within
        /// EchoOldRoadDist of this arrival (pod echoes never count).
        /// </summary>
        public static bool MatchesOldRoad(IReadOnlyList<RaidEcho> echoes, int factionId, IntVec3 arrival)
        {
            float maxSq = OldScoresTuning.EchoOldRoadDist * OldScoresTuning.EchoOldRoadDist;
            for (int i = 0; i < echoes.Count; i++)
            {
                RaidEcho e = echoes[i];
                if (e.factionId != factionId || !e.walkIn) continue;
                if (e.arrival.IsValid && (e.arrival - arrival).LengthHorizontalSquared <= maxSq) return true;
                for (int l = 0; l < e.laneStarts.Count; l++)
                {
                    int s = e.laneStarts[l];
                    if (s >= 0 && s < e.points.Count && (e.points[s] - arrival).LengthHorizontalSquared <= maxSq) return true;
                }
            }
            return false;
        }

        void TryOldRoad(Faction f, IntVec3 arrival, List<Pawn> pawns)
        {
            if (!MatchesOldRoad(echoes, f.loadID, arrival)) return;
            string text = "OldScores_Echo_OldRoad".Translate(f.Name, DirectionLabel(arrival)).Resolve();
            Pawn first = null;
            for (int i = 0; i < pawns.Count && first == null; i++)
                if (pawns[i] != null && pawns[i].Spawned && pawns[i].Map == map) first = pawns[i];
            LookTargets look = first != null ? new LookTargets(first) : new LookTargets(new TargetInfo(arrival, map));
            Messages.Message(text, look, MessageTypeDefOf.NegativeEvent);
            lastOldRoadText = text;
            lastOldRoadTick = Find.TickManager.TicksGame;
        }

        string DirectionLabel(IntVec3 c)
        {
            int dx = c.x - map.Size.x / 2, dz = c.z - map.Size.z / 2;
            string key = Mathf.Abs(dx) > Mathf.Abs(dz)
                ? (dx > 0 ? "OldScores_Echo_East" : "OldScores_Echo_West")
                : (dz > 0 ? "OldScores_Echo_North" : "OldScores_Echo_South");
            return key.Translate().Resolve();
        }

        // --- sampling -----------------------------------------------------------------------------

        void SampleRaids(int now)
        {
            if (!OldScoresMod.Settings.echoes) { activeRaids.Clear(); return; }
            for (int i = activeRaids.Count - 1; i >= 0; i--)
            {
                ActiveRaid raid = activeRaids[i];
                bool ongoing = SampleRaid(raid, now);
                if (ongoing && now - raid.startTick <= OldScoresTuning.EchoMaxRaidTicks) continue;
                activeRaids.RemoveAt(i);
                FinalizeRaid(raid, now);
            }
        }

        /// <summary>Samples every recording track; true while the raid is still on (see DESIGN.md).</summary>
        bool SampleRaid(ActiveRaid raid, int now)
        {
            bool ongoing = false;
            bool young = now - raid.startTick < OldScoresTuning.EchoPendingTimeout;
            Area home = map.areaManager?.Home;
            for (int i = 0; i < raid.tracks.Count; i++)
            {
                RaidTrack t = raid.tracks[i];
                Pawn p = t.pawn;
                if (p == null) continue;
                if (p.Dead || p.Destroyed) { t.Stop(); continue; }
                if (!p.Spawned)
                {
                    if (t.seen && p.MapHeld == map)
                    {
                        // still here but not standing on the map: mid-jump (PawnFlyer, longjump) or carried while
                        // downed. Keep the track (a later death is still his); no point while he is held.
                        if (!p.Downed && p.HostileTo(Faction.OfPlayer)) ongoing = true;
                        continue;
                    }
                    if (!t.seen && young) ongoing = true;   // still in a drop pod
                    else t.Stop();                           // left the map, carried off, or never landed
                    continue;
                }
                if (p.Map != map) { t.Stop(); continue; }
                t.seen = true;
                bool fighting = !p.Downed && p.HostileTo(Faction.OfPlayer);
                if (fighting) ongoing = true;
                if (!t.recording) continue;
                if (!fighting || p.InMentalState || IsLeaving(p)) { t.recording = false; continue; }
                IntVec3 c = p.Position;
                if (home != null && home[c])
                {
                    AddPoint(t.points, c, force: true);
                    t.breach = c;
                    t.recording = false;
                    continue;
                }
                if (Loitering(t, c)) continue;
                AddPoint(t.points, c, force: false);
            }
            return ongoing;
        }

        /// <summary>
        /// A raider who has not got any farther from where his road started for EchoStallSamples samples is
        /// milling around (siege camp, firing line): no points until he pushes past his farthest reach again.
        /// </summary>
        public static bool Loitering(RaidTrack t, IntVec3 c)
        {
            if (t.points.Count == 0) return false;
            float d = (c - t.points[0]).LengthHorizontal;
            if (d > t.maxDist + 0.5f)
            {
                t.maxDist = d;
                t.stall = 0;
                return false;
            }
            t.stall++;
            return t.stall >= OldScoresTuning.EchoStallSamples;
        }

        static bool IsLeaving(Pawn p)
        {
            DutyDef d = p.mindState?.duty?.def;
            return d != null && (d == DutyDefOf.ExitMapBest || d == DutyDefOf.ExitMapRandom || d == DutyDefOf.ExitMapBestAndDefendSelf
                                 || d == DutyDefOf.Steal || d == DutyDefOf.Kidnap);
        }

        static void AddPoint(List<IntVec3> pts, IntVec3 c, bool force)
        {
            if (pts.Count > 0)
            {
                IntVec3 last = pts[pts.Count - 1];
                if (last == c) return;
                int min = OldScoresTuning.EchoMinStep;
                if (!force && (last - c).LengthHorizontalSquared < min * min) return;
            }
            if (pts.Count >= OldScoresTuning.EchoMaxPointsPerTrack) Decimate(pts);
            pts.Add(c);
        }

        /// <summary>Keeps the points at even indices plus the last one: 64 -> 33.</summary>
        public static void Decimate(List<IntVec3> pts)
        {
            if (pts.Count < 3) return;
            IntVec3 last = pts[pts.Count - 1];
            int w = 1;
            for (int r = 2; r < pts.Count; r += 2) pts[w++] = pts[r];
            pts.RemoveRange(w, pts.Count - w);
            if (pts[pts.Count - 1] != last) pts.Add(last);
        }

        // --- end of a raid ------------------------------------------------------------------------

        void FinalizeRaid(ActiveRaid raid, int now)
        {
            var lanes = new List<List<IntVec3>>();
            for (int i = 0; i < raid.tracks.Count; i++)
                if (raid.tracks[i].points.Count >= 2) lanes.Add(Simplify(raid.tracks[i].points, OldScoresTuning.EchoSimplifyEpsilon));
            lanes.Sort((a, b) => PathLength(b).CompareTo(PathLength(a)));

            var echo = new RaidEcho
            {
                factionId = raid.factionId,
                factionName = raid.factionName,
                endTick = now,
                startTick = raid.startTick,
                arrival = raid.arrival,
                walkIn = raid.walkIn,
                raiders = raid.tracks.Count,
            };
            float mergeSq = OldScoresTuning.EchoLaneMergeDist * OldScoresTuning.EchoLaneMergeDist;
            for (int i = 0; i < lanes.Count && echo.laneStarts.Count < OldScoresTuning.EchoMaxLanes; i++)
            {
                List<IntVec3> lane = lanes[i];
                bool same = false;
                for (int k = 0; k < echo.laneStarts.Count && !same; k++)
                {
                    IntVec3 ks = echo.points[echo.laneStarts[k]], ke = echo.points[echo.LaneEnd(k) - 1];
                    same = (ks - lane[0]).LengthHorizontalSquared <= mergeSq && (ke - lane[lane.Count - 1]).LengthHorizontalSquared <= mergeSq;
                }
                if (same) continue;
                echo.laneStarts.Add(echo.points.Count);
                echo.points.AddRange(lane);
            }
            float breachSq = OldScoresTuning.EchoBreachMergeDist * OldScoresTuning.EchoBreachMergeDist;
            for (int i = 0; i < raid.tracks.Count && echo.breaches.Count < OldScoresTuning.EchoMaxBreaches; i++)
            {
                IntVec3 b = raid.tracks[i].breach;
                if (!b.IsValid) continue;
                bool near = false;
                for (int k = 0; k < echo.breaches.Count && !near; k++) near = (echo.breaches[k] - b).LengthHorizontalSquared <= breachSq;
                if (!near) echo.breaches.Add(b);
            }
            echo.deaths.AddRange(raid.deaths);
            if (echo.laneStarts.Count == 0 && echo.deaths.Count == 0) return;   // nothing to remember
            if (!echo.arrival.IsValid) echo.arrival = echo.points.Count > 0 ? echo.points[0] : echo.deaths[0];
            echoes.Add(echo);
            while (echoes.Count > OldScoresTuning.EchoMaxPerMap) echoes.RemoveAt(0);
            MarkEchoesDirty();
        }

        void ForgetOldEchoes(int now)
        {
            echoes.RemoveAll(e => now - e.endTick > OldScoresTuning.EchoForgetTicks);
            MarkEchoesDirty();   // also refreshes fading and faction names
        }

        static float PathLength(List<IntVec3> pts)
        {
            float len = 0f;
            for (int i = 1; i < pts.Count; i++) len += (pts[i] - pts[i - 1]).LengthHorizontal;
            return len;
        }

        /// <summary>Ramer–Douglas–Peucker on cells; always keeps the first and the last point.</summary>
        public static List<IntVec3> Simplify(List<IntVec3> pts, float epsilon)
        {
            var result = new List<IntVec3>();
            if (pts.Count < 3) { result.AddRange(pts); return result; }
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            SimplifyRange(pts, 0, pts.Count - 1, epsilon, keep);
            for (int i = 0; i < pts.Count; i++) if (keep[i]) result.Add(pts[i]);
            return result;
        }

        static void SimplifyRange(List<IntVec3> pts, int first, int last, float epsilon, bool[] keep)
        {
            if (last - first < 2) return;
            Vector2 a = new Vector2(pts[first].x, pts[first].z), b = new Vector2(pts[last].x, pts[last].z);
            float best = -1f;
            int index = -1;
            for (int i = first + 1; i < last; i++)
            {
                float d = DistToSegment(new Vector2(pts[i].x, pts[i].z), a, b);
                if (d > best) { best = d; index = i; }
            }
            if (best <= epsilon) return;
            keep[index] = true;
            SimplifyRange(pts, first, index, epsilon, keep);
            SimplifyRange(pts, index, last, epsilon, keep);
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.0001f) return (p - a).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return (p - (a + ab * t)).magnitude;
        }

        // --- save ---------------------------------------------------------------------------------

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref echoes, "echoes", LookMode.Deep);
            Scribe_Collections.Look(ref activeRaids, "activeRaids", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                echoes ??= new List<RaidEcho>();
                activeRaids ??= new List<ActiveRaid>();
                echoes.RemoveAll(e => e == null || !e.IsWellFormed);
                activeRaids.RemoveAll(r => r == null);
                MarkEchoesDirty();
            }
        }

        /// <summary>Test support: forget everything recorded on this map.</summary>
        public void ClearEchoes()
        {
            echoes.Clear();
            activeRaids.Clear();
            lastOldRoadText = null;
            lastOldRoadTick = -1;
            MarkEchoesDirty();
        }

        /// <summary>Test support: add an echo as if a raid had just ended (e.g. after a Scribe roundtrip).</summary>
        public void AddEcho(RaidEcho e)
        {
            if (e == null || !e.IsWellFormed) return;
            echoes.Add(e);
            MarkEchoesDirty();
        }

        /// <summary>Test support: runs the 2500-tick forgetting pass now.</summary>
        public void ForgetOldEchoesNow() => ForgetOldEchoes(Find.TickManager.TicksGame);

        /// <summary>Test support: runs the 60-tick sampling pass now.</summary>
        public void SampleRaidsNow() => SampleRaids(Find.TickManager.TicksGame);
    }

    /// <summary>One raider's road while the raid is on.</summary>
    public class RaidTrack : IExposable
    {
        public Pawn pawn;
        public List<IntVec3> points = new List<IntVec3>();
        public IntVec3 breach = IntVec3.Invalid;
        public bool recording = true;
        public bool seen;
        public float maxDist;
        public int stall;

        public void Stop()
        {
            pawn = null;
            recording = false;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Collections.Look(ref points, "points", LookMode.Value);
            Scribe_Values.Look(ref breach, "breach", IntVec3.Invalid);
            Scribe_Values.Look(ref recording, "recording", true);
            Scribe_Values.Look(ref seen, "seen");
            Scribe_Values.Look(ref maxDist, "maxDist");
            Scribe_Values.Look(ref stall, "stall");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) points ??= new List<IntVec3>();
        }
    }

    /// <summary>A raid still going on, per map.</summary>
    public class ActiveRaid : IExposable
    {
        public int factionId = -1;
        public string factionName;
        public int startTick;
        public IntVec3 arrival = IntVec3.Invalid;
        public bool walkIn;
        public List<RaidTrack> tracks = new List<RaidTrack>();
        public List<IntVec3> deaths = new List<IntVec3>();

        public bool Has(Pawn p)
        {
            for (int i = 0; i < tracks.Count; i++) if (tracks[i].pawn == p) return true;
            return false;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionId, "factionId", -1);
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref arrival, "arrival", IntVec3.Invalid);
            Scribe_Values.Look(ref walkIn, "walkIn");
            Scribe_Collections.Look(ref tracks, "tracks", LookMode.Deep);
            Scribe_Collections.Look(ref deaths, "deaths", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                tracks ??= new List<RaidTrack>();
                deaths ??= new List<IntVec3>();
                tracks.RemoveAll(t => t == null);
            }
        }
    }

    /// <summary>What the map remembers of one raid. Values only (the faction by loadID), so it always loads.</summary>
    public class RaidEcho : IExposable
    {
        public int factionId = -1;
        public string factionName;
        public int startTick;
        public int endTick;
        public IntVec3 arrival = IntVec3.Invalid;
        public bool walkIn;
        public int raiders;
        /// <summary>All lanes, one after the other; lane i is points[laneStarts[i] .. LaneEnd(i)).</summary>
        public List<IntVec3> points = new List<IntVec3>();
        public List<int> laneStarts = new List<int>();
        public List<IntVec3> breaches = new List<IntVec3>();
        public List<IntVec3> deaths = new List<IntVec3>();

        public int LaneEnd(int lane) => lane + 1 < laneStarts.Count ? laneStarts[lane + 1] : points.Count;

        public bool IsWellFormed
        {
            get
            {
                if (points == null || laneStarts == null || breaches == null || deaths == null) return false;
                if (laneStarts.Count == 0 && deaths.Count == 0) return false;
                for (int i = 0; i < laneStarts.Count; i++)
                {
                    int s = laneStarts[i];
                    if (s < 0 || s >= points.Count || LaneEnd(i) - s < 2) return false;
                }
                return true;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionId, "factionId", -1);
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref endTick, "endTick");
            Scribe_Values.Look(ref arrival, "arrival", IntVec3.Invalid);
            Scribe_Values.Look(ref walkIn, "walkIn");
            Scribe_Values.Look(ref raiders, "raiders");
            Scribe_Collections.Look(ref points, "points", LookMode.Value);
            Scribe_Collections.Look(ref laneStarts, "laneStarts", LookMode.Value);
            Scribe_Collections.Look(ref breaches, "breaches", LookMode.Value);
            Scribe_Collections.Look(ref deaths, "deaths", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                points ??= new List<IntVec3>();
                laneStarts ??= new List<int>();
                breaches ??= new List<IntVec3>();
                deaths ??= new List<IntVec3>();
            }
        }
    }
}
