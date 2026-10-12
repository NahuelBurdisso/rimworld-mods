using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Aftermath
{
    /// <summary>
    /// Materials of the real weapon and helmet graphics, from unspawned things made once per (def, stuff).
    /// Print runs on the main thread, so these are built lazily there.
    /// </summary>
    public static class FoeGraphics
    {
        struct Entry { public Material mat; public Vector2 size; }

        static readonly Dictionary<(ThingDef, ThingDef), Entry> weapons = new Dictionary<(ThingDef, ThingDef), Entry>();
        static readonly Dictionary<(ThingDef, ThingDef), Entry> helmets = new Dictionary<(ThingDef, ThingDef), Entry>();

        static Thing Make(ThingDef def, ThingDef stuff)
        {
            if (def.MadeFromStuff) { if (stuff == null || !stuff.IsStuff) stuff = GenStuff.DefaultStuffFor(def); }
            else stuff = null;
            return ThingMaker.MakeThing(def, stuff);
        }

        public static bool Weapon(ThingDef def, ThingDef stuff, out Material mat, out Vector2 size)
        {
            mat = null; size = Vector2.one;
            if (def == null || def.graphicData == null) return false;
            if (!weapons.TryGetValue((def, stuff), out Entry e))
            {
                try
                {
                    Thing t = Make(def, stuff);
                    Graphic g = t.Graphic;
                    // same as PawnRenderUtility.DrawEquipmentAiming
                    e.mat = g is Graphic_StackCount sc ? sc.SubGraphicForStackCount(1, def).MatSingleFor(t) : g.MatSingleFor(t);
                    e.size = g.drawSize;
                }
                catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] weapon graphic for " + def + " failed: " + ex.Message, def.shortHash ^ 0x0AF7E10); }
                weapons[(def, stuff)] = e;
            }
            mat = e.mat; size = e.size;
            return mat != null;
        }

        public static bool Helmet(ThingDef def, ThingDef stuff, out Material mat)
        {
            mat = null;
            if (def?.apparel == null) return false;
            if (!helmets.TryGetValue((def, stuff), out Entry e))
            {
                try
                {
                    if (Make(def, stuff) is Apparel a
                        && ApparelGraphicRecordGetter.TryGetGraphicApparel(a, BodyTypeDefOf.Male, false, out ApparelGraphicRecord rec)
                        && rec.graphic != null)
                        e.mat = rec.graphic.MatAt(Rot4.South);
                    else if (def.graphicData != null)
                        e.mat = Make(def, stuff).Graphic.MatSingle;   // no worn graphic: the item graphic
                }
                catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] helmet graphic for " + def + " failed: " + ex.Message, def.shortHash ^ 0x0AF7E11); }
                helmets[(def, stuff)] = e;
            }
            mat = e.mat;
            return mat != null;
        }
    }

    /// <summary>
    /// Injected into every Building_Grave def (CompInjector). When the grave holds a worthy foe, prints his
    /// weapon stood upright with his helmet on top, and tells his story in the inspect pane.
    /// </summary>
    public class CompHonoredFoe : ThingComp
    {
        public const float WeaponScale = 0.85f, HelmetSize = 0.95f;

        public FoeRecord Record()
        {
            if (!AftermathMod.Settings.honoredFoes) return null;
            Pawn dead = (parent as Building_Grave)?.Corpse?.InnerPawn;
            if (dead == null) return null;
            FoeRecord rec = FoeTracker.Instance?.RecordOf(dead.thingIDNumber);
            if (rec != null) rec.buried = true;
            return rec;
        }

        /// <summary>What the marker would print: weapon and/or helmet materials. False for ordinary dead.</summary>
        public bool Marker(out Material weapon, out Material helmet)
        {
            weapon = helmet = null;
            FoeRecord rec = Record();
            if (rec == null) return false;
            FoeGraphics.Weapon(rec.weaponDef, rec.weaponStuff, out weapon, out _);
            FoeGraphics.Helmet(rec.helmetDef, rec.helmetStuff, out helmet);
            return weapon != null || helmet != null;
        }

        public override void PostPrintOnto(SectionLayer layer)
        {
            FoeRecord rec = Record();
            if (rec == null) return;
            Vector3 head = parent.Position.ToVector3Shifted();
            head.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            float top = head.z;
            if (FoeGraphics.Weapon(rec.weaponDef, rec.weaponStuff, out Material wMat, out Vector2 wSize))
            {
                Vector2 size = wSize * WeaponScale;
                // rotated 90 + equippedAngleOffset: the angle vanilla uses to aim it south, i.e. muzzle down
                Printer_Plane.PrintPlane(layer, head, size, wMat, 90f + rec.weaponDef.equippedAngleOffset);
                top = head.z + size.x * 0.34f;
            }
            if (FoeGraphics.Helmet(rec.helmetDef, rec.helmetStuff, out Material hMat))
            {
                var at = new Vector3(head.x, head.y + Altitudes.AltInc * 0.1f, top);
                Printer_Plane.PrintPlane(layer, at, new Vector2(HelmetSize, HelmetSize), hMat);
            }
        }

        public override string CompInspectStringExtra()
        {
            FoeRecord rec = Record();
            if (rec == null) return null;
            var sb = new StringBuilder();
            if (rec.factionName.NullOrEmpty() || rec.factionName == "?")
                sb.Append("Aftermath_FoeHereLiesNoFaction".Translate(rec.name));
            else
                sb.Append("Aftermath_FoeHereLies".Translate(rec.name, rec.factionName));
            var downed = new List<string>();
            var killed = new List<string>();
            foreach (FoeVictim v in rec.victims) (v.killed ? killed : downed).Add(v.name);
            sb.AppendLine();
            if (downed.Count > 0 && killed.Count > 0)
                sb.Append("Aftermath_FoeDeedsBoth".Translate(AftermathUtil.Join(downed), AftermathUtil.Join(killed)));
            else if (killed.Count > 0)
                sb.Append("Aftermath_FoeDeedsKilled".Translate(AftermathUtil.Join(killed)));
            else
                sb.Append("Aftermath_FoeDeedsDowned".Translate(AftermathUtil.Join(downed)));
            sb.AppendLine();
            sb.Append("Aftermath_FoeFell".Translate(rec.killerName, AftermathUtil.Date(rec.deathTick, rec.longLat)));
            return sb.ToString();
        }
    }
}
