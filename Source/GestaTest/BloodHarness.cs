using System.Linq;
using Gesta;
using RimWorld;
using Verse;

namespace GestaTest
{
    /// <summary>Blood-Stained through real damage. Runs at tick 2250, after the tide harness.</summary>
    public class BloodHarness : GameComponent
    {
        bool active, done;
        int ticks;

        public BloodHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done || ++ticks != 2250) return;
            done = true;
            try { Run(); }
            catch (System.Exception ex) { Kit.Check(false, "blood harness threw: " + ex); }
            Kit.Summary("Gesta/Blood");
        }

        static Hediff Stain(Pawn p) => p.health.hediffSet.GetFirstHediffOfDef(GestaDefOf.Gesta_Bloodstained);

        void Run()
        {
            Map map = Find.CurrentMap;
            IntVec3 root = map.mapPawns.FreeColonistsSpawned.First().Position + new IntVec3(0, 0, 12);
            Pawn fighter = NewColonist(map, root);
            Pawn shooter = NewColonist(map, root + new IntVec3(-12, 0, 0));
            Pawn enemy = Kit.SpawnHostile(map, root + IntVec3.East);
            enemy.equipment?.DestroyAllEquipment();
            Kit.Teleport(enemy, root + IntVec3.East);

            enemy.TakeDamage(new DamageInfo(DamageDefOf.Cut, 8f, 1f, -1f, fighter));
            Kit.Check(Stain(fighter) != null, "a melee hit that draws blood stains the attacker");
            Kit.Check(Stain(enemy) != null, "the one who bleeds is stained too");

            Pawn enemy2 = Kit.SpawnHostile(map, root + new IntVec3(6, 0, 6));
            Kit.Teleport(enemy2, root + new IntVec3(6, 0, 6));
            enemy2.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 8f, 1f, -1f, shooter));
            Kit.Check(Stain(shooter) == null && Stain(enemy2) != null, "a shooter from afar stays clean; the target does not");

            Pawn cutter = NewColonist(map, root + new IntVec3(10, 0, -6));
            if (Faction.OfMechanoids != null)
            {
                Pawn mech = Kit.SpawnHostile(map, cutter.Position + IntVec3.East, PawnKindDef.Named("Mech_Scyther"), Faction.OfMechanoids);
                Kit.Teleport(mech, cutter.Position + IntVec3.East);
                mech.TakeDamage(new DamageInfo(DamageDefOf.Cut, 8f, 1f, -1f, cutter));
                Kit.Check(Stain(cutter) == null, "cutting a mechanoid leaves no blood");
                mech.Kill(null);
            }

            Hediff h = Stain(fighter);
            Kit.Check(h != null && h.Visible && h.def.HasDefinedGraphicProperties, "the stain is a visible hediff with render nodes");
            BloodStains.Add(fighter, 0.3f);
            Kit.Check(h != null && h.CurStageIndex == 1, $"more blood raises the stage ({h?.LabelCap})");
            BloodStains.Add(fighter, 0.5f);
            Kit.Check(h != null && h.CurStageIndex == 2 && h.Severity <= 1f, $"up to drenched, capped ({h?.Severity:0.00})");
            var fade = h?.TryGetComp<HediffComp_BloodFades>();
            Kit.Check(fade != null && fade.SeverityChangePerDay() < 0f, "it washes off over time");

            enemy.Kill(null);
            enemy2.Kill(null);
        }

        static Pawn NewColonist(Map map, IntVec3 at)
        {
            Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(p, at, map);
            return p;
        }
    }
}
