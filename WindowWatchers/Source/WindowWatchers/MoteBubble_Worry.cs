using RimWorld;
using UnityEngine;
using Verse;

namespace WindowWatchers
{
    /// <summary>
    /// Thought bubble with the worry icon and the vanilla arrow toward the loved one (MoteBubble.arrowTarget).
    /// Shake(): trembles and turns red for a while (the attach-link offset is jittered each frame, Verse/MoteAttachLink.cs:35).
    /// </summary>
    public class MoteBubble_Worry : MoteBubble
    {
        public static readonly Color HurtTint = new Color(1f, 0.45f, 0.45f);
        public static readonly Color GriefTint = new Color(0.9f, 0.2f, 0.2f);

        int shakeUntilTick = -1;
        Color restColor = Color.white;

        public bool Shaking => Find.TickManager.TicksGame < shakeUntilTick;

        public void Shake(int ticks, Color tint, bool keepTint = false)
        {
            shakeUntilTick = Find.TickManager.TicksGame + ticks;
            instanceColor = tint;
            if (keepTint) restColor = tint;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (link1.Linked)
            {
                if (Shaking)
                {
                    float t = Time.realtimeSinceStartup * 45f + thingIDNumber;
                    link1.UpdateTarget(link1.Target, new Vector3(Mathf.Sin(t) * 0.08f, 0f, Mathf.Sin(t * 1.37f) * 0.05f));
                }
                else
                {
                    link1.UpdateTarget(link1.Target, Vector3.zero);
                    instanceColor = restColor;
                }
            }
            base.DrawAt(drawLoc, flip);
        }

        public static MoteBubble_Worry Make(ThingDef def, Pawn watcher, Pawn target)
        {
            var mote = (MoteBubble_Worry)ThingMaker.MakeThing(def);
            mote.SetupMoteBubble(WWTextures.Worry, target);
            mote.Attach(watcher);
            GenSpawn.Spawn(mote, watcher.Position, watcher.Map);
            return mote;
        }
    }

    [StaticConstructorOnStartup]
    public static class WWTextures
    {
        public static readonly Texture2D Worry = ContentFinder<Texture2D>.Get("Things/Mote/WindowWatchers/Worry");
    }
}
