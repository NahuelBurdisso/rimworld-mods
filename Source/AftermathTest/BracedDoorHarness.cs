using System.Collections.Generic;
using System.Linq;
using Aftermath;
using RimWorld;
using Verse;

namespace AftermathTest
{
    /// <summary>Feature 1 — Braced Door: hostile kills and batterings of player doors, rebuilds, repairs, negatives.</summary>
    public class BracedDoorHarness : HarnessBase
    {
        public BracedDoorHarness(Game game) { }

        protected override int StartTick => TestUtil.DoorsStart;
        protected override string Name => "BracedDoor";

        IntVec3 root;
        Pawn hostile, colonist;
        readonly List<Thing> doors = new List<Thing>();

        BreachTracker T => BreachTracker.For(map);

        protected override void Step()
        {
            if (phase != 0) return;
            Next();
            root = TestUtil.PrepareStrip(map, TestUtil.Anchor(map, -14, 10), 9, 3, Name);
            Kit.Check(root.IsValid, "setup: a clear strip for the doors");
            Faction enemy = TestUtil.HostileFaction();
            Kit.Check(enemy != null, $"setup: a hostile faction ({enemy?.Name ?? "none"})");
            if (!root.IsValid || enemy == null) { Finish(); return; }
            IntVec3 c1 = root + new IntVec3(1, 0, 1), c2 = root + new IntVec3(4, 0, 1), c3 = root + new IntVec3(7, 0, 1);
            hostile = TestUtil.SpawnAt(map, root + new IntVec3(8, 0, 3), enemy);
            colonist = map.mapPawns.FreeColonistsSpawned.First();
            Kit.Check(AftermathDefOf.Aftermath_CapturedStandard != null && ThingDefOf.Door.HasComp(typeof(CompBracedDoor)),
                "the door def got CompBracedDoor injected at startup");

            // 1. a raider breaks a player door: the cell remembers, with his faction
            Thing d = Door(c1);
            d.Kill(Hit(hostile));
            List<BreachRecord> at1 = T.At(c1);
            Kit.Check(at1.Count == 1 && at1[0].kind == BreachKind.Broken && at1[0].attacker == enemy.Name,
                $"a door broken by a raider leaves one 'broken' breach naming {enemy.Name} ({Describe(at1)})");

            // 2. the door rebuilt on that cell shows it
            d = Door(c1);
            var comp = d.TryGetComp<CompBracedDoor>();
            string inspect = d.GetInspectString();
            Kit.Check(comp != null && comp.VisiblePlanks() == 1, $"the rebuilt door shows one plank ({comp?.VisiblePlanks()})");
            Kit.Check(inspect.Contains("Braced 1\u00D7") && inspect.Contains(enemy.Name), $"its inspect pane lists the breach: \"{inspect.Replace("\n", " | ")}\"");

            // 3. broken again the same day: one breach per raid, still one record
            d.Kill(Hit(hostile));
            Kit.Check(T.At(c1).Count == 1, $"broken again within the day: still one breach ({T.At(c1).Count})");

            // 4. a day later it is a new breach: two planks on the next door
            foreach (BreachRecord r in T.At(c1)) r.tick -= 2 * GenDate.TicksPerDay;
            d = Door(c1);
            d.Kill(Hit(hostile));
            d = Door(c1);
            comp = d.TryGetComp<CompBracedDoor>();
            Kit.Check(T.At(c1).Count == 2 && comp.VisiblePlanks() == 2, $"a later raid adds a second breach and a second plank ({T.At(c1).Count}, {comp.VisiblePlanks()})");

            // 5. battered below 30% by a raider: a 'battered' breach that shows only once repaired
            Thing d2 = Door(c2);
            for (int i = 0; i < 300 && !d2.Destroyed && d2.HitPoints > d2.MaxHitPoints * 0.3f; i++)
                d2.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 5f, 0f, -1f, hostile));
            var comp2 = d2.TryGetComp<CompBracedDoor>();
            List<BreachRecord> at2 = T.At(c2);
            Kit.Check(!d2.Destroyed && at2.Count == 1 && at2[0].kind == BreachKind.Battered,
                $"a door battered to {d2.HitPoints}/{d2.MaxHitPoints} by a raider leaves one 'battered' breach ({Describe(at2)})");
            Kit.Check(comp2.VisiblePlanks() == 0 && d2.GetInspectString().Contains("repair it fully"),
                $"...no plank until it is repaired ({comp2.VisiblePlanks()})");
            d2.HitPoints = d2.MaxHitPoints;
            Kit.Check(comp2.VisiblePlanks() == 1 && T.At(c2)[0].repaired, $"...repaired to full: one plank ({comp2.VisiblePlanks()})");

            // 6. negative: a colonist breaks a door
            Thing d3 = Door(c3);
            d3.Kill(Hit(colonist));
            Kit.Check(T.At(c3).Count == 0, "a door broken by a colonist leaves no breach");

            // 7. negative: feature off
            AftermathMod.Settings.bracedDoors = false;
            d3 = Door(c3);
            d3.Kill(Hit(hostile));
            AftermathMod.Settings.bracedDoors = true;
            Kit.Check(T.At(c3).Count == 0, "with the feature off, a raider breaking a door leaves no breach");

            // 8. deconstructing the door clears the cell
            d.Destroy(DestroyMode.Deconstruct);
            Kit.Check(T.At(c1).Count == 0, "deconstructing the door clears its cell's breaches");

            // 9. save/load
            BreachRecord rec = T.At(c2)[0];
            BreachRecord copy = TestUtil.Roundtrip(rec);
            Kit.Check(copy != null && copy.cell == rec.cell && copy.tick == rec.tick && copy.attacker == rec.attacker
                && copy.kind == rec.kind && copy.repaired == rec.repaired, "a breach record survives a save/load roundtrip");
            Finish();
        }

        Thing Door(IntVec3 c)
        {
            Thing d = TestUtil.SpawnBuilding<Thing>(ThingDefOf.Door, ThingDefOf.WoodLog, c, map);
            doors.Add(d);
            return d;
        }

        static DamageInfo Hit(Thing by) => new DamageInfo(DamageDefOf.Blunt, 99999f, 0f, -1f, by);

        static string Describe(List<BreachRecord> l) =>
            l.Count == 0 ? "none" : string.Join("; ", l.Select(r => $"{r.kind} by {r.attacker} at {r.tick}{(r.repaired ? " repaired" : "")}"));

        protected override void Cleanup()
        {
            AftermathMod.Settings.bracedDoors = true;
            if (hostile != null && !hostile.Dead && hostile.Spawned) hostile.Kill(null);
            foreach (Thing d in doors) if (!d.Destroyed) d.Destroy(DestroyMode.Vanish);
        }
    }
}
