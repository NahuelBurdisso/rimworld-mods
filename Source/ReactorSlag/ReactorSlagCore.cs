using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ReactorSlag
{
    /// <summary>
    /// The spilled core of a dead heavy mech: a pool of slag pieces printed into the map mesh, a vanilla
    /// CompGlower whose radius and colour follow the heat, a realtime halo, smoke and sparks. Cools over
    /// <see cref="ReactorSlagSettings.coolingDays"/>; when cold it leaves scar filth (and, with Odyssey, cooled lava
    /// on natural ground) and vanishes. Ethereal: never hit, never blocks, never selected.
    /// </summary>
    public class ReactorSlagCore : ThingWithComps
    {
        /// <summary>Light re-registrations over the whole life (each one dirties the glow grid in the light's rect).</summary>
        public const int LightSteps = 24;
        const int HaloSteps = 32;

        /// <summary>Not spilled yet (spawnTick can be negative: early cores, tests that age a core).</summary>
        public const int NotSet = int.MinValue;

        public int spawnTick = NotSet;
        int appliedStep = -1;

        static readonly Material[] haloMats = new Material[HaloSteps + 1];
        static Texture2D haloTex;

        ReactorSlagExtension ext;
        public ReactorSlagExtension Ext => ext ??= def.GetModExtension<ReactorSlagExtension>() ?? new ReactorSlagExtension();

        CompGlower glower;
        public CompGlower Glower => glower ??= GetComp<CompGlower>();

        public static int CoolingTicks
        {
            get
            {
                float days = ReactorSlagMod.Settings?.coolingDays ?? ReactorSlagSettings.DefaultCoolingDays;
                return Mathf.Max(GenTicks.TickRareInterval, Mathf.RoundToInt(days * GenDate.TicksPerDay));
            }
        }

        /// <summary>1 = white-hot (just spilled), 0 = cold.</summary>
        public float Heat
        {
            get
            {
                if (spawnTick == NotSet) return 1f;
                return Mathf.Clamp01(1f - (float)(Find.TickManager.TicksGame - spawnTick) / CoolingTicks);
            }
        }

        /// <summary>Hot orange, then red, then a dull dark red.</summary>
        public static Color HeatColor(float h)
        {
            Color hot = new Color(1f, 0.59f, 0.24f), mid = new Color(0.86f, 0.27f, 0.10f), cold = new Color(0.43f, 0.10f, 0.04f);
            return h >= 0.5f ? Color.Lerp(mid, hot, (h - 0.5f) * 2f) : Color.Lerp(cold, mid, h * 2f);
        }

        public float RadiusForStep(int step) => Ext.glowRadius * Mathf.Lerp(0.3f, 1f, (float)step / LightSteps);

        public static IEnumerable<IntVec3> PoolCells(IntVec3 center, Map map, float radius)
        {
            foreach (IntVec3 c in GenRadial.RadialCellsAround(center, radius, true))
                if (c.InBounds(map)) yield return c;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref spawnTick, "spawnTick", NotSet);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            if (spawnTick == NotSet) spawnTick = Find.TickManager.TicksGame;
            // CompGlower does not save its radius (Verse/CompGlower.cs:173): set radius and colour before
            // base.SpawnSetup, whose CompGlower.PostSpawnSetup registers the light with them.
            appliedStep = -1;
            ApplyHeat(force: true);
            base.SpawnSetup(map, respawningAfterLoad);
        }

        /// <summary>Moves the light to the current heat step. Re-registers it only when the step changes.</summary>
        public void ApplyHeat(bool force = false)
        {
            CompGlower g = Glower;
            if (g == null) return;
            int step = Mathf.CeilToInt(Heat * LightSteps);
            if (!force && step == appliedStep) return;
            appliedStep = step;
            Color c = HeatColor((float)step / LightSteps);
            // radius first: the GlowColor setter de-registers and re-registers a lit glower (CompGlower.cs:357),
            // and the glow grid takes radius and colour at registration (GlowGrid.cs:283)
            g.GlowRadius = RadiusForStep(step);
            g.GlowColor = new ColorInt(Mathf.RoundToInt(c.r * 255f), Mathf.RoundToInt(c.g * 255f), Mathf.RoundToInt(c.b * 255f), 0);
        }

        public override void TickRare()
        {
            base.TickRare();
            if (!Spawned) return;
            float h = Heat;
            if (h <= 0f)
            {
                Solidify();
                return;
            }
            ApplyHeat();
            Map map = Map;
            if (h > 0.35f && Rand.Chance(0.5f * h))
            {
                IntVec3 c = RandomPoolCell();
                if (c.InBounds(map) && !c.Fogged(map) && c.ShouldSpawnMotesAt(map))
                    FleckMaker.ThrowSmoke(c.ToVector3Shifted(), map, Rand.Range(0.5f, 0.9f) * (Ext.poolRadius > 2f ? 1.3f : 1f));
            }
            if (h > 0.7f && Rand.Chance(0.25f))
            {
                IntVec3 c = RandomPoolCell();
                if (c.InBounds(map) && !c.Fogged(map) && c.ShouldSpawnMotesAt(map))
                    FleckMaker.ThrowMicroSparks(c.ToVector3Shifted(), map);
            }
        }

        /// <summary>A random cell of the pool disc (GenRadial is ordered by distance: the first N cells are the disc).</summary>
        IntVec3 RandomPoolCell()
        {
            int n = GenRadial.NumCellsInRadius(Ext.poolRadius);
            return Position + GenRadial.RadialPattern[Rand.Range(0, n)];
        }

        /// <summary>Cold: scar filth over the pool, cooled lava at the centre (Odyssey), then the core vanishes.</summary>
        public void Solidify()
        {
            if (!Spawned) return;
            Map map = Map;
            IntVec3 center = Position;
            foreach (IntVec3 c in PoolCells(center, map, Ext.poolRadius))
                FilthMaker.TryMakeFilth(c, map, ReactorSlagDefOf.ReactorSlag_Scar, 1, FilthSourceFlags.None, shouldPropagate: false);
            if ((ReactorSlagMod.Settings?.groundScar ?? true) && ModsConfig.OdysseyActive && TerrainDefOf.CooledLava != null)
            {
                foreach (IntVec3 c in GroundScarCells(center, Ext.wideGroundScar))
                    if (CanScarGround(c, map)) map.terrainGrid.SetTerrain(c, TerrainDefOf.CooledLava);
            }
            Destroy(DestroyMode.Vanish);
        }

        public static IEnumerable<IntVec3> GroundScarCells(IntVec3 center, bool wide)
        {
            yield return center;
            if (!wide) yield break;
            for (int i = 0; i < 4; i++) yield return center + GenAdj.CardinalDirections[i];
        }

        /// <summary>Natural, dry, unbuilt ground outside growing zones only: never a floor, a farm or water.</summary>
        public static bool CanScarGround(IntVec3 c, Map map)
        {
            if (!c.InBounds(map)) return false;
            TerrainGrid grid = map.terrainGrid;
            TerrainDef t = grid.TerrainAt(c);
            if (t == null || !t.natural || t.IsWater || t.IsIce || t.IsFlood || t.passability == Traversability.Impassable) return false;
            if (t == TerrainDefOf.CooledLava) return false;
            if (grid.UnderTerrainAt(c) != null || grid.FoundationAt(c) != null || grid.TempTerrainAt(c) != null) return false;
            if (c.GetEdifice(map) != null) return false;
            if (map.zoneManager.ZoneAt(c) is Zone_Growing) return false;
            // cooled lava has fertility 0 and SetTerrain destroys plants that need more (TerrainGrid.DoTerrainChangedEffects):
            // grass may burn, trees (Gauranlen, anima included) and harvestable plants never
            List<Thing> things = c.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
                if (things[i] is Plant plant && (plant.def.plant.IsTree || plant.def.plant.harvestedThingDef != null)) return false;
            return true;
        }

        // ---- drawing ----

        /// <summary>
        /// A dark scorch disc and slag pieces over the pool, printed once into the map mesh; seeded so a reload looks
        /// the same.
        /// </summary>
        public override void Print(SectionLayer layer)
        {
            ReactorSlagExtension e = Ext;
            Vector3 center = Position.ToVector3Shifted();
            center.y = def.Altitude;
            float d = e.poolRadius * 2f + 1f;
            Printer_Plane.PrintPlane(layer, center, new Vector2(d, d), ScorchMat, (thingIDNumber % 360));
            Graphic g = Graphic;
            if (g == null) return;
            float big = e.poolRadius > 2f ? 1.15f : 1f;
            Rand.PushState(thingIDNumber ^ 0x5A6);
            try
            {
                for (int i = 0; i < e.pieces; i++)
                {
                    // uniform over the disc: the centre is under the wreck anyway
                    Vector2 off = Rand.InsideUnitCircle * (e.poolRadius + 0.1f);
                    Vector3 pos = center + new Vector3(off.x, 0f, off.y);
                    pos.y += 0.001f + i * 0.0002f;
                    float s = Rand.Range(1.2f, 1.8f) * big;
                    Printer_Plane.PrintPlane(layer, pos, new Vector2(s, s), g.MatSingle, Rand.Range(0f, 360f), Rand.Bool);
                }
            }
            finally
            {
                Rand.PopState();
            }
        }

        static Material scorchMat;

        /// <summary>A soft dark-brown disc (alpha 0.6 at the centre, 0 at the ragged rim), made once on the main thread.</summary>
        public static Material ScorchMat
        {
            get
            {
                if (scorchMat != null) return scorchMat;
                const int N = 64;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "ReactorSlagScorch", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        // a little ragged at the rim
                        float rim = 0.82f + 0.12f * Mathf.Sin(Mathf.Atan2(dy, dx) * 5f) * Mathf.Sin(Mathf.Atan2(dy, dx) * 3f + 1f);
                        // Unity's SmoothStep(from, to, t) interpolates from..to: feed it a 0..1 t
                        float a = dist >= rim ? 0f : 0.6f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, rim, dist)));
                        px[y * N + x] = new Color(0.16f, 0.12f, 0.10f, Mathf.Max(0f, a));
                    }
                tex.SetPixels(px);
                tex.Apply(false, true);
                scorchMat = MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, Color.white);
                return scorchMat;
            }
        }

        /// <summary>The halo: drawn every frame, so the pool reads as molten by day too.</summary>
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (ReactorSlagMod.Settings != null && !ReactorSlagMod.Settings.drawHalo) return;
            float h = Heat;
            Material mat = HaloMaterial(h);
            if (mat == null) return;
            Vector3 pos = Position.ToVector3Shifted();
            // under items, corpses and pawns (the wreck is not washed out), over the slag; the glower lights the cells at night
            pos.y = AltitudeLayer.MoteLow.AltitudeFor();
            // molten metal breathes: +-5%, out of phase between pools
            float t = Time.realtimeSinceStartup * 1.3f + (thingIDNumber % 89) * 0.41f;
            float s = Ext.haloSize * Mathf.Lerp(0.6f, 1f, h) * (1f + 0.05f * Mathf.Sin(t) * Mathf.Sin(t * 0.71f + 0.6f));
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(s, 1f, s)), mat, 0);
        }

        /// <summary>Cached halo material for a heat (null when there is nothing to draw). Exposed for the test.</summary>
        public static Material HaloMaterial(float heat)
        {
            int step = Mathf.Clamp(Mathf.RoundToInt(heat * HaloSteps), 0, HaloSteps);
            if (step == 0) return null;
            if (haloMats[step] == null)
            {
                float h = (float)step / HaloSteps;
                Color c = HeatColor(h) * (0.95f * Mathf.Pow(h, 0.7f));
                c.a = 1f;
                haloMats[step] = MaterialPool.MatFrom(HaloTex, ShaderDatabase.MoteGlow, c);
            }
            return haloMats[step];
        }

        /// <summary>A soft radial blob, brightest in the middle, made once on the main thread.</summary>
        public static Texture2D HaloTex
        {
            get
            {
                if (haloTex != null) return haloTex;
                const int N = 128;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "ReactorSlagHalo", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float v = d >= 1f ? 0f : Mathf.Pow(1f - Mathf.SmoothStep(0f, 1f, d), 1.6f);
                        px[y * N + x] = new Color(v, v, v, v);
                    }
                tex.SetPixels(px);
                tex.Apply(false, true);
                haloTex = tex;
                return haloTex;
            }
        }
    }
}
