using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace TheDogKnows
{
    public static class TDKUtility
    {
        /// <summary>The stare ends when the entity strays this far beyond the sense radius.</summary>
        public const float LoseInterestFactor = 1.5f;

        /// <summary>Counters for the test harness.</summary>
        public static int Growls, MotesMade, MessagesSent;

        /// <summary>
        /// Has an invisibility hediff that is currently hiding the pawn. Walks the hediffs instead of calling
        /// IsPsychologicallyInvisible: Melee Animation prefixes that method to return true during duels.
        /// </summary>
        public static bool HasActiveInvisibility(Pawn p)
        {
            List<Hediff> hediffs = p.health?.hediffSet?.hediffs;
            if (hediffs == null) return false;
            for (int i = 0; i < hediffs.Count; i++)
            {
                HediffComp_Invisibility inv = hediffs[i].TryGetComp<HediffComp_Invisibility>();
                if (inv != null && !inv.PsychologicallyVisible) return true;
            }
            return false;
        }

        /// <summary>A hostile the colonists cannot see, on a cell the player can see.</summary>
        public static bool IsUnseen(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead) return false;
            if (p.Position.Fogged(p.Map)) return false;
            if (!HasActiveInvisibility(p)) return false;
            return p.HostileTo(Faction.OfPlayer);
        }

        /// <summary>Can this colony animal drop what it is doing and stare now? Cooldown is checked by the map component.</summary>
        public static bool CanReact(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed || p.Faction == null || !p.Faction.IsPlayer) return false;
            if (p.RaceProps == null || !p.RaceProps.Animal) return false;
            if (UnseenSenseDef.For(p.def) == null) return false;
            if (!p.Awake() || p.InMentalState || p.Drafted) return false;
            if (p.GetLord() != null) return false;
            if (p.roping != null && p.roping.IsRoped) return false;
            if (p.IsBurning()) return false;
            if (p.needs?.food != null && p.needs.food.Starving) return false;
            if (HealthAIUtility.ShouldSeekMedicalRest(p)) return false;
            if (p.carryTracker?.CarriedThing != null) return false;
            // escorting its master (vanilla CanCasuallyInteractNow skips these too)
            if (ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(p)) return false;
            Job cur = p.CurJob;
            if (cur == null) return true;
            JobDef d = cur.def;
            if (d == TDKDefOf.TDK_StareAtUnseen || cur.playerForced) return false;
            if (d == JobDefOf.Flee || d == JobDefOf.FleeAndCower || d == JobDefOf.FleeAndCowerShort) return false;
            bool waitLike = d == JobDefOf.Wait || d == JobDefOf.Wait_MaintainPosture || d == JobDefOf.LayDown || d == JobDefOf.LayDownAwake;
            // PawnUtility.ForceWait (milking, shearing, training, tending) = a wait-like job with an expiry
            if (waitLike && cur.expiryInterval > 0) return false;
            if (d == JobDefOf.LayDown || d == JobDefOf.LayDownAwake) return true;
            return d.casualInterruptible;
        }

        /// <summary>Is <paramref name="unseen"/> within the animal's senses right now?</summary>
        public static bool Senses(Pawn animal, Pawn unseen, UnseenSenseDef def, float radiusFactor = 1f)
        {
            if (unseen.Map != animal.Map) return false;
            if (!animal.Position.InHorDistOf(unseen.Position, def.radius * radiusFactor)) return false;
            return !def.requireLineOfSight || GenSight.LineOfSight(animal.Position, unseen.Position, animal.Map, skipFirstCell: true);
        }

        /// <summary>
        /// The job carries no targets: the entity is handed to the driver (saved by reference there), so nothing
        /// that draws or lists job targets (vanilla draws a line to targetA) can point at it.
        /// </summary>
        public static bool StartStare(Pawn animal, Pawn unseen)
        {
            Job job = JobMaker.MakeJob(TDKDefOf.TDK_StareAtUnseen);
            JobDriver_StareAtUnseen.Pending = unseen;
            try { animal.jobs.StartJob(job, JobCondition.InterruptForced); }
            finally { JobDriver_StareAtUnseen.Pending = null; }
            return animal.CurJob == job;
        }

        /// <summary>The current life stage's angry sound, else its call; babies and juveniles of vanilla
        /// dogs and cats have neither, so fall back to the oldest stage that has one.</summary>
        public static SoundDef GrowlSoundFor(Pawn p, UnseenSenseDef def)
        {
            if (def.growlSound != null) return def.growlSound;
            LifeStageAge stage = p.ageTracker?.CurLifeStageRace;
            SoundDef s = stage?.soundAngry ?? stage?.soundCall;
            if (s != null) return s;
            List<LifeStageAge> stages = p.RaceProps?.lifeStageAges;
            if (stages == null) return null;
            for (int i = stages.Count - 1; i >= 0; i--)
            {
                s = stages[i].soundAngry ?? stages[i].soundCall;
                if (s != null) return s;
            }
            return null;
        }

        public static void Growl(Pawn p, UnseenSenseDef def)
        {
            if (!TheDogKnowsMod.Settings.growl || !p.Spawned) return;
            SoundDef sound = GrowlSoundFor(p, def);
            if (sound == null) return;
            SoundInfo info = SoundInfo.InMap(new TargetInfo(p.Position, p.Map));
            info.pitchFactor = def.growlPitch;
            info.volumeFactor = def.growlVolume;
            sound.PlayOneShot(info);
            Growls++;
        }

        public static void Hackles(Pawn p)
        {
            if (!p.Spawned) return;
            float size = Mathf.Min(p.BodySize, 2f);
            MoteMaker.MakeAttachedOverlay(p, TDKDefOf.TDK_Mote_Hackles, new Vector3(0f, 0f, 0.45f + 0.35f * size), 0.55f + 0.15f * size);
            MotesMade++;
        }

        /// <summary>A small shiver: the coat standing up. Cosmetic: does nothing if the private field moved.</summary>
        public static void Shiver(Pawn p)
        {
            AccessTools.FieldRef<Pawn_DrawTracker, JitterHandler> r = JitterAccess.Ref;
            if (r == null || p.Drawer == null) return;
            r(p.Drawer)?.AddOffset(0.03f, Rand.Range(0f, 360f));
        }

        /// <summary>Lazy holder: a failure here must not take TDKUtility (IsUnseen, CanReact) down with it.</summary>
        private static class JitterAccess
        {
            public static readonly AccessTools.FieldRef<Pawn_DrawTracker, JitterHandler> Ref = Make();

            private static AccessTools.FieldRef<Pawn_DrawTracker, JitterHandler> Make()
            {
                try { return AccessTools.FieldRefAccess<Pawn_DrawTracker, JitterHandler>("jitterer"); }
                catch (System.Exception ex)
                {
                    Log.Warning("[The Dog Knows] no Pawn_DrawTracker.jitterer, the shiver is off: " + ex.Message);
                    return null;
                }
            }
        }

        public static void SendMessage(Pawn animal)
        {
            Messages.Message("TDK_MessageStaring".Translate(animal.Named("ANIMAL")), animal, MessageTypeDefOf.CautionInput, historical: false);
            MessagesSent++;
        }
    }
}
