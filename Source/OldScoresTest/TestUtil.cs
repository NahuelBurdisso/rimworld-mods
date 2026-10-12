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
        // Gesta's harnesses own ticks 60-~2900 of the same game (ChampionHarness runs real raids up to
        // ~2900). Each of ours first clears the map of hostiles at its start tick and begins work
        // ClearTicks later. Worst-case ends: nicknames ~3380, spared ~3670, beasts ~3760 (all < 4400);
        // echoes (three real raids) 3900 to ~6100, worst case ~7400: run with --ticks 8000.
        public const int NicknamesStart = 3000, SparedStart = 3400, BeastsStart = 3750, EchoesStart = 3900, ClearTicks = 5;

        /// <summary>
        /// Start-of-harness prelude: at the start tick removes every hostile pawn and hostile building
        /// left on the map (earlier harness raids, turrets, manhunters); returns true while the harness
        /// should still wait.
        /// </summary>
        public static bool Prelude(int ticks, int start, string who)
        {
            if (ticks < start) return true;
            if (ticks == start) ClearHostiles(Find.CurrentMap, who);
            return ticks < start + ClearTicks;
        }

        public static void ClearHostiles(Map map, string who)
        {
            if (map == null) return;
            int pawns = 0, buildings = 0;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (p.Dead || p.Destroyed || !p.HostileTo(Faction.OfPlayer) || p.IsPrisonerOfColony) continue;
                p.Kill(null);
                p.Corpse?.Destroy();
                pawns++;
            }
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).ToList())
            {
                if (t.Destroyed || t.Faction == null || !t.HostileTo(Faction.OfPlayer)) continue;
                t.Destroy();
                buildings++;
            }
            Log.Message($"[Test] ({who}: cleared {pawns} hostile pawns and {buildings} hostile buildings before starting)");
        }

        public static Pawn NewColonist(Map map, IntVec3 near, System.Predicate<Pawn> accept = null)
        {
            for (int i = 0; i < 30; i++)
            {
                Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (p.WorkTagIsDisabled(WorkTags.Violent) || (accept != null && i < 29 && !accept(p)))
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
            && !c.GetTerrain(map).IsWater;

        /// <summary>Nothing in the cell that clearing it would hurt: pawns, player things, built (non-rock) buildings.</summary>
        static bool Clearable(IntVec3 c, Map map)
        {
            List<Thing> things = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t is Pawn || t.Faction == Faction.OfPlayer) return false;
                if (t is Building && !(t.def.building?.isNaturalRock ?? false)) return false;
            }
            return true;
        }

        static bool RectOk(IntVec3 c, Map map, int left, int right, int half, bool clearOnly)
        {
            if (c.x - left < 3 || c.x + right >= map.Size.x - 3 || c.z - half < 3 || c.z + half >= map.Size.z - 3) return false;
            for (int x = c.x - left; x <= c.x + right; x++)
                for (int z = c.z - half; z <= c.z + half; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (clearOnly ? !Clear(cell, map) || cell.GetFirstPawn(map) != null : !Clearable(cell, map)) return false;
                }
            return true;
        }

        /// <summary>
        /// A test strip [x-left, x+right] x [z-half, z+half] of open soil, guaranteed on any quicktest map:
        /// near <paramref name="near"/> (a colonist), it first looks (bounded, radius 40) for a strip that is
        /// already clear, else one that can be cleared without touching pawns or player things, and then
        /// clears it: unfogs, removes plants, rocks, chunks, filth and items, and lays soil over water or
        /// impassable ground. Logs and returns the root cell.
        /// </summary>
        public static IntVec3 PrepareStrip(Map map, IntVec3 near, int left, int right, int half)
        {
            IntVec3 root = IntVec3.Invalid;
            for (int pass = 0; pass < 2 && !root.IsValid; pass++)
            {
                for (int r = 0; r <= 40 && !root.IsValid; r++)
                    for (int dx = -r; dx <= r && !root.IsValid; dx++)
                        for (int dz = -r; dz <= r && !root.IsValid; dz++)
                        {
                            if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != r) continue;
                            IntVec3 c = new IntVec3(near.x + dx, 0, near.z + dz);
                            if (RectOk(c, map, left, right, half, clearOnly: pass == 0)) root = c;
                        }
            }
            if (!root.IsValid)
            {
                // last resort: the map centre, clamped into bounds; pawns there are moved, nothing else is spared
                root = new IntVec3(
                    UnityEngine.Mathf.Clamp(map.Center.x, left + 3, map.Size.x - right - 4), 0,
                    UnityEngine.Mathf.Clamp(map.Center.z, half + 3, map.Size.z - half - 4));
            }
            var tmp = new List<Thing>();
            for (int x = root.x - left; x <= root.x + right; x++)
                for (int z = root.z - half; z <= root.z + half; z++)
                {
                    IntVec3 c = new IntVec3(x, 0, z);
                    map.fogGrid.Unfog(c);
                    tmp.Clear();
                    tmp.AddRange(map.thingGrid.ThingsListAt(c));
                    foreach (Thing t in tmp)
                    {
                        if (t.Destroyed) continue;
                        if (t is Pawn p)
                        {
                            IntVec3 away = CellFinder.StandableCellNear(new IntVec3(root.x, 0, root.z + half + 4), map, 10f);
                            if (away.IsValid) Teleport(p, away);
                            continue;
                        }
                        t.Destroy(DestroyMode.Vanish);
                    }
                    TerrainDef terr = c.GetTerrain(map);
                    if (terr.IsWater || terr.passability != Traversability.Standable || !c.Walkable(map))
                        map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                }
            bool ok = RectOk(root, map, left, right, half, clearOnly: true);
            Log.Message($"[Test] test strip at {root} ({left + right + 1}x{2 * half + 1}), clear={ok}");
            return ok ? root : IntVec3.Invalid;
        }

        /// <summary>
        /// A hostile humanlike faction that can raid. Factions matching <paramref name="prefer"/> first
        /// (e.g. one nobody has a nickname from yet), pirates before others. Null if there is none.
        /// </summary>
        public static Faction HostileFaction(System.Predicate<Faction> prefer = null)
        {
            List<Faction> all = Find.FactionManager.AllFactionsListForReading.Where(f =>
                !f.IsPlayer && !f.Hidden && !f.defeated && f.def.humanlikeFaction && f.def.basicMemberKind != null
                && f.HostileTo(Faction.OfPlayer)
                && f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Any(g => g.kindDef == PawnGroupKindDefOf.Combat)).ToList();
            IEnumerable<Faction> ordered = all.OrderByDescending(f => prefer != null && prefer(f)).ThenByDescending(f => f == Faction.OfPirates);
            return ordered.FirstOrDefault() ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
        }

        // --- keep the game running while a harness waits -------------------------------------------

        static readonly HashSet<string> finished = new HashSet<string>();
        public const int HarnessCount = 4;
        public static void MarkFinished(string name) => finished.Add(name);
        public static bool AllFinished => finished.Count >= HarnessCount;

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

namespace OldScoresTest
{
    /// <summary>
    /// A ThreatBig letter (every raid) pauses the game when Prefs.AutomaticPauseMode is MajorThreat
    /// (LetterStack.ReceiveLetter -> TickManager.Pause). Paused, no GameComponentTick runs and the
    /// harness waits forever. GameComponentUpdate still runs every frame: while a harness is unfinished,
    /// resume the speed the game had before the pause.
    /// </summary>
    public class HarnessWatchdog : GameComponent
    {
        bool active;
        int resumes;

        public HarnessWatchdog(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentUpdate()
        {
            if (!active || TestUtil.AllFinished || Find.TickManager == null) return;
            if (Find.TickManager.CurTimeSpeed != TimeSpeed.Paused) return;
            TimeSpeed back = Find.TickManager.prePauseTimeSpeed;
            Find.TickManager.CurTimeSpeed = back == TimeSpeed.Paused ? TimeSpeed.Normal : back;
            if (resumes++ < 20) Log.Message($"[Test] (watchdog: the game was paused at tick {Find.TickManager.TicksGame}, probably by a letter; resumed at {Find.TickManager.CurTimeSpeed})");
        }
    }
}
