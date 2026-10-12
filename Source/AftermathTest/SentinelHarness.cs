using System.Collections.Generic;
using System.Linq;
using Aftermath;
using RimWorld;
using Verse;

namespace AftermathTest
{
    /// <summary>
    /// Feature 6 — The Old Sentinel: real turret fire, credit rules (turret, manned mortar, negatives), battles and
    /// the naming threshold, old hands, minify/reinstall, save roundtrip, the fall (remains, mourning, letter).
    /// </summary>
    public class SentinelHarness : HarnessBase
    {
        public SentinelHarness(Game game) { }

        protected override int StartTick => TestUtil.SentinelStart;
        protected override string Name => "Sentinel";

        const int FireWait = 1200;

        IntVec3 root;
        Faction enemy;
        Building_TurretGun t0;
        Pawn rat;
        readonly List<Pawn> foes = new List<Pawn>();
        readonly List<Pawn> colonists = new List<Pawn>();

        static ThingDef MiniTurret => DefDatabase<ThingDef>.GetNamedSilentFail("Turret_MiniTurret");
        static ThingDef Mortar => DefDatabase<ThingDef>.GetNamedSilentFail("Turret_Mortar");
        static ThingDef MiniGun => DefDatabase<ThingDef>.GetNamedSilentFail("Gun_MiniTurret");

        protected override void Step()
        {
            switch (phase)
            {
                case 0:
                    Next();
                    Setup();
                    break;
                case 1:
                    {
                        // real fire: wait (bounded) for the switched-on turret to bring the manhunter down
                        if (!(rat.Dead || rat.Downed) && InPhase < FireWait) break;
                        CompOldSentinel c0 = t0.GetComp<CompOldSentinel>();
                        Kit.Check((rat.Dead || rat.Downed) && c0.rec != null && c0.rec.recentVictims.Contains(rat.thingIDNumber)
                            && c0.Takedowns >= 1 && c0.Battles == 1,
                            $"a switched-on mini-turret really shoots a manhunter down and is credited (rat dead {rat.Dead} downed {rat.Downed}, takedowns {c0.Takedowns}, battles {c0.Battles}, last shot tick {t0.LastAttackTargetTick}, active {t0.Active})");
                        CompPowerTrader power = t0.GetComp<CompPowerTrader>();
                        if (power != null) power.PowerOn = false;
                        if (rat.Corpse != null && rat.Corpse.Spawned) rat.Corpse.Destroy();
                        else if (!rat.Dead && rat.Spawned) rat.Destroy();
                        Next();
                        Rules();
                        Finish();
                        break;
                    }
            }
        }

        void Setup()
        {
            enemy = TestUtil.HostileFaction();
            root = TestUtil.PrepareStrip(map, TestUtil.Anchor(map, 0, -26), 17, 7, Name);
            Kit.Check(enemy != null && root.IsValid && MiniTurret != null && Mortar != null && MiniGun != null,
                $"setup: hostile faction ({enemy?.Name ?? "none"}), strip, mini-turret and mortar defs");
            if (enemy == null || !root.IsValid || MiniTurret == null || Mortar == null) { Finish(); return; }
            Kit.Check(MiniTurret.HasComp(typeof(CompOldSentinel)) && Mortar.HasComp(typeof(CompOldSentinel)) && MiniTurret.drawGUIOverlay,
                "CompOldSentinel was injected into the mini-turret and the mortar, with the map label overlay on");

            t0 = Turret(MiniTurret, root + new IntVec3(1, 0, 1));
            CompPowerTrader power = t0.GetComp<CompPowerTrader>();
            if (power != null) power.PowerOn = true;   // unconnected: nothing switches it off again
            PawnKindDef ratKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Rat") ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Squirrel");
            rat = PawnGenerator.GeneratePawn(ratKind, null);
            IntVec3 pen = root + new IntVec3(3, 0, 5);
            // a fence pen: animals cannot cross fences (building.isFence), the turret still sees over them
            ThingDef fence = DefDatabase<ThingDef>.GetNamedSilentFail("Fence");
            if (fence != null)
                foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(pen, Rot4.North, IntVec2.One))
                    if (c.InBounds(map) && c.GetEdifice(map) == null) TestUtil.SpawnBuilding<Thing>(fence, ThingDefOf.WoodLog, c, map);
            GenSpawn.Spawn(rat, pen, map);
            rat.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter, forced: true);
            Kit.Check(t0.Active && SentinelUtility.IsFoe(rat), $"setup: the turret is switched on ({t0.Active}) and the manhunting {rat.KindLabel} counts as a foe ({SentinelUtility.IsFoe(rat)})");
        }

        void Rules()
        {
            int gap = AftermathTuning.BattleGapTicks;
            Building_TurretGun t1 = Turret(MiniTurret, root + new IntVec3(6, 0, 3));
            CompOldSentinel c1 = t1.GetComp<CompOldSentinel>();

            // battle 1: two kills
            KillByTurret(t1, Foe(1)); KillByTurret(t1, Foe(2));
            Kit.Check(c1.Takedowns == 2 && c1.Battles == 1, $"two foes killed by the turret's shots in one fight: 2 takedowns, 1 battle ({c1.Takedowns}, {c1.Battles})");
            // battle 2 (13 hours later)
            c1.rec.lastEngagedTick -= gap + 1;
            KillByTurret(t1, Foe(3));
            Kit.Check(c1.Battles == 2, $"a fight more than {gap / GenDate.TicksPerHour} hours after the last one is a new battle ({c1.Battles})");
            // battle 3: the fourth takedown is not enough
            c1.rec.lastEngagedTick -= gap + 1;
            KillByTurret(t1, Foe(4));
            Kit.Check(c1.Takedowns == 4 && c1.Battles == 3 && !c1.Named, $"4 takedowns in 3 battles: still unnamed (negative; {c1.Takedowns}, {c1.Battles}, \"{c1.SentinelName}\")");
            string unnamedInspect = t1.GetInspectString();
            Kit.Check(unnamedInspect.Contains("Foes brought down: 4") && !t1.LabelCap.Contains("("),
                $"before its name the turret only shows a quiet tally line: \"{unnamedInspect.Replace("\n", " | ")}\" / {t1.LabelCap}");

            // the fifth, brought down (downed) by its shots: named
            Pawn fifth = Foe(5);
            DownByTurret(t1, fifth);
            Kit.Check((fifth.Downed || fifth.Dead) && c1.Takedowns == 5 && c1.Named && c1.rec.namedTick == TestUtil.Now,
                $"the fifth foe brought down in the third battle names it: \"{c1.SentinelName}\" ({c1.Takedowns} takedowns; foe downed {fifth.Downed}, dead {fifth.Dead}; named at {c1.rec?.namedTick}, now {TestUtil.Now})");
            string name = c1.SentinelName ?? "<none>";
            if (fifth.Downed && !fifth.Dead)
            {
                KillByTurret(t1, fifth);
                Kit.Check(c1.Takedowns == 5, $"finishing off the downed foe does not count him twice (negative; {c1.Takedowns})");
            }
            else Log.Message("[Test] (the fifth foe died of the blows instead of going down; skipped the finishing-off check)");
            string label = t1.LabelCap, inspect = t1.GetInspectString();
            Kit.Check(label.Contains(name) && label.Contains(MiniTurret.label) && inspect.Contains(name) && inspect.Contains("5 foes brought down in 3 battles"),
                $"its label and inspect carry the name and tally: {label} / \"{inspect.Replace("\n", " | ")}\"");
            Kit.Check(c1.OverlayText.Contains(name) && c1.OverlayText.Contains("5"), $"the map label reads \"{c1.OverlayText}\"");

            // negatives on credit
            Pawn shooter = Colonist(root + new IntVec3(9, 0, 6));
            Pawn byColonist = Foe(6);
            Kit.KillBy(byColonist, shooter);
            Pawn prisoner = Foe(7);
            prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            KillByTurret(t1, prisoner);
            Kit.Check(c1.Takedowns == 5, $"a colonist's kill and a turret shot killing a prisoner are not credited (negative; {c1.Takedowns})");

            // a second turret: six kills in a single battle is a lot, but one fight is not a legend
            Building_TurretGun t2 = Turret(MiniTurret, root + new IntVec3(15, 0, 3));
            CompOldSentinel c2 = t2.GetComp<CompOldSentinel>();
            for (int i = 0; i < 6; i++) KillByTurret(t2, Foe(8 + i));
            Kit.Check(c2.Takedowns == 6 && c2.Battles == 1 && !c2.Named, $"6 takedowns in one battle leave a turret unnamed (negative; {c2.Takedowns}, {c2.Battles})");

            // a manned mortar: the gunner is the instigator, the mortar the weapon
            Building_TurretGun mortar = Turret(Mortar, root + new IntVec3(11, 0, 5));
            CompOldSentinel cm = mortar.GetComp<CompOldSentinel>();
            Pawn gunner = Colonist(root + new IntVec3(12, 0, 3));
            gunner.mindState.lastMannedThing = mortar;
            Pawn shelled = Foe(14);
            shelled.Kill(new DamageInfo(DamageDefOf.Bomb, 60f, 0f, -1f, gunner, null, Mortar));
            Kit.Check(cm.Takedowns == 1 && cm.rec.HasComrade(gunner.thingIDNumber),
                $"a manned mortar's shell credits the mortar and makes the gunner an old hand ({cm.Takedowns}, gunner {cm.rec?.HasComrade(gunner.thingIDNumber)})");
            ThingDef rifle = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_AssaultRifle");
            Pawn shot = Foe(15);
            shot.Kill(new DamageInfo(DamageDefOf.Bullet, 60f, 0f, -1f, gunner, null, rifle));
            Kit.Check(cm.Takedowns == 1, $"the same gunner's rifle kill is not the mortar's (negative; {cm.Takedowns})");

            // fighting beside it: within 10 cells of a turret that is in the fight
            KillByTurret(t1, Foe(16));   // t1 engaged just now (an already counted kind of thing: +1)
            int before = c1.Takedowns;
            Pawn near = Colonist(root + new IntVec3(6, 0, 6));
            TestUtil.Teleport(near, t1.Position + new IntVec3(0, 0, 3));
            Pawn far = Colonist(root);
            foreach (IntVec3 off in new[] { new IntVec3(0, 0, 20), new IntVec3(0, 0, -20), new IntVec3(20, 0, 0), new IntVec3(-20, 0, 0) })
            {
                IntVec3 c = t1.Position + off;
                if (!c.InBounds(map)) continue;
                IntVec3 farCell = CellFinder.StandableCellNear(c, map, 5f, x => x.GetFirstPawn(map) == null);
                if (!farCell.IsValid || (farCell - t1.Position).LengthHorizontal <= 15f) continue;
                TestUtil.Teleport(far, farCell);
                break;
            }
            Pawn h1 = Foe(17), h2 = Foe(18);
            h1.TakeDamage(new DamageInfo(DamageDefOf.Cut, 3f, 0f, -1f, near));
            h2.TakeDamage(new DamageInfo(DamageDefOf.Cut, 3f, 0f, -1f, far));
            float dFar = (far.Position - t1.Position).LengthHorizontal;
            Kit.Check(c1.rec.HasComrade(near.thingIDNumber) && !c1.rec.HasComrade(far.thingIDNumber) && dFar > AftermathTuning.SentinelComradeRadius,
                $"a colonist who hits a foe 3 cells from the fighting turret is an old hand, one {dFar:0} cells away is not");
            Kit.Check(t1.GetInspectString().Contains("Old hands: ") && t1.GetInspectString().Contains(near.LabelShort), "...and the inspect pane lists its old hands");
            Kit.Check(c1.Takedowns == before, "a colonist's blow never changes the turret's tally (negative)");
            TestUtil.KillAll(new[] { h1, h2 });

            // uninstall and reinstall: the same Thing, the same record
            int tally = c1.Takedowns;
            IntVec3 cell = t1.Position;
            MinifiedThing mini = t1.MakeMinified();
            Kit.Check(mini != null && mini.InnerThing == t1 && mini.LabelNoCount.Contains(name), $"uninstalled, the crate still carries the name ({mini?.LabelNoCount})");
            Thing inner = mini.InnerThing;
            mini.InnerThing = null;
            mini.Destroy();
            GenSpawn.Spawn(inner, cell, map);
            Kit.Check(t1.Spawned && c1.SentinelName == name && c1.Takedowns == tally && SentinelTracker.For(map).sentinels.Contains(c1) && t1.LabelCap.Contains(name),
                $"reinstalled, it keeps its name, tally and label ({t1.LabelCap}, {c1.Takedowns})");

            SentinelRecord copy = TestUtil.Roundtrip(c1.rec);
            Kit.Check(copy != null && copy.name == c1.rec.name && copy.takedowns == c1.rec.takedowns && copy.battles == c1.rec.battles
                && copy.comradeIds.Count == c1.rec.comradeIds.Count && copy.comradeNames.SequenceEqual(c1.rec.comradeNames)
                && copy.recentVictims.Count == c1.rec.recentVictims.Count && copy.firstTick == c1.rec.firstTick,
                "a sentinel record survives a save/load roundtrip");

            // the unnamed one falls first: nothing (negative)
            TestUtil.Teleport(near, root + new IntVec3(0, 0, 6));
            Pawn bystander = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p.needs?.mood != null && !c1.rec.HasComrade(p.thingIDNumber) && p != far);
            int remainsBefore = Remains().Count;
            int moodBefore = SentinelThoughts(bystander);
            Pawn wrecker = Foe(19);
            t2.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f, 0f, -1f, wrecker));
            Kit.Check(Remains().Count == remainsBefore && SentinelThoughts(bystander) == moodBefore,
                "an unnamed turret destroyed leaves no remains and no mourning (negative)");

            // the named one falls
            int letters = Find.LetterStack.LettersListForReading.Count;
            t1.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f, 0f, -1f, wrecker));
            SentinelRemains rem = Remains().FirstOrDefault(r => r.sentinelName == name);
            Kit.Check(rem != null && rem.Position.DistanceTo(cell) <= 2.5f && rem.LabelNoCount.Contains(name),
                $"the named turret leaves its remains where it stood ({rem?.LabelNoCount} at {rem?.Position}, turret at {cell})");
            string rinspect = rem?.GetInspectString() ?? "";
            Kit.Check(rinspect.Contains(tally + " foes in") && rinspect.Contains(enemy.Name) && rem != null && rem.WreckMaterials(out _, out _),
                $"...telling its tally and who destroyed it, drawn from the turret's own graphics: \"{rinspect.Replace("\n", " | ")}\"");
            Thought_Memory comrade = near.needs?.mood?.thoughts.memories.Memories.FirstOrDefault(m => m.def == AftermathDefOf.Aftermath_SentinelFellComrade);
            Thought_Memory general = bystander?.needs?.mood?.thoughts.memories.Memories.FirstOrDefault(m => m.def == AftermathDefOf.Aftermath_SentinelFell);
            Kit.Check(comrade != null && comrade.LabelCap.Contains(name) && comrade.MoodOffset() < 0,
                $"its old hand {near.LabelShort} mourns it: \"{comrade?.LabelCap}\" ({comrade?.MoodOffset()})");
            Kit.Check(general != null && general.LabelCap.Contains(name) && bystander.needs.mood.thoughts.memories.Memories.All(m => m.def != AftermathDefOf.Aftermath_SentinelFellComrade),
                $"the rest of the colony mourns it less: {bystander?.LabelShort} \"{general?.LabelCap}\" ({general?.MoodOffset()})");
            Letter letter = Find.LetterStack.LettersListForReading.Skip(letters).FirstOrDefault(l => l.Label.ToString().Contains(name));
            Kit.Check(letter != null, $"a letter tells the colony that {name} has fallen ({letter?.Label})");

            // feature off: nothing credited, the name hidden
            AftermathMod.Settings.oldSentinels = false;
            // (clear of the two blasts: 5+ cells from where t1 and t2 stood)
            Building_TurretGun t3 = Turret(MiniTurret, root + new IntVec3(1, 0, 5));
            CompOldSentinel c3 = t3.GetComp<CompOldSentinel>();
            KillByTurret(t3, Foe(20));
            bool notCredited = c3.rec == null;
            c3.rec = new SentinelRecord { name = "Test Bess", takedowns = 9, battles = 4 };
            Kit.Check(notCredited && !t3.LabelCap.Contains("Test Bess") && c3.CompInspectStringExtra() == null,
                $"with the feature off a turret's kills are not credited and its name is hidden (negative; credited {!notCredited}, {t3.LabelCap})");
            AftermathMod.Settings.oldSentinels = true;
            Kit.Check(t3.LabelCap.Contains("Test Bess"), "...and the name comes back when it is turned on");
            c3.rec = null;   // a test name must never fall and be mourned later
        }

        // ------------------------------------------------------------------ helpers

        static void KillByTurret(Building_TurretGun t, Pawn victim)
        {
            if (victim.Dead) return;
            victim.Kill(new DamageInfo(DamageDefOf.Bullet, 40f, 0f, -1f, t, null, t.gun?.def));
        }

        /// <summary>Real bullet hits from the turret on the legs until the victim is down.</summary>
        static void DownByTurret(Building_TurretGun t, Pawn victim)
        {
            for (int i = 0; i < 150 && !victim.Downed && !victim.Dead; i++)
            {
                BodyPartRecord leg = victim.health.hediffSet.GetNotMissingParts().Where(x => x.def == BodyPartDefOf.Leg).RandomElementWithFallback();
                victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 3f, 1f, -1f, t, leg, t.gun?.def));
            }
        }

        Building_TurretGun Turret(ThingDef def, IntVec3 at)
        {
            var t = TestUtil.SpawnBuilding<Building_TurretGun>(def, null, at, map, Rot4.North);
            return t;
        }

        Pawn Foe(int i)
        {
            Pawn p = TestUtil.SpawnAt(map, root + new IntVec3(i % 17, 0, 0), enemy);
            p.equipment?.DestroyAllEquipment();
            foes.Add(p);
            return p;
        }

        Pawn Colonist(IntVec3 at)
        {
            Pawn p = TestUtil.NewColonist(map, at);
            TestUtil.Teleport(p, at);
            p.drafter.Drafted = true;   // stays put
            colonists.Add(p);
            return p;
        }

        List<SentinelRemains> Remains() =>
            map.listerThings.ThingsOfDef(AftermathDefOf.Aftermath_SentinelRemains).OfType<SentinelRemains>().ToList();

        static int SentinelThoughts(Pawn p) =>
            p?.needs?.mood?.thoughts.memories.Memories.Count(m => m.def == AftermathDefOf.Aftermath_SentinelFell || m.def == AftermathDefOf.Aftermath_SentinelFellComrade) ?? 0;

        protected override string Facts() => $"t0 active {t0?.Active}, rat dead {rat?.Dead} downed {rat?.Downed} at {rat?.Position}";

        protected override void Cleanup()
        {
            AftermathMod.Settings.oldSentinels = true;
            CompPowerTrader power = t0?.GetComp<CompPowerTrader>();
            if (power != null && t0.Spawned) power.PowerOn = false;
            TestUtil.KillAll(foes);
            foreach (Pawn p in colonists)
            {
                if (p.drafter != null && p.Spawned && !p.Dead) p.drafter.Drafted = false;
                TestUtil.Heal(p);
            }
        }
    }
}
