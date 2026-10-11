using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Aftermath
{
    public enum BreachKind { Broken, Battered }

    /// <summary>One breach of the door standing on a cell. Saved in the map's BreachTracker.</summary>
    public class BreachRecord : IExposable
    {
        public IntVec3 cell;
        public int tick;
        public Vector2 longLat;
        public string attacker;
        public BreachKind kind;
        public bool repaired;   // battered only: the door was patched back to full hit points

        public bool ShowsPlank => kind == BreachKind.Broken || repaired;

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref longLat, "longLat");
            Scribe_Values.Look(ref attacker, "attacker");
            Scribe_Values.Look(ref kind, "kind", BreachKind.Broken);
            Scribe_Values.Look(ref repaired, "repaired");
        }
    }

    /// <summary>Breaches belong to the cell, so the door rebuilt there inherits them.</summary>
    public class BreachTracker : MapComponent
    {
        List<BreachRecord> records = new List<BreachRecord>();
        readonly Dictionary<IntVec3, List<BreachRecord>> byCell = new Dictionary<IntVec3, List<BreachRecord>>();
        static readonly List<BreachRecord> Empty = new List<BreachRecord>();

        /// <summary>Bumped on every change; door comps re-read their cell when it moves.</summary>
        public int version;

        /// <summary>Battered records not yet repaired, map-wide: doors only check their hit points while this is above 0.</summary>
        public int pendingRepairs;

        public BreachTracker(Map map) : base(map) { }

        public int Count => records.Count;

        /// <summary>The cell's records, oldest first. Do not modify.</summary>
        public List<BreachRecord> At(IntVec3 c) => byCell.TryGetValue(c, out var l) ? l : Empty;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "breaches", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (records == null) records = new List<BreachRecord>();
                records.RemoveAll(r => r == null);
                Rebuild();
            }
        }

        void Rebuild()
        {
            byCell.Clear();
            foreach (BreachRecord r in records)
            {
                if (!byCell.TryGetValue(r.cell, out var l)) byCell[r.cell] = l = new List<BreachRecord>();
                l.Add(r);
            }
            foreach (var l in byCell.Values) l.SortBy(r => r.tick);
            CountPending();
            version++;
        }

        void CountPending()
        {
            pendingRepairs = 0;
            foreach (BreachRecord r in records) if (r.kind == BreachKind.Battered && !r.repaired) pendingRepairs++;
        }

        /// <summary>Adds a breach, or upgrades the cell's breach of the last day (one breach per raid).</summary>
        public BreachRecord Register(IntVec3 cell, BreachKind kind, string attacker)
        {
            int now = Find.TickManager.TicksGame;
            List<BreachRecord> here = At(cell);
            for (int i = here.Count - 1; i >= 0; i--)
            {
                BreachRecord recent = here[i];
                if (now - recent.tick >= AftermathTuning.OneBreachPerTicks) continue;
                if (kind == BreachKind.Broken && recent.kind == BreachKind.Battered)
                {
                    recent.kind = BreachKind.Broken;
                    recent.attacker = attacker;
                    CountPending();
                    version++;
                }
                return recent;
            }
            var rec = new BreachRecord { cell = cell, tick = now, longLat = AftermathUtil.LongLat(map), attacker = attacker, kind = kind };
            // keep the newest MaxBreachesPerCell per cell (here is oldest first)
            int drop = here.Count + 1 - AftermathTuning.MaxBreachesPerCell;
            for (int i = 0; i < drop && i < here.Count; i++) records.Remove(here[i]);
            records.Add(rec);
            Rebuild();
            return rec;
        }

        public void Clear(IntVec3 cell)
        {
            if (records.RemoveAll(r => r.cell == cell) > 0) Rebuild();
        }

        /// <summary>Battered records of the cell latch "repaired" once the door is back at full hit points.</summary>
        public void CheckRepaired(IntVec3 cell, Thing door)
        {
            if (pendingRepairs == 0) return;
            List<BreachRecord> here = At(cell);
            bool any = false;
            for (int i = 0; i < here.Count && !any; i++) any = here[i].kind == BreachKind.Battered && !here[i].repaired;
            if (!any || door.HitPoints < door.MaxHitPoints) return;
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i].kind == BreachKind.Battered && !here[i].repaired)
                {
                    here[i].repaired = true;
                    pendingRepairs--;
                    version++;
                }
            }
        }

        public static BreachTracker For(Map map) => map?.GetComponent<BreachTracker>();
    }
}
