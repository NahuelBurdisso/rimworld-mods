using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LightForTheAbsent
{
    public enum AbsenceState
    {
        Away,
        Returning,
        Mourning
    }

    /// <summary>One colonist away from home and the bed whose light waits for them. Saved; the light is not.</summary>
    public class AbsentRecord : IExposable
    {
        public Pawn pawn;
        public Building_Bed bed;
        public AbsenceState state;
        /// <summary>Dusks at which the light came on while the pawn was away.</summary>
        public int nightsLit;
        /// <summary>The light already came on tonight (Away): a night is counted once.</summary>
        public bool litTonight;
        /// <summary>It was night at the last poll (Mourning): dusks are counted from the sky.</summary>
        public bool nightLastPoll;
        /// <summary>Who else owned the bed when the absence began (double beds, bunks): not a sign it was given away.</summary>
        public List<Pawn> coOwners = new List<Pawn>();
        public int fadePolls;
        public int mourningNight;
        public int mourningLitPolls;

        /// <summary>Polls the light burned while away: a quick trip out and back does not earn the memory.</summary>
        public int litPolls;

        /// <summary>Not saved. What this absence wants of its bed's lamp after this poll; the tracker lights one lamp
        /// per bed (a couple away together share it).</summary>
        public bool wantOn;
        public float wantLevel;
        public bool wantFlicker;
        /// <summary>Not saved: the bed's lamp while this absence keeps it lit, set by the tracker after each poll.</summary>
        public AbsentLight light;

        public AbsentRecord() { }

        public AbsentRecord(Pawn pawn, Building_Bed bed)
        {
            this.pawn = pawn;
            this.bed = bed;
            state = AbsenceState.Away;
            List<Pawn> owners = bed.OwnersForReading;
            for (int i = 0; i < owners.Count; i++)
                if (owners[i] != null && owners[i] != pawn) coOwners.Add(owners[i]);
        }

        public bool LightOn => light != null && light.Spawned;

        public void SetLight(bool on, float level, bool flickering)
        {
            wantOn = on;
            wantLevel = level;
            wantFlicker = flickering;
        }

        /// <summary>Gives up the lamp; the tracker puts it out after the poll unless another absence shares the bed.</summary>
        public void Extinguish()
        {
            wantOn = false;
            light = null;
        }

        public void ExposeData()
        {
            // a pawn who died far away is Destroyed: without saveDestroyedThings the reference would be written as null
            Scribe_References.Look(ref pawn, "pawn", saveDestroyedThings: true);
            Scribe_References.Look(ref bed, "bed");
            Scribe_Values.Look(ref state, "state");
            Scribe_Values.Look(ref nightsLit, "nightsLit");
            Scribe_Values.Look(ref litTonight, "litTonight");
            Scribe_Values.Look(ref nightLastPoll, "nightLastPoll");
            Scribe_Collections.Look(ref coOwners, "coOwners", LookMode.Reference);
            Scribe_Values.Look(ref fadePolls, "fadePolls");
            Scribe_Values.Look(ref mourningNight, "mourningNight");
            Scribe_Values.Look(ref mourningLitPolls, "mourningLitPolls");
            Scribe_Values.Look(ref litPolls, "litPolls");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                coOwners ??= new List<Pawn>();
                coOwners.RemoveAll(x => x == null);
            }
        }

        public override string ToString() => $"{pawn?.LabelShort ?? "null"} {state} nights={nightsLit} lit={LightOn}";
    }
}
