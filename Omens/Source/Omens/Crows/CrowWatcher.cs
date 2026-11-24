using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Omens
{
    /// <summary>
    /// The Crows Know. Crows circle over hostile raids that are staging, leave as a flock toward the colony when
    /// the assault starts, and circle over the unburied dead after a battle with at least three deaths.
    /// Crows are motes (not saved); only the carrion window is saved.
    /// </summary>
    public class CrowWatcher : MapComponent
    {
        /// <summary>A place crows circle: a staging raid (key = lord id) or the carrion omen (key = -1).</summary>
        public class Site
        {
            public int key;
            public Vector3 center;
            public int want;
            public int lastSpawnTick = -99999;
            public readonly List<CrowMote> crows = new List<CrowMote>();
        }

        public const int CarrionKey = -1;

        readonly List<Site> sites = new List<Site>();

        // carrion: battle window and the open omen (saved)
        int battleStart = -1;
        int omenStart = -1, omenEnd = -1, omenExpire = -1;
        IntVec3 omenCenter = IntVec3.Invalid;
        int omenCorpses;

        public CrowWatcher(Map map) : base(map) { }

        public List<Site> Sites => sites;
        public bool CarrionOpen => omenExpire >= 0;
        public IntVec3 CarrionCenter => omenCenter;
        public int CarrionCorpses => omenCorpses;
        public int BattleStart => battleStart;

        public Site SiteFor(int key)
        {
            for (int i = 0; i < sites.Count; i++)
                if (sites[i].key == key) return sites[i];
            return null;
        }

        bool Visible => OmensMod.Settings.crows && map == Find.CurrentMap && OmensUtil.BirdsCanFly(map);

        public override void MapComponentTick()
        {
            int t = Find.TickManager.TicksGame + map.uniqueID;
            try
            {
                if (t % OmensTuning.CarrionPollInterval == 7) CarrionPoll();
                if (t % OmensTuning.CrowPollInterval == 13) CrowPoll();
            }
            catch (System.Exception ex) { Log.ErrorOnce("[Omens] CrowWatcher: " + ex, 0x0e3a4); }
        }

        // ------------------------------------------------------------------ circling

        /// <summary>Keeps the crows of every active site (public for the harness).</summary>
        public void CrowPoll()
        {
            int now = Find.TickManager.TicksGame;
            StagingWatcher sw = StagingWatcher.For(map);

            // which sites should exist
            if (sw != null)
                foreach (StageRecord r in sw.Records)
                    if (r.Staging && r.cell.IsValid && SiteFor(r.lordId) == null)
                        sites.Add(new Site { key = r.lordId, center = r.cell.ToVector3Shifted(), want = 2 + Mathf.Abs(r.lordId) % 2 });
            if (CarrionOpen && SiteFor(CarrionKey) == null)
                sites.Add(new Site { key = CarrionKey, center = omenCenter.ToVector3Shifted(), want = omenCorpses >= OmensTuning.CarrionManyCorpses ? 3 : 2 });

            for (int i = sites.Count - 1; i >= 0; i--)
            {
                Site s = sites[i];
                s.crows.RemoveAll(c => c == null || c.Destroyed || c.Departing);
                bool alive;
                IntVec3 cell;
                if (s.key == CarrionKey)
                {
                    alive = CarrionOpen;
                    cell = omenCenter;
                    if (alive && cell.IsValid)
                    {
                        s.center = cell.ToVector3Shifted();
                        s.want = omenCorpses >= OmensTuning.CarrionManyCorpses ? 3 : 2;
                        for (int k = 0; k < s.crows.Count; k++) s.crows[k].center = s.center;
                    }
                }
                else
                {
                    StageRecord r = sw?.RecordFor(s.key);
                    alive = r != null && r.Staging;
                    cell = r?.cell ?? IntVec3.Invalid;
                }
                if (!alive)
                {
                    Scatter(s);
                    sites.RemoveAt(i);
                    continue;
                }
                if (!Visible || !OmensUtil.OpenSky(cell, map))
                {
                    // settings off, another map in view, or no sky: no new crows, and the ones there leave
                    Scatter(s);
                    continue;
                }
                if (s.crows.Count < s.want && now - s.lastSpawnTick >= OmensTuning.CrowSpawnGapTicks)
                {
                    CrowMote c = SpawnCircling(s.center);
                    if (c != null) { s.crows.Add(c); s.lastSpawnTick = now; }
                }
            }
        }

        CrowMote SpawnCircling(Vector3 center)
        {
            var c = (CrowMote)ThingMaker.MakeThing(OmensDefOf.Omens_Crow);
            float speed = Rand.Range(0.25f, 0.45f) * (Rand.Bool ? 1f : -1f);
            c.Init(center, Rand.Range(2.5f, 5f), speed);
            Vector3 at = c.OrbitPoint;
            IntVec3 cell = at.ToIntVec3();
            if (!cell.InBounds(map)) return null;
            c.exactPosition = at;
            c.Scale = Rand.Range(0.9f, 1.1f);
            GenSpawn.Spawn(c, cell, map);
            OmensStats.CrowsSpawned++;
            return c;
        }

        CrowMote SpawnFlying(Vector3 at, float headingDeg)
        {
            IntVec3 cell = at.ToIntVec3();
            if (!cell.InBounds(map)) return null;
            var c = (CrowMote)ThingMaker.MakeThing(OmensDefOf.Omens_Crow);
            c.exactPosition = at;
            c.exactRotation = headingDeg;
            c.Scale = Rand.Range(0.85f, 1.05f);
            GenSpawn.Spawn(c, cell, map);
            c.Depart(headingDeg, Rand.Range(3f, 4.5f), Rand.Range(6f, 9f));
            OmensStats.CrowsSpawned++;
            return c;
        }

        void Scatter(Site s)
        {
            foreach (CrowMote c in s.crows)
                if (c != null && !c.Destroyed) c.Depart(Rand.Range(0f, 360f), Rand.Range(2.5f, 3.5f), Rand.Range(3f, 6f));
            s.crows.Clear();
        }

        /// <summary>From StagingWatcher: the assault started (flock to the colony) or the raid is gone (scatter).</summary>
        public void Notify_StageEnded(StageRecord rec)
        {
            Site s = SiteFor(rec.lordId);
            if (s != null) sites.Remove(s);
            if (!rec.assault || !Visible || !rec.cell.IsValid || !OmensUtil.OpenSky(rec.cell, map))
            {
                if (s != null) Scatter(s);
                return;
            }
            Vector3 from = rec.cell.ToVector3Shifted();
            float toColony = (OmensUtil.ColonyCenter(map) - from).AngleFlat();
            int flock = 6 + Mathf.Abs(rec.lordId) % 3;
            int n = 0;
            if (s != null)
                foreach (CrowMote c in s.crows)
                {
                    if (c == null || c.Destroyed) continue;
                    c.Depart(toColony + Rand.Range(-OmensTuning.FlockSpreadDegrees, OmensTuning.FlockSpreadDegrees), Rand.Range(3f, 4.5f), Rand.Range(6f, 9f));
                    n++;
                    OmensStats.FlockCrows++;
                }
            for (; n < flock; n++)
            {
                Vector3 at = from + new Vector3(Rand.Range(-2.5f, 2.5f), 0f, Rand.Range(-2.5f, 2.5f));
                if (SpawnFlying(at, toColony + Rand.Range(-OmensTuning.FlockSpreadDegrees, OmensTuning.FlockSpreadDegrees)) != null)
                    OmensStats.FlockCrows++;
            }
        }

        // ------------------------------------------------------------------ carrion

        /// <summary>Battle open/close and the carrion omen (public for the harness).</summary>
        public void CarrionPoll()
        {
            int now = Find.TickManager.TicksGame;
            bool threat = GenHostility.AnyHostileActiveThreatToPlayer(map);
            if (threat && battleStart < 0) battleStart = now;
            else if (!threat && battleStart >= 0)
            {
                int start = battleStart;
                battleStart = -1;
                if (CountCorpses(start, now, out IntVec3 c) >= OmensTuning.CarrionMinCorpses)
                {
                    omenStart = start;
                    omenEnd = now;
                    omenExpire = now + OmensTuning.CarrionDurationTicks;
                    omenCenter = c;
                    omenCorpses = CountCorpses(omenStart, omenEnd, out _);
                    OmensStats.CarrionOmens++;
                    Site old = SiteFor(CarrionKey);
                    if (old != null) old.center = c.ToVector3Shifted();
                }
            }
            if (CarrionOpen)
            {
                omenCorpses = CountCorpses(omenStart, omenEnd, out IntVec3 c);
                if (omenCorpses == 0 || now > omenExpire) EndCarrion();
                else omenCenter = c;
            }
        }

        public void EndCarrion()
        {
            omenStart = omenEnd = omenExpire = -1;
            omenCenter = IntVec3.Invalid;
            omenCorpses = 0;
        }

        /// <summary>Unburied flesh corpses that died in [from, to], lying under open sky; their centroid.</summary>
        public int CountCorpses(int from, int to, out IntVec3 centroid)
        {
            List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            int n = 0;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < corpses.Count; i++)
            {
                if (!(corpses[i] is Corpse c) || !c.Spawned || c.timeOfDeath < from || c.timeOfDeath > to) continue;
                if (c.InnerPawn == null || !c.InnerPawn.RaceProps.IsFlesh) continue;
                if (!OmensUtil.OpenSky(c.Position, map)) continue;
                n++;
                sum += c.Position.ToVector3Shifted();
            }
            centroid = n > 0 ? (sum / n).ToIntVec3() : IntVec3.Invalid;
            return n;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref battleStart, "omensBattleStart", -1);
            Scribe_Values.Look(ref omenStart, "omensCarrionStart", -1);
            Scribe_Values.Look(ref omenEnd, "omensCarrionEnd", -1);
            Scribe_Values.Look(ref omenExpire, "omensCarrionExpire", -1);
            Scribe_Values.Look(ref omenCenter, "omensCarrionCenter", IntVec3.Invalid);
            Scribe_Values.Look(ref omenCorpses, "omensCarrionCorpses");
        }
    }
}
