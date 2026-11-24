using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ReactorSlag;
using RimWorld;
using UnityEngine;
using Verse;

namespace ReactorSlagTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N`. Kills real mechs (and a pirate) in open natural ground and checks
    /// which deaths spill a core, its size and light; then ages, "reloads" and cools cores and checks the light,
    /// the scar filth and the Odyssey cooled lava (never on a built floor). One core is left hot under the camera
    /// so Print/DrawAt run for a few hundred frames.
    /// </summary>
    public class SlagHarness : GameComponent
    {
        bool active, done;
        int ticks;
        Map map;
        readonly List<IntVec3> used = new List<IntVec3>();
        ReactorSlagCore centipedeCore, apocritonCore, floorCore, centurionCore;

        static readonly FieldInfo GlowRadiusOverride =
            typeof(CompGlower).GetField("glowRadiusOverride", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo GlowOnInt =
            typeof(CompGlower).GetField("glowOnInt", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo LitGlowers =
            typeof(GlowGrid).GetField("litGlowers", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Is the glower really registered in the map's glow grid (not just flagged on)?</summary>
        bool InGrid(CompGlower g) => LitGlowers?.GetValue(map.glowGrid) is HashSet<CompGlower> set && set.Contains(g);

        int ScarsAround(IntVec3 center, float radius) =>
            ReactorSlagCore.PoolCells(center, map, radius).Count(x => x.GetThingList(map).Any(t => t.def == ReactorSlagDefOf.ReactorSlag_Scar));

        int ScarableAround(IntVec3 center, float radius) =>
            ReactorSlagCore.PoolCells(center, map, radius).Count(x => x.WalkableByAny(map) && FilthMaker.CanMakeFilth(x, map, ReactorSlagDefOf.ReactorSlag_Scar));

        public SlagHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (ticks == 60) Start();
            else if (ticks == 700) Later();
        }

        static int Count(Map m, ThingDef def) => m.listerThings.ThingsOfDef(def).Count;
        int Cores => Count(map, ReactorSlagDefOf.ReactorSlag_Core) + Count(map, ReactorSlagDefOf.ReactorSlag_CoreLarge);

        static ReactorSlagCore CoreAt(Map m, IntVec3 c) =>
            c.GetThingList(m).OfType<ReactorSlagCore>().FirstOrDefault();

        bool OpenArea(IntVec3 c)
        {
            if (used.Any(u => u.DistanceTo(c) < 14f)) return false;
            foreach (IntVec3 x in GenRadial.RadialCellsAround(c, 3.5f, true))
            {
                if (!x.InBounds(map) || !x.Standable(map) || x.Fogged(map) || x.GetEdifice(map) != null) return false;
                TerrainDef t = x.GetTerrain(map);
                if (!t.natural || t.IsWater || map.terrainGrid.UnderTerrainAt(x) != null) return false;
                if (map.zoneManager.ZoneAt(x) != null) return false;
                if (x.GetThingList(map).Any(th => th is Pawn || th.def.category == ThingCategory.Item)) return false;
            }
            return true;
        }

        bool FindArea(out IntVec3 c)
        {
            if (CellFinder.TryFindRandomCellNear(map.Center, map, 60, OpenArea, out c)) { used.Add(c); return true; }
            Kit.Check(false, "found an open natural area");
            return false;
        }

        /// <summary>Spawns a pawn of the kind exactly at the cell and kills it with a bomb; returns its corpse cell.</summary>
        IntVec3 SpawnAndKill(string kindName, Faction faction, IntVec3 cell, out Pawn pawn)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(kindName);
            pawn = null;
            if (kind == null) { Kit.Check(false, "pawn kind " + kindName + " exists"); return IntVec3.Invalid; }
            pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, forceGenerateNewPawn: true));
            GenSpawn.Spawn(pawn, cell, map);
            pawn.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f));
            Corpse corpse = pawn.Corpse;
            return corpse != null && corpse.Spawned ? corpse.Position : IntVec3.Invalid;
        }

        void Start()
        {
            map = Find.CurrentMap;
            Faction mechs = Faction.OfMechanoids;
            Kit.Check(SlagInjector.Injected > 0, $"comp injected into {SlagInjector.Injected} mechanoid races");
            Kit.Check(DefDatabase<ThingDef>.GetNamedSilentFail("Mech_Lancer")?.HasComp(typeof(CompReactorSlag)) == true, "Mech_Lancer race has the comp (the size test is at death)");
            Kit.Check(!ThingDefOf.Human.HasComp(typeof(CompReactorSlag)), "Human race has no comp");

            // 1. hostile centipede blaster -> one small core under the corpse, light at full radius
            if (!FindArea(out IntVec3 a)) { Finish(); return; }
            int before = Cores;
            IntVec3 corpseA = SpawnAndKill("Mech_CentipedeBlaster", mechs, a, out _);
            centipedeCore = corpseA.IsValid ? CoreAt(map, corpseA) : null;
            Kit.Check(centipedeCore != null && centipedeCore.def == ReactorSlagDefOf.ReactorSlag_Core && Cores == before + 1,
                $"centipede death spilled exactly one small core under the corpse ({Cores - before} new)");
            if (centipedeCore != null)
            {
                Kit.Check(centipedeCore.Glower != null && centipedeCore.Glower.Glows && InGrid(centipedeCore.Glower) && Mathf.Approximately(centipedeCore.Glower.GlowRadius, 6f),
                    $"small core lit and in the glow grid at radius 6 (glows {centipedeCore.Glower?.Glows}, radius {centipedeCore.Glower?.GlowRadius})");
                Kit.Check(ReactorSlagCore.HaloMaterial(centipedeCore.Heat) != null && ReactorSlagCore.HaloTex != null, "halo material for a hot core");
            }

            // 2. apocriton (boss) -> one large core
            if (!FindArea(out IntVec3 b)) { Finish(); return; }
            before = Cores;
            IntVec3 corpseB = SpawnAndKill("Mech_Apocriton", mechs, b, out _);
            apocritonCore = corpseB.IsValid ? CoreAt(map, corpseB) : null;
            Kit.Check(apocritonCore != null && apocritonCore.def == ReactorSlagDefOf.ReactorSlag_CoreLarge && Cores == before + 1,
                $"apocriton death spilled exactly one large core ({Cores - before} new)");
            if (apocritonCore != null)
                Kit.Check(Mathf.Approximately(apocritonCore.Glower.GlowRadius, 9f) && apocritonCore.Ext.poolRadius > 2f,
                    $"large core: radius 9, 5x5 pool (radius {apocritonCore.Glower.GlowRadius}, pool {apocritonCore.Ext.poolRadius})");

            // 3. negatives: a lancer and a pirate leave nothing
            if (!FindArea(out IntVec3 c)) { Finish(); return; }
            before = Cores;
            SpawnAndKill("Mech_Lancer", mechs, c, out _);
            Kit.Check(Cores == before, "lancer death spilled no core");
            before = Cores;
            Pawn pirate = Kit.SpawnHostile(map, c);
            pirate.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f));
            Kit.Check(Cores == before, "pirate death spilled no core");

            // 4. a player's centurion counts too (any faction); this one stays hot under the camera
            if (ModsConfig.BiotechActive)
            {
                if (!FindArea(out IntVec3 d)) { Finish(); return; }
                before = Cores;
                IntVec3 corpseD = SpawnAndKill("Mech_Centurion", Faction.OfPlayer, d, out _);
                centurionCore = corpseD.IsValid ? CoreAt(map, corpseD) : null;
                Kit.Check(centurionCore != null && centurionCore.def == ReactorSlagDefOf.ReactorSlag_Core && Cores == before + 1,
                    "player centurion death spilled a small core");
                if (centurionCore != null) Find.CameraDriver.JumpToCurrentMapLoc(centurionCore.Position);
            }

            // 5. a centipede dying on a built wooden floor (for the ground-scar negative)
            if (!FindArea(out IntVec3 f)) { Finish(); return; }
            foreach (IntVec3 x in GenRadial.RadialCellsAround(f, 1.5f, true)) map.terrainGrid.SetTerrain(x, TerrainDefOf.WoodPlankFloor);
            IntVec3 corpseF = SpawnAndKill("Mech_CentipedeGunner", mechs, f, out _);
            floorCore = corpseF.IsValid ? CoreAt(map, corpseF) : null;
            Kit.Check(floorCore != null, "centipede on a wooden floor spilled a core");

            // 6. negative: a mech dismantled in a mech bay (JobDriver_DisassembleMech) spills nothing
            if (ModsConfig.BiotechActive && FindArea(out IntVec3 g))
            {
                before = Cores;
                Pawn mech = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Mech_Centurion"), Faction.OfPlayer, forceGenerateNewPawn: true));
                GenSpawn.Spawn(mech, g, map);
                mech.forceNoDeathNotification = true;
                mech.Kill(null, null);
                mech.forceNoDeathNotification = false;
                mech.Corpse?.Destroy();
                Kit.Check(Cores == before, "a disassembled centurion spilled no core");
            }
        }

        void Later()
        {
            int now = Find.TickManager.TicksGame;
            bool odyssey = ModsConfig.OdysseyActive && TerrainDefOf.CooledLava != null;

            // 7. one day of cooling (of 3): light ~4.6 (6 * lerp(0.3, 1, 2/3)), redder, still registered
            if (centipedeCore != null && centipedeCore.Spawned)
            {
                float r0 = centipedeCore.Glower.GlowRadius;
                ColorInt c0 = centipedeCore.Glower.GlowColor;
                centipedeCore.spawnTick = now - GenDate.TicksPerDay;
                centipedeCore.TickRare();
                float r1 = centipedeCore.Glower.GlowRadius;
                ColorInt c1 = centipedeCore.Glower.GlowColor;
                Kit.Check(r1 >= 4.5f && r1 <= 4.8f && centipedeCore.Glower.Glows && InGrid(centipedeCore.Glower),
                    $"after 1 day the light shrank {r0:0.##} -> {r1:0.##} (expected ~4.6) and is still in the glow grid");
                Kit.Check((float)c1.g / c1.r < (float)c0.g / c0.r, $"after 1 day the light is redder ({c0} -> {c1})");
            }
            else Kit.Check(false, "centipede core still there after 640 ticks");

            // 8. "reload": like a loaded save, the glower is flagged on and has no radius override (it is not saved);
            //    respawning after load must register a radius-9 light: a cell 7 away gets light from it
            if (apocritonCore != null && apocritonCore.Spawned && GlowRadiusOverride != null && GlowOnInt != null)
            {
                IntVec3 pos = apocritonCore.Position;
                apocritonCore.DeSpawn();
                GlowRadiusOverride.SetValue(apocritonCore.Glower, null);
                GlowOnInt.SetValue(apocritonCore.Glower, true);
                float forgotten = apocritonCore.Glower.GlowRadius;
                GenSpawn.Spawn(apocritonCore, pos, map, Rot4.North, WipeMode.Vanish, respawningAfterLoad: true);
                map.glowGrid.GlowGridUpdate_First();
                IntVec3 probe = pos + new IntVec3(7, 0, 0);
                float glow = probe.InBounds(map) ? map.glowGrid.GroundGlowAt(probe, ignoreSky: true) : 0f;
                Kit.Check(!Mathf.Approximately(forgotten, 9f) && Mathf.Approximately(apocritonCore.Glower.GlowRadius, 9f)
                          && InGrid(apocritonCore.Glower) && glow > 0f,
                    $"respawn after load registered the radius-9 light ({forgotten} -> {apocritonCore.Glower.GlowRadius}, glow 7 cells away {glow:0.###})");
            }
            else Kit.Check(false, "apocriton core and CompGlower private fields available");

            // 9. small core cools: gone, light out of the grid, scar on every cell that takes filth, cooled lava at the centre only
            if (centipedeCore != null && centipedeCore.Spawned)
            {
                ReactorSlagCore core = centipedeCore;
                IntVec3 center = core.Position;
                bool eligible = ReactorSlagCore.CanScarGround(center, map);
                int expected = ScarableAround(center, 1.5f);
                core.spawnTick = now - ReactorSlagCore.CoolingTicks - 1;
                core.TickRare();
                int scars = ScarsAround(center, 1.5f);
                Kit.Check(core.Destroyed && !core.Glower.Glows && !InGrid(core.Glower), "cold small core vanished and its light left the glow grid");
                Kit.Check(expected == 9 && scars == expected, $"scar filth on {scars}/{expected} pool cells (9 expected)");
                if (odyssey)
                    Kit.Check(eligible && center.GetTerrain(map) == TerrainDefOf.CooledLava, $"cooled lava at the centre (eligible {eligible}, now {center.GetTerrain(map)})");
                Kit.Check(GenAdj.CardinalDirections.All(dir => (center + dir).GetTerrain(map) != TerrainDefOf.CooledLava),
                    "small core scarred only its centre cell");
            }

            // 10. large core cools: scar on all 21 cells, cooled lava on the centre and its 4 cardinal neighbours
            if (apocritonCore != null && apocritonCore.Spawned)
            {
                ReactorSlagCore core = apocritonCore;
                IntVec3 center = core.Position;
                List<IntVec3> cells = ReactorSlagCore.GroundScarCells(center, true).ToList();
                List<bool> ok = cells.Select(x => ReactorSlagCore.CanScarGround(x, map)).ToList();
                int eligible = ok.Count(b => b);
                string blockers = string.Join(",", cells.Where((x, i) => !ok[i]).Select(x => x.GetPlant(map)?.def.defName ?? x.GetTerrain(map).defName));
                int expected = ScarableAround(center, 2.5f);
                core.spawnTick = now - ReactorSlagCore.CoolingTicks - 1;
                core.TickRare();
                int scars = ScarsAround(center, 2.5f);
                Kit.Check(core.Destroyed && expected == 21 && scars == expected, $"cold large core vanished, scar filth on {scars}/{expected} cells (21 expected)");
                if (odyssey)
                    Kit.Check(cells.Count == 5 && eligible >= 3 && cells.Select((x, i) => (x.GetTerrain(map) == TerrainDefOf.CooledLava) == ok[i]).All(b => b),
                        $"cooled lava on exactly the {eligible}/5 eligible cross cells (lava on {cells.Count(x => x.GetTerrain(map) == TerrainDefOf.CooledLava)}; spared: {blockers})");
            }

            // 11. negative: a built floor is never turned into lava; the scar crust still lands on it
            if (floorCore != null && floorCore.Spawned)
            {
                IntVec3 center = floorCore.Position;
                Kit.Check(!ReactorSlagCore.CanScarGround(center, map), "a wooden floor cell is not eligible for the ground scar");
                floorCore.spawnTick = now - ReactorSlagCore.CoolingTicks - 1;
                floorCore.TickRare();
                Kit.Check(floorCore.Destroyed && center.GetTerrain(map) == TerrainDefOf.WoodPlankFloor,
                    $"cooled on a wooden floor: floor kept ({center.GetTerrain(map)})");
                Kit.Check(center.GetThingList(map).Any(t => t.def == ReactorSlagDefOf.ReactorSlag_Scar), "scar crust on the wooden floor");
            }
            else Kit.Check(false, "floor core still there");

            // 12. negatives for the cross: a tree east and a growing zone north are spared; west and south still scar
            if (odyssey && FindArea(out IntVec3 h))
            {
                IntVec3 east = h + IntVec3.East, north = h + IntVec3.North;
                ThingDef treeDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TreeOak");
                east.GetPlant(map)?.Destroy();
                Thing tree = treeDef != null ? GenSpawn.Spawn(treeDef, east, map) : null;
                bool treeEligible = ReactorSlagCore.CanScarGround(east, map);
                var zone = new Zone_Growing(map.zoneManager);
                map.zoneManager.RegisterZone(zone);
                zone.AddCell(north);
                ReactorSlagCore core = CompReactorSlag.SpillAt(ReactorSlagDefOf.ReactorSlag_CoreLarge, h, map);
                core.spawnTick = now - ReactorSlagCore.CoolingTicks - 1;
                core.TickRare();
                Kit.Check(tree != null && !tree.Destroyed && east.GetTerrain(map) != TerrainDefOf.CooledLava, $"a tree in the cross is spared (tree {tree?.def.defName}, destroyed {tree?.Destroyed}, eligible {treeEligible}, terrain {east.GetTerrain(map)})");
                Kit.Check(north.GetTerrain(map) != TerrainDefOf.CooledLava, "a growing-zone cell in the cross gets no lava");
                Kit.Check(h.GetTerrain(map) == TerrainDefOf.CooledLava && (h + IntVec3.West).GetTerrain(map) == TerrainDefOf.CooledLava
                          && (h + IntVec3.South).GetTerrain(map) == TerrainDefOf.CooledLava, "the rest of the cross is cooled lava");
            }

            // 13. negative: with the groundScar setting off, a cooled pool leaves the ground alone (but still the crust)
            if (FindArea(out IntVec3 k))
            {
                TerrainDef was = k.GetTerrain(map);
                bool setting = ReactorSlagMod.Settings.groundScar;
                ReactorSlagMod.Settings.groundScar = false;
                try
                {
                    ReactorSlagCore core = CompReactorSlag.SpillAt(ReactorSlagDefOf.ReactorSlag_Core, k, map);
                    core.spawnTick = now - ReactorSlagCore.CoolingTicks - 1;
                    core.TickRare();
                    Kit.Check(core.Destroyed && k.GetTerrain(map) == was && ScarsAround(k, 1.5f) == 9,
                        $"groundScar off: terrain kept ({was} -> {k.GetTerrain(map)}), crust laid");
                }
                finally { ReactorSlagMod.Settings.groundScar = setting; }
            }

            // 14. the core left hot has been ticking and drawn under the camera for ~640 ticks
            if (centurionCore != null)
            {
                Kit.Check(centurionCore.Spawned && centurionCore.Glower.Glows && InGrid(centurionCore.Glower) && centurionCore.Heat > 0.9f,
                    $"untouched core still hot and lit after {now - centurionCore.spawnTick} ticks (heat {centurionCore.Heat:0.###})");
                Kit.Check(Find.CameraDriver.CurrentViewRect.Contains(centurionCore.Position), "the hot core is on screen (Print and DrawAt ran)");
            }

            Finish();
        }

        void Finish()
        {
            done = true;
            Kit.Summary("Reactor Slag");
        }
    }
}
