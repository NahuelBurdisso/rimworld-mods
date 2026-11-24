// Forge test kit - copy into a "<Mod> Test" harness mod and rename the namespace.
// Every call below was checked against decomp/1.6.4871 (paths in comments).
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace UnlikelyHeroTest
{
    public static class Kit
    {
        public static int Passed, Failed;

        /// <summary>Armed only by `rw launch --ticks N` (arg is -rwtools-ticks=N).</summary>
        public static bool HarnessArmed => GenCommandLine.TryGetCommandLineArg("rwtools-ticks", out _);

        public static void Check(bool ok, string what)
        {
            if (ok) { Passed++; Log.Message("[Test] PASS " + what); }
            else { Failed++; Log.Error("[Test] FAIL " + what); }
        }

        public static List<Pawn> Colonists(Map map) =>
            map.mapPawns.FreeColonistsSpawned.Where(p => p.story?.traits?.HasTrait(TraitDefOf.Psychopath) != true).ToList();

        /// <summary>Hostile humanlike near a cell. FactionManager.cs:192, PawnGenerator.cs:121.</summary>
        public static Pawn SpawnHostile(Map map, IntVec3 near, PawnKindDef kind = null, Faction faction = null)
        {
            faction ??= Faction.OfPirates ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            Pawn p = PawnGenerator.GeneratePawn(kind ?? PawnKindDefOf.Pirate, faction);
            IntVec3 cell = CellFinder.StandableCellNear(near, map, 6f);
            GenSpawn.Spawn(p, cell.IsValid ? cell : near, map);
            return p;
        }

        /// <summary>Verse/HealthUtility.cs:245 / :327.</summary>
        public static void Down(Pawn p) => HealthUtility.DamageUntilDowned(p, allowBleedingWounds: false);
        /// <summary>Downs a pawn with real blows from an attacker (so the downing has an instigator).</summary>
        public static void DownBy(Pawn victim, Pawn attacker)
        {
            for (int i = 0; i < 60 && !victim.Downed && !victim.Dead; i++)
                victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 4f, 0f, -1f, attacker));
        }

        public static void KillBy(Pawn victim, Pawn killer, DamageDef def = null)
        {
            // Pawn.Kill(DamageInfo? dinfo, Hediff exactCulprit) - Verse/Pawn.cs:2096
            var dinfo = new DamageInfo(def ?? DamageDefOf.Bullet, 30f, 0f, -1f, killer, null, killer?.equipment?.Primary?.def);
            victim.Kill(dinfo);
        }

        public static void Teleport(Pawn p, IntVec3 cell)
        {
            p.Position = cell;
            p.Notify_Teleported();
            p.jobs?.StopAll();
        }

        public static void Summary(string mod)
        {
            if (Failed == 0) Log.Message($"[Test] DONE {mod}: {Passed} passed");
            else Log.Error($"[Test] DONE {mod}: {Passed} passed, {Failed} FAILED");
        }
    }
}
