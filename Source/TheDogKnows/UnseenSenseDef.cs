using System.Collections.Generic;
using Verse;

namespace TheDogKnows
{
    /// <summary>
    /// Which animal races sense hidden hostiles, and how. Several defs combine; a race listed in two
    /// uses the one with the larger radius. Add races with your own def or a PatchOperationAdd on races.
    /// See 1.6/Defs/TheDogKnows_Defs.xml.
    /// </summary>
    public class UnseenSenseDef : Def
    {
        public List<ThingDef> races = new List<ThingDef>();
        public float radius = 10f;
        public bool requireLineOfSight = true;
        public IntRange stareTicks = new IntRange(300, 420);
        public int cooldownTicks = 2500;
        /// <summary>Optional; otherwise the life stage's soundAngry, then soundCall.</summary>
        public SoundDef growlSound;
        public float growlPitch = 0.75f;
        public float growlVolume = 1f;

        private static Dictionary<ThingDef, UnseenSenseDef> byRace;

        public static UnseenSenseDef For(ThingDef race)
        {
            if (race == null) return null;
            if (byRace == null) Build();
            byRace.TryGetValue(race, out UnseenSenseDef def);
            return def;
        }

        /// <summary>Test seam: rebuild after a def's races change at runtime.</summary>
        public static void ResetCache() => byRace = null;

        private static void Build()
        {
            byRace = new Dictionary<ThingDef, UnseenSenseDef>();
            List<UnseenSenseDef> all = DefDatabase<UnseenSenseDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                UnseenSenseDef d = all[i];
                if (d.races == null) continue;
                for (int j = 0; j < d.races.Count; j++)
                {
                    ThingDef race = d.races[j];
                    if (race?.race == null || !race.race.Animal) continue;
                    if (!byRace.TryGetValue(race, out UnseenSenseDef old) || d.radius > old.radius) byRace[race] = d;
                }
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (radius <= 0f) yield return "radius must be > 0";
            if (stareTicks.min <= 0 || stareTicks.max < stareTicks.min) yield return "stareTicks must be a positive range";
            if (cooldownTicks < 0) yield return "cooldownTicks must be >= 0";
            if (races != null)
                for (int i = 0; i < races.Count; i++)
                    if (races[i] != null && (races[i].race == null || !races[i].race.Animal))
                        yield return races[i].defName + " is not an animal race";
        }
    }
}
