using System.Collections.Generic;
using Verse;

namespace PackHowls
{
    /// <summary>Which animal races howl. See 1.6/Defs/PackHowls_Defs.xml.</summary>
    public class HowlerListDef : Def
    {
        public List<ThingDef> races = new List<ThingDef>();
        public List<SoundDef> callSounds = new List<SoundDef>();
        public List<ThingDef> excludedRaces = new List<ThingDef>();

        private Dictionary<ThingDef, bool> cache;

        public bool IsHowler(ThingDef race)
        {
            if (race?.race == null || !race.race.Animal) return false;
            cache ??= new Dictionary<ThingDef, bool>();
            if (cache.TryGetValue(race, out bool known)) return known;
            bool result = Compute(race);
            cache[race] = result;
            return result;
        }

        private bool Compute(ThingDef race)
        {
            if (excludedRaces != null && excludedRaces.Contains(race)) return false;
            if (races != null && races.Contains(race)) return true;
            List<LifeStageAge> stages = race.race.lifeStageAges;
            if (stages == null || callSounds == null) return false;
            for (int i = 0; i < stages.Count; i++)
            {
                SoundDef call = stages[i].soundCall;
                if (call != null && callSounds.Contains(call)) return true;
            }
            return false;
        }
    }
}
