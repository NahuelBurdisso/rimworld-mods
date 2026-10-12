using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Aftermath
{
    /// <summary>
    /// What is left of a named turret: its own base and gun graphics, charred and knocked askew. Keeps the
    /// name and the tally; a gray name label shows on the map at close zoom. An item (no hit points, no
    /// deterioration), so the auto-rebuild frame does not wipe it; haulable to a memorial spot.
    /// </summary>
    public class SentinelRemains : ThingWithComps
    {
        public static readonly Color CharColor = new Color(0.46f, 0.43f, 0.39f);
        public static readonly Color LabelColor = new Color(0.78f, 0.76f, 0.72f);
        public const float WreckScale = 0.9f;

        public ThingDef turretDef;
        public string sentinelName;
        public int takedowns, battles;
        public int firstTick, fallTick;
        public Vector2 longLat;
        public string destroyedBy;

        public void Init(ThingDef turret, SentinelRecord r, string by, Map map)
        {
            turretDef = turret;
            sentinelName = r.name;
            takedowns = r.takedowns;
            battles = r.battles;
            firstTick = r.firstTick;
            fallTick = Find.TickManager.TicksGame;
            longLat = AftermathUtil.LongLat(map);
            destroyedBy = by;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref turretDef, "turretDef");
            Scribe_Values.Look(ref sentinelName, "sentinelName");
            Scribe_Values.Look(ref takedowns, "takedowns");
            Scribe_Values.Look(ref battles, "battles");
            Scribe_Values.Look(ref firstTick, "firstTick");
            Scribe_Values.Look(ref fallTick, "fallTick");
            Scribe_Values.Look(ref longLat, "longLat");
            Scribe_Values.Look(ref destroyedBy, "destroyedBy");
        }

        public override string LabelNoCount =>
            sentinelName.NullOrEmpty() ? base.LabelNoCount : "Aftermath_RemainsLabel".Translate(sentinelName).ToString();

        // ------------------------------------------------------------------ drawing

        /// <summary>Charred materials of the turret's base and gun; null when the turret has no usable graphic.</summary>
        public bool WreckMaterials(out Material baseMat, out Material topMat)
        {
            baseMat = topMat = null;
            if (turretDef?.graphicData == null) return false;
            try
            {
                if (turretDef.graphic?.MatSingle?.mainTexture is Texture2D bt && bt != BaseContent.BadTex)
                    baseMat = MaterialPool.MatFrom(bt, ShaderDatabase.Cutout, CharColor);
                if (turretDef.building?.turretTopMat?.mainTexture is Texture2D tt && tt != BaseContent.BadTex)
                    topMat = MaterialPool.MatFrom(tt, ShaderDatabase.Cutout, CharColor);
            }
            catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] remains graphic for " + turretDef + " failed: " + ex.Message, turretDef.shortHash ^ 0x0AF7E21); }
            return baseMat != null;
        }

        public override void Print(SectionLayer layer)
        {
            if (!WreckMaterials(out Material baseMat, out Material topMat))
            {
                base.Print(layer);
                return;
            }
            // the slag of the def's own graphic first: a heap of scrap under the charred frame
            base.Print(layer);
            Vector3 c = DrawPos;
            c.y += Altitudes.AltInc * 0.4f;
            Vector2 size = turretDef.graphicData.drawSize * WreckScale;
            Printer_Plane.PrintPlane(layer, c, size, baseMat, (thingIDNumber % 4) * 90f);
            if (topMat == null) return;
            float top = turretDef.building.turretTopDrawSize * WreckScale;
            var at = new Vector3(c.x + 0.12f, c.y + Altitudes.AltInc * 0.6f, c.z - 0.08f);
            Printer_Plane.PrintPlane(layer, at, new Vector2(top, top), topMat, 135f + thingIDNumber % 50);
        }

        public override void DrawGUIOverlay()
        {
            if (sentinelName.NullOrEmpty() || Find.CameraDriver.CurrentZoom > CameraZoomRange.Close) return;
            GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(this, -0.4f), sentinelName, LabelColor);
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            void Line(string s) { if (sb.Length > 0) sb.AppendLine(); sb.Append(s); }
            if (sentinelName.NullOrEmpty()) return sb.ToString();
            Line("Aftermath_RemainsWhat".Translate(sentinelName, turretDef?.label ?? "turret"));
            Line("Aftermath_RemainsTally".Translate(takedowns, battles));
            string date = AftermathUtil.Date(fallTick, longLat);
            Line(destroyedBy.NullOrEmpty() ? "Aftermath_RemainsFell".Translate(date) : "Aftermath_RemainsFellTo".Translate(date, destroyedBy));
            return sb.ToString();
        }
    }

    /// <summary>"Lost Old Bess": a memory that carries the turret's name in its label and description.</summary>
    public class Thought_SentinelFell : Thought_Memory
    {
        public string sentinelName;

        string Name => sentinelName.NullOrEmpty() ? "?" : sentinelName;

        public override string LabelCap => ((string)CurStage.label.Formatted(Name.Named("SENTINEL"))).CapitalizeFirst();

        public override string Description => CurStage.description.Formatted(Name.Named("SENTINEL"));

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sentinelName, "sentinelName");
        }
    }
}
