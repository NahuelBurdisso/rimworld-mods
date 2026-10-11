using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gesta
{
    /// <summary>
    /// The shadow of someone killed by a blast or fire, burned into the floor. Drawn into the map
    /// mesh from a PNG snapshot of the pawn taken just before death. See DESIGN.md "Feature 4".
    /// </summary>
    public class AshSilhouette : Thing
    {
        public const float SizeCells = 2.6f;
        public const int LifetimeTicks = GenDate.TicksPerYear;

        public byte[] png;
        public string pawnName;
        public bool byFire;
        public int createdTick;
        public int deathTicksAbs;
        public TerrainDef terrainAtDeath;

        // built lazily on the main thread (saves load asynchronously); owned by this mark, never pooled,
        // and destroyed with it - MaterialPool never forgets a texture, which would leak one per mark per load
        Material material;
        Texture2D texture;

        public Material Material
        {
            get
            {
                if (material == null && png != null)
                {
                    texture = new Texture2D(2, 2, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp };
                    if (texture.LoadImage(png, markNonReadable: true))
                    {
                        material = new Material(ShaderDatabase.Transparent);
                        material.mainTexture = texture;
                    }
                    else
                    {
                        Object.Destroy(texture);
                        texture = null;
                    }
                }
                return material;
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            base.DeSpawn(mode);
            if (material != null) Object.Destroy(material);
            if (texture != null) Object.Destroy(texture);
            material = null;
            texture = null;
        }

        public override void Print(SectionLayer layer)
        {
            Material mat = Material;
            if (mat == null) return;
            Vector3 c = Position.ToVector3Shifted();
            c.y = def.Altitude;
            Printer_Plane.PrintPlane(layer, c, new Vector2(SizeCells, SizeCells), mat);
        }

        public override void TickLong()
        {
            if (Destroyed || !Spawned) return;
            Building edifice = Position.GetEdifice(Map);
            if (Find.TickManager.TicksGame - createdTick > LifetimeTicks || Position.GetTerrain(Map) != terrainAtDeath
                || (edifice != null && edifice.def.passability == Traversability.Impassable))
                Destroy();
        }

        public override string GetInspectString()
        {
            string date = Spawned ? GenDate.DateFullStringAt(deathTicksAbs, Find.WorldGrid.LongLatOf(Map.Tile)) : "";
            return (byFire ? "Gesta_Ash_InspectFire" : "Gesta_Ash_InspectBlast").Translate(pawnName ?? "?", date);
        }

        public override string LabelNoCount => pawnName == null ? base.LabelNoCount : $"{base.LabelNoCount} ({pawnName})";

        public override void ExposeData()
        {
            base.ExposeData();
            DataExposeUtility.LookByteArray(ref png, "png");
            Scribe_Values.Look(ref pawnName, "pawnName");
            Scribe_Values.Look(ref byFire, "byFire");
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref deathTicksAbs, "deathTicksAbs");
            Scribe_Defs.Look(ref terrainAtDeath, "terrainAtDeath");
        }
    }
}
