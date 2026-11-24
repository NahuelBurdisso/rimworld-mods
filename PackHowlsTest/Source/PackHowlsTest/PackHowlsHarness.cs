using System.Collections.Generic;
using System.Linq;
using PackHowls;
using RimWorld;
using Verse;
using Verse.AI;

namespace PackHowlsTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N`. Drives the real path: a raider kills a colonist and her bonded dog,
    /// the colony's huskies (one asleep) howl at night, a wild wolf answers, colonists get the right memories.
    /// Negative cases: unbonded animal, raider death, a peaceful colonist death, daytime, no repeat.
    /// </summary>
    public class PackHowlsHarness : GameComponent
    {
        bool active, done;
        int ticks, phase, phaseTick, firedAt = -1;
        Map map;
        Pawn victim, spouse, other, dogVictim, husky1, husky2, husky3, cat, wolf, child, grandchild, far;
        IntVec3 deathCell;
        int howlsFiredBefore;

        public PackHowlsHarness(Game game) { }

        public override void FinalizeInit()
        {
            active = Kit.HarnessArmed;
        }

        int resumes;

        /// <summary>The manhunter husky trips the "major threat" auto-pause; keep the test running.</summary>
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
            try
            {
                if (ticks == 60) Setup();
                else if (ticks > 60) Step();
            }
            catch (System.Exception e)
            {
                Kit.Check(false, "harness threw: " + e);
                Finish();
            }
        }

        static Pawn MakeColonist(Map map, IntVec3 near)
        {
            var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, canGeneratePawnRelations: false,
                mustBeCapableOfViolence: false, prohibitedTraits: new[] { TraitDefOf.Psychopath }, fixedBiologicalAge: 30f);
            Pawn p = PawnGenerator.GeneratePawn(req);
            GenSpawn.Spawn(p, CellFinder.StandableCellNear(near, map, 5f), map);
            return p;
        }

        static Pawn MakeAnimal(Map map, string kind, Faction faction, IntVec3 near, float radius = 5f)
        {
            var req = new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed(kind), faction, fixedBiologicalAge: 3f);
            Pawn p = PawnGenerator.GeneratePawn(req);
            GenSpawn.Spawn(p, CellFinder.StandableCellNear(near, map, radius), map);
            if (p.needs?.food != null) p.needs.food.CurLevel = p.needs.food.MaxLevel;
            return p;
        }

        MapComponent_PackHowls Comp => MapComponent_PackHowls.For(map);
        int PendingCount => Comp?.pending?.victims.Count ?? 0;

        void Setup()
        {
            map = Find.CurrentMap;
            IntVec3 center = Kit.Colonists(map).FirstOrDefault()?.Position ?? map.Center;
            Kit.Check(Comp != null, "MapComponent_PackHowls exists on the map");
            Kit.Check(IsHowler(("Husky")) && !IsHowler(("Cat")),
                "howler list: Husky howls, Cat does not");
            Kit.Check(IsHowler(("Wolf_Arctic")) && IsHowler(("YorkshireTerrier")),
                "howler list: Wolf_Arctic and YorkshireTerrier howl");

            Log.Message("[Test] info howler races: " + string.Join(", ", DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.race != null && PackHowlsDefOf.PackHowls_Howlers.IsHowler(d)).Select(d => d.defName)));
            victim = MakeColonist(map, center);
            spouse = MakeColonist(map, center);
            other = MakeColonist(map, center);
            victim.relations.AddDirectRelation(PawnRelationDefOf.Spouse, spouse);
            child = MakeColonist(map, center);
            child.relations.AddDirectRelation(PawnRelationDefOf.Parent, victim);
            grandchild = MakeColonist(map, center);
            grandchild.relations.AddDirectRelation(PawnRelationDefOf.Parent, child);
            Pawn ffVictim = MakeColonist(map, center);
            Pawn bleeder = MakeColonist(map, center);
            Pawn sick = MakeColonist(map, center);
            Pawn ff2 = MakeColonist(map, center);
            if (CellFinder.TryFindRandomCellNear(center, map, 90, c => c.Standable(map) && c.DistanceTo(center) > 75f, out IntVec3 farCell))
                far = MakeColonist(map, farCell);
            else Log.Message("[Test] note: no cell > 75 from the colony, earshot check skipped");

            husky1 = MakeAnimal(map, "Husky", Faction.OfPlayer, center);
            husky2 = MakeAnimal(map, "Husky", Faction.OfPlayer, center);
            cat = MakeAnimal(map, "Cat", Faction.OfPlayer, center);
            dogVictim = MakeAnimal(map, "LabradorRetriever", Faction.OfPlayer, center);
            dogVictim.relations.AddDirectRelation(PawnRelationDefOf.Bond, victim);
            Pawn unbonded = MakeAnimal(map, "LabradorRetriever", Faction.OfPlayer, center);

            IntVec3 wolfCell;
            if (!CellFinder.TryFindRandomCellNear(center, map, 24, c => c.Standable(map) && !c.Fogged(map) && c.DistanceTo(center) > 14f, out wolfCell))
                wolfCell = CellFinder.StandableCellNear(center, map, 20f);
            wolf = MakeAnimal(map, "Wolf_Timber", null, wolfCell, 2f);
            IntVec3 h3Cell;
            if (!CellFinder.TryFindRandomCellNear(center, map, 22, c => c.Standable(map) && !c.Fogged(map) && c.DistanceTo(center) > 14f && c.DistanceTo(wolfCell) > 8f, out h3Cell))
                h3Cell = CellFinder.StandableCellNear(center, map, 18f);
            husky3 = MakeAnimal(map, "Husky", Faction.OfPlayer, h3Cell, 2f);

            // husky2 goes to sleep: the howl must wake it.
            husky2.needs.rest.CurLevel = 0.1f;
            husky2.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, husky2.Position), JobCondition.InterruptForced);

            Pawn raider = Kit.SpawnHostile(map, center + new IntVec3(6, 0, 0));

            Kit.KillBy(victim, raider);
            Kit.Check(victim.Dead && PendingCount == 1, $"colonist killed by a raider queues a howl (victims {PendingCount})");
            Kit.KillBy(dogVictim, raider);
            Kit.Check(dogVictim.Dead && PendingCount == 2, $"bonded dog killed by the raider joins the same pending howl (victims {PendingCount})");
            Kit.Check(Comp.pending != null && Comp.pending.names.Contains(victim.LabelShort) && Comp.pending.names.Contains(dogVictim.LabelShort),
                "pending keeps both names");
            Kit.KillBy(unbonded, raider);
            Kit.Check(unbonded.Dead && PendingCount == 2, "an unbonded colony animal does not count");
            Kit.KillBy(ffVictim, spouse);
            Kit.Check(ffVictim.Dead && PendingCount == 3, $"friendly fire during the fight counts (victims {PendingCount})");

            bleeder.TakeDamage(new DamageInfo(DamageDefOf.Cut, 2f, 0f, -1f, raider));
            sick.TakeDamage(new DamageInfo(DamageDefOf.Cut, 2f, 0f, -1f, raider));
            Kit.Check(!bleeder.Dead && bleeder.mindState.lastHarmTick == Find.TickManager.TicksGame, "the raider wounds the bleeder (lastHarmTick set)");

            raider.Kill(null);
            Kit.Check(raider.Dead && PendingCount == 3, "the raider's own death does not count");
            bool threat = GenHostility.AnyHostileActiveThreatToPlayer(map);
            Kit.Check(!threat, "no hostile threat left on the map");

            deathCell = bleeder.Position;
            bleeder.Kill(null, null);
            Kit.Check(bleeder.Dead && PendingCount == 4, $"bleeding out after recent wounds, fight over, counts (victims {PendingCount})");
            sick.Kill(null, HediffMaker.MakeHediff(HediffDefOf.Malnutrition, sick));
            Kit.Check(sick.Dead && PendingCount == 4, "a non-violent culprit (malnutrition) does not count, even after recent wounds");
            Kit.KillBy(ff2, spouse);
            Kit.Check(ff2.Dead && PendingCount == 4, "a colonist killed by a colonist with no fight on does not count");
            Pawn elder = MakeColonist(map, center);
            elder.Kill(null);
            Kit.Check(elder.Dead && PendingCount == 4, "a peaceful colonist death (no instigator, no recent harm, no threat) does not count");
            Kit.Check(Comp.pending.lastDeathCell == deathCell, $"pending remembers where the last one fell ({deathCell})");

            // Daytime, quiet long enough: must not fire.
            PackHowlsUtility.HourOverride = 12;
            Comp.pending.lastDeathTick -= MapComponent_PackHowls.MinQuietTicks + 500;
            howlsFiredBefore = PackHowlsUtility.HowlsFired;
            phase = 1; phaseTick = ticks;
        }

        static bool IsHowler(string race) => PackHowlsDefOf.PackHowls_Howlers.IsHowler(DefDatabase<ThingDef>.GetNamed(race));

        void Step()
        {
            int since = ticks - phaseTick;
            switch (phase)
            {
                case 1:   // noon
                    if (since < 300) return;
                    Kit.Check(Comp.pending != null && PackHowlsUtility.HowlsFired == howlsFiredBefore, "no howl at noon");
                    Kit.Check(!husky2.Awake(), "husky2 is asleep before the howl");
                    PackHowlsUtility.HourOverride = 23;
                    husky3.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter, forced: true);
                    Kit.Check(husky3.InMentalState && GenHostility.AnyHostileActiveThreatToPlayer(map) && !PackHowlsUtility.AnyRealThreat(map),
                        "a colony husky gone manhunter is a vanilla threat but not a real one for the howl");
                    phase = 2; phaseTick = ticks;
                    return;
                case 2:   // night: fires within one check interval
                    if (Comp.pending == null && PackHowlsUtility.HowlsFired == howlsFiredBefore + 1)
                    {
                        firedAt = ticks;
                        Kit.Check(true, $"howl fired at night after {since} ticks");
                        CheckHowlers();
                        Kit.Check(!Comp.lastHowlers.Contains(husky3) && husky3.CurJobDef != PackHowlsDefOf.PackHowls_Howl, "the manhunter husky does not howl");
                        husky3.MentalState?.RecoverFromState();
                        CheckMemories();
                        CheckMessage();
                        phase = 3; phaseTick = ticks;
                    }
                    else if (since > 300)
                    {
                        Kit.Check(false, "howl fired at night within 300 ticks");
                        Finish();
                    }
                    return;
                case 3:   // facing, once settled
                    if (since < 60) return;
                    foreach (Pawn h in new[] { husky1, husky2 })
                    {
                        Rot4 expected = h.Position == deathCell ? Rot4.North
                            : Pawn_RotationTracker.RotFromAngleBiased((deathCell - h.Position).ToVector3().AngleFlat());
                        Kit.Check(h.Rotation == expected, $"{h.LabelShort} faces where the last one fell ({expected}), got {h.Rotation}");
                    }
                    Kit.Check(husky1.pather == null || !husky1.pather.Moving, "husky1 stands still while howling");
                    phase = 4; phaseTick = ticks;
                    return;
                case 4:   // after the second howl
                    if (since < 370) return;
                    Kit.Check(PackHowlsUtility.HowlsPlayed >= 4, $"howl sounds played (>= 2 per husky): {PackHowlsUtility.HowlsPlayed}");
                    Kit.Check(PackHowlsUtility.RingsMade >= 16, $"rising rings made: {PackHowlsUtility.RingsMade}");
                    phase = 5; phaseTick = ticks;
                    return;
                case 5:   // wild answer at +600, job over at +420
                    if (ticks - firedAt < MapComponent_PackHowls.WildAnswerDelay + 30) return;
                    var wild = Comp.lastWildHowlers;
                    Kit.Check(wild.Contains(wolf) && wolf.CurJobDef == PackHowlsDefOf.PackHowls_Howl,
                        $"the wild wolf answers (wild howlers {wild.Count}, wolf job {wolf.CurJobDef?.defName}, fogged {wolf.Position.Fogged(map)}, can {PackHowlsUtility.CanHowlNow(wolf)})");
                    Kit.Check(husky1.CurJobDef != PackHowlsDefOf.PackHowls_Howl && husky2.CurJobDef != PackHowlsDefOf.PackHowls_Howl,
                        $"huskies are back to their own AI ({husky1.CurJobDef?.defName}, {husky2.CurJobDef?.defName})");
                    phase = 6; phaseTick = ticks;
                    return;
                case 6:   // no repeat
                    if (since < 500) return;
                    Kit.Check(PackHowlsUtility.HowlsFired == howlsFiredBefore + 1 && Comp.pending == null, "the howl does not repeat without new deaths");
                    Finish();
                    return;
            }
        }

        void CheckHowlers()
        {
            var howlers = Comp.lastHowlers;
            Kit.Check(howlers.Contains(husky1) && husky1.CurJobDef == PackHowlsDefOf.PackHowls_Howl, $"husky1 howls (job {husky1.CurJobDef?.defName})");
            Kit.Check(howlers.Contains(husky2) && husky2.CurJobDef == PackHowlsDefOf.PackHowls_Howl && husky2.Awake(),
                $"sleeping husky2 woke up and howls (job {husky2.CurJobDef?.defName}, awake {husky2.Awake()})");
            Kit.Check(!howlers.Contains(cat) && cat.CurJobDef != PackHowlsDefOf.PackHowls_Howl, "the cat does not howl");
            Kit.Check(!howlers.Contains(wolf), "the wild wolf is not part of the colony's howl");
        }

        void CheckMemories()
        {
            var sm = spouse.needs.mood.thoughts.memories;
            Thought_Memory strong = sm.GetFirstMemoryOfDef(PackHowlsDefOf.PackHowls_PackHowledForMine);
            Kit.Check(strong != null && strong.otherPawn == victim, $"spouse gets the strong memory about {victim.LabelShort} (got {strong?.otherPawn?.LabelShort ?? "none"})");
            Kit.Check(sm.GetFirstMemoryOfDef(PackHowlsDefOf.PackHowls_PackHowled) == null, "spouse does not also get the ordinary memory");
            var om = other.needs.mood.thoughts.memories;
            Thought_Memory normal = om.GetFirstMemoryOfDef(PackHowlsDefOf.PackHowls_PackHowled);
            Kit.Check(normal != null && normal.otherPawn == victim, $"an unrelated colonist in earshot gets the ordinary memory (got {normal?.otherPawn?.LabelShort ?? "none"})");
            Kit.Check(om.GetFirstMemoryOfDef(PackHowlsDefOf.PackHowls_PackHowledForMine) == null, "the unrelated colonist does not get the strong memory");
            if (normal != null) Kit.Check(normal.LabelCap.ToString().Contains(victim.LabelShort), $"memory label names the fallen: \"{normal.LabelCap}\"");
            if (strong != null)
            {
                string d = strong.Description;
                Kit.Check(d.Contains(victim.LabelShort) && !d.Contains(spouse.LabelShort), $"strong memory description names the fallen, not the reader: \"{d}\"");
            }
            foreach (Pawn kin in new[] { child, grandchild })
            {
                Thought_Memory m = kin.needs.mood.thoughts.memories.GetFirstMemoryOfDef(PackHowlsDefOf.PackHowls_PackHowledForMine);
                Kit.Check(m != null && m.otherPawn == victim, $"{(kin == child ? "child" : "grandchild (implied relation)")} gets the strong memory (got {m?.otherPawn?.LabelShort ?? "none"})");
            }
            if (far != null)
            {
                var fm = far.needs.mood.thoughts.memories;
                Kit.Check(fm.GetFirstMemoryOfDef(PackHowlsDefOf.PackHowls_PackHowled) == null && fm.GetFirstMemoryOfDef(PackHowlsDefOf.PackHowls_PackHowledForMine) == null,
                    $"an unrelated colonist out of earshot ({far.Position.DistanceTo(husky1.Position):F0} cells) gets nothing");
            }
        }

        void CheckMessage()
        {
            var msg = Find.Archive.ArchivablesListForReading.OfType<Message>().LastOrDefault(m => m.text != null && m.text.StartsWith("The pack howls for"));
            Kit.Check(msg != null && msg.text.Contains(victim.LabelShort) && msg.text.Contains(dogVictim.LabelShort),
                $"message names both fallen: \"{msg?.text}\"");
        }

        void Finish()
        {
            PackHowlsUtility.HourOverride = -1;
            done = true;
            Kit.Summary("PackHowls");
        }
    }
}
