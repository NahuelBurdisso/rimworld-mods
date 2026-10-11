using System.Collections.Generic;
using System.Linq;
using Gesta;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace GestaTest
{
    /// <summary>The Breaking Blow with real raids. Tick 2520, after the chronicle harness.</summary>
    public class KillcamHarness : GameComponent
    {
        bool active, done;
        int ticks;

        public KillcamHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done || ++ticks != 2520) return;
            done = true;
            try { Run(); }
            catch (System.Exception ex) { Kit.Check(false, "killcam harness threw: " + ex); }
            Kit.Summary("Gesta/Killcam");
        }

        static Faction Enemy() =>
            Faction.OfPirates is Faction f && f.HostileTo(Faction.OfPlayer) ? f : Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);

        static List<Pawn> Raid(Map map, float points)
        {
            var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = Enemy();
            parms.points = points;
            parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            var before = new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned);
            TimeSpeed speed = Find.TickManager.CurTimeSpeed;
            IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            if (Find.TickManager.Paused) Find.TickManager.CurTimeSpeed = speed;   // the raid letter auto-pauses (MajorThreat)
            return map.mapPawns.AllPawnsSpawned.Where(p => !before.Contains(p) && p.GetLord() != null && p.HostileTo(Faction.OfPlayer)).ToList();
        }

        void Run()
        {
            Map map = Find.CurrentMap;
            var kc = Killcam.Instance;
            Pawn shooter = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(shooter, map.mapPawns.FreeColonistsSpawned.First().Position + new IntVec3(2, 0, 2), map);
            // clear any hostiles left over from earlier harnesses
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer) && !p.Downed).ToList()) p.Kill(null);

            List<Pawn> small = Raid(map, 500f);
            Kit.Check(small.Count > 0, $"setup: small raid arrived ({small.Count})");
            Lord smallLord = small.FirstOrDefault()?.GetLord();
            Kit.Check(smallLord != null && kc.PointsOf(smallLord) > 0f && kc.PointsOf(smallLord) < 3000f,
                $"the raid's size is remembered ({kc.PointsOf(smallLord):0} points)");
            int played = kc.FinalesPlayed;
            foreach (Pawn p in small) Kit.KillBy(p, shooter);
            Kit.Check(kc.FinalesPlayed == played, "a small raid ends without a finale");

            List<Pawn> big = Raid(map, 3000f);
            Kit.Check(big.Count > 1, $"setup: big raid arrived ({big.Count})");
            Lord bigLord = big.FirstOrDefault()?.GetLord();
            Kit.Check(kc.PointsOf(bigLord) >= GestaMod.Settings.killcamMinPoints, $"big raid remembered ({kc.PointsOf(bigLord):0} points)");
            foreach (Pawn p in big.Take(big.Count - 1)) Kit.KillBy(p, shooter);
            Kit.Check(kc.FinalesPlayed == played, "no finale while raiders remain");
            Kit.KillBy(big.Last(), shooter);
            Kit.Check(kc.FinalesPlayed == played + 1 && kc.LastShooter == shooter, "the last raider's death plays the finale for the shooter");
            Kit.Check(Killcam.SlowMoActive && Find.TickManager.TickRateMultiplier <= Killcam.SlowMoFactor + 0.001f,
                $"time slows for a breath (multiplier {Find.TickManager.TickRateMultiplier:0.00})");

            List<Pawn> again = Raid(map, 3000f);
            foreach (Pawn p in again) Kit.KillBy(p, shooter);
            Kit.Check(kc.FinalesPlayed == played + 1, "a second big raid the same half-day gets no second finale");
            Traverse.Create(kc).Field("startReal").SetValue(-999f);   // let the rest of the run go at full speed
            Traverse.Create(kc).Field("pendingCamera").SetValue(false);
        }
    }
}
