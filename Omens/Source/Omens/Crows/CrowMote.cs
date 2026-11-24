using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Omens
{
    /// <summary>
    /// A crow: a mote (never saved) that either circles a centre or flies away in a straight line and fades.
    /// Faces its heading (rotateTowardsMoveDirection) and flaps in short bursts (x-scale squeeze).
    /// </summary>
    public class CrowMote : MoteThrown
    {
        public Vector3 center;
        public float radius = 3f;
        public float angularSpeed = 0.3f;     // rad/s, sign = direction
        float angle;
        bool departing;
        float departAge, flyTime;
        float flapPhase;
        bool initialized;

        const float MaxOrbitLifeSecs = 3f * 60000f / 60f;   // 3 days of game time: a hard cap
        const float DepartFade = 1.5f;

        public bool Departing => departing;

        /// <summary>Every crow spawned on any map (motes are not in ListerThings). For the harness and debugging.</summary>
        public static readonly HashSet<CrowMote> Live = new HashSet<CrowMote>();

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            Live.Add(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Live.Remove(this);
            base.DeSpawn(mode);
        }

        public override void Notify_MyMapRemoved()
        {
            Live.Remove(this);
            base.Notify_MyMapRemoved();
        }

        public void Init(Vector3 center, float radius, float angularSpeed)
        {
            this.center = center;
            this.radius = radius;
            this.angularSpeed = angularSpeed;
            angle = Rand.Range(0f, Mathf.PI * 2f);
            flapPhase = Rand.Range(0f, 10f);
            initialized = true;
        }

        /// <summary>Where on its circle this crow starts (spawn it there).</summary>
        public Vector3 OrbitPoint => center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

        /// <summary>Leave in a straight line at <paramref name="speed"/> cells/s, heading <paramref name="headingDeg"/> (0 = north).</summary>
        public void Depart(float headingDeg, float speed, float seconds)
        {
            if (departing) return;
            departing = true;
            departAge = AgeSecs;
            flyTime = seconds;
            SetVelocity(headingDeg, speed);
        }

        protected override bool EndOfLife =>
            departing ? AgeSecs > departAge + flyTime + DepartFade : AgeSecs > MaxOrbitLifeSecs;

        public override float Alpha
        {
            get
            {
                float age = AgeSecs;
                float a = def.mote.fadeInTime > 0f ? Mathf.Clamp01(age / def.mote.fadeInTime) : 1f;
                if (departing)
                {
                    float t = age - departAge - flyTime;
                    if (t > 0f) a *= 1f - Mathf.Clamp01(t / DepartFade);
                }
                return a;
            }
        }

        protected override void TimeInterval(float deltaTime)
        {
            base.TimeInterval(deltaTime);
            if (Destroyed) return;
            // wing beats: three quick strokes, then a glide of a few seconds
            float cycle = (AgeSecs + flapPhase) % (departing ? 1.6f : 4.5f);
            float squeeze = cycle < 0.9f ? Mathf.Abs(Mathf.Sin(cycle / 0.3f * Mathf.PI)) * 0.45f : 0f;
            curvedScale = new Vector3(1f - squeeze, 1f, 1f);
        }

        protected override Vector3 NextExactPosition(float deltaTime)
        {
            if (departing || !initialized) return base.NextExactPosition(deltaTime);
            angle += angularSpeed * deltaTime;
            Vector3 target = OrbitPoint;
            target.y = exactPosition.y;
            // move toward the point on the circle at a bounded speed: no jumps when the centre moves
            float maxStep = (Mathf.Abs(angularSpeed) * radius * 1.6f + 1.5f) * deltaTime;
            Vector3 next = Vector3.MoveTowards(exactPosition, target, maxStep);
            Vector3 v = (next - exactPosition) / Mathf.Max(deltaTime, 0.0001f);
            if (v.sqrMagnitude > 0.0001f) velocity = v;
            return next;
        }
    }
}
