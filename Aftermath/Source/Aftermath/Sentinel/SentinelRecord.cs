using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Aftermath
{
    /// <summary>A turret's service record: tally, battles, name and old hands. Saved inside its comp.</summary>
    public class SentinelRecord : IExposable
    {
        public string name;
        public int takedowns;
        public int battles;
        public int firstTick = -1;
        public int lastEngagedTick = -1;
        public int namedTick = -1;
        public Vector2 longLat;
        public List<int> comradeIds = new List<int>();
        public List<string> comradeNames = new List<string>();
        public List<int> recentVictims = new List<int>();

        public bool Named => !name.NullOrEmpty();

        public bool HasComrade(int id) => comradeIds.Contains(id);

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref takedowns, "takedowns");
            Scribe_Values.Look(ref battles, "battles");
            Scribe_Values.Look(ref firstTick, "firstTick", -1);
            Scribe_Values.Look(ref lastEngagedTick, "lastEngagedTick", -1);
            Scribe_Values.Look(ref namedTick, "namedTick", -1);
            Scribe_Values.Look(ref longLat, "longLat");
            Scribe_Collections.Look(ref comradeIds, "comradeIds", LookMode.Value);
            Scribe_Collections.Look(ref comradeNames, "comradeNames", LookMode.Value);
            Scribe_Collections.Look(ref recentVictims, "recentVictims", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (comradeIds == null) comradeIds = new List<int>();
                if (comradeNames == null) comradeNames = new List<string>();
                if (recentVictims == null) recentVictims = new List<int>();
                // the two comrade lists are parallel: never let a bad save desync them
                while (comradeNames.Count < comradeIds.Count) comradeNames.Add("?");
                while (comradeNames.Count > comradeIds.Count) comradeNames.RemoveAt(comradeNames.Count - 1);
            }
        }
    }
}
