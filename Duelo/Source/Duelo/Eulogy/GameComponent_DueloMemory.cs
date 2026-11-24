using System.Collections.Generic;
using Verse;

namespace Duelo
{
    /// <summary>A eulogy written at the moment of death, kept until someone reads it at the grave.</summary>
    public class EulogyRecord : IExposable
    {
        public string pawnId;
        public string pawnName;
        public int deathTick = -1;
        public List<string> lines = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref pawnName, "pawnName");
            Scribe_Values.Look(ref deathTick, "deathTick", -1);
            Scribe_Collections.Look(ref lines, "lines", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && lines == null) lines = new List<string>();
        }
    }

    /// <summary>
    /// Eulogies composed at death (when the cause and the volatile tales are still known), keyed by
    /// the dead pawn's ThingID so nothing here holds a reference to a pawn that may be destroyed.
    /// </summary>
    public class GameComponent_DueloMemory : GameComponent
    {
        public const int MaxRecords = 300;

        List<EulogyRecord> eulogies = new List<EulogyRecord>();

        public GameComponent_DueloMemory(Game game) { }

        public static GameComponent_DueloMemory Instance => Current.Game?.GetComponent<GameComponent_DueloMemory>();

        public int Count => eulogies.Count;

        public EulogyRecord Find(string pawnId)
        {
            if (pawnId == null) return null;
            for (int i = eulogies.Count - 1; i >= 0; i--)
                if (eulogies[i].pawnId == pawnId) return eulogies[i];
            return null;
        }

        public void Store(EulogyRecord record)
        {
            for (int i = eulogies.Count - 1; i >= 0; i--)
                if (eulogies[i].pawnId == record.pawnId) eulogies.RemoveAt(i);
            eulogies.Add(record);
            while (eulogies.Count > MaxRecords) eulogies.RemoveAt(0);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref eulogies, "eulogies", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && eulogies == null) eulogies = new List<EulogyRecord>();
        }
    }
}
