using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using OldScores;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace OldScoresTest
{
    /// <summary>Feature 2 — The One You Spared: capture, release, exit, a real raid, recognition.</summary>
    public class SparedHarness : GameComponent
    {
        bool active, done;
        int ticks, phase, phaseTick;
        Map map;
        Faction pirates;
        Pawn rescuer, spared;
        readonly List<Pawn> spawned = new List<Pawn>();
        readonly List<Pawn> raiders = new List<Pawn>();

        static readonly MethodInfo IsValidCandidateToRedress = AccessTools.Method(typeof(PawnGenerator), "IsValidCandidateToRedress");

        public SparedHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (ticks < TestUtil.SparedStart) return;
            try { Step(); }
            catch (System.Exception ex) { Kit.Check(false, "spared harness threw: " + ex); Finish(); }
        }

        OldScoresTracker T => OldScoresTracker.Instance;

        void Step()
        {
            switch (phase)
            {
                case 0:
                    phase = 1;
                    Setup();
                    break;
                case 1:
                    // a new tick for the second raid
                    phase = 2;
                    if (!SecondRaid()) { Finish(); break; }
                    phaseTick = ticks;
                    break;
                case 2:
                    if (ticks - phaseTick < 61) break;
                    phase = 3;
                    FarCheckThenMeet();
                    phaseTick = ticks;
                    break;
                case 3:
                    if (spared.Faction == Faction.OfPlayer || ticks - phaseTick > 90)
                    {
                        Recognized();
                        Negatives();
                        Finish();
                    }
                    break;
            }
        }

        void Setup()
        {
            map = Find.CurrentMap;
            pirates = Faction.OfPirates;
            IntVec3 center = TestUtil.FindClearArea(map, 2, 2, 2);
            if (!center.IsValid) center = map.Center;
            rescuer = TestUtil.NewColonist(map, center);
            rescuer.equipment?.DestroyAllEquipment();   // she must not shoot him when they meet

            spared = Kit.SpawnHostile(map, rescuer.Position + new IntVec3(4, 0, 0), null, pirates);
            spared.guest.CapturedBy(Faction.OfPlayer, rescuer);
            SparedRecord rec = T.GetSpared(spared);
            Kit.Check(rec != null && rec.stage == SparedStage.Captured && rec.rescuer == rescuer, "a hostile captured by a colonist is remembered with his captor");

            GenGuest.PrisonerRelease(spared);
            Kit.Check(rec != null && rec.stage == SparedStage.Released, "releasing him marks him as spared");

            spared.ExitMap(false, Rot4.North);
            Kit.Check(rec.stage == SparedStage.Away && spared.IsWorldPawn() && !spared.Dead, "when he walks off the map he stays alive as a world pawn");
            Kit.Check(TestUtil.IsPinned(spared), "...and is pinned in ForcefullyKeptPawns");
            Kit.Check(IsValidCandidateToRedress != null, "setup: found private PawnGenerator.IsValidCandidateToRedress");
            if (IsValidCandidateToRedress != null)
            {
                var request = new PawnGenerationRequest(spared.kindDef, spared.Faction);
                // what vanilla alone says (the mod does not hold Captured pawns), then with the record Away again
                rec.stage = SparedStage.Captured;
                bool vanilla = (bool)IsValidCandidateToRedress.Invoke(null, new object[] { spared, request });
                rec.stage = SparedStage.Away;
                bool redress = (bool)IsValidCandidateToRedress.Invoke(null, new object[] { spared, request });
                Kit.Check(!redress, "PawnGenerator will never redress him into another pawn");
                if (vanilla) Kit.Check(true, "...and that refusal is the mod's postfix (vanilla alone would have allowed it)");
                else Log.Message("[Test] (note: vanilla also refuses to redress him, so the redress check above is weak)");
            }

            // a raid of his faction too soon after: he is not in it, even at a 100% chance
            OldScoresTuning.SparedReturnChance = 1f;
            bool ok = Raid();
            Kit.Check(ok, "setup: first pirate raid executed");
            Kit.Check(!spared.Spawned, "a raid less than 3 days after he left does not bring him");
            TestUtil.KillAll(raiders);
            raiders.Clear();
        }

        bool SecondRaid()
        {
            SparedRecord rec = T.GetSpared(spared);
            rec.leftTick -= 4 * GenDate.TicksPerDay;
            bool ok = Raid();
            Kit.Check(ok, "setup: second pirate raid executed");
            Kit.Check(spared.Spawned && spared.Map == map && spared.Faction == pirates, "4 days later he comes back with his faction's raid");
            if (!spared.Spawned) return false;
            Lord lord = spared.GetLord();
            Kit.Check(lord != null && raiders.Any(r => r != spared && r.GetLord() == lord), "...in the raid's own lord");
            Kit.Check(rec.stage == SparedStage.Returned, "record is now Returned");
            Kit.Check(spared.equipment?.Primary != null, $"he came armed ({spared.equipment?.Primary?.LabelShort ?? "nothing"})");
            Kit.Check(spared.GetInspectString().Contains(rescuer.LabelShort), "his inspect pane says who spared him");
            OldScoresTuning.SparedReturnChance = 0.35f;
            return true;
        }

        void FarCheckThenMeet()
        {
            float dist = spared.Position.DistanceTo(rescuer.Position);
            if (dist > OldScoresTuning.SparedRecognizeRadius + 1f)
                Kit.Check(spared.Faction == pirates, $"60 ticks with his captor {dist:0} cells away: still a raider");
            else
                Log.Message($"[Test] (skipped far check: raid spawned only {dist:0} cells from the captor)");
            // the rest of the raid would shoot the captor: gone before she walks up to him
            TestUtil.KillAll(raiders.Where(r => r != spared));
            IntVec3 cell = CellFinder.StandableCellNear(spared.Position, map, 7f,
                c => c.DistanceTo(spared.Position) >= 3f && GenSight.LineOfSight(c, spared.Position, map));
            if (!cell.IsValid) cell = CellFinder.StandableCellNear(spared.Position, map, 3f);
            TestUtil.Teleport(rescuer, cell);
            rescuer.drafter.Drafted = true;   // stand there instead of fleeing from a raider
        }

        void Recognized()
        {
            Kit.Check(spared.Faction == Faction.OfPlayer, $"when he sees his captor within 12 cells he joins the colony ({ticks - phaseTick} ticks)");
            Kit.Check(spared.equipment == null || spared.equipment.Primary == null, "...dropping his weapon");
            Kit.Check(T.GetSpared(spared) == null && !TestUtil.IsPinned(spared), "...and the record is cleared and he is unpinned");
            Kit.Check(TestUtil.LetterAbout(spared.LabelShort), "...with a letter telling the story");
        }

        void Negatives()
        {
            // captured, never released, escapes: owes nothing
            Pawn runner = Kit.SpawnHostile(map, rescuer.Position + new IntVec3(5, 0, 0), null, pirates);
            runner.guest.CapturedBy(Faction.OfPlayer, rescuer);
            runner.ExitMap(false, Rot4.North);
            Kit.Check(T.GetSpared(runner) == null && !TestUtil.IsPinned(runner), "a prisoner who escapes without being released is not remembered");

            // feature off
            OldScoresMod.Settings.spared = false;
            Pawn other = Kit.SpawnHostile(map, rescuer.Position + new IntVec3(5, 0, 0), null, pirates);
            spawned.Add(other);
            other.guest.CapturedBy(Faction.OfPlayer, rescuer);
            OldScoresMod.Settings.spared = true;
            Kit.Check(T.GetSpared(other) == null, "with the feature off, a capture is not remembered");
        }

        bool Raid()
        {
            List<Pawn> before = map.mapPawns.SpawnedPawnsInFaction(pirates).ToList();
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = pirates;
            parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            parms.points = 200f;
            parms.forced = true;
            bool ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            raiders.AddRange(map.mapPawns.SpawnedPawnsInFaction(pirates).Where(p => !before.Contains(p)));
            return ok;
        }

        void Finish()
        {
            OldScoresTuning.SparedReturnChance = 0.35f;
            OldScoresMod.Settings.spared = true;
            if (rescuer?.drafter != null && rescuer.Spawned) rescuer.drafter.Drafted = false;
            TestUtil.KillAll(raiders.Where(r => r != spared && r.Faction != Faction.OfPlayer));
            TestUtil.KillAll(spawned);
            Kit.Summary("OldScores/Spared");
            done = true;
        }
    }
}
