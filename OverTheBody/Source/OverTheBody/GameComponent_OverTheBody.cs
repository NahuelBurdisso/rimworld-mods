using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using RimWorld.Planet;

namespace OverTheBody
{
    /// <summary>
    /// Every 60 ticks: wakes / re-thinks bonded animals whose colonist just fell with enemies near (so they react
    /// within a second instead of finishing their current job), counts guard time, and settles finished records
    /// (memory + tale when the colonist gets up, or when the animal died guarding).
    /// </summary>
    public class GameComponent_OverTheBody : GameComponent
    {
        public const int Interval = 60;
        public const int MinGuardTicks = 250;
        public const int DiedGuardingWindow = 120;
        public const int MaxRecordTicks = 5 * GenDate.TicksPerDay;

        public static GameComponent_OverTheBody Instance;

        List<GuardRecord> records = new List<GuardRecord>();
        static readonly List<Pawn> animalsBuffer = new List<Pawn>();

        public IReadOnlyList<GuardRecord> Records => records;

        public GameComponent_OverTheBody(Game game) { Instance = this; }

        public override void FinalizeInit() { Instance = this; }

        public GuardRecord RecordFor(Pawn animal)
        {
            for (int i = 0; i < records.Count; i++)
                if (records[i].animal == animal) return records[i];
            return null;
        }

        /// <summary>Called by the job giver when it hands out one of our jobs.</summary>
        public GuardRecord Notify_Guarding(Pawn animal, Pawn master)
        {
            int now = Find.TickManager.TicksGame;
            GuardRecord r = RecordFor(animal);
            if (r != null && r.master == master)
            {
                r.lastGuardTick = now;   // a job of ours was just handed out: dying from here on counts as dying for them
                return r;
            }
            if (r != null) records.Remove(r);
            r = new GuardRecord { animal = animal, master = master, startTick = now, lastGuardTick = now };
            records.Add(r);
            if (animal.Spawned)
                Messages.Message("OverTheBody_Rushes".Translate(animal.Named("ANIMAL"), master.Named("PAWN")),
                    animal, MessageTypeDefOf.NeutralEvent, historical: false);
            return r;
        }


        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % Interval != 0) return;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++) WakeAnimals(maps[i]);
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (!Update(records[i], now)) records.RemoveAt(i);
            }
        }

        static void WakeAnimals(Map map)
        {
            animalsBuffer.Clear();
            animalsBuffer.AddRange(map.mapPawns.SpawnedColonyAnimals);
            for (int i = 0; i < animalsBuffer.Count; i++)
            {
                Pawn a = animalsBuffer[i];
                if (!OverTheBodyUtility.CanGuard(a) || a.jobs == null || OverTheBodyUtility.IsGuardJob(a.CurJobDef)) continue;
                if (a.InMentalState || a.roping?.IsRoped == true || a.CurJob?.playerForced == true || a.GetLord() != null) continue;
                Pawn master = OverTheBodyUtility.FallenMasterFor(a);
                if (master == null || !OverTheBodyUtility.InDanger(master)) continue;
                // only disturb the animal if the think tree would really hand it one of our jobs
                Job job = JobGiver_StandOverMaster.TryMakeJob(a, notify: false);
                if (job == null) continue;
                JobMaker.ReturnToPool(job);
                if (!a.Awake() || a.CurJobDef == JobDefOf.LayDown) RestUtility.WakeUp(a);
                else a.jobs.CheckForJobOverride();
            }
            animalsBuffer.Clear();
        }

        bool Update(GuardRecord r, int now)
        {
            Pawn animal = r.animal, master = r.master;
            if (animal == null || master == null) return false;

            bool guardingJustNow = now - r.lastGuardTick <= DiedGuardingWindow;
            if (animal.Dead)
            {
                if ((guardingJustNow || r.fellGuarding) && !master.Dead) DiedGuarding(r);
                return false;
            }
            if (animal.Downed) { if (guardingJustNow) r.fellGuarding = true; }
            else r.fellGuarding = false;
            if (master.Dead || master.Destroyed || animal.Destroyed) return false;
            if (animal.Faction != Faction.OfPlayer || !OverTheBodyUtility.HasBondWith(animal, master)) return false;
            if (now - r.startTick > MaxRecordTicks) return false;
            // carried off the map by raiders (or otherwise gone from every map, not in a caravan): the watch failed
            if (master.MapHeld == null && master.GetCaravan() == null) return false;

            JobDef cur = animal.CurJobDef;
            if (OverTheBodyUtility.IsGuardJob(cur)) r.lastGuardTick = now;
            if (cur == OverTheBodyDefOf.OverTheBody_Guard || cur == OverTheBodyDefOf.OverTheBody_Bite) r.ticksGuarded += Interval;

            if (!master.Downed)
            {
                if (r.ticksGuarded >= MinGuardTicks) StoodOver(r);
                return false;
            }
            return true;
        }

        static void StoodOver(GuardRecord r)
        {
            r.master.needs?.mood?.thoughts?.memories?.TryGainMemory(OverTheBodyDefOf.OverTheBody_StoodOverMe, r.animal);
            TaleRecorder.RecordTale(OverTheBodyDefOf.OverTheBody_StoodOverTale, r.animal, r.master);
            if (r.master.SpawnedOrAnyParentSpawned)
                Messages.Message("OverTheBody_Remembers".Translate(r.animal.Named("ANIMAL"), r.master.Named("PAWN")),
                    new LookTargets(r.master), MessageTypeDefOf.PositiveEvent);
        }

        static void DiedGuarding(GuardRecord r)
        {
            r.master.needs?.mood?.thoughts?.memories?.TryGainMemory(OverTheBodyDefOf.OverTheBody_DiedOverMe, r.animal);
            TaleRecorder.RecordTale(OverTheBodyDefOf.OverTheBody_DiedOverTale, r.animal, r.master);
            if (r.master.SpawnedOrAnyParentSpawned)
                Messages.Message("OverTheBody_DiedGuarding".Translate(r.animal.Named("ANIMAL"), r.master.Named("PAWN")),
                    new LookTargets(r.master), MessageTypeDefOf.NegativeEvent);
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
                records.RemoveAll(r => r.animal == null || r.master == null || r.animal.Destroyed && !r.animal.Dead);
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                records ??= new List<GuardRecord>();
                records.RemoveAll(r => r == null || r.animal == null || r.master == null);
            }
        }
    }
}
