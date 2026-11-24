using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gesta
{
    /// <summary>Takes the snapshot in the Pawn.Kill prefix and spawns the silhouette.</summary>
    public static class AshMaker
    {
        public const int MaxPerTick = 3, MaxPerMap = 40, TexSize = 128;
        static readonly Color32 Soot = new Color32(22, 18, 16, 255);
        const float AlphaScale = 0.78f;

        static int lastTick = -1, madeThisTick;
        static HashSet<DamageDef> blastDefs;

        static bool IsBlastOrFire(DamageDef def)
        {
            if (def == null) return false;
            if (blastDefs == null)
            {
                blastDefs = new HashSet<DamageDef>();
                foreach (string name in new[] { "Bomb", "BombSuper", "Flame", "Burn", "Vaporize" })
                {
                    DamageDef d = DefDatabase<DamageDef>.GetNamedSilentFail(name);
                    if (d != null) blastDefs.Add(d);
                }
            }
            return def.isExplosive || blastDefs.Contains(def);
        }

        /// <summary>Called from the Kill prefix: the pawn is alive, spawned and drawable.</summary>
        public static void TryMake(Pawn pawn, DamageInfo? dinfo)
        {
            if (!GestaMod.Settings.ashSilhouettes || !pawn.Spawned || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return;
            bool burning = pawn.IsBurning();
            DamageDef def = dinfo?.Def;
            if (!burning && !IsBlastOrFire(def)) return;

            int now = Find.TickManager.TicksGame;
            if (now != lastTick) { lastTick = now; madeThisTick = 0; }
            if (madeThisTick >= MaxPerTick) return;
            Map map = pawn.Map;
            byte[] png = Snapshot(pawn);
            if (png == null) return;
            madeThisTick++;

            var mark = (AshSilhouette)ThingMaker.MakeThing(GestaDefOf.Gesta_AshSilhouette);
            mark.png = png;
            mark.pawnName = pawn.LabelShort;
            mark.byFire = burning || def == DamageDefOf.Flame || def == DamageDefOf.Burn;
            mark.createdTick = now;
            mark.deathTicksAbs = GenTicks.TicksAbs;
            mark.terrainAtDeath = pawn.Position.GetTerrain(map);
            // one silhouette per cell: a new death replaces an older mark; at the cap the oldest fades
            foreach (Thing old in pawn.Position.GetThingList(map).ToArray())
                if (old is AshSilhouette) old.Destroy();
            List<Thing> all = map.listerThings.ThingsOfDef(GestaDefOf.Gesta_AshSilhouette);
            if (all.Count >= MaxPerMap)
                all.OfType<AshSilhouette>().OrderBy(a => a.createdTick).First().Destroy();
            GenSpawn.Spawn(mark, pawn.Position, map);
        }

        /// <summary>Vanilla's pawn renderer into a render texture, read back and turned to soot.</summary>
        /// <summary>For tests: visible pixels in the last snapshot.</summary>
        public static int LastVisiblePixels;

        public static byte[] Snapshot(Pawn pawn)
        {
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                rt = RenderTexture.GetTemporary(TexSize, TexSize, 24, RenderTextureFormat.ARGB32);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = prev;

                // camera zoom z shows 2/z cells; 2/0.77 ~= 2.6 cells, the size the mark is printed at
                Find.PawnCacheRenderer.RenderPawn(pawn, rt, Vector3.zero, 2f / AshSilhouette.SizeCells, 0f, Rot4.South,
                    renderHead: pawn.health.hediffSet.HasHead, renderHeadgear: true, renderClothes: true, portrait: true);
                tex = rt.CreateTexture2D();
                Color32[] px = tex.GetPixels32();
                int visible = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    byte a = (byte)(px[i].a * AlphaScale);
                    px[i] = new Color32(Soot.r, Soot.g, Soot.b, a);
                    if (a > 20) visible++;
                }
                LastVisiblePixels = visible;
                if (visible < 40) return null;   // nothing was drawn
                tex.SetPixels32(px);
                tex.Apply();
                return tex.EncodeToPNG();
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[Gesta] could not take an ash silhouette snapshot: " + ex.Message, 0x6E57A1);
                return null;
            }
            finally
            {
                if (tex != null) Object.Destroy(tex);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
