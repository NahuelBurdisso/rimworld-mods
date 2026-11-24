using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Gesta
{
    public static class CombatUtil
    {
        static readonly List<Pawn> tmpFighters = new List<Pawn>();

        /// <summary>Free colonist on its feet who can fight.</summary>
        public static bool IsFighter(Pawn p) =>
            p != null && p.Spawned && !p.Dead && !p.Downed && !p.IsSlave && !p.WorkTagIsDisabled(WorkTags.Violent);

        /// <summary>Fighters on the map. The returned list is reused: copy it to keep it.</summary>
        public static List<Pawn> Fighters(Map map)
        {
            tmpFighters.Clear();
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
                if (IsFighter(colonists[i])) tmpFighters.Add(colonists[i]);
            return tmpFighters;
        }

        /// <summary>Non-downed hostile pawns that vanilla counts as an active threat to the player.</summary>
        public static int CountFighters(Map map)
        {
            int n = Fighters(map).Count;
            tmpFighters.Clear();
            return n;
        }

        /// <summary>The single fighter on the map, or null if there are none or several.</summary>
        public static Pawn SoleFighter(Map map)
        {
            List<Pawn> f = Fighters(map);
            Pawn result = f.Count == 1 ? f[0] : null;
            tmpFighters.Clear();
            return result;
        }

        public static int ActiveHostilePawns(Map map)
        {
            int n = 0;
            foreach (IAttackTarget t in map.attackTargetsCache.TargetsHostileToColony)
            {
                if (t.Thing is Pawn p && !p.Downed && GenHostility.IsActiveThreatToPlayer(t)) n++;
            }
            return n;
        }

        public static bool IsPlayerHome(Map map) => map != null && map.IsPlayerHome;
    }
}
