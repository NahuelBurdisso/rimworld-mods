using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace AftermathTest
{
    /// <summary>Helpers shared by the Aftermath harnesses. All calls checked against decomp/1.6.4871.</summary>
    public static class TestUtil
    {
        /// <summary>Gesta's harness owns ticks 60-2650, Old Scores' 2700-~4000 of the same game; ours start at 4600.</summary>
        public const int OfferingsStart = 4600, DoorsStart = 4650, StandardsStart = 4700, FoesStart = 5200, VigilStart = 5400;

        /// <summary>Hard stop: whatever is still waiting fails here (the run is `rw launch --ticks 7000`).</summary>
        public const int Deadline = 6900;

        public static int Now => Find.TickManager.TicksGame;

        public static Pawn NewColonist(Map map, IntVec3 near, System.Predicate<Pawn> accept = null)
        {
            for (int i = 0; i < 30; i++)
            {
                Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (p.WorkTagIsDisabled(WorkTags.Violent) || p.WorkTagIsDisabled(WorkTags.Social)
                    || (accept != null && i < 29 && !accept(p)))
                {
                    Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard);
                    continue;
                }
                IntVec3 cell = CellFinder.StandableCellNear(near, map, 4f, c => c.GetFirstPawn(map) == null);
                GenSpawn.Spawn(p, cell.IsValid ? cell : near, map);
                return p;
            }
            throw new System.Exception("could not generate a colonist");
        }

        /// <summary>Spawns a pawn of the faction at (or next to) the cell.</summary>
        public static Pawn SpawnAt(Map map, IntVec3 cell, Faction faction, PawnKindDef kind = null)
        {
            Pawn p = PawnGenerator.GeneratePawn(kind ?? faction.def.basicMemberKind ?? PawnKindDefOf.Pirate, faction);
            IntVec3 at = cell.Standable(map) && cell.GetFirstPawn(map) == null ? cell : CellFinder.StandableCellNear(cell, map, 4f);
            GenSpawn.Spawn(p, at.IsValid ? at : cell, map);
            return p;
        }

        public static void Teleport(Pawn p, IntVec3 cell)
        {
            p.Position = cell;
            p.Notify_Teleported();
            p.jobs?.StopAll();
        }

        public static T SpawnBuilding<T>(ThingDef def, ThingDef stuff, IntVec3 cell, Map map, Rot4? rot = null) where T : Thing
        {
            Thing t = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff ?? GenStuff.DefaultStuffFor(def) : null);
            t.SetFaction(Faction.OfPlayer);
            return (T)GenSpawn.Spawn(t, cell, map, rot ?? Rot4.South);
        }

        // ------------------------------------------------------------------ clear test strips

        static readonly HashSet<IntVec3> reserved = new HashSet<IntVec3>();

        static bool Clear(IntVec3 c, Map map) =>
            c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null && !c.Fogged(map)
            && !c.GetTerrain(map).IsWater && c.GetFirstItem(map) == null && !reserved.Contains(c);

        /// <summary>Nothing in the cell that clearing it would hurt: pawns, player things, built (non-rock) buildings.</summary>
        static bool Clearable(IntVec3 c, Map map)
        {
            if (reserved.Contains(c)) return false;
            List<Thing> things = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t is Pawn || t.Faction == Faction.OfPlayer) return false;
                if (t is Building && !(t.def.building?.isNaturalRock ?? false)) return false;
            }
            return true;
        }

        static bool RectOk(IntVec3 c, Map map, int w, int h, bool clearOnly)
        {
            if (c.x < 4 || c.x + w >= map.Size.x - 4 || c.z < 4 || c.z + h >= map.Size.z - 4) return false;
            for (int x = c.x - 1; x <= c.x + w; x++)          // one cell of margin all round
                for (int z = c.z - 1; z <= c.z + h; z++)
                {
                    var cell = new IntVec3(x, 0, z);
                    if (clearOnly ? !Clear(cell, map) || cell.GetFirstPawn(map) != null : !Clearable(cell, map)) return false;
                }
            return true;
        }

        /// <summary>
        /// A w x h strip of open soil (bottom-left corner returned) near <paramref name="near"/>, never overlapping
        /// another harness's strip. Bounded search (radius 45): first an already clear strip, else one that can be
        /// cleared without touching pawns or player things; then clears it (unfogs, removes plants/rocks/items,
        /// lays soil over water or impassable ground). Invalid if nothing works.
        /// </summary>
        public static IntVec3 PrepareStrip(Map map, IntVec3 near, int w, int h, string who)
        {
            IntVec3 root = IntVec3.Invalid;
            for (int pass = 0; pass < 2 && !root.IsValid; pass++)
                for (int r = 0; r <= 45 && !root.IsValid; r++)
                    for (int dx = -r; dx <= r && !root.IsValid; dx++)
                        for (int dz = -r; dz <= r && !root.IsValid; dz++)
                        {
                            if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != r) continue;
                            var c = new IntVec3(near.x + dx, 0, near.z + dz);
                            if (RectOk(c, map, w, h, clearOnly: pass == 0)) root = c;
                        }
            if (!root.IsValid) { Log.Message($"[Test] {who}: no strip {w}x{h} found"); return IntVec3.Invalid; }
            var tmp = new List<Thing>();
            for (int x = root.x - 1; x <= root.x + w; x++)
                for (int z = root.z - 1; z <= root.z + h; z++)
                {
                    var c = new IntVec3(x, 0, z);
                    reserved.Add(c);
                    map.fogGrid.Unfog(c);
                    tmp.Clear();
                    tmp.AddRange(map.thingGrid.ThingsListAt(c));
                    foreach (Thing t in tmp)
                    {
                        if (t.Destroyed || t is Pawn) continue;
                        t.Destroy(DestroyMode.Vanish);
                    }
                    TerrainDef terr = c.GetTerrain(map);
                    if (terr.IsWater || terr.passability != Traversability.Standable || !c.Walkable(map))
                        map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                }
            Log.Message($"[Test] {who}: test strip at {root} ({w}x{h})");
            return root;
        }

        /// <summary>A colonist's position, used as the anchor for strips (each harness offsets it).</summary>
        public static IntVec3 Anchor(Map map, int dx, int dz)
        {
            IntVec3 c = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center;
            return new IntVec3(Mathf.Clamp(c.x + dx, 8, map.Size.x - 16), 0, Mathf.Clamp(c.z + dz, 8, map.Size.z - 16));
        }

        // ------------------------------------------------------------------ factions and raids

        /// <summary>A hostile humanlike faction that can raid; pirates first. Null if there is none.</summary>
        public static Faction HostileFaction(Faction not = null)
        {
            List<Faction> all = Find.FactionManager.AllFactionsListForReading.Where(f =>
                f != not && !f.IsPlayer && !f.Hidden && !f.defeated && f.def.humanlikeFaction
                && f.HostileTo(Faction.OfPlayer)
                && f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Any(g => g.kindDef == PawnGroupKindDefOf.Combat)).ToList();
            // basicMemberKind can be null (SpawnAt falls back to Pirate): prefer factions that have one, pirates first
            return all.OrderByDescending(f => f.def.basicMemberKind != null).ThenByDescending(f => f == Faction.OfPirates).FirstOrDefault()
                ?? (not == null ? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false) : null);
        }

        /// <summary>A real raid (IncidentWorker_RaidEnemy) of the faction; returns the raiders it spawned.</summary>
        public static List<Pawn> Raid(Map map, Faction faction, float points)
        {
            List<Pawn> before = map.mapPawns.SpawnedPawnsInFaction(faction).ToList();
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = faction;
            parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            parms.points = points;
            parms.forced = true;
            TimeSpeed speed = Find.TickManager.CurTimeSpeed;
            bool ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            // the raid letter (ThreatBig) pauses the game under the default auto-pause setting
            if (Find.TickManager.CurTimeSpeed == TimeSpeed.Paused && speed != TimeSpeed.Paused) Find.TickManager.CurTimeSpeed = speed;
            if (!ok) return new List<Pawn>();
            return map.mapPawns.SpawnedPawnsInFaction(faction).Where(p => !before.Contains(p)).ToList();
        }

        public static void KillAll(IEnumerable<Pawn> pawns)
        {
            foreach (Pawn p in pawns.ToList())
                if (p != null && !p.Dead && !p.Destroyed && p.Spawned) p.Kill(null);
        }

        /// <summary>
        /// Real blows from an attacker until the victim is down (or dead): blunt hits on the legs with full armour
        /// penetration, so armoured raid leaders go down too and the downing carries the attacker as instigator.
        /// Bounded at 150 hits.
        /// </summary>
        public static void DownBy(Pawn victim, Pawn attacker)
        {
            for (int i = 0; i < 150 && !victim.Downed && !victim.Dead; i++)
            {
                BodyPartRecord leg = victim.health.hediffSet.GetNotMissingParts().Where(x => x.def == BodyPartDefOf.Leg).RandomElementWithFallback();
                victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 6f, 1f, -1f, attacker, leg));
            }
        }

        /// <summary>Heals a test colonist enough to stand up again (injuries, blood loss, legs).</summary>
        public static void Heal(Pawn p)
        {
            if (p == null || p.Dead) return;
            HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(p);
            foreach (Hediff h in p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.BloodLoss).ToList())
                p.health.RemoveHediff(h);
        }

        public static BodyPartRecord Part(Pawn p, BodyPartDef def) =>
            p.health.hediffSet.GetNotMissingParts().FirstOrDefault(x => x.def == def);

        // ------------------------------------------------------------------ save/load roundtrip

        /// <summary>
        /// Saves an IExposable with Scribe to a temp file and loads it back (values only - no cross references).
        /// GenFilePaths.TempFolderPath is Unity's temporary cache folder.
        /// </summary>
        public static T Roundtrip<T>(T obj) where T : class, IExposable
        {
            string path = Path.Combine(GenFilePaths.TempFolderPath, "aftermath_roundtrip.xml");
            Scribe.saver.InitSaving(path, "AftermathTest");
            Scribe_Deep.Look(ref obj, "rec");
            Scribe.saver.FinalizeSaving();
            T copy = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref copy, "rec");
            Scribe.loader.FinalizeLoading();
            try { File.Delete(path); } catch { }
            return copy;
        }

        // ------------------------------------------------------------------ keep the game running while a harness waits

        static readonly HashSet<string> finished = new HashSet<string>();
        public const int HarnessCount = 5;
        public static void MarkFinished(string name) => finished.Add(name);
        public static bool AllFinished => finished.Count >= HarnessCount;
    }

    /// <summary>
    /// A ThreatBig letter (every raid) and some death letters pause the game under the default auto-pause
    /// setting (LetterStack.ReceiveLetter -> TickManager.Pause). Paused, no GameComponentTick runs and the
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
