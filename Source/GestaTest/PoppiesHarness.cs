using System.Linq;
using Gesta;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace GestaTest
{
    /// <summary>Battlefield Poppies through real kills. Runs after the feats harness (tick 1500).</summary>
    public class PoppiesHarness : GameComponent
    {
        bool active, done;
        int ticks;

        public PoppiesHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done || ++ticks != 1500) return;
            done = true;
            try { Run(); }
            catch (System.Exception ex) { Kit.Check(false, "poppies harness threw: " + ex); }
            Kit.Summary("Gesta/Poppies");
        }

        void Run()
        {
            Map map = Find.CurrentMap;
            var field = map.GetComponent<PoppyField>();
            IntVec3 root = map.mapPawns.FreeColonistsSpawned.First().Position;
            IntVec3 cell = FertileCell(map, root, IntVec3.Invalid);
            IntVec3 enemyCell = FertileCell(map, root, cell);
            Kit.Check(cell.IsValid && enemyCell.IsValid, "setup: found open fertile cells");

            Pawn victim = NewColonist(map, cell);
            Pawn mourner = NewColonist(map, cell + IntVec3.North * 3);
            victim.relations.AddDirectRelation(PawnRelationDefOf.Spouse, mourner);
            Pawn killer = Kit.SpawnHostile(map, cell + IntVec3.East * 8);
            int before = field.Pending.Count;
            Kit.KillBy(victim, killer);
            PoppyMemory mem = field.Pending.FirstOrDefault(m => m.cell == cell);
            Kit.Check(mem != null && field.Pending.Count == before + 1, "a combat death on open fertile ground is remembered");
            Kit.Check(mem != null && mem.victimName == victim.LabelShort && mem.killerName == killer.LabelShort && mem.colonist,
                $"the record names victim and killer ({mem?.victimName} / {mem?.killerName})");
            Kit.Check(mem != null && mem.mourners.Contains(mourner), "the spouse is listed as a mourner");

            // merged with the adjacent record
            Pawn victim3 = NewColonist(map, cell + IntVec3.East);
            Kit.KillBy(victim3, killer);
            Kit.Check(field.Pending.Count == before + 1, "a death in the next cell does not add a second poppy");

            // under a roof: nothing
            IntVec3 roofCell = cell + IntVec3.South * 6;
            if (roofCell.InBounds(map))
            {
                map.roofGrid.SetRoof(roofCell, RoofDefOf.RoofConstructed);
                Pawn victim2 = NewColonist(map, roofCell);
                Kit.Teleport(victim2, roofCell);
                Kit.KillBy(victim2, killer);
                Kit.Check(field.Pending.All(m => m.cell != roofCell), "a death under a roof grows nothing");
                map.roofGrid.SetRoof(roofCell, null);
            }

            // an enemy killed by a colonist
            Pawn enemy = Kit.SpawnHostile(map, enemyCell);
            Kit.Teleport(enemy, enemyCell);
            Kit.KillBy(enemy, mourner);
            PoppyMemory enemyMem = field.Pending.FirstOrDefault(m => m.cell == enemyCell);
            Kit.Check(enemyMem != null && !enemyMem.colonist, "an enemy's death is remembered too");

            // a natural death (no killer) is not a battle
            int countBefore = field.Pending.Count;
            killer.Kill(null);
            Kit.Check(field.Pending.Count == countBefore, "a death without a killer is not a battlefield");

            // an execution is not a battle
            IntVec3 execCell = FertileCell(map, root, cell, enemyCell);
            if (execCell.IsValid)
            {
                Pawn prisoner = Kit.SpawnHostile(map, execCell);
                Kit.Teleport(prisoner, execCell);
                prisoner.Kill(new DamageInfo(DamageDefOf.ExecutionCut, 999f, 0f, -1f, mourner));
                Kit.Check(field.Pending.All(m => m.cell != execCell), "an execution grows no poppy");
            }

            // bleeding out after being shot: no DamageInfo at death, the last attacker is remembered
            IntVec3 bleedCell = FertileCell(map, root, cell, enemyCell, execCell);
            if (bleedCell.IsValid)
            {
                Pawn bleeder = Kit.SpawnHostile(map, bleedCell);
                Kit.Teleport(bleeder, bleedCell);
                bleeder.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 6f, 2f, -1f, mourner));
                bleeder.Kill(null);
                PoppyMemory bleedMem = field.Pending.FirstOrDefault(m => m.cell == bleedCell);
                Kit.Check(bleedMem != null && bleedMem.killerName == mourner.LabelShort, "a raider who bleeds out still gets a poppy naming who shot them");
            }

            // a valuable wild plant on the death cell is never cleared
            ThingDef healroot = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_HealrootWild");
            Plant herb = null;
            if (healroot != null)
            {
                foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, 2f, true))
                    if (c.InBounds(map)) c.GetPlant(map)?.Destroy();
                herb = (Plant)GenSpawn.Spawn(healroot, cell, map);
            }

            field.TryBloomAll(force: true);
            if (herb != null) Kit.Check(!herb.Destroyed, "blooming does not destroy a wild healroot");
            Plant_Poppy red = GenRadial.RadialCellsAround(cell, 2f, true).Select(c => c.GetPlant(map)).OfType<Plant_Poppy>().FirstOrDefault();
            Kit.Check(red != null && red.def == GestaDefOf.Gesta_PoppyRed, "a red poppy blooms where the colonist fell");
            Kit.Check(red != null && red.LabelNoCount.Contains(victim.LabelShort), $"its label names the fallen ({red?.LabelNoCount})");
            string inspect = red?.GetInspectString() ?? "";
            Kit.Check(inspect.Contains(victim.LabelShort) && inspect.Contains(killer.LabelShort), $"inspect text tells the story ({inspect.Split('\n')[0]})");
            Kit.Check(red != null && !red.IngestibleNow && red.CurrentDyingDamagePerTick == 0f, "it is not food and never dies of age");
            Plant_Poppy white = GenRadial.RadialCellsAround(enemyCell, 2f, true).Select(c => c.GetPlant(map)).OfType<Plant_Poppy>().FirstOrDefault();
            Kit.Check(white != null && white.def == GestaDefOf.Gesta_PoppyWhite, "a white poppy blooms where the enemy fell");
            Kit.Check(field.Pending.All(m => m.cell != cell && m.cell != enemyCell), "bloomed records leave the pending list");

            if (red != null)
            {
                Kit.Teleport(mourner, red.Position + IntVec3.West);
                red.TickLong();
                Kit.Check(mourner.needs.mood.thoughts.memories.GetFirstMemoryOfDef(GestaDefOf.Gesta_VisitedPoppy) != null,
                    "the spouse passing by remembers the fallen");
            }
        }

        static IntVec3 FertileCell(Map map, IntVec3 root, params IntVec3[] awayFromAll)
        {
            foreach (IntVec3 c in GenRadial.RadialCellsAround(root, 40f, false))
            {
                if (awayFromAll.Any(a => a.IsValid && c.InHorDistOf(a, 8f))) continue;
                IntVec3 found = FertileCellAt(map, c);
                if (found.IsValid) return found;
            }
            return IntVec3.Invalid;
        }

        /// <summary>Any open standable cell; the harness lays soil around it so the map's biome does not matter.</summary>
        static IntVec3 FertileCellAt(Map map, IntVec3 c)
        {
            if (!c.InBounds(map) || !c.Standable(map) || c.Roofed(map) || c.GetTerrain(map).IsWater) return IntVec3.Invalid;
            if (c.GetEdifice(map) != null || c.GetZone(map) != null || c.GetPlant(map)?.def.plant.IsTree == true) return IntVec3.Invalid;
            foreach (IntVec3 n in GenRadial.RadialCellsAround(c, 2.5f, true))
                if (!n.InBounds(map) || n.Roofed(map) || n.GetEdifice(map) != null) return IntVec3.Invalid;
            foreach (IntVec3 n in GenRadial.RadialCellsAround(c, 2.5f, true))
                if (map.fertilityGrid.FertilityAt(n) < 0.9f) map.terrainGrid.SetTerrain(n, TerrainDefOf.Soil);
            return c;
        }

        static Pawn NewColonist(Map map, IntVec3 at)
        {
            Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(p, at, map);
            return p;
        }
    }
}
