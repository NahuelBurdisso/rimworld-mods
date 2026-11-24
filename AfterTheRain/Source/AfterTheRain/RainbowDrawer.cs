using UnityEngine;
using Verse;

namespace AfterTheRain
{
    /// <summary>
    /// Draws the arc as one screen-space quad (the same idea as SkyOverlay.DrawScreenOverlay), twice: an alpha-blended
    /// pastel body and a faint additive glow. Vanilla shaders only; two materials of our own so changing their colour
    /// each frame touches nothing shared. No allocations per frame.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RainbowDrawer
    {
        public const float BodyAlpha = 0.12f, GlowAlpha = 0.045f;
        /// <summary>Quad width relative to the visible width; the texture's arc centre sits at its bottom edge.</summary>
        public const float WidthFactor = 1.15f;
        /// <summary>Arc centre below the camera centre, in visible half-heights.</summary>
        public const float CentreDrop = 1.24f;
        /// <summary>How far the arc leans toward the anti-solar side, in visible widths.</summary>
        public const float SideShift = 0.2f;

        public static readonly Texture2D Tex = ContentFinder<Texture2D>.Get("AfterTheRain/Rainbow");
        static readonly Material Body;
        static readonly Material Glow;

        public static bool Ready => Body != null && Glow != null;

        static RainbowDrawer()
        {
            if (Tex == null) return;
            Body = new Material(ShaderDatabase.Transparent) { name = "AfterTheRain_RainbowBody", mainTexture = Tex, color = Color.clear };
            Glow = new Material(ShaderDatabase.MoteGlow) { name = "AfterTheRain_RainbowGlow", mainTexture = Tex, color = Color.clear };
        }

        public static void Draw(float alpha, float side)
        {
            if (!Ready) return;
            Camera cam = Find.Camera;
            if (cam == null) return;
            float halfH = cam.orthographicSize;
            float visW = halfH * 2f * cam.aspect;
            float quadW = visW * WidthFactor;
            float quadH = quadW * 0.5f;
            Vector3 pos = cam.transform.position;
            pos.x += side * SideShift * visW;
            pos.z += -CentreDrop * halfH + quadH * 0.5f;
            pos.y = AltitudeLayer.Weather.AltitudeFor() + 0.04f;
            Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(quadW, 1f, quadH));

            Body.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha * BodyAlpha));
            Glow.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha * GlowAlpha));
            Graphics.DrawMesh(MeshPool.plane10, m, Body, 0);
            Graphics.DrawMesh(MeshPool.plane10, m, Glow, 0);
        }
    }
}
