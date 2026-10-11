using System.Collections.Generic;
using System.Linq;
using OldScores;
using RimWorld;
using Verse;

namespace OldScoresTest
{
    /// <summary>Feature 3 — The Beast That Came Back: real damage, ExitMap, the queued incident, the kill.</summary>
    public class BeastHarness : GameComponent
    {
        bool active, done;
        int ticks, phase;
        Map map;
        IntVec3 origin;
        Pawn victim, wolf;
        IncidentParms parms;
        readonly List<Pawn> spawned = new List<Pawn>();

        public BeastHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (ticks < TestUtil.BeastsStart) return;
            try { Step(); }
            catch (System.Exception ex) { Kit.Check(false, "beast harness threw: " + ex); Finish(); }
        }

        OldScoresTracker T => OldScoresTracker.Instance;
        static PawnKindDef Wolf => PawnKindDef.Named("Wolf_Timber");

        void Step()
        {
            if (phase == 0)
            {
                phase = 1;
                MarkAndEscape();
            }
            else if (phase == 1)
            {
                // a later tick: IncidentWorker.CanFireNow caches its answer per tick
                phase = 2;
                ReturnAndKill();
                Negatives();
                Finish();
            }
        }

        void MarkAndEscape()
        {
            map = Find.CurrentMap;
            origin = TestUtil.PrepareStrip(map, map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center, 3, 12, 3);
            Kit.Check(origin.IsValid, $"setup: prepared a clear test strip at {origin}");
            if (!origin.IsValid) origin = map.Center;
            victim = TestUtil.NewColonist(map, origin);

            wolf = SpawnWolf(null);
            Kit.Check(wolf.Faction == null && wolf.RaceProps.Animal, "setup: a wild timber wolf");
            float dealt = Bite(victim, wolf, 15f);
            BeastRecord rec = T.GetBeast(wolf);
            Kit.Check(rec != null && rec.stage == BeastStage.Marked && rec.victimNames.Contains(victim.LabelShort),
                $"a wild wolf that bites a colonist for {dealt:0.#} is marked with her name");

            Pawn nipper = SpawnWolf(null);
            Pawn victim2 = TestUtil.NewColonist(map, origin);
            float small = Bite(victim2, nipper, 3f);
            Kit.Check(T.GetBeast(nipper) == null, $"a wolf that only nips ({small:0.#} damage, not downed) is not");

            Faction enemy = TestUtil.HostileFaction();
            Pawn pirateDog = SpawnWolf(enemy);
            Kit.Check(pirateDog.Faction != null, $"setup: a wolf of a faction ({pirateDog.Faction?.Name ?? "none"})");
            Pawn victim3 = TestUtil.NewColonist(map, origin);
            Bite(victim3, pirateDog, 15f);
            Kit.Check(T.GetBeast(pirateDog) == null, "a wolf belonging to a faction is not");

            // wound the wolf so there is something to scar
            wolf.TakeDamage(new DamageInfo(DamageDefOf.Cut, 4f, 2f, -1f, victim));
            Kit.Check(Injuries(wolf).Any(), "setup: the wolf is wounded");
            wolf.ExitMap(false, Rot4.North);
            Kit.Check(wolf.Name != null && !wolf.Name.ToStringShort.NullOrEmpty() && rec.nickname == wolf.Name.ToStringShort,
                $"when it escapes it gets a name: {wolf.Name?.ToStringShort}");
            Kit.Check(Injuries(wolf).Any() && Injuries(wolf).All(h => h.TryGetComp<HediffComp_GetsPermanent>()?.IsPermanent ?? true),
                "its wounds become permanent scars");
            Kit.Check(rec.stage == BeastStage.Away && wolf.IsWorldPawnSafe() && TestUtil.IsPinned(wolf), "it is kept alive and pinned as a world pawn");
            int now = Find.TickManager.TicksGame;
            bool queued = false;
            foreach (object o in Find.Storyteller.incidentQueue)
            {
                if (o is QueuedIncident qi && qi.FiringIncident.def == OldScoresDefOf.OldScores_BeastReturns
                    && qi.FireTick >= now + GenDate.TicksPerQuadrum && qi.FireTick <= now + 2 * GenDate.TicksPerQuadrum)
                    queued = true;
            }
            Kit.Check(queued, $"its return is queued 1-2 quadrums out (in {(rec.fireTick - now) / (float)GenDate.TicksPerDay:0.#} days)");

            nipper.ExitMap(false, Rot4.North);
            Kit.Check(nipper.Name == null && !TestUtil.IsPinned(nipper), "the unmarked wolf leaves unnamed and is not kept");

            parms = new IncidentParms { target = map, forced = true };
            Kit.Check(!OldScoresDefOf.OldScores_BeastReturns.Worker.CanFireNow(parms), "the return incident cannot fire before it is due");
        }

        void ReturnAndKill()
        {
            T.ForceBeastDue(wolf);
            IncidentWorker worker = OldScoresDefOf.OldScores_BeastReturns.Worker;
            Kit.Check(worker.CanFireNow(parms), "once due, the return incident can fire");
            bool fired = worker.TryExecute(parms);
            BeastRecord rec = T.GetBeast(wolf);
            Kit.Check(fired && wolf.Spawned && wolf.Map == map, "the incident brings the same wolf back to the map");
            Kit.Check(wolf.health.hediffSet.HasHediff(OldScoresDefOf.OldScores_Veteran), "...as a veteran (hediff)");
            Kit.Check(wolf.MentalStateDef == MentalStateDefOf.ManhunterPermanent, "...hunting the colony (ManhunterPermanent)");
            Kit.Check(wolf.mindState.exitMapAfterTick > Find.TickManager.TicksGame, "...until it gives up and leaves (exitMapAfterTick)");
            Kit.Check(rec != null && rec.stage == BeastStage.Returned, "record is now Returned");
            Kit.Check(TestUtil.LetterAbout(wolf.LabelShort), "a letter announces it by name");
            Kit.Check(rec != null && T.BeastLabel(rec).Contains(victim.LabelShort) && wolf.GetInspectString().Contains(victim.LabelShort),
                $"its red label / inspect line lists whom it hurt ({(rec != null ? T.BeastLabel(rec) : "")})");

            int trophiesBefore = map.listerThings.ThingsOfDef(OldScoresDefOf.OldScores_BeastTrophy).Count;
            string name = wolf.Name.ToStringShort;
            Kit.KillBy(wolf, victim);
            List<Thing> trophies = map.listerThings.ThingsOfDef(OldScoresDefOf.OldScores_BeastTrophy);
            Kit.Check(trophies.Count == trophiesBefore + 1, "killing it leaves a trophy");
            string history = trophies.Count > 0 ? trophies.Last().TryGetComp<CompBeastTrophy>()?.History() : null;
            Kit.Check(history != null && history.Contains(name) && history.Contains(victim.LabelShort) && trophies.Last().LabelNoCount.Contains(name),
                $"the trophy tells its story: {history}");
            Kit.Check(victim.needs.mood.thoughts.memories.GetFirstMemoryOfDef(OldScoresDefOf.OldScores_SettledOldScore) != null,
                "the killer settled an old score (memory)");
            Kit.Check(T.GetBeast(wolf) == null && !TestUtil.IsPinned(wolf), "record cleared and unpinned");
        }

        void Negatives()
        {
            OldScoresMod.Settings.beasts = false;
            Pawn w = SpawnWolf(null);
            Pawn v = TestUtil.NewColonist(map, origin);
            Bite(v, w, 15f);
            OldScoresMod.Settings.beasts = true;
            Kit.Check(T.GetBeast(w) == null, "with the feature off, a mauling wolf is not marked");
        }

        Pawn SpawnWolf(Faction f)
        {
            Pawn p = PawnGenerator.GeneratePawn(Wolf, f);
            GenSpawn.Spawn(p, CellFinder.StandableCellNear(origin + new IntVec3(8, 0, 0), map, 5f), map);
            spawned.Add(p);
            return p;
        }

        /// <summary>A real bite on a leg (armour ignored) from the animal; returns the damage dealt.</summary>
        static float Bite(Pawn target, Pawn animal, float amount)
        {
            BodyPartRecord leg = target.health.hediffSet.GetNotMissingParts().FirstOrDefault(p => p.def == BodyPartDefOf.Leg);
            var dinfo = new DamageInfo(DamageDefOf.Bite, amount, 2f, -1f, animal, leg);
            return target.TakeDamage(dinfo).totalDamageDealt;
        }

        static IEnumerable<Hediff_Injury> Injuries(Pawn p) => p.health.hediffSet.hediffs.OfType<Hediff_Injury>();

        void Finish()
        {
            OldScoresMod.Settings.beasts = true;
            TestUtil.KillAll(spawned);
            Kit.Summary("OldScores/Beasts");
            TestUtil.MarkFinished("Beasts");
            done = true;
        }
    }

    static class PawnTestExt
    {
        public static bool IsWorldPawnSafe(this Pawn p) => !p.Spawned && RimWorld.Planet.WorldPawnsUtility.IsWorldPawn(p);
    }
}
