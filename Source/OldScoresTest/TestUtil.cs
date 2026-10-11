using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace OldScoresTest
{
    /// <summary>Helpers shared by the Old Scores harnesses. All calls checked against decomp/1.6.4871.</summary>
    public static class TestUtil
    {
        /// <summary>The Gesta harness owns ticks 60-2200 of the same game; ours start here.</summary>
        public const int NicknamesStart = 2700, SparedStart = 3100, BeastsStart = 3700;

        public static Pawn NewColonist(Map map, IntVec3 near)
        {
            for (int i = 0; i < 30; i++)
            {
                Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (p.WorkTagIsDisabled(WorkTags.Violent))
                {
                    Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard);
                    continue;
                }
                IntVec3 cell = CellFinder.StandableCellNear(near, map, 6f);
                GenSpawn.Spawn(p, cell.IsValid ? cell : near, map);
                return p;
            }
            throw new System.Exception("could not generate a violent-capable colonist");
        }

        /// <summary>Spawns a pawn of the faction exactly at the cell (the cell must be standable).</summary>
        public static Pawn SpawnAt(Map map, IntVec3 cell, Faction faction, PawnKindDef kind = null)
        {
            Pawn p = PawnGenerator.GeneratePawn(kind ?? faction.def.basicMemberKind ?? PawnKindDefOf.Pirate, faction);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        static bool Clear(IntVec3 c, Map map) =>
            c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null && !c.Fogged(map)
            && c.GetFirstPawn(map) == null && !c.GetTerrain(map).IsWater;

        /// <summary>
        /// A cell such that the rect [x-left, x+right] x [z-half, z+half] is empty open ground, searched
        /// outward from the map centre. Invalid if none.
        /// </summary>
        public static IntVec3 FindClearArea(Map map, int left, int right, int half)
        {
            IntVec3 center = map.Center;
            int max = System.Math.Max(map.Size.x, map.Size.z) / 2 - 2;
            for (int r = 0; r < max; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != r) continue;
                        IntVec3 c = new IntVec3(center.x + dx, 0, center.z + dz);
                        if (c.x - left < 3 || c.x + right >= map.Size.x - 3 || c.z - half < 3 || c.z + half >= map.Size.z - 3) continue;
                        bool ok = true;
                        for (int x = c.x - left; x <= c.x + right && ok; x++)
                            for (int z = c.z - half; z <= c.z + half && ok; z++)
                                ok = Clear(new IntVec3(x, 0, z), map);
                        if (ok) return c;
                    }
                }
            }
            return IntVec3.Invalid;
        }

        public static void Teleport(Pawn p, IntVec3 cell)
        {
            p.Position = cell;
            p.Notify_Teleported();
            p.jobs?.StopAll();
        }

        public static Faction OtherHostileFaction(Faction not)
        {
            return Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f =>
                f != not && !f.IsPlayer && !f.Hidden && !f.defeated && f.def.humanlikeFaction
                && f.def.basicMemberKind != null && f.HostileTo(Faction.OfPlayer));
        }

        public static void KillAll(IEnumerable<Pawn> pawns)
        {
            foreach (Pawn p in pawns.ToList())
                if (p != null && !p.Dead && !p.Destroyed && p.Spawned) p.Kill(null);
        }

        public static bool LetterAbout(string mustContain, Faction relatedFaction = null)
        {
            foreach (Letter l in Find.LetterStack.LettersListForReading)
            {
                if (relatedFaction != null && l.relatedFaction != relatedFaction) continue;
                if (l.Label.ToString().Contains(mustContain)) return true;
            }
            return false;
        }

        public static bool IsPinned(Pawn p) => Find.WorldPawns.ForcefullyKeptPawns.Contains(p);
    }
}
