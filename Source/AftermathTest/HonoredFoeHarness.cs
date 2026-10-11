using System.Collections.Generic;
using System.Linq;
using Aftermath;
using RimWorld;
using UnityEngine;
using Verse;

namespace AftermathTest
{
    /// <summary>Feature 3 — Honored Foe: deeds from real blows, the snapshot at death, the grave marker and story.</summary>
    public class HonoredFoeHarness : HarnessBase
    {
        public HonoredFoeHarness(Game game) { }

        protected override int StartTick => TestUtil.FoesStart;
        protected override string Name => "HonoredFoe";

        IntVec3 root;
        Faction enemy;
        readonly List<Pawn> colonists = new List<Pawn>();
        readonly List<Pawn> foes = new List<Pawn>();

        FoeTracker T => FoeTracker.Instance;

        protected override void Step()
        {
            if (phase != 0) return;
            Next();
            enemy = TestUtil.HostileFaction();
            root = TestUtil.PrepareStrip(map, TestUtil.Anchor(map, -14, -12), 9, 5, Name);
            Kit.Check(enemy != null && root.IsValid && T != null, $"setup: hostile faction ({enemy?.Name ?? "none"}), strip, tracker");
            if (enemy == null || !root.IsValid || T == null) { Finish(); return; }
            ThingDef rifleDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_AssaultRifle");
            ThingDef helmetDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_SimpleHelmet");
            Kit.Check(rifleDef != null && helmetDef != null, "setup: assault rifle and simple helmet defs");
            Kit.Check(ThingDefOf.Grave.HasComp(typeof(CompHonoredFoe)), "the grave def got CompHonoredFoe injected at startup");

            // the worthy one: rifle and helmet, downs two colonists with real blows
            Pawn foe = Foe(root + new IntVec3(8, 0, 4));
            foe.equipment.DestroyAllEquipment();
            foe.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(rifleDef));
            foe.apparel.Wear((Apparel)ThingMaker.MakeThing(helmetDef, ThingDefOf.Steel), dropReplacedApparel: false);
            Pawn a = Colonist(root + new IntVec3(1, 0, 4)), b = Colonist(root + new IntVec3(2, 0, 4));
            TestUtil.DownBy(a, foe);
            FoeDeeds d = T.DeedsOf(foe);
            Kit.Check(d != null && d.victims.Count == 1 && !d.Worthy, $"a raider who brought down one colonist is not yet worthy ({d?.victims.Count})");
            TestUtil.DownBy(b, foe);
            Kit.Check(d != null && d.victims.Count == 2 && d.Worthy, $"...after bringing down a second ({a.LabelShort}, {b.LabelShort}) he is worthy");

            // the unworthy one: downs only one
            Pawn small = Foe(root + new IntVec3(8, 0, 0));
            Pawn c = Colonist(root + new IntVec3(3, 0, 4));
            TestUtil.DownBy(c, small);
            Kit.Check(T.DeedsOf(small) != null && !T.DeedsOf(small).Worthy, "a raider with one victim is not worthy (negative)");

            // a killer: one kill is enough
            Pawn killer = Foe(root + new IntVec3(6, 0, 0));
            Pawn e = Colonist(root + new IntVec3(4, 0, 4));
            Kit.KillBy(e, killer);
            Kit.Check(T.DeedsOf(killer) != null && T.DeedsOf(killer).Worthy && T.DeedsOf(killer).Kills == 1, "a raider who kills a colonist is worthy at once");

            // death of the worthy one: the record
            Pawn finisher = Colonist(root + new IntVec3(5, 0, 4));
            string foeName = foe.Name.ToStringShort;
            Kit.KillBy(foe, finisher);
            FoeRecord rec = T.RecordOf(foe.thingIDNumber);
            Kit.Check(rec != null && rec.weaponDef == rifleDef && rec.helmetDef == helmetDef && rec.helmetStuff == ThingDefOf.Steel,
                $"his death leaves a record with his rifle and helmet ({rec?.weaponDef?.defName}, {rec?.helmetDef?.defName}/{rec?.helmetStuff?.defName})");
            Kit.Check(rec != null && rec.victims.Count == 2 && rec.victims.Any(v => v.name == a.LabelShort) && rec.victims.Any(v => v.name == b.LabelShort)
                && rec.killerName == finisher.LabelShort, "...naming both victims and who killed him");
            Kit.KillBy(small, finisher);
            Kit.Check(T.RecordOf(small.thingIDNumber) == null, "the unworthy one dies without a record (negative)");

            // burial
            Building_Grave g1 = Grave(root + new IntVec3(0, 0, 1));
            Building_Grave g2 = Grave(root + new IntVec3(2, 0, 1));
            Bury(g1, foe.Corpse);
            Bury(g2, small.Corpse);
            Kit.Check(g1.HasCorpse && g2.HasCorpse, "setup: both corpses are in their graves");
            var comp1 = g1.GetComp<CompHonoredFoe>();
            bool marked = comp1.Marker(out Material wMat, out Material hMat);
            Kit.Check(marked && wMat != null && hMat != null, $"the worthy foe's grave carries his weapon and helmet graphics ({wMat?.mainTexture?.name}, {hMat?.mainTexture?.name})");
            string inspect = g1.GetInspectString();
            Kit.Check(inspect.Contains(foeName) && inspect.Contains("worthy foe") && inspect.Contains(a.LabelShort) && inspect.Contains(finisher.LabelShort),
                $"...and its inspect pane tells his name and deeds: \"{inspect.Replace("\n", " | ")}\"");
            Kit.Check(rec.buried, "...and the record is marked buried (kept)");
            Kit.Check(!g2.GetComp<CompHonoredFoe>().Marker(out _, out _) && !g2.GetInspectString().Contains("worthy foe"),
                "an ordinary raider's grave has no marker and no story (negative)");

            // feature off
            AftermathMod.Settings.honoredFoes = false;
            Kit.Check(!comp1.Marker(out _, out _) && !g1.GetInspectString().Contains("worthy foe"), "with the feature off the marker and story are hidden");
            Pawn offFoe = Foe(root + new IntVec3(7, 0, 2));
            Pawn f = Colonist(root + new IntVec3(6, 0, 4));
            Kit.KillBy(f, offFoe);
            Kit.Check(T.DeedsOf(offFoe) == null, "with the feature off, a kill is not credited (negative)");
            AftermathMod.Settings.honoredFoes = true;

            FoeRecord copy = TestUtil.Roundtrip(rec);
            Kit.Check(copy != null && copy.name == rec.name && copy.weaponDef == rec.weaponDef && copy.helmetStuff == rec.helmetStuff
                && copy.victims.Count == 2 && copy.victims[0].name == rec.victims[0].name && copy.deathTick == rec.deathTick,
                "a foe record survives a save/load roundtrip");
            Finish();
        }

        Pawn Foe(IntVec3 at)
        {
            Pawn p = TestUtil.SpawnAt(map, at, enemy);
            p.equipment?.DestroyAllEquipment();   // nobody shoots during the test; the worthy one is re-armed
            foes.Add(p);
            return p;
        }

        Pawn Colonist(IntVec3 at)
        {
            Pawn p = TestUtil.NewColonist(map, at);
            TestUtil.Teleport(p, at);
            colonists.Add(p);
            return p;
        }

        Building_Grave Grave(IntVec3 at) => TestUtil.SpawnBuilding<Building_Grave>(ThingDefOf.Grave, null, at, map, Rot4.South);

        static void Bury(Building_Grave g, Corpse corpse)
        {
            if (corpse == null) return;
            if (corpse.Spawned) corpse.DeSpawn();
            g.TryAcceptThing(corpse);
        }

        protected override void Cleanup()
        {
            AftermathMod.Settings.honoredFoes = true;
            TestUtil.KillAll(foes);
            foreach (Pawn p in colonists) TestUtil.Heal(p);
        }
    }
}
