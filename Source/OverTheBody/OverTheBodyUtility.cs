using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace OverTheBody
{
    public static class OverTheBodyUtility
    {
        public const float DangerRadius = 25f;
        public const float BiteRadius = 2.5f;
        public const float LeashRadius = 5f;
        public const float FollowRadius = 2.5f;

        public static bool IsGuardJob(JobDef def) =>
            def != null && (def == OverTheBodyDefOf.OverTheBody_Guard || def == OverTheBodyDefOf.OverTheBody_Rush
                || def == OverTheBodyDefOf.OverTheBody_Bite || def == OverTheBodyDefOf.OverTheBody_Follow);

        /// <summary>Animal of the player, spawned, able to act. Cheap: checked before anything else.</summary>
        public static bool CanGuard(Pawn animal) =>
            animal != null && animal.Spawned && animal.IsAnimal && animal.Faction == Faction.OfPlayer
            && !animal.Dead && !animal.Downed && !animal.Drafted && animal.relations != null;

        /// <summary>The thing standing where the fallen colonist is: the colonist, or whoever carries them.</summary>
        public static Pawn Anchor(Pawn master)
        {
            if (master.Spawned) return master;
            Pawn carrier = master.CarriedBy;
            return carrier != null && carrier.Spawned ? carrier : null;
        }

        static bool IsFallen(Pawn p, Map map)
        {
            if (p == null || p.Dead || p.Destroyed || !p.Downed || !p.IsColonist) return false;
            Pawn anchor = Anchor(p);
            return anchor != null && anchor.Map == map;
        }

        /// <summary>
        /// A downed colonist this animal is bonded to, on the animal's map (lying there or being carried).
        /// The animal's master first, else the closest one. Null when none.
        /// </summary>
        public static Pawn FallenMasterFor(Pawn animal)
        {
            List<DirectPawnRelation> rels = animal.relations.DirectRelations;
            Pawn best = null;
            float bestDist = float.MaxValue;
            Pawn master = animal.playerSettings?.Master;
            for (int i = 0; i < rels.Count; i++)
            {
                if (rels[i].def != PawnRelationDefOf.Bond) continue;
                Pawn p = rels[i].otherPawn;
                if (!IsFallen(p, animal.Map)) continue;
                if (p == master) return p;
                float d = Anchor(p).Position.DistanceToSquared(animal.Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        public static bool HasBondWith(Pawn animal, Pawn other)
        {
            List<DirectPawnRelation> rels = animal.relations?.DirectRelations;
            if (rels == null) return false;
            for (int i = 0; i < rels.Count; i++)
                if (rels[i].def == PawnRelationDefOf.Bond && rels[i].otherPawn == other) return true;
            return false;
        }

        public static bool CarriedByHostile(Pawn master)
        {
            Pawn carrier = master.CarriedBy;
            return carrier != null && carrier.HostileTo(Faction.OfPlayer);
        }

        /// <summary>Enemies within 25 cells of the fallen colonist (or of whoever carries them), or a kidnapper.</summary>
        public static bool InDanger(Pawn master)
        {
            if (CarriedByHostile(master)) return true;
            Pawn anchor = Anchor(master);
            return anchor != null && GenAI.EnemyIsNear(anchor, DangerRadius);
        }

        /// <summary>Closest active threat to the fallen colonist within 25 cells (any attack target), for facing.</summary>
        public static Thing NearestThreat(Pawn master)
        {
            Pawn anchor = Anchor(master);
            if (anchor == null) return null;
            Thing best = null;
            float bestDist = float.MaxValue;
            List<IAttackTarget> targets = anchor.Map.attackTargetsCache.GetPotentialTargetsFor(anchor);
            for (int i = 0; i < targets.Count; i++)
            {
                IAttackTarget t = targets[i];
                Thing thing = t.Thing;
                if (thing == null || !thing.Spawned || t.ThreatDisabled(anchor) || thing.Position.Fogged(thing.Map)) continue;
                float d = thing.Position.DistanceToSquared(anchor.Position);
                if (d < bestDist && d <= DangerRadius * DangerRadius)
                {
                    bestDist = d;
                    best = thing;
                }
            }
            return best;
        }

        /// <summary>A hostile pawn on its feet within BiteRadius of the fallen colonist that the animal can reach.</summary>
        public static Pawn BiteTarget(Pawn animal, Pawn master)
        {
            if (!master.Spawned) return null;
            Pawn best = null;
            float bestDist = float.MaxValue;
            List<IAttackTarget> targets = master.Map.attackTargetsCache.GetPotentialTargetsFor(master);
            for (int i = 0; i < targets.Count; i++)
            {
                if (!(targets[i].Thing is Pawn p) || !p.Spawned || p.Downed || p.Dead) continue;
                if (targets[i].ThreatDisabled(animal) || !p.HostileTo(animal)) continue;
                if (!p.Position.InHorDistOf(master.Position, BiteRadius)) continue;
                float d = p.Position.DistanceToSquared(animal.Position);
                if (d < bestDist && animal.CanReach(p, PathEndMode.Touch, Danger.Deadly))
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        public static bool AdjacentTo(Pawn animal, Pawn master) =>
            master.Spawned && animal.Map == master.Map && animal.Position.AdjacentTo8Way(master.Position);

        /// <summary>
        /// Free, standable, reachable cell next to the fallen colonist: the one closest to the threat (stand between),
        /// else the one closest to the animal. Cells with a building (bed) are a last resort.
        /// </summary>
        public static IntVec3 GuardCell(Pawn animal, Pawn master, Thing threat)
        {
            Map map = master.Map;
            IntVec3 center = master.Position;
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                IntVec3 c = center + GenAdj.AdjacentCells[i];
                if (!c.InBounds(map) || !c.StandableBy(map, animal)) continue;
                if (!map.pawnDestinationReservationManager.CanReserve(c, animal)) continue;
                float score = threat != null && threat.Spawned ? c.DistanceToSquared(threat.Position) : c.DistanceToSquared(animal.Position);
                if (c.GetEdifice(map) != null) score += 10000f;
                if (score < bestScore && animal.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }
    }
}
