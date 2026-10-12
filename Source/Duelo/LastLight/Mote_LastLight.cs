using RimWorld;
using UnityEngine;
using Verse;

namespace Duelo
{
    /// <summary>
    /// A small light that rises from a body, flies to the pawn who loved the deceased most,
    /// circles them once and fades. With no target it rises higher and fades where it is.
    /// Not saved (MoteBase isSaveable=false): saving mid-flight simply loses it.
    /// </summary>
    public class Mote_LastLight : Mote
    {
        const float RiseSecs = 1.6f, RiseHeight = 1.1f;
        const float LoneRiseSecs = 3.5f, LoneRiseHeight = 2.6f;
        const float MinTravelSecs = 2.5f, MaxTravelSecs = 8f, CellsPerSecGuide = 6f, MaxTravelTotal = 20f;
        const float CircleSecs = 2.2f, CircleRadius = 0.75f;
        const float FadeSecs = 1.4f;
        const float HoverAbovePawn = 0.35f;   // +z is "up" on screen

        public enum Phase { Rise, Travel, Circle, Fade }

        public Phase CurrentPhase => phase;

        public Pawn deceased;
        public Pawn target;

        Phase phase;
        float phaseAge, totalAge;
        Vector3 origin;
        float travelSpeed;
        float circleStartAngle;
        float baseAlpha;
        bool thoughtGiven;

        public void Init(Pawn deceased, Pawn target, Vector3 from)
        {
            this.deceased = deceased;
            this.target = target;
            exactPosition = from;
            origin = from;
        }

        bool TargetValid => target != null && target.Spawned && !target.Dead && target.Map == Map;

        Vector3 TargetCenter
        {
            get
            {
                Vector3 c = target.DrawPos;
                c.z += HoverAbovePawn;
                return c;
            }
        }

        protected override bool EndOfLife => phase == Phase.Fade && phaseAge >= FadeSecs;

        // Gentle flicker on top of the phase alpha, so it reads as a living flame.
        public override float Alpha => baseAlpha * (0.85f + 0.15f * Mathf.Sin(totalAge * 5f));

        protected override void TimeInterval(float deltaTime)
        {
            base.TimeInterval(deltaTime);
            if (Destroyed) return;

            phaseAge += deltaTime;
            totalAge += deltaTime;

            if (!bloomedAtBirth)
            {
                bloomedAtBirth = true;
                Bloom(2.4f);
            }

            switch (phase)
            {
                case Phase.Rise: TickRise(); break;
                case Phase.Travel: TickTravel(deltaTime); break;
                case Phase.Circle: TickCircle(); break;
                case Phase.Fade: TickFade(deltaTime); break;
            }

            IntVec3 cell = exactPosition.ToIntVec3();
            if (cell.InBounds(Map)) Position = cell;

            // A comet tail makes the motion readable at normal zoom; sparser while rising.
            trailCounter++;
            int every = phase == Phase.Travel || phase == Phase.Circle ? 3 : phase == Phase.Rise ? 6 : 0;
            if (every > 0 && trailCounter % every == 0) Spark();
        }

        bool bloomedAtBirth;
        int trailCounter;

        void Spark()
        {
            if (!InMapBounds()) return;
            FleckCreationData data = FleckMaker.GetDataStatic(exactPosition, Map, DueloDefOf.Duelo_Fleck_LastLightTrail, Rand.Range(0.7f, 1.1f));
            data.velocityAngle = Rand.Range(-25f, 25f);   // 0 = up the screen
            data.velocitySpeed = Rand.Range(0.1f, 0.3f);
            Map.flecks.CreateFleck(data);
        }

        void Bloom(float scale)
        {
            if (!InMapBounds()) return;
            Map.flecks.CreateFleck(FleckMaker.GetDataStatic(exactPosition, Map, DueloDefOf.Duelo_Fleck_LastLightBloom, scale));
        }

        bool InMapBounds() => exactPosition.ToIntVec3().InBounds(Map);

        void TickRise()
        {
            bool lone = target == null;
            float secs = lone ? LoneRiseSecs : RiseSecs;
            float height = lone ? LoneRiseHeight : RiseHeight;
            float t = Mathf.Clamp01(phaseAge / secs);

            float eased = 1f - (1f - t) * (1f - t);
            exactPosition.x = origin.x + Mathf.Sin(totalAge * 2.3f) * 0.08f;
            exactPosition.z = origin.z + eased * height;
            baseAlpha = Mathf.Clamp01(t * 2f);
            Scale = Mathf.Lerp(0.5f, 1f, t);

            if (t < 1f) return;
            if (!lone && TargetValid)
            {
                float dist = Flat(TargetCenter - exactPosition).magnitude;
                float secsWanted = Mathf.Clamp(dist / CellsPerSecGuide, MinTravelSecs, MaxTravelSecs);
                travelSpeed = Mathf.Max(dist / secsWanted, 1.5f);
                SetPhase(Phase.Travel);
            }
            else
            {
                SetPhase(Phase.Fade);
            }
        }

        void TickTravel(float dt)
        {
            if (!TargetValid || totalAge > MaxTravelTotal)
            {
                SetPhase(Phase.Fade);
                return;
            }
            Vector3 goal = TargetCenter;
            Vector3 toGoal = Flat(goal - exactPosition);
            float dist = toGoal.magnitude;

            if (dist <= CircleRadius + 0.05f)
            {
                circleStartAngle = Mathf.Atan2(-toGoal.z, -toGoal.x);
                SetPhase(Phase.Circle);
                return;
            }

            Vector3 dir = toGoal / dist;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            float step = Mathf.Min(travelSpeed * dt, dist - CircleRadius);
            exactPosition += dir * step + side * (Mathf.Sin(totalAge * 3.1f) * 0.6f * dt);
            baseAlpha = 1f;
        }

        void TickCircle()
        {
            if (!TargetValid)
            {
                SetPhase(Phase.Fade);
                return;
            }
            float t = Mathf.Clamp01(phaseAge / CircleSecs);
            float angle = circleStartAngle + t * Mathf.PI * 2f;
            Vector3 c = TargetCenter;
            exactPosition.x = c.x + Mathf.Cos(angle) * CircleRadius;
            exactPosition.z = c.z + Mathf.Sin(angle) * CircleRadius;

            if (t < 1f) return;
            GiveFarewellThought();
            SetPhase(Phase.Fade);
        }

        void TickFade(float dt)
        {
            float t = Mathf.Clamp01(phaseAge / FadeSecs);
            if (TargetValid && thoughtGiven)
            {
                // sink into the person it found
                Vector3 c = TargetCenter;
                exactPosition.x = Mathf.Lerp(exactPosition.x, c.x, dt * 3f);
                exactPosition.z = Mathf.Lerp(exactPosition.z, c.z - HoverAbovePawn * 0.5f, dt * 3f);
            }
            else
            {
                exactPosition.z += 0.25f * dt;
            }
            baseAlpha = 1f - t;
            Scale = Mathf.Lerp(1f, 0.4f, t);
        }

        void GiveFarewellThought()
        {
            thoughtGiven = true;
            Bloom(1.8f);
            if (deceased == null || target.needs?.mood == null) return;
            target.needs.mood.thoughts.memories.TryGainMemory(DueloDefOf.Duelo_LastLightFarewell, deceased);
        }

        void SetPhase(Phase p)
        {
            phase = p;
            phaseAge = 0f;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
