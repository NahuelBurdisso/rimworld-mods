using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Duelo
{
    /// <summary>Who gets a last light, and who it flies to.</summary>
    public static class LastLightUtility
    {
        // A candidate needs at least this much (opinion + relation weight) to count as
        // "someone who loved them". Below it the light just rises and fades.
        public const int MinBondScore = 20;

        static int RelationWeight(PawnRelationDef rel)
        {
            if (rel == PawnRelationDefOf.Spouse) return 100;
            if (rel == PawnRelationDefOf.Fiance) return 90;
            if (rel == PawnRelationDefOf.Lover) return 80;
            if (rel == PawnRelationDefOf.Bond) return 70;
            if (rel == PawnRelationDefOf.Parent || rel == PawnRelationDefOf.Child) return 60;
            if (rel == PawnRelationDefOf.Sibling) return 30;
            if (rel == PawnRelationDefOf.HalfSibling) return 20;
            return 0;
        }

        /// <summary>Player-faction humanlikes always get a light; player animals only if someone loved them.</summary>
        public static bool Qualifies(Pawn victim)
        {
            if (victim.Faction == null || !victim.Faction.IsPlayer) return false;
            if (victim.RaceProps.IsMechanoid) return false;
            return victim.RaceProps.Humanlike || victim.RaceProps.Animal;
        }

        /// <summary>
        /// The living player-faction pawn on <paramref name="map"/> with the strongest bond to
        /// <paramref name="victim"/>, or null. Call before the victim dies: opinion is read from
        /// the living side, and relations are still intact.
        /// </summary>
        public static Pawn FindStrongestBond(Pawn victim, Map map, out int bestScore)
        {
            Pawn best = null;
            bestScore = MinBondScore - 1;
            List<Pawn> pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == victim || p.Dead || p.relations == null || p.RaceProps.IsMechanoid) continue;

                int score = 0;
                if (p.RaceProps.Humanlike && victim.RaceProps.Humanlike)
                    score += p.relations.OpinionOf(victim);
                foreach (PawnRelationDef rel in p.GetRelations(victim))
                    score += RelationWeight(rel);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            return best;
        }

        // Motes are not in ListerThings; these let tests and debug tools see what was released.
        public static int EmittedCount;
        public static Mote_LastLight LastEmitted;

        public static void Emit(Pawn deceased, Map map, Vector3 from, Pawn target)
        {
            var mote = (Mote_LastLight)ThingMaker.MakeThing(DueloDefOf.Duelo_Mote_LastLight);
            mote.Init(deceased, target, from);
            GenSpawn.Spawn(mote, from.ToIntVec3().ClampInsideMap(map), map);
            EmittedCount++;
            LastEmitted = mote;
            if (Prefs.DevMode)
                Log.Message($"[Duelo] last light: {deceased.LabelShort} -> {(target != null ? target.LabelShort : "nobody")}");
        }
    }
}
