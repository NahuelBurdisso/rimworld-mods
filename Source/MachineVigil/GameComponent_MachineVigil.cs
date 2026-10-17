using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MachineVigil
{
    /// <summary>
    /// Registry of active vigils. Registration comes from the Overseer relation worker (Harmony);
    /// behaviour comes from JobGiver_KeepVigil in the Mechanoid think tree; this component prunes,
    /// announces and hands out the witness memory every 250 ticks.
    /// </summary>
    public class GameComponent_MachineVigil : GameComponent
    {
        public const int CheckInterval = 250;
        public const int MaxDurationTicks = 3 * GenDate.TicksPerDay;
        public const float WitnessRadius = 6f;

        public static GameComponent_MachineVigil Instance;

        List<Vigil> vigils = new List<Vigil>();
        readonly Dictionary<Pawn, Vigil> byMech = new Dictionary<Pawn, Vigil>();

        public IReadOnlyList<Vigil> Vigils => vigils;

        public GameComponent_MachineVigil(Game game) { Instance = this; }

        public Vigil VigilFor(Pawn mech) => mech != null && byMech.TryGetValue(mech, out Vigil v) ? v : null;

        public Vigil VigilOfMechanitor(Pawn mechanitor)
        {
            for (int i = 0; i < vigils.Count; i++)
                if (vigils[i].mechanitor == mechanitor) return vigils[i];
            return null;
        }

        /// <summary>Called when the Overseer relation between a dead mechanitor and a living mech is removed.</summary>
        public void Register(Pawn mechanitor, Pawn mech)
        {
            Vigil old = VigilFor(mech);
            if (old != null)
            {
                old.mechs.Remove(mech);
                byMech.Remove(mech);
            }
            Vigil v = VigilOfMechanitor(mechanitor);
            if (v == null)
            {
                Map map = mechanitor.MapHeld;
                v = new Vigil
                {
                    mechanitor = mechanitor,
                    lastMap = map,
                    lastCenter = map != null ? mechanitor.PositionHeld : IntVec3.Invalid,
                    startTick = Find.TickManager.TicksGame,
                };
                vigils.Add(v);
            }
            if (!v.mechs.Contains(mech)) v.mechs.Add(mech);
            byMech[mech] = v;
        }

        /// <summary>A mech got a new overseer: flash and drop the vigil job.</summary>
        public void Wake(Pawn mech, bool showMessage)
        {
            Vigil v = VigilFor(mech);
            if (v == null) return;
            v.mechs.Remove(mech);
            byMech.Remove(mech);
            if (mech.Spawned)
            {
                FleckMaker.Static(mech.DrawPos, mech.Map, FleckDefOf.FlashHollow, 1.5f);
                if (showMessage && v.mechanitor != null)
                    Messages.Message("MachineVigil_Wakes".Translate(mech.Named("MECH"), v.mechanitor.Named("PAWN")),
                        mech, MessageTypeDefOf.SilentInput, historical: false);
            }
            StopVigilJob(mech);
        }

        public static bool IsVigilJob(JobDef def) =>
            def != null && (def == MachineVigilDefOf.MachineVigil_Keep || def == MachineVigilDefOf.MachineVigil_Approach);

        static void StopVigilJob(Pawn mech)
        {
            if (mech.jobs?.curJob != null && IsVigilJob(mech.CurJobDef))
                mech.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);   // next tick re-thinks, once the new relation is stored
        }

        public override void GameComponentTick()
        {
            if (vigils.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            if (now % CheckInterval != 0) return;
            for (int i = vigils.Count - 1; i >= 0; i--)
            {
                Vigil v = vigils[i];
                if (!Update(v, now))
                {
                    End(v);
                    vigils.RemoveAt(i);
                }
            }
        }

        bool Update(Vigil v, int now)
        {
            if (v.mechanitor != null && !v.mechanitor.Dead) return false;   // resurrected
            if (now - v.startTick > MaxDurationTicks) return false;

            for (int i = v.mechs.Count - 1; i >= 0; i--)
            {
                Pawn m = v.mechs[i];
                if (m == null || m.Destroyed || m.Dead || m.Faction != Faction.OfPlayer)
                {
                    v.mechs.RemoveAt(i);
                    if (m != null) byMech.Remove(m);
                }
                else if (m.GetOverseer() != null)
                {
                    Wake(m, showMessage: false);
                }
            }
            if (v.mechs.Count == 0) return false;

            if (!v.TryGetCenter(out IntVec3 center, out Map map)) return true;

            bool anyHere = false, anyKeeping = false;
            for (int i = 0; i < v.mechs.Count; i++)
            {
                Pawn m = v.mechs[i];
                if (m.Spawned && m.Map == map)
                {
                    anyHere = true;
                    if (m.CurJobDef == MachineVigilDefOf.MachineVigil_Keep)
                    {
                        // the body was moved: re-path now instead of waiting for the job to expire
                        if (!JobGiver_KeepVigil.InRing(m.Position, center)) m.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        else anyKeeping = true;
                    }
                }
            }
            if (!v.announced && anyHere && v.mechanitor != null)
            {
                v.announced = true;
                Messages.Message("MachineVigil_VigilBegins".Translate(v.mechanitor.Named("PAWN")),
                    new TargetInfo(center, map), MessageTypeDefOf.NeutralEvent);
            }
            if (anyKeeping && v.mechanitor != null) GiveWitnessMemories(v, center, map);
            return true;
        }

        static void GiveWitnessMemories(Vigil v, IntVec3 center, Map map)
        {
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn c = colonists[i];
                if (c.needs?.mood == null || c.Downed || !c.Awake()) continue;
                if (v.witnessIds.Contains(c.thingIDNumber)) continue;
                if (!c.Position.InHorDistOf(center, WitnessRadius)) continue;
                if (!GenSight.LineOfSight(c.Position, center, map)) continue;
                v.witnessIds.Add(c.thingIDNumber);
                c.needs.mood.thoughts.memories.TryGainMemory(MachineVigilDefOf.MachineVigil_WitnessedVigil, v.mechanitor);
            }
        }

        void End(Vigil v)
        {
            for (int i = 0; i < v.mechs.Count; i++)
            {
                Pawn m = v.mechs[i];
                if (m == null) continue;
                byMech.Remove(m);
                if (!m.Destroyed) StopVigilJob(m);
            }
            v.mechs.Clear();
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                foreach (Vigil v in vigils) v.PruneMechs();
                vigils.RemoveAll(v => v.mechs.Count == 0);
                RebuildIndex();
            }
            Scribe_Collections.Look(ref vigils, "vigils", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                vigils ??= new List<Vigil>();
                RebuildIndex();
            }
        }

        public override void FinalizeInit()
        {
            Instance = this;
            RebuildIndex();
        }

        void RebuildIndex()
        {
            byMech.Clear();
            foreach (Vigil v in vigils)
                foreach (Pawn m in v.mechs)
                    if (m != null) byMech[m] = v;
        }
    }
}
