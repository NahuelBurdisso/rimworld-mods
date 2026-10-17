using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace PackHowls
{
    public static class PackHowlsUtility
    {
        /// <summary>Night window, local hours [NightStart, 24) and [0, NightEnd).</summary>
        public const int NightStart = 21, NightEnd = 4;
        /// <summary>Bleed-outs count as combat deaths when violence hurt the victim this recently (12 h).</summary>
        public const int RecentHarmTicks = 30000;
        /// <summary>The 45-cell earshot for the ordinary memory.</summary>
        public const float EarshotRadius = 45f;

        /// <summary>Test seam: forces the local hour when >= 0.</summary>
        public static int HourOverride = -1;

        /// <summary>Counters for the test harness.</summary>
        public static int HowlsPlayed, RingsMade, HowlsFired;

        public static HowlerListDef Howlers => PackHowlsDefOf.PackHowls_Howlers;

        public static bool IsHowlerRace(Pawn p) => p?.def != null && Howlers.IsHowler(p.def);

        public static int LocalHour(Map map) => HourOverride >= 0 ? HourOverride : GenLocalDate.HourOfDay(map);

        public static bool IsNight(Map map)
        {
            int h = LocalHour(map);
            return h >= NightStart || h < NightEnd;
        }

        /// <summary>A colonist, or a player animal bonded to someone.</summary>
        public static bool IsMournedVictim(Pawn p)
        {
            if (p == null || p.RaceProps == null) return false;
            if (p.IsColonist) return true;
            if (!p.RaceProps.Animal || p.Faction == null || !p.Faction.IsPlayer || p.relations == null) return false;
            return p.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Bond) != null;
        }

        /// <summary>Called from the Kill prefix while the victim is still alive. See DESIGN.md "En combate".</summary>
        public static bool IsCombatDeath(Pawn victim, DamageInfo? dinfo, Hediff exactCulprit, Map map)
        {
            Thing instigator = dinfo?.Instigator;
            if (instigator != null && IsHostileInstigator(instigator)) return true;
            if (dinfo.HasValue)
            {
                // Friendly fire, or a blow with no instigator: only violence during a fight.
                return dinfo.Value.Def != null && dinfo.Value.Def.ExternalViolenceFor(victim) && AnyRealThreat(map);
            }
            // No damage info: bleeding out (or an untracked cause). Heart attacks, disease, old age and
            // starvation name their hediff as the culprit and never count.
            if (exactCulprit != null && exactCulprit.def != HediffDefOf.BloodLoss && !(exactCulprit is Hediff_Injury)) return false;
            if (AnyRealThreat(map)) return true;
            int lastHarm = victim.mindState?.lastHarmTick ?? -99999;
            return lastHarm > 0 && Find.TickManager.TicksGame - lastHarm <= RecentHarmTicks;
        }

        /// <summary>HostileTo is false for destroyed things (a dead raider's molotov or mortar shell): fall back to the faction.</summary>
        public static bool IsHostileInstigator(Thing instigator)
        {
            if (instigator.HostileTo(Faction.OfPlayer)) return true;
            return instigator.Destroyed && instigator.Faction != null && instigator.Faction.HostileTo(Faction.OfPlayer);
        }

        /// <summary>
        /// GenHostility.AnyHostileActiveThreatToPlayer, minus the player's own pawns in a hostile mental state
        /// (the fallen colonist's bonded animal often goes berserk or manhunter for a day: that must not
        /// postpone the howl or make its death count as combat).
        /// </summary>
        public static bool AnyRealThreat(Map map)
        {
            foreach (IAttackTarget t in map.attackTargetsCache.TargetsHostileToFaction(Faction.OfPlayer))
            {
                if (t.Thing is Pawn p && p.Faction != null && p.Faction.IsPlayer) continue;
                if (GenHostility.IsActiveThreatTo(t, Faction.OfPlayer)) return true;
            }
            return false;
        }

        public static bool AnyPlayerHowler(Map map)
        {
            var pawns = map.mapPawns.SpawnedColonyAnimals;
            for (int i = 0; i < pawns.Count; i++)
                if (!pawns[i].Dead && IsHowlerRace(pawns[i])) return true;
            return false;
        }

        /// <summary>Can this animal drop what it is doing and howl now? Sleeping is allowed: the howl wakes them.</summary>
        public static bool CanHowlNow(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed || p.InMentalState || p.Drafted) return false;
            if (!IsHowlerRace(p)) return false;
            if (p.GetLord() != null) return false;
            if (p.roping != null && p.roping.IsRoped) return false;
            if (p.IsBurning()) return false;
            if (p.needs?.food != null && p.needs.food.Starving) return false;
            if (HealthAIUtility.ShouldSeekMedicalRest(p)) return false;
            if (p.carryTracker?.CarriedThing != null) return false;
            Job cur = p.CurJob;
            if (cur == null) return true;
            if (cur.def == PackHowlsDefOf.PackHowls_Howl) return false;
            return cur.def.casualInterruptible || cur.def == JobDefOf.LayDown || cur.def == JobDefOf.LayDownAwake;
        }

        public static bool StartHowl(Pawn p, IntVec3 faceCell)
        {
            if (!CanHowlNow(p)) return false;
            Job job = JobMaker.MakeJob(PackHowlsDefOf.PackHowls_Howl, faceCell.IsValid && faceCell.InBounds(p.Map) ? new LocalTargetInfo(faceCell) : LocalTargetInfo.Invalid);
            p.jobs.StartJob(job, JobCondition.InterruptForced);
            return p.CurJobDef == PackHowlsDefOf.PackHowls_Howl;
        }

        public static void PlayHowl(Pawn p)
        {
            if (!p.Spawned) return;
            HowlsPlayed++;
            SoundInfo info = SoundInfo.InMap(new TargetInfo(p.Position, p.Map));
            // Small dogs sound higher, wargs lower.
            info.pitchFactor = Mathf.Lerp(1.35f, 0.85f, Mathf.InverseLerp(0.2f, 1.5f, p.BodySize));
            PackHowlsDefOf.PackHowls_HowlSound.PlayOneShot(info);
        }

        public static void MakeRing(Pawn p)
        {
            Map map = p.Map;
            if (map == null || p.Position.Fogged(map)) return;
            RingsMade++;
            Vector3 head = p.DrawPos + new Vector3(0f, 0f, 0.25f + 0.2f * p.BodySize);
            FleckCreationData data = FleckMaker.GetDataStatic(head, map, PackHowlsDefOf.PackHowls_HowlRing, 0.45f + 0.35f * p.BodySize);
            data.velocityAngle = 0f;            // straight up on screen
            data.velocitySpeed = 0.45f;
            data.rotationRate = Rand.Range(-15f, 15f);
            map.flecks.CreateFleck(data);
        }
    }
}
