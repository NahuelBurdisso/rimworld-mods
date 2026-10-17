using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TheDogKnows;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TheDogKnowsTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N`. DESIGN.md "Plan de prueba": a husky stares at a real revenant and a
    /// real sightstealer (the latter from the map tick, not a direct scan), a cat at a hidden raider; the entity is
    /// never revealed; cooldowns, reveal, distance, walls and every excluded animal are checked.
    /// Entities are frozen (mindState.Active = false, no job) so they stay where the test puts them.
    /// </summary>
    public class TheDogKnowsHarness : GameComponent
    {
        bool active, done;
        int ticks, wait, step, resumes;
        List<Func<int>> steps;

        Map map;
        MapComponent_TheDogKnows comp;
        List<IntVec3> lane;
        Pawn husky, entity, cat, muffalo, wild, sleeper;
        int growlsBefore, passiveStart;
        readonly List<Thing> extras = new List<Thing>();

        static JobDef Stare => TDKDefOf.TDK_StareAtUnseen;

        public TheDogKnowsHarness(Game game) { }

        public override void FinalizeInit()
        {
            active = Kit.HarnessArmed;
        }

        /// <summary>Hostiles on the map can trip the auto-pause; keep the test running.</summary>
        public override void GameComponentUpdate()
        {
            if (!active || done || Find.TickManager == null || Find.TickManager.CurTimeSpeed != TimeSpeed.Paused) return;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            if (resumes++ < 10) Log.Message($"[Test] (watchdog: game auto-paused at tick {Find.TickManager.TicksGame}, resumed)");
        }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (ticks < 60) return;
            if (wait > 0) { wait--; return; }
            try
            {
                steps ??= new List<Func<int>> { Setup, RevenantStare, FacingAndCooldown, RevealEnds, AnyInvisibilityHediff,
                    LoseInterest, Negatives, AnimalsAndCat, SightstealerSetup, PollPassive };
                if (step >= steps.Count) { Finish(); return; }
                wait = steps[step++]();
            }
            catch (Exception e)
            {
                Kit.Check(false, "harness threw: " + e);
                Finish();
            }
        }

        // ---------- helpers ----------

        static ThingDef Race(string name) => DefDatabase<ThingDef>.GetNamed(name);

        Pawn Animal(string kind, Faction faction, IntVec3 cell)
        {
            var req = new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed(kind), faction, fixedBiologicalAge: 3f);
            Pawn p = PawnGenerator.GeneratePawn(req);
            GenSpawn.Spawn(p, cell, map);
            if (p.needs?.food != null) p.needs.food.CurLevel = p.needs.food.MaxLevel;
            if (p.needs?.rest != null) p.needs.rest.CurLevel = p.needs.rest.MaxLevel;
            extras.Add(p);
            return p;
        }

        Pawn SpawnAt(Pawn p, IntVec3 cell)
        {
            GenSpawn.Spawn(p, cell, map);
            extras.Add(p);
            return p;
        }

        /// <summary>No job and an inactive mind: the pawn stands where it is put (like a dormant mech).</summary>
        static void Freeze(Pawn p)
        {
            p.jobs?.StopAll();
            p.mindState.Active = false;
        }

        static void Move(Pawn p, IntVec3 cell)
        {
            p.Position = cell;
            p.Notify_Teleported();
            p.jobs?.StopAll();
        }

        void Kill(Thing t)
        {
            if (t != null && !t.Destroyed) t.Destroy();
        }

        /// <summary>
        /// A straight 3-wide lane of 16 cells along +x: standable, unfogged, empty, in sight end to end, and at
        /// least 25 cells from every colonist (sightstealers turn visible when a colonist sees them).
        /// </summary>
        List<IntVec3> FindLane()
        {
            const int len = 16;
            List<Pawn> cols = map.mapPawns.FreeColonistsSpawned.ToList();
            IntVec3 center = cols.FirstOrDefault()?.Position ?? map.Center;
            IntVec3 best = IntVec3.Invalid;
            int bestDist = int.MaxValue;
            foreach (IntVec3 c in map.AllCells)
            {
                int d = (c - center).LengthHorizontalSquared;
                if (d >= bestDist || d < 26 * 26) continue;
                if (c.x + len >= map.Size.x - 3 || c.z < 3 || c.z >= map.Size.z - 3) continue;
                bool ok = true;
                for (int i = 0; i < len && ok; i++)
                    for (int dz = -1; dz <= 1 && ok; dz++)
                    {
                        IntVec3 x = new IntVec3(c.x + i, 0, c.z + dz);
                        if (!x.Standable(map) || x.Fogged(map) || x.GetFirstPawn(map) != null || x.GetEdifice(map) != null) ok = false;
                        else if (cols.Any(p => p.Position.InHorDistOf(x, 25f))) ok = false;
                    }
                if (!ok || !GenSight.LineOfSight(c, c + new IntVec3(len - 1, 0, 0), map)) continue;
                best = c;
                bestDist = d;
            }
            if (!best.IsValid) return null;
            var cells = new List<IntVec3>();
            for (int i = 0; i < len; i++) cells.Add(best + new IntVec3(i, 0, 0));
            return cells;
        }

        Pawn HiddenRaider(IntVec3 cell)
        {
            Faction f = Faction.OfPirates ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            Pawn p = SpawnAt(PawnGenerator.GeneratePawn(PawnKindDefOf.Pirate, f), cell);
            Freeze(p);
            p.health.AddHediff(HediffDefOf.HoraxianInvisibility);
            return p;
        }

        // ---------- steps (each returns ticks to wait before the next) ----------

        int Setup()
        {
            map = Find.CurrentMap;
            comp = MapComponent_TheDogKnows.For(map);
            Kit.Check(comp != null, "MapComponent_TheDogKnows exists on the map");
            UnseenSenseDef h = UnseenSenseDef.For(Race("Husky")), c = UnseenSenseDef.For(Race("Cat"));
            Kit.Check(h?.defName == "TDK_Canines" && c?.defName == "TDK_Felines" && UnseenSenseDef.For(Race("Muffalo")) == null,
                $"sense list: Husky -> {h?.defName}, Cat -> {c?.defName}, Muffalo -> none");
            Kit.Check(UnseenSenseDef.For(Race("YorkshireTerrier")) == h && UnseenSenseDef.For(Race("LabradorRetriever")) == h,
                "sense list: Yorkshire terrier and Labrador are canines");
            Kit.Check(h != null && c != null && h.radius == 11f && c.radius == 9f, "radius: dogs 11, cats 9");
            if (ModsConfig.IsActive("sarg.alphaanimals"))
            {
                ThingDef skip = DefDatabase<ThingDef>.GetNamedSilentFail("AA_Skiphound");
                ThingDef owl = DefDatabase<ThingDef>.GetNamedSilentFail("AA_PhoenixOwlcat");
                Kit.Check(skip != null && UnseenSenseDef.For(skip) == h && owl != null && UnseenSenseDef.For(owl) == c,
                    "Alpha Animals active: skiphound is a canine, phoenix owlcat a feline (MayRequire entries resolved)");
            }
            Pawn pup = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Husky"), Faction.OfPlayer, fixedBiologicalAge: 0.05f));
            Kit.Check(pup.ageTracker.CurLifeStageIndex == 0 && TDKUtility.GrowlSoundFor(pup, h) != null,
                $"a husky puppy ({pup.ageTracker.CurLifeStage.defName}) still has a growl ({TDKUtility.GrowlSoundFor(pup, h)?.defName})");
            Find.WorldPawns.PassToWorld(pup, RimWorld.Planet.PawnDiscardDecideMode.Discard);
            Log.Message("[Test] info sensing races: " + string.Join(", ", DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.race != null && UnseenSenseDef.For(d) != null).Select(d => d.defName)));

            lane = FindLane();
            if (lane == null) throw new Exception("no free lane on the map");
            Log.Message($"[Test] info lane from {lane[0]} to {lane[lane.Count - 1]}");
            husky = Animal("Husky", Faction.OfPlayer, lane[0]);
            entity = SpawnAt(PawnGenerator.GeneratePawn(PawnKindDefOf.Revenant, Faction.OfEntities), lane[6]);
            Freeze(entity);
            return 3;
        }

        int RevenantStare()
        {
            Kit.Check(TDKUtility.IsUnseen(entity) && entity.IsHiddenFromPlayer(), "precondition: the revenant is hidden (HoraxianInvisibility active)");
            Kit.Check(TDKUtility.CanReact(husky), $"precondition: the husky can react (job {husky.CurJobDef?.defName ?? "none"})");
            int motes = TDKUtility.MotesMade, msgs = TDKUtility.MessagesSent;
            growlsBefore = TDKUtility.Growls;
            int n = comp.ScanNow();
            Kit.Check(husky.CurJobDef == Stare, $"revenant 6 cells away: the husky stares (started {n}, job {husky.CurJobDef?.defName})");
            if (husky.CurJobDef != Stare) Log.Message("[Test] info " + comp.Explain(husky));
            Kit.Check(husky.CurJob != null && !husky.CurJob.targetA.IsValid && !husky.CurJob.targetB.IsValid
                && (husky.jobs.curDriver as JobDriver_StareAtUnseen)?.Unseen == entity,
                "the job has no targets (nothing to draw a line to); the driver holds the entity");
            Kit.Check(entity.IsPsychologicallyInvisible() && entity.IsHiddenFromPlayer(), "the revenant stays hidden: no reveal");
            string report = husky.GetJobReport() ?? "";
            Kit.Check(report.Contains("something you can't see"), $"inspect report: \"{report}\"");
            Kit.Check(TDKUtility.MotesMade == motes + 1, "hackles mote over the husky");
            Kit.Check(TDKUtility.MessagesSent == msgs + 1, "one message pointing at the husky");
            Kit.Check(comp.OnCooldown(husky), "the husky is on cooldown");
            return 20;
        }

        int FacingAndCooldown()
        {
            Kit.Check(TDKUtility.Growls > growlsBefore, $"low growl played ({TDKUtility.Growls - growlsBefore})");
            Kit.Check(husky.CurJobDef == Stare, "still staring after 20 ticks");
            float ang = (entity.DrawPos - husky.DrawPos).AngleFlat();
            Rot4 want = Rot4.FromAngleFlat(ang);
            Kit.Check(husky.Rotation == want, $"the husky faces the revenant ({husky.Rotation.ToStringHuman()} vs {want.ToStringHuman()}, angle {ang:F0})");
            Kit.Check(entity.IsHiddenFromPlayer(), "after 20 ticks of staring the revenant is still hidden");

            int started = MapComponent_TheDogKnows.StaresStarted;
            comp.ScanNow();
            Kit.Check(started == MapComponent_TheDogKnows.StaresStarted && husky.CurJobDef == Stare, "a scan during the stare does not restart it");

            husky.jobs.EndCurrentJob(JobCondition.InterruptForced);
            comp.ScanNow();
            Kit.Check(husky.CurJobDef != Stare, $"cooldown: right after a stare the husky does not stare again (job {husky.CurJobDef?.defName})");

            comp.ClearCooldowns(messageToo: false);
            int msgs = TDKUtility.MessagesSent;
            comp.ScanNow();
            Kit.Check(husky.CurJobDef == Stare, "cooldown over: the husky stares again");
            Kit.Check(TDKUtility.MessagesSent == msgs, "message cooldown: no second message within the hour");

            entity.GetInvisibilityComp().BecomeVisible(instant: true);
            return JobDriver_StareAtUnseen.CheckEvery + 1;
        }

        int RevealEnds()
        {
            Kit.Check(!entity.IsPsychologicallyInvisible(), "precondition: the revenant was made visible");
            Kit.Check(husky.CurJobDef != Stare, $"the stare ends once the entity is visible (job {husky.CurJobDef?.defName})");
            Kill(entity);
            comp.ClearCooldowns();
            int checks = MapComponent_TheDogKnows.AnimalChecks;
            comp.ScanNow();
            Kit.Check(MapComponent_TheDogKnows.AnimalChecks == checks, "no hidden hostile on the map: the scan never looks at the animals");
            return 1;
        }

        int AnyInvisibilityHediff()
        {
            Faction f = Faction.OfPirates ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            entity = SpawnAt(PawnGenerator.GeneratePawn(PawnKindDefOf.Pirate, f), lane[5]);
            Freeze(entity);
            Move(husky, lane[0]);
            comp.ScanNow();
            Kit.Check(husky.CurJobDef != Stare, "a visible raider 5 cells away does not trigger a stare");
            entity.health.AddHediff(HediffDefOf.HoraxianInvisibility);
            Kit.Check(TDKUtility.IsUnseen(entity), "precondition: the raider with HoraxianInvisibility is unseen");
            comp.ScanNow();
            Kit.Check(husky.CurJobDef == Stare, "a raider hidden by an invisibility hediff triggers the stare");

            // walk away beyond radius x 1.5 (16.5): the stare must end at the next check
            IntVec3 far = IntVec3.Invalid;
            CellFinder.TryFindRandomCellNear(husky.Position, map, 30,
                x => x.Standable(map) && !x.Fogged(map) && x.GetFirstPawn(map) == null && !x.InHorDistOf(husky.Position, 19f), out far);
            if (!far.IsValid) throw new Exception("no far cell");
            Move(entity, far);
            return JobDriver_StareAtUnseen.CheckEvery + 1;
        }

        int LoseInterest()
        {
            Kit.Check(husky.CurJobDef != Stare, $"the stare ends when the entity is out of range (job {husky.CurJobDef?.defName})");
            comp.ClearCooldowns();
            Move(husky, lane[0]);
            Move(entity, lane[13]);
            comp.ScanNow();
            Kit.Check(husky.CurJobDef != Stare, "13 cells away (dog radius 11): no stare");

            Move(entity, lane[6]);
            Thing wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog), lane[3], map);
            Kit.Check(!GenSight.LineOfSight(lane[0], lane[6], map, skipFirstCell: true), "precondition: the wall blocks sight");
            comp.ScanNow();
            Kit.Check(husky.CurJobDef != Stare, "a wall in between blocks the sense");
            Kill(wall);
            Kit.Check(GenSight.LineOfSight(lane[0], lane[6], map, skipFirstCell: true), "wall removed: sight is clear again");
            comp.ScanNow();
            Kit.Check(husky.CurJobDef == Stare, "control: without the wall the husky stares");
            Kill(entity);
            husky.jobs.EndCurrentJob(JobCondition.InterruptForced);
            comp.ClearCooldowns();
            return 1;
        }

        int Negatives()
        {
            // your own invisible colonist (psycast invisibility) never counts
            var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, canGeneratePawnRelations: false);
            Pawn col = SpawnAt(PawnGenerator.GeneratePawn(req), lane[6]);
            Freeze(col);
            HediffDef psy = DefDatabase<HediffDef>.GetNamedSilentFail("PsychicInvisibility") ?? HediffDefOf.HoraxianInvisibility;
            col.health.AddHediff(psy);
            Move(husky, lane[0]);
            Kit.Check(TDKUtility.HasActiveInvisibility(col), $"precondition: the colonist is invisible ({psy.defName})");
            comp.ScanNow();
            Kit.Check(husky.CurJobDef != Stare && !TDKUtility.IsUnseen(col), "your own invisible colonist does not count");
            Kill(col);

            entity = HiddenRaider(lane[6]);
            Kit.Check(TDKUtility.IsUnseen(entity), "precondition: a dormant hidden raider (inactive mind) is unseen and scanned");

            Move(husky, lane[0]);
            husky.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Flee, lane[0] + new IntVec3(0, 0, 1)), JobCondition.InterruptForced);
            comp.ScanNow();
            Kit.Check(husky.CurJobDef != Stare, $"a fleeing husky does not stop to stare (job {husky.CurJobDef?.defName})");

            Move(husky, lane[0]);
            PawnUtility.ForceWait(husky, 500);
            Kit.Check(husky.CurJob?.expiryInterval > 0, $"precondition: forced wait like milking or tending (job {husky.CurJobDef?.defName})");
            comp.ScanNow();
            Kit.Check(husky.CurJobDef != Stare, "a husky held still by a colonist (ForceWait) is not interrupted");
            husky.jobs.EndCurrentJob(JobCondition.InterruptForced);

            IntVec3 fogged = IntVec3.Invalid;
            foreach (IntVec3 x in map.AllCells)
                if (x.Fogged(map) && x.Standable(map) && x.GetFirstPawn(map) == null) { fogged = x; break; }
            if (fogged.IsValid)
            {
                Move(entity, fogged);
                Kit.Check(TDKUtility.HasActiveInvisibility(entity) && !TDKUtility.IsUnseen(entity),
                    "a hidden raider on a fogged cell is ignored (fog is information)");
            }
            else Log.Message("[Test] note: no fogged standable cell on this map, fog check skipped");
            Kill(entity);
            comp.ClearCooldowns();
            return 1;
        }

        int AnimalsAndCat()
        {
            entity = HiddenRaider(lane[8]);
            cat = Animal("Cat", Faction.OfPlayer, lane[4] + IntVec3.North);
            muffalo = Animal("Muffalo", Faction.OfPlayer, lane[5] + IntVec3.North);
            wild = Animal("Husky", null, lane[6] + IntVec3.South);
            sleeper = Animal("Husky", Faction.OfPlayer, lane[7] + IntVec3.South);
            sleeper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, sleeper.Position), JobCondition.InterruptForced);
            if (sleeper.jobs.curDriver != null) sleeper.jobs.curDriver.asleep = true;
            Kit.Check(!sleeper.Awake(), "precondition: the second husky is asleep");
            Move(husky, lane[0]);
            Job w = JobMaker.MakeJob(JobDefOf.Wait, 2000);
            w.playerForced = true;
            husky.jobs.StartJob(w, JobCondition.InterruptForced);
            Kit.Check(husky.CurJob == w, "precondition: the first husky is on a player-forced job");

            comp.ScanNow();
            Kit.Check(cat.CurJobDef == Stare, $"a cat 4 cells from a hidden raider stares (job {cat.CurJobDef?.defName})");
            Kit.Check(muffalo.CurJobDef != Stare, "an unlisted animal (muffalo) ignores it");
            Kit.Check(wild.CurJobDef != Stare, "a wild husky ignores it");
            Kit.Check(sleeper.CurJobDef != Stare, "a sleeping husky does not react");
            Kit.Check(husky.CurJobDef != Stare, "a husky on a player-forced job is not interrupted");
            Kit.Check(entity.IsHiddenFromPlayer(), "the hidden raider is still hidden");
            return 1;
        }

        int SightstealerSetup()
        {
            Kill(entity); Kill(cat); Kill(muffalo); Kill(wild); Kill(sleeper);
            comp.ClearCooldowns();
            Move(husky, lane[0]);
            husky.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait), JobCondition.InterruptForced); // idle wait: in place, casual, no expiry
            entity = SpawnAt(PawnGenerator.GeneratePawn(PawnKindDefOf.Sightstealer, Faction.OfEntities), lane[7]);
            Freeze(entity);
            passiveStart = Find.TickManager.TicksGame;
            return 2;
        }

        int PollPassive()
        {
            int waited = Find.TickManager.TicksGame - passiveStart;
            if (husky.CurJobDef == Stare)
            {
                Kit.Check(true, $"sightstealer: the husky starts staring by itself from the map tick ({waited} ticks)");
                Kit.Check(entity.IsHiddenFromPlayer(), "the sightstealer is still hidden");
                return 1;
            }
            if (waited > MapComponent_TheDogKnows.ScanInterval + 10)
            {
                Kit.Check(false, $"sightstealer: no stare within {waited} ticks (unseen {TDKUtility.IsUnseen(entity)}, " +
                    $"canReact {TDKUtility.CanReact(husky)}, job {husky.CurJobDef?.defName})");
                return 1;
            }
            step--; // poll again next tick
            return 0;
        }

        void Finish()
        {
            done = true;
            foreach (Thing t in extras) Kill(t);
            Kit.Summary("TheDogKnows");
        }
    }
}
