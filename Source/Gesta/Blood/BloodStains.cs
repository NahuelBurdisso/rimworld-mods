using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gesta
{
    /// <summary>
    /// Blood-Stained: a visible hediff whose render node draws blood over body and clothes, heavier
    /// the more a pawn bled or made others bleed up close; it washes off with time and sleep.
    /// See DESIGN.md "Feature 6".
    /// </summary>
    public static class BloodStains
    {
        public const float PerDamageVictim = 0.012f;
        public const float PerDamageAttacker = 0.009f;

        static bool RedBlooded(Pawn p) => p?.RaceProps?.BloodDef != null && p.RaceProps.BloodDef == ThingDefOf.Filth_Blood;

        public static bool CanStain(Pawn p) =>
            p != null && !p.Dead && p.RaceProps != null && p.RaceProps.Humanlike && p.health?.hediffSet != null;

        /// <summary>From the Pawn.PostApplyDamage postfix.</summary>
        public static void Notify_Damage(Pawn victim, DamageInfo dinfo, float damage)
        {
            if (!GestaMod.Settings.bloodStains || damage <= 0f || dinfo.Def == null || !dinfo.Def.makesBlood) return;
            if (!RedBlooded(victim) || !victim.health.CanBleed) return;

            if (CanStain(victim)) Add(victim, damage * PerDamageVictim);

            if (dinfo.Instigator is Pawn attacker && attacker != victim && CanStain(attacker)
                && !dinfo.Def.isRanged && !dinfo.Def.isExplosive
                && attacker.Spawned && victim.Spawned && attacker.Map == victim.Map
                && attacker.Position.AdjacentTo8WayOrInside(victim.Position))
                Add(attacker, damage * PerDamageAttacker);
        }

        public static void Add(Pawn pawn, float amount)
        {
            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(GestaDefOf.Gesta_Bloodstained);
            if (h == null)
            {
                h = HediffMaker.MakeHediff(GestaDefOf.Gesta_Bloodstained, pawn);
                h.Severity = amount;
                pawn.health.AddHediff(h);
            }
            else
            {
                h.Severity = Mathf.Min(h.def.maxSeverity, h.Severity + amount);
            }
        }
    }

    public class Hediff_Bloodstained : HediffWithComps
    {
        PawnBloodDrawer drawer;
        public PawnBloodDrawer Drawer => drawer ??= new PawnBloodDrawer(pawn, this);

        protected override void OnStageIndexChanged(int stageIndex)
        {
            base.OnStageIndexChanged(stageIndex);
            drawer?.ClearCache();
            if (pawn.Spawned) pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }

    /// <summary>Washes off: faster while asleep.</summary>
    public class HediffComp_BloodFades : HediffComp_SeverityPerDay
    {
        public override float SeverityChangePerDay()
        {
            float v = base.SeverityChangePerDay();
            return Pawn != null && !Pawn.Awake() ? v * 3f : v;
        }
    }

    /// <summary>
    /// Copy of vanilla PawnFirefoamDrawer: a tiled splatter texture masked to the body (or head)
    /// graphic, drawn over apparel with the vanilla FirefoamOverlay shader. Texture by stage.
    /// </summary>
    public class PawnBloodDrawer : PawnOverlayDrawer
    {
        const float TextureScaleFactor = 2.8f, TextureTiles = 1.4f, OffsetMagnitude = 2f;
        static readonly string[] Levels = { "Light", "Medium", "Heavy" };
        readonly Hediff_Bloodstained hediff;

        public PawnBloodDrawer(Pawn pawn, Hediff_Bloodstained hediff) : base(pawn) { this.hediff = hediff; }

        protected override void WriteCache(CacheKey key, PawnDrawParms parms, List<DrawCall> writeTarget)
        {
            Rot4 rot = key.pawnRot;
            Mesh bodyMesh = key.bodyMesh;
            Graphic graphic = key.layer == OverlayLayer.Body ? pawn.Drawer.renderer.BodyGraphic : pawn.Drawer.renderer.HeadGraphic;
            if (graphic == null) return;
            int level = Mathf.Clamp(hediff.CurStageIndex, 0, Levels.Length - 1);
            Rand.PushState(pawn.thingIDNumber * ((int)key.layer + 7));
            try
            {
                bool flipped = (graphic.EastFlipped && rot == Rot4.East) || (graphic.WestFlipped && rot == Rot4.West);
                string tex = "Things/Gesta/Blood/Blood" + Levels[level] + (Rand.Bool ? "A" : "B");
                Material baseMat = MaterialPool.MatFrom(tex, ShaderDatabase.FirefoamOverlay, Color.white);
                Material mat = MaterialPool.MatFrom(new MaterialRequest
                {
                    maskTex = (Texture2D)graphic.MatAt(rot).mainTexture,
                    mainTex = baseMat.mainTexture,
                    color = baseMat.color,
                    shader = baseMat.shader
                });
                Mesh mesh = flipped ? MeshPool.GridPlaneFlip(Vector2.one * 0.25f) : MeshPool.GridPlane(Vector2.one * 0.25f);
                Vector3 size = bodyMesh.bounds.size;
                float scale = size.magnitude * TextureScaleFactor;
                Vector3 offset = Rand.InsideUnitCircleVec3 * OffsetMagnitude;
                Vector3 meshSize = mesh.bounds.size * scale;
                writeTarget.Add(new DrawCall
                {
                    overlayMat = mat,
                    matrix = Matrix4x4.Scale(Vector3.one * scale),
                    overlayMesh = mesh,
                    displayOverApparel = true,
                    maskTexScale = new Vector4(meshSize.x / size.x, meshSize.z / size.z),
                    mainTexScale = new Vector4(TextureTiles, TextureTiles, 1f, 1f),
                    mainTexOffset = new Vector4(offset.x, offset.z)
                });
            }
            finally
            {
                Rand.PopState();
            }
        }
    }

    public class PawnRenderNodeWorker_Blood : PawnRenderNodeWorker_Overlay
    {
        static Hediff_Bloodstained Stain(Pawn pawn) =>
            pawn.health?.hediffSet?.GetFirstHediffOfDef(GestaDefOf.Gesta_Bloodstained) as Hediff_Bloodstained;

        protected override PawnOverlayDrawer OverlayDrawer(Pawn pawn) => Stain(pawn)?.Drawer;

        public override bool ShouldListOnGraph(PawnRenderNode node, PawnDrawParms parms) => Stain(parms.pawn) != null;

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms) =>
            base.CanDrawNow(node, parms) && parms.rotDrawMode == RotDrawMode.Fresh && Stain(parms.pawn) != null;
    }
}
