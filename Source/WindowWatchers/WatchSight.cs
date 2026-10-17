using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WindowWatchers
{
    /// <summary>Where a watcher stands: a roofed indoor cell that sees the loved one, safe from the enemy.</summary>
    public struct WatchSpot
    {
        public IntVec3 cell;
        public Building_Door door;   // non-null: seen through this door, which the watcher keeps ajar
        public bool IsValid => cell.IsValid;
        public static WatchSpot Invalid => new WatchSpot { cell = IntVec3.Invalid };
    }

    public static class WatchSight
    {
        public const float MaxSightDist = 36f;
        public const float SafeRadius = 14f;
        public const float HostileSightRadius = 24f;
        public const float DoorSafeRadius = 24f;
        public const int MaxRegions = 48;
        public const int MaxCandidates = 40;

        /// <summary>
        /// Same walk as GenSight.LineOfSight(start, end, map) (Verse/GenSight.cs:70, start and end cells excluded), but
        /// the given door counts as open. With openDoorsSeeThrough false (the watcher's own view), other doors block
        /// unless held open: a door someone happens to walk through must not make a spot that vanishes a second later.
        /// </summary>
        public static bool CanSee(IntVec3 start, IntVec3 end, Map map, Building_Door ajar, bool openDoorsSeeThrough = false)
        {
            if (!start.InBounds(map) || !end.InBounds(map)) return false;
            bool flag = start.x != end.x ? start.x < end.x : start.z < end.z;
            int dx = Mathf.Abs(end.x - start.x);
            int dz = Mathf.Abs(end.z - start.z);
            int x = start.x, z = start.z;
            int n = 1 + dx + dz;
            int xInc = end.x > start.x ? 1 : -1;
            int zInc = end.z > start.z ? 1 : -1;
            int err = dx - dz;
            dx *= 2;
            dz *= 2;
            IntVec3 c = default;
            while (n > 1)
            {
                c.x = x;
                c.z = z;
                if (c == end) return true;
                if (c != start)
                {
                    Building edifice = c.GetEdifice(map);
                    if (edifice != null && edifice != ajar && Blocks(edifice, openDoorsSeeThrough)) return false;
                }
                if (err > 0 || (err == 0 && flag)) { x += xInc; err -= dz; }
                else { z += zInc; err += dx; }
                n--;
            }
            return true;
        }

        static bool Blocks(Building b, bool openDoorsSeeThrough)
        {
            if (b is Building_Door door)
            {
                if (door.def.Fillage != FillCategory.Full) return false;
                return !(door.HoldOpen || (openDoorsSeeThrough && door.Open));
            }
            return !b.CanBeSeenOver();
        }

        public static bool IsIndoorRoom(Room room) =>
            room != null && !room.TouchesMapEdge && !room.IsDoorway && !room.UsesOutdoorTemperature;

        /// <summary>No active hostile within SafeRadius, none within HostileSightRadius that can see the cell.</summary>
        public static bool IsSafe(IntVec3 cell, Map map, Building_Door ajar)
        {
            float safeSq = SafeRadius * SafeRadius;
            float sightSq = HostileSightRadius * HostileSightRadius;
            foreach (IAttackTarget t in map.attackTargetsCache.TargetsHostileToColony)
            {
                if (!GenHostility.IsActiveThreatToPlayer(t)) continue;
                IntVec3 hp = t.Thing.Position;
                float d = (hp - cell).LengthHorizontalSquared;
                if (d < safeSq) return false;
                if (ajar != null && (hp - ajar.Position).LengthHorizontalSquared < DoorSafeRadius * DoorSafeRadius) return false;
                if (d < sightSq && CanSee(hp, cell, map, ajar, openDoorsSeeThrough: true)) return false;
            }
            return true;
        }

        /// <summary>Whether the watcher at cell (through door, if any) sees the target.</summary>
        public static bool SeesTarget(IntVec3 cell, Pawn target, Map map, Building_Door door, Pawn watcher)
        {
            if (!cell.InHorDistOf(target.Position, MaxSightDist)) return false;
            if (door != null && (door.Map != map || !WatcherUtility.DoorUsable(door, watcher))) return false;
            return CanSee(cell, target.Position, map, door);
        }

        struct Candidate
        {
            public IntVec3 cell;
            public Building_Door door;
            public float score;
        }

        static readonly List<Candidate> tmpCandidates = new List<Candidate>();

        /// <summary>
        /// BFS over the regions the pawn can walk, only into door regions and indoor rooms; best = nearest to the pawn,
        /// direct sight preferred over a door at equal distance. claimed: cells other watchers took in this scan.
        /// </summary>
        public static WatchSpot FindSpot(Pawn pawn, Pawn target, HashSet<IntVec3> claimed)
        {
            Map map = pawn.Map;
            Region root = pawn.GetRegion();
            if (root == null || target.Map != map) return WatchSpot.Invalid;
            TraverseParms tp = TraverseParms.For(pawn);
            IntVec3 tpos = target.Position;
            IntVec3 ppos = pawn.Position;
            float maxSq = MaxSightDist * MaxSightDist;
            tmpCandidates.Clear();

            // indoors: never step outside. Outdoors already: may cross outdoor regions to reach cover, but only regions
            // with no enemy within SafeRadius of their center.
            bool startIndoor = root.door != null || IsIndoorRoom(root.Room);
            float safeSq = SafeRadius * SafeRadius;
            RegionTraverser.BreadthFirstTraverse(root,
                (Region from, Region to) => to.Allows(tp, false) && (to.door != null || IsIndoorRoom(to.Room)
                    || (!startIndoor && WatcherUtility.NearestThreatDistSq(map, to.extentsClose.CenterCell) >= safeSq)),
                reg =>
                {
                    if (reg.door != null || !IsIndoorRoom(reg.Room)) return false;
                    if ((reg.extentsClose.ClosestCellTo(tpos) - tpos).LengthHorizontalSquared > maxSq) return false;
                    foreach (IntVec3 c in reg.Cells)
                    {
                        if ((c - tpos).LengthHorizontalSquared > maxSq) continue;
                        if (claimed != null && claimed.Contains(c)) continue;
                        if (!c.Roofed(map) || c.Fogged(map) || !c.Standable(map)) continue;
                        if (!c.InAllowedArea(pawn)) continue;
                        float dist = (c - ppos).LengthHorizontal;
                        if (CanSee(c, tpos, map, null))
                        {
                            if (map.pawnDestinationReservationManager.CanReserve(c, pawn))
                                tmpCandidates.Add(new Candidate { cell = c, door = null, score = dist });
                            continue;
                        }
                        for (int i = 0; i < 4; i++)
                        {
                            Building_Door door = (c + GenAdj.CardinalDirections[i]).GetDoor(map);
                            if (!WatcherUtility.DoorUsable(door, pawn)) continue;
                            if (CanSee(c, tpos, map, door))
                            {
                                if (!map.pawnDestinationReservationManager.CanReserve(c, pawn)) break;
                                tmpCandidates.Add(new Candidate { cell = c, door = door, score = dist + 0.5f });
                                break;
                            }
                        }
                    }
                    return tmpCandidates.Count >= MaxCandidates;
                }, MaxRegions);

            tmpCandidates.Sort((a, b) => a.score.CompareTo(b.score));
            WatchSpot result = WatchSpot.Invalid;
            int checkedSafe = 0;
            for (int i = 0; i < tmpCandidates.Count && checkedSafe < 12; i++)
            {
                Candidate cand = tmpCandidates[i];
                checkedSafe++;
                if (!IsSafe(cand.cell, map, cand.door)) continue;
                if (!pawn.CanReach(cand.cell, PathEndMode.OnCell, Danger.Some)) continue;
                result = new WatchSpot { cell = cand.cell, door = cand.door };
                break;
            }
            tmpCandidates.Clear();
            return result;
        }
    }
}
