using RimWorld;
using UnityEngine;
using Verse;

namespace Duelo
{
    /// <summary>
    /// Draws grave moss: a moss texture on the mound (Transparent: darkened at night like the grave)
    /// and a soft glow over it (TransparentPostLight: drawn after the lighting overlay, like fire, so it
    /// reads at night). Emits no light: no effect on darkness, mood or work.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MossDrawer
    {
        static readonly Material MossMat = MaterialPool.MatFrom("Things/Duelo/GraveMoss", ShaderDatabase.Transparent, Color.white);
        static readonly Material GlowMat = MaterialPool.MatFrom("Things/Duelo/MossGlow", ShaderDatabase.TransparentPostLight, Color.white);
        static readonly MaterialPropertyBlock Props = new MaterialPropertyBlock();
        static readonly Color GlowColor = new Color(0.45f, 1f, 0.78f);

        public static void Draw(CompDueloGrave comp, float night, float brightness)
        {
            Thing grave = comp.parent;
            float level = comp.mossLevel;
            Vector3 center = grave.TrueCenter();
            Quaternion rot = grave.Rotation.AsQuat;
            IntVec2 size = grave.def.size;   // unrotated footprint; the quaternion turns it

            float mossAlpha = Mathf.Clamp01(level * 1.6f) * 0.9f;
            center.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            Props.SetColor(ShaderPropertyIDs.Color, new Color(1f, 1f, 1f, mossAlpha));
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(center, rot, new Vector3(size.x * 1.1f, 1f, size.z * 1.05f)), MossMat, 0, null, 0, Props);

            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.realtimeSinceStartup * 1.3f + grave.thingIDNumber);
            float glowAlpha = Mathf.Clamp01(level * (0.12f + 0.88f * night) * brightness * pulse);
            if (glowAlpha < 0.01f) return;
            center.y = AltitudeLayer.MoteOverheadLow.AltitudeFor();
            float grow = 0.8f + 0.4f * level;
            Props.SetColor(ShaderPropertyIDs.Color, new Color(GlowColor.r, GlowColor.g, GlowColor.b, glowAlpha));
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(center, rot, new Vector3(size.x * 2.4f * grow, 1f, size.z * 1.7f * grow)), GlowMat, 0, null, 0, Props);
        }
    }
}
