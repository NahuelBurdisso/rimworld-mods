using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace Omens
{
    /// <summary>One hostile raid that staged on this map: while it waits, and afterwards while its raiders are followed.</summary>
    public class StageRecord : IExposable
    {
        public int lordId = -1;
        public Faction faction;
        public string factionName;
        public IntVec3 cell = IntVec3.Invalid;
        public int startTick, endTick = -1;
        public bool ended, assault;
        public List<int> seenIds = new List<int>();
        public List<Pawn> pending = new List<Pawn>();
        public int returned, died, captured;
        public Building_ColdCamp camp;
        public int campEntryId = -1;

        public bool Staging => !ended;
        public int Came => seenIds.Count;
        public int WaitTicks => (ended ? endTick : Find.TickManager.TicksGame) - startTick;

        public void See(List<Pawn> pawns)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || seenIds.Contains(p.thingIDNumber)) continue;
                seenIds.Add(p.thingIDNumber);
                pending.Add(p);
            }
        }

        /// <summary>Resolves pending raiders (DESIGN.md "Following raiders"). True if a count changed.</summary>
        public bool Resolve(Map map)
        {
            bool changed = false;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pawn p = pending[i];
                if (p == null) { pending.RemoveAt(i); continue; }
                if (p.Dead) { died++; changed = true; pending.RemoveAt(i); continue; }
                if (p.Destroyed || p.Discarded) { pending.RemoveAt(i); continue; }
                if (!p.Spawned && p.MapHeld == null) { returned++; changed = true; pending.RemoveAt(i); continue; }
                if (p.Faction == Faction.OfPlayer || p.IsPrisonerOfColony || p.IsSlaveOfColony)
                {
                    captured++; changed = true; pending.RemoveAt(i); continue;
                }
            }
            return changed;
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // only living, saved pawns are written as references
                pending.RemoveAll(p => p == null || p.Dead || p.Destroyed || p.Discarded);
                if (camp != null && camp.Destroyed) camp = null;
            }
            Scribe_Values.Look(ref lordId, "lordId", -1);
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Values.Look(ref cell, "cell", IntVec3.Invalid);
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref endTick, "endTick", -1);
            Scribe_Values.Look(ref ended, "ended");
            Scribe_Values.Look(ref assault, "assault");
            Scribe_Collections.Look(ref seenIds, "seenIds", LookMode.Value);
            Scribe_Collections.Look(ref pending, "pending", LookMode.Reference);
            Scribe_Values.Look(ref returned, "returned");
            Scribe_Values.Look(ref died, "died");
            Scribe_Values.Look(ref captured, "captured");
            Scribe_References.Look(ref camp, "camp");
            Scribe_Values.Look(ref campEntryId, "campEntryId", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                seenIds ??= new List<int>();
                pending ??= new List<Pawn>();
                pending.RemoveAll(p => p == null);
            }
        }

        public override string ToString() =>
            $"stage[lord {lordId} {factionName} at {cell} start {startTick} end {endTick} came {Came} back {returned} dead {died} capt {captured} pending {pending.Count}]";
    }

    /// <summary>
    /// Shared hook of the pack: polls the map's lords every 60 ticks and keeps a StageRecord per hostile raid
    /// waiting in LordToil_Stage. Raises StageEnded to the crows and the cold camp, then follows the raiders
    /// until each is dead, gone home or captured.
    /// </summary>
    public class StagingWatcher : MapComponent
    {
        List<StageRecord> records = new List<StageRecord>();

        public StagingWatcher(Map map) : base(map) { }

        public List<StageRecord> Records => records;

        public static StagingWatcher For(Map map) => map?.GetComponent<StagingWatcher>();

        public StageRecord RecordFor(int lordId)
        {
            for (int i = 0; i < records.Count; i++)
                if (records[i].lordId == lordId) return records[i];
            return null;
        }

        public override void MapComponentTick()
        {
            if ((Find.TickManager.TicksGame + map.uniqueID) % OmensTuning.StagingPollInterval != 0) return;
            try { Poll(); }
            catch (System.Exception ex) { Log.ErrorOnce("[Omens] StagingWatcher: " + ex, 0x0e3a0); }
        }

        static Lord FindLord(List<Lord> lords, int id)
        {
            for (int i = 0; i < lords.Count; i++)
                if (lords[i].loadID == id) return lords[i];
            return null;
        }

        /// <summary>The toils a staging raid moves to when it attacks (LordJob_AssaultColony's subgraph, bossgroups).</summary>
        public static bool IsAssaultToil(LordToil t) =>
            t is LordToil_AssaultColony || t is LordToil_AssaultColonyBreaching || t is LordToil_AssaultColonyPrisoners
            || t is LordToil_AssaultColonySappers || t is LordToil_AssaultColonyBossgroup || t is LordToil_MoveInBossgroup;

        public static bool IsHostileStaging(Lord lord) =>
            lord?.CurLordToil is LordToil_Stage && lord.faction != null && lord.faction.HostileTo(Faction.OfPlayer);

        /// <summary>One poll (public for the harness).</summary>
        public void Poll()
        {
            List<Lord> lords = map.lordManager.lords;
            int now = Find.TickManager.TicksGame;

            // new stagings
            for (int i = 0; i < lords.Count; i++)
            {
                Lord lord = lords[i];
                if (!IsHostileStaging(lord) || RecordFor(lord.loadID) != null) continue;
                var rec = new StageRecord
                {
                    lordId = lord.loadID,
                    faction = lord.faction,
                    factionName = lord.faction.Name,
                    cell = lord.CurLordToil.FlagLoc,
                    startTick = now - lord.ticksInToil,
                };
                rec.See(lord.ownedPawns);
                records.Add(rec);
            }

            // update, end, follow
            for (int i = records.Count - 1; i >= 0; i--)
            {
                StageRecord rec = records[i];
                Lord lord = FindLord(lords, rec.lordId);
                int came0 = rec.Came;
                if (lord != null) rec.See(lord.ownedPawns);
                if (!rec.ended)
                {
                    if (lord != null && lord.CurLordToil is LordToil_Stage)
                    {
                        IntVec3 flag = lord.CurLordToil.FlagLoc;
                        if (flag.IsValid) rec.cell = flag;
                        continue;
                    }
                    rec.ended = true;
                    rec.endTick = now;
                    rec.assault = lord != null && lord.faction != null && lord.faction.HostileTo(Faction.OfPlayer) && IsAssaultToil(lord.CurLordToil);
                    rec.Resolve(map);
                    StageEnded(rec);
                }
                bool changed = rec.Resolve(map) | rec.Came != came0;
                if (changed && rec.camp != null && !rec.camp.Destroyed)
                    rec.camp.UpdateEntry(rec.campEntryId, rec.Came, rec.returned);
                if ((rec.pending.Count == 0 && lord == null) || now - rec.endTick > OmensTuning.FollowTimeoutTicks)
                    records.RemoveAt(i);
            }
        }

        void StageEnded(StageRecord rec)
        {
            try { map.GetComponent<CrowWatcher>()?.Notify_StageEnded(rec); }
            catch (System.Exception ex) { Log.ErrorOnce("[Omens] crows on stage end: " + ex, 0x0e3a1); }
            try { ColdCamp.Notify_StageEnded(map, rec); }
            catch (System.Exception ex) { Log.ErrorOnce("[Omens] cold camp on stage end: " + ex, 0x0e3a2); }
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
                foreach (StageRecord r in records)
                    if (r.Resolve(map) && r.camp != null && !r.camp.Destroyed) r.camp.UpdateEntry(r.campEntryId, r.Came, r.returned);
            Scribe_Collections.Look(ref records, "omensStagings", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) records ??= new List<StageRecord>();
        }
    }
}
