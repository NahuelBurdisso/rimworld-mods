using System.Collections.Generic;
using System.IO;
using System.Linq;
using OldScores;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace OldScoresTest
{
    /// <summary>
    /// Feature 5 — The Beast Remembers: real hits on a colony husky, the map component's 60-tick growl pass,
    /// a raider downed by the husky and kept prisoner, the daily fear, forgetting and a Scribe roundtrip.
    /// Starts once the Echoes harness has finished (it runs real raids on the same map).
    /// </summary>
    public class BeastMemoryHarness : GameComponent
    {
        const int Wait = 130;

        bool active, done, cleared;
        int ticks, phase, since;
        Map map;
        Faction enemy;
        IntVec3 origin;
        Pawn husky, colonist, r1, r7;
        Thing wall;
        int growlTick;
        float resistanceBefore;
        readonly List<Pawn> spawned = new List<Pawn>();

        public BeastMemoryHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (!cleared)
            {
                if (!TestUtil.IsFinished("Echoes") && ticks < TestUtil.BeastMemoryLatestStart) return;
                cleared = true;
                TestUtil.ClearHostiles(Find.CurrentMap, "beast memory");
                Next(0);
                return;
            }
            if (ticks - since < TestUtil.ClearTicks && phase == 0) return;
            try { Step(); }
            catch (System.Exception ex) { Kit.Check(false, "beast memory harness threw: " + ex); Finish(); }
        }

        OldScoresTracker T => OldScoresTracker.Instance;
        void Next(int p) { phase = p; since = ticks; }
        int Waited => ticks - since;

        void Step()
        {
            switch (phase)
            {
                case 0: Setup(); break;
                case 1: GrowlWait(); break;
                case 2: if (Waited >= Wait) Cooldown(); break;
                case 3: if (Waited >= Wait) FarAway(); break;
                case 4: if (Waited >= Wait) BehindWall(); break;
                case 5: if (Waited >= Wait) { NoGrowlBehindWall(); FearSetup(); } break;
                case 6: FearWait(); break;
                case 7: if (Waited >= Wait) SameDay(); break;
                case 8: if (Waited >= Wait) NextDay(); break;
                case 9: if (Waited >= Wait) { Roundtrip(); Death(); Finish(); } break;
            }
        }

        // --- (a) who is remembered ----------------------------------------------------------------

        void Setup()
        {
            map = Find.CurrentMap;
            T.ClearBeastMemory();
            enemy = TestUtil.HostileFaction();
            Kit.Check(enemy != null, $"setup: a hostile faction ({enemy?.Name ?? "none"})");
            if (enemy == null) { Finish(); return; }
            origin = TestUtil.PrepareStrip(map, map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center, 4, 16, 3);
            Kit.Check(origin.IsValid, $"setup: a clear strip at {origin}");
            if (!origin.IsValid) { Finish(); return; }
            colonist = TestUtil.NewColonist(map, origin + new IntVec3(0, 0, 6));
            husky = SpawnAnimal("Husky", Faction.OfPlayer, origin);
            Kit.Check(husky.Faction == Faction.OfPlayer && husky.RaceProps.Animal, "setup: a colony husky");

            r1 = Raider(origin + new IntVec3(8, 0, 0));
            Hit(husky, r1, 4f);
            GrudgeRecord g = T.GetGrudge(husky.thingIDNumber, r1.thingIDNumber);
            Kit.Check(g != null && g.humanName == r1.LabelShort, $"a raider's blow on a colony husky is remembered ({g?.humanName ?? "nothing"})");

            Hit(husky, colonist, 2f);
            Kit.Check(T.GetGrudge(husky.thingIDNumber, colonist.thingIDNumber) == null, "a colonist's blow (friendly fire) is not");

            if (ModsConfig.IdeologyActive)
            {
                Pawn slave = TestUtil.NewColonist(map, origin + new IntVec3(0, 0, 5));
                spawned.Add(slave);
                slave.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);
                Hit(husky, slave, 2f);
                Kit.Check(slave.IsSlaveOfColony && T.GetGrudge(husky.thingIDNumber, slave.thingIDNumber) == null,
                    $"a colony slave's blow is not (slave {slave.IsSlaveOfColony})");
                Remove(slave);
            }

            Pawn wolf = SpawnAnimal("Wolf_Timber", null, origin + new IntVec3(10, 0, 2));
            Hit(husky, wolf, 3f);
            Kit.Check(!T.Grudges.Any(x => x.humanId == wolf.thingIDNumber), "an animal's bite is not (only people are remembered)");

            Pawn boar = SpawnAnimal("WildBoar", null, origin + new IntVec3(10, 0, -2));
            Hit(boar, r1, 3f);
            Kit.Check(!T.Grudges.Any(x => x.animalId == boar.thingIDNumber), "a wild animal hurt by a raider remembers nothing (colony animals only)");

            // cap: a second husky hurt by four raiders keeps the last three
            Pawn husky2 = SpawnAnimal("Husky", Faction.OfPlayer, origin + new IntVec3(2, 0, -3));
            var hitters = new List<Pawn>();
            for (int i = 0; i < 4; i++)
            {
                Pawn r = Raider(origin + new IntVec3(12, 0, i - 2));
                hitters.Add(r);
                Hit(husky2, r, 2f);
            }
            List<GrudgeRecord> h2 = T.Grudges.Where(x => x.animalId == husky2.thingIDNumber).ToList();
            Kit.Check(h2.Count == OldScoresTuning.GrudgesPerAnimal && !h2.Any(x => x.humanId == hitters[0].thingIDNumber),
                $"an animal keeps at most {OldScoresTuning.GrudgesPerAnimal} grudges, the oldest goes ({h2.Count}, first hitter kept {h2.Any(x => x.humanId == hitters[0].thingIDNumber)})");

            int deadId = hitters[3].thingIDNumber;
            hitters[3].Kill(null);
            Kit.Check(!T.Grudges.Any(x => x.humanId == deadId), "when a remembered man dies, the grudges against him go");

            OldScoresMod.Settings.beastMemory = false;
            Pawn r6 = Raider(origin + new IntVec3(12, 0, 3));
            Hit(husky, r6, 2f);
            OldScoresMod.Settings.beastMemory = true;
            Kit.Check(T.GetGrudge(husky.thingIDNumber, r6.thingIDNumber) == null, "with the feature off nothing is remembered");

            string inspect = husky.GetInspectString() ?? "";
            Kit.Check(inspect.Contains("Remembers who hurt it") && inspect.Contains(r1.LabelShort), $"the husky's inspect pane says whom it remembers ({inspect.Replace("\n", " | ")})");
            OldScoresMod.Settings.beastMemory = false;
            string inspectOff = husky.GetInspectString() ?? "";
            OldScoresMod.Settings.beastMemory = true;
            Kit.Check(!inspectOff.Contains("Remembers who hurt it"), "...and says nothing with the feature off");

            // clean up the extras (their deaths also clear their records)
            foreach (Pawn p in new[] { wolf, boar, husky2, r6 }.Concat(hitters)) Remove(p);

            // (a) the growl: the raider, now a prisoner, 4 cells from the husky in the open
            r1.equipment?.DestroyAllEquipment();
            r1.guest.CapturedBy(Faction.OfPlayer, colonist);
            Kit.Check(r1.IsPrisonerOfColony, "setup: the raider is now our prisoner");
            Place(husky, origin);
            Place(r1, origin + new IntVec3(4, 0, 0));
            OldScoresTracker.LastGrowlBubble = null;
            Next(1);
        }

        void GrowlWait()
        {
            GrudgeRecord g = T.GetGrudge(husky.thingIDNumber, r1.thingIDNumber);
            MoteBubble b = OldScoresTracker.LastGrowlBubble;
            if (g != null && g.lastGrowlTick >= 0)
            {
                growlTick = g.lastGrowlTick;
                Kit.Check(true, $"the husky growls at him within {Waited} ticks of him being 4 cells away (map component pass)");
                Color c = b?.iconMatPropertyBlock?.GetColor(ShaderPropertyIDs.Color) ?? Color.clear;
                Kit.Check(b != null && !b.Destroyed && b.arrowTarget == r1 && b.link1.Target.Thing == husky && c.r > 0.8f && c.g < 0.4f,
                    $"...with a red bubble on the husky pointing at him (target {b?.arrowTarget?.LabelShort}, on {b?.link1.Target.Thing?.LabelShort}, colour {c})");
                Next(2);
            }
            else if (Waited > Wait)
            {
                Kit.Check(false, $"the husky growls at him when he is 4 cells away (dist {husky.Position.DistanceTo(r1.Position):0.0}, awake {husky.Awake()}, near {OldScoresTracker.Near(husky, r1)})");
                Next(2);
            }
        }

        void Cooldown()
        {
            GrudgeRecord g = T.GetGrudge(husky.thingIDNumber, r1.thingIDNumber);
            Kit.Check(g != null && g.lastGrowlTick == growlTick, $"no second growl at the same man within {OldScoresTuning.GrowlCooldown} ticks");
            if (g != null) g.lastGrowlTick = -1;   // cooldown over
            Place(husky, origin);
            Place(r1, origin + new IntVec3(10, 0, 0));
            Next(3);
        }

        void FarAway()
        {
            GrudgeRecord g = T.GetGrudge(husky.thingIDNumber, r1.thingIDNumber);
            Kit.Check(g != null && g.lastGrowlTick < 0, $"10 cells away: no growl ({husky.Position.DistanceTo(r1.Position):0.0} cells)");
            wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog), origin + new IntVec3(2, 0, 0), map);
            Place(husky, origin);
            Place(r1, origin + new IntVec3(4, 0, 0));
            Next(4);
        }

        void BehindWall()
        {
            Kit.Check(!GenSight.LineOfSight(husky.Position, r1.Position, map), $"setup: the wall blocks the husky's sight ({husky.Position} -> {r1.Position})");
            Next(5);
        }

        void NoGrowlBehindWall()
        {
            GrudgeRecord g = T.GetGrudge(husky.thingIDNumber, r1.thingIDNumber);
            Kit.Check(g != null && g.lastGrowlTick < 0, "behind a wall outdoors (no sight): no growl");
            if (wall != null && !wall.Destroyed) wall.Destroy();
        }

        // --- (b) fear -----------------------------------------------------------------------------

        void FearSetup()
        {
            r7 = Raider(origin + new IntVec3(6, 0, 2));
            DownBy(r7, husky);
            FearRecord f = T.GetFear(r7.thingIDNumber);
            Kit.Check(r7.Downed && !r7.Dead && f != null && f.animalId == husky.thingIDNumber,
                $"a raider brought down by the husky's bites fears it (downed {r7.Downed}, dead {r7.Dead}, record {(f != null)})");
            Pawn r8 = Raider(origin + new IntVec3(10, 0, -2));
            DownBy(r8, colonist);
            Kit.Check(r8.Downed && T.GetFear(r8.thingIDNumber) == null, "one brought down by a colonist does not");
            Pawn wolf = SpawnAnimal("Wolf_Timber", null, origin + new IntVec3(12, 0, 2));
            Pawn r9 = Raider(origin + new IntVec3(12, 0, -2));
            DownBy(r9, wolf);
            Kit.Check(r9.Downed && T.GetFear(r9.thingIDNumber) == null, "one brought down by a wild animal does not");
            foreach (Pawn p in new[] { r8, r9, wolf }) Remove(p);
            if (f == null || !r7.Downed) { Finish(); return; }

            // still a downed raider, not our prisoner: the husky next to him changes nothing
            Place(husky, r7.Position + new IntVec3(-3, 0, 0));
            T.BeastMemoryCheck(map);
            Kit.Check(FearMemory() == null && T.GetFear(r7.thingIDNumber).lastFearDay < 0, "no fear while he is not yet our prisoner");

            r7.guest.CapturedBy(Faction.OfPlayer, colonist);
            r7.guest.resistance = 10f;
            resistanceBefore = r7.guest.resistance;
            Remove(r1);   // only one man near the husky now
            Place(husky, r7.Position + new IntVec3(-3, 0, 0));
            Next(6);
        }

        Thought_Memory FearMemory() =>
            r7.needs?.mood?.thoughts.memories.Memories.FirstOrDefault(m => m.def == OldScoresDefOf.OldScores_FearOfBeast);

        void FearWait()
        {
            Thought_Memory m = FearMemory();
            if (m != null)
            {
                Kit.Check(m.otherPawn == husky && m.LabelCap.ToString().Contains(husky.LabelShort), $"the prisoner sees the husky 3 cells away and is afraid: \"{m.LabelCap}\" ({Waited} ticks)");
                Kit.Check(Mathf.Abs(r7.guest.resistance - (resistanceBefore - OldScoresTuning.FearResistanceLoss)) < 0.01f,
                    $"...and his resistance drops by {OldScoresTuning.FearResistanceLoss} ({resistanceBefore} -> {r7.guest.resistance})");
                Next(7);
            }
            else if (Waited > Wait)
            {
                Kit.Check(false, $"the prisoner fears the husky 3 cells away (prisoner {r7.IsPrisonerOfColony}, dist {husky.Position.DistanceTo(r7.Position):0.0}, near {OldScoresTracker.Near(husky, r7)})");
                Next(9);
            }
        }

        void SameDay()
        {
            Kit.Check(Mathf.Abs(r7.guest.resistance - (resistanceBefore - OldScoresTuning.FearResistanceLoss)) < 0.01f,
                $"the same day it does not happen again ({r7.guest.resistance})");
            FearRecord f = T.GetFear(r7.thingIDNumber);
            if (f != null) f.lastFearDay--;   // as if it had been yesterday
            Next(8);
        }

        void NextDay()
        {
            Kit.Check(Mathf.Abs(r7.guest.resistance - (resistanceBefore - 2 * OldScoresTuning.FearResistanceLoss)) < 0.01f,
                $"the next day it happens again ({r7.guest.resistance})");
            Next(9);
        }

        // --- save, forgetting ---------------------------------------------------------------------

        void Roundtrip()
        {
            // a fresh grudge so both lists have something
            Pawn r10 = Raider(origin + new IntVec3(12, 0, 0));
            Hit(husky, r10, 2f);
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "OldScoresBeastMemoryRoundtrip.xml");
            List<GrudgeRecord> g = T.Grudges.ToList(), g2 = null;
            List<FearRecord> f = T.Fears.ToList(), f2 = null;
            try
            {
                Scribe.saver.InitSaving(path, "beastMemoryTest");
                Scribe_Collections.Look(ref g, "grudges", LookMode.Deep);
                Scribe_Collections.Look(ref f, "fears", LookMode.Deep);
                Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref g2, "grudges", LookMode.Deep);
                Scribe_Collections.Look(ref f2, "fears", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                if (File.Exists(path)) File.Delete(path);
            }
            bool same = g.Count > 0 && f.Count > 0 && g2 != null && f2 != null && g2.Count == g.Count && f2.Count == f.Count
                        && g.Zip(g2, (a, b) => a.animalId == b.animalId && a.humanId == b.humanId && a.humanName == b.humanName && a.animalName == b.animalName
                                                && a.createdTick == b.createdTick && a.lastGrowlTick == b.lastGrowlTick && a.colonistSinceTick == b.colonistSinceTick).All(x => x)
                        && f.Zip(f2, (a, b) => a.prisonerId == b.prisonerId && a.animalId == b.animalId && a.prisonerName == b.prisonerName
                                                && a.animalName == b.animalName && a.lastFearDay == b.lastFearDay).All(x => x);
            Kit.Check(same, $"save/load: {g.Count} grudges and {f.Count} fear records read back identical");

            // forgiveness: the man joins the colony; after 30 days as one of us the husky lets it go
            r10.SetFaction(Faction.OfPlayer);
            T.CleanupBeastMemoryNow();
            GrudgeRecord gr = T.GetGrudge(husky.thingIDNumber, r10.thingIDNumber);
            Kit.Check(r10.IsColonist && gr != null && gr.colonistSinceTick == Find.TickManager.TicksGame, $"a remembered man who joins the colony is not forgiven at once (colonist since {gr?.colonistSinceTick})");
            if (gr != null) gr.colonistSinceTick -= OldScoresTuning.GrudgeForgiveTicks;
            T.CleanupBeastMemoryNow();
            Kit.Check(T.GetGrudge(husky.thingIDNumber, r10.thingIDNumber) == null, "...after 30 days as a colonist he is");

            // an animal that leaves the colony (sold, released) forgets
            Pawn husky3 = SpawnAnimal("Husky", Faction.OfPlayer, origin + new IntVec3(0, 0, -2));
            Pawn r11 = Raider(origin + new IntVec3(12, 0, 1));
            Hit(husky3, r11, 2f);
            bool had = T.GetGrudge(husky3.thingIDNumber, r11.thingIDNumber) != null;
            husky3.SetFaction(null);
            T.CleanupBeastMemoryNow();
            Kit.Check(had && T.GetGrudge(husky3.thingIDNumber, r11.thingIDNumber) == null, "an animal released from the colony forgets");
            Remove(r10);
            Remove(r11);
            Remove(husky3);

            Pawn husky4 = SpawnAnimal("Husky", Faction.OfPlayer, origin + new IntVec3(0, 0, -1));
            Pawn a = Raider(origin + new IntVec3(12, 0, -1)), b = Raider(origin + new IntVec3(12, 0, 1));
            Hit(husky4, a, 2f);
            Hit(husky4, b, 2f);
            Place(husky4, origin);
            Place(a, origin + new IntVec3(3, 0, 0));
            Place(b, origin + new IntVec3(0, 0, 3));
            T.BeastMemoryCheck(map);
            int growled = T.Grudges.Count(x => x.animalId == husky4.thingIDNumber && x.lastGrowlTick >= 0);
            Kit.Check(growled == 1, $"two remembered men next to one animal: one growl per pass ({growled})");
            Remove(a);
            Remove(b);
            Remove(husky4);
        }

        void Death()
        {
            int id = husky.thingIDNumber;
            Kit.Check(T.Grudges.Any(x => x.animalId == id) || T.Fears.Any(x => x.animalId == id), "setup: the husky has records before it dies");
            husky.Kill(null);
            Kit.Check(!T.Grudges.Any(x => x.animalId == id) && !T.Fears.Any(x => x.animalId == id), "when the husky dies its grudges and the fear of it go too");
        }

        // --- helpers ------------------------------------------------------------------------------

        Pawn SpawnAnimal(string kind, Faction f, IntVec3 at)
        {
            Pawn p = PawnGenerator.GeneratePawn(PawnKindDef.Named(kind), f);
            GenSpawn.Spawn(p, CellFinder.StandableCellNear(at, map, 3f), map);
            spawned.Add(p);
            return p;
        }

        Pawn Raider(IntVec3 at)
        {
            Pawn p = Kit.SpawnHostile(map, at, null, enemy);
            p.equipment?.DestroyAllEquipment();
            spawned.Add(p);
            return p;
        }

        static void Hit(Pawn victim, Pawn by, float amount)
        {
            victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, amount, 0f, -1f, by));
        }

        /// <summary>
        /// Real bites on the legs from the attacker until the victim goes down (bounded). Legs only: no organ is
        /// ever hit, and a man who cannot walk is downed, so he goes down instead of dying.
        /// </summary>
        static void DownBy(Pawn victim, Pawn by)
        {
            // vanilla kills a share of downed raiders on the spot (death-on-downed chance): not in this test
            victim.health.overrideDeathOnDownedChance = 0f;
            for (int i = 0; i < 60 && !victim.Downed && !victim.Dead; i++)
            {
                BodyPartRecord leg = victim.health.hediffSet.GetNotMissingParts().Where(p => p.def == BodyPartDefOf.Leg).Skip(i % 2).FirstOrDefault()
                                     ?? victim.health.hediffSet.GetNotMissingParts().FirstOrDefault(p => p.def == BodyPartDefOf.Leg);
                if (leg == null) break;
                victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 5f, 0f, -1f, by, leg));
            }
        }

        void Place(Pawn p, IntVec3 c)
        {
            TestUtil.Teleport(p, c);
            if (!p.Downed && p.jobs != null)
            {
                Job j = JobMaker.MakeJob(JobDefOf.Wait);
                j.expiryInterval = 3000;
                p.jobs.StartJob(j, JobCondition.InterruptForced);
            }
        }

        static void Remove(Pawn p)
        {
            if (p != null && !p.Dead && !p.Destroyed) { p.Kill(null); p.Corpse?.Destroy(); }
        }

        void Finish()
        {
            OldScoresMod.Settings.beastMemory = true;
            if (wall != null && !wall.Destroyed) wall.Destroy();
            foreach (Pawn p in spawned) Remove(p);
            Kit.Summary("OldScores/BeastMemory");
            TestUtil.MarkFinished("BeastMemory");
            done = true;
        }
    }
}
