using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace OldScores
{
    /// <summary>Feature 2 — The One You Spared. See DESIGN.md.</summary>
    public partial class OldScoresTracker
    {
        public SparedRecord GetSpared(Pawn p)
        {
            for (int i = 0; i < spared.Count; i++)
                if (spared[i].pawn == p) return spared[i];
            return null;
        }

        public bool AnyReturnedSpared
        {
            get
            {
                for (int i = 0; i < spared.Count; i++)
                    if (spared[i].stage == SparedStage.Returned) return true;
                return false;
            }
        }

        public void Notify_Captured(Pawn p, Faction by, Pawn byPawn)
        {
            if (!OldScoresMod.Settings.spared) return;
            if (by != Faction.OfPlayer || byPawn == null || byPawn.Faction != Faction.OfPlayer || !byPawn.RaceProps.Humanlike) return;
            if (p == null || !p.RaceProps.Humanlike || !p.IsPrisoner) return;
            Faction home = p.Faction;
            if (home == null || home.IsPlayer || !home.HostileTo(Faction.OfPlayer)) return;
            SparedRecord rec = GetSpared(p);
            if (rec == null)
            {
                rec = new SparedRecord { pawn = p };
                spared.Add(rec);
            }
            rec.rescuer = byPawn;
            rec.stage = SparedStage.Captured;
            rec.capturedTick = Find.TickManager.TicksGame;
        }

        public void Notify_Released(Pawn p)
        {
            SparedRecord rec = GetSpared(p);
            if (rec == null || rec.stage != SparedStage.Captured) return;
            if (p.Faction == null || p.Faction.IsPlayer) { spared.Remove(rec); return; }
            rec.stage = SparedStage.Released;
        }

        void Notify_SparedExitedMap(Pawn p)
        {
            SparedRecord rec = GetSpared(p);
            if (rec == null) return;
            if (rec.stage == SparedStage.Captured)
            {
                // escaped, never released: he owes nothing
                spared.Remove(rec);
                Unpin(p);
                return;
            }
            if (p.Dead || p.Spawned || !p.IsWorldPawn()) return;
            rec.stage = SparedStage.Away;
            rec.leftTick = Find.TickManager.TicksGame;
            Pin(p);
        }

        /// <summary>IncidentWorker_Raid.PostProcessSpawnedPawns postfix: at most one spared pawn joins the raid.</summary>
        public void Notify_RaidSpawned(IncidentParms parms, List<Pawn> pawns)
        {
            if (!OldScoresMod.Settings.spared || spared.Count == 0 || pawns == null) return;
            if (!(parms.target is Map map) || !map.IsPlayerHome || parms.faction == null || parms.faction.IsPlayer) return;
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < spared.Count; i++)
            {
                SparedRecord rec = spared[i];
                Pawn p = rec.pawn;
                if (rec.stage != SparedStage.Away || p == null || p.Dead || p.Destroyed || p.Spawned || !p.IsWorldPawn() || p.IsCaravanMember()) continue;
                if (p.Faction != parms.faction || now - rec.leftTick < OldScoresTuning.SparedMinTicksAway) continue;
                if (rec.rescuer == null || rec.rescuer.Dead || !rec.rescuer.Spawned || rec.rescuer.Map != map) continue;
                if (!Rand.Chance(OldScoresTuning.SparedReturnChance)) return;   // one roll per raid
                JoinRaid(rec, parms, pawns, map);
                return;
            }
        }

        static void JoinRaid(SparedRecord rec, IncidentParms parms, List<Pawn> pawns, Map map)
        {
            Pawn p = rec.pawn;
            IntVec3 center = parms.spawnCenter;
            if (!center.IsValid || !center.InBounds(map))
                center = pawns.Count > 0 && pawns[0].Spawned ? pawns[0].Position : CellFinder.RandomEdgeCell(map);
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(center, map, 6);
            GenSpawn.Spawn(p, cell, map, parms.spawnRotation);
            if (p.equipment != null && p.equipment.Primary == null)
                PawnWeaponGenerator.TryGenerateWeaponFor(p, new PawnGenerationRequest(p.kindDef, p.Faction));
            pawns.Add(p);
            if (parms.pawnGroups != null)
            {
                int group = 0;
                if (pawns.Count > 1 && parms.pawnGroups.TryGetValue(pawns[0], out int g)) group = g;
                parms.pawnGroups[p] = group;
            }
            rec.stage = SparedStage.Returned;
            Log.Message($"[Old Scores] {p.LabelShort} came back with the {p.Faction.Name} raid");
        }

        /// <summary>Every 30 ticks from the map component.</summary>
        public void SparedCheck(Map map)
        {
            if (!OldScoresMod.Settings.spared) return;
            float r2 = OldScoresTuning.SparedRecognizeRadius * OldScoresTuning.SparedRecognizeRadius;
            for (int i = spared.Count - 1; i >= 0; i--)
            {
                SparedRecord rec = spared[i];
                Pawn p = rec.pawn, rescuer = rec.rescuer;
                if (rec.stage != SparedStage.Returned || p == null || !p.Spawned || p.Map != map || p.Dead || p.Downed) continue;
                if (p.InAggroMentalState) continue;
                if (rescuer == null || rescuer.Dead || !rescuer.Spawned || rescuer.Map != map) continue;
                if ((p.Position - rescuer.Position).LengthHorizontalSquared > r2) continue;
                if (!GenSight.LineOfSight(p.Position, rescuer.Position, map)) continue;
                Recognize(rec);
            }
        }

        void Recognize(SparedRecord rec)
        {
            Pawn p = rec.pawn, rescuer = rec.rescuer;
            Map map = p.Map;
            string line = ("OldScores_Spared_Line" + Rand.RangeInclusive(1, 5)).Translate(rescuer.LabelShort, rescuer.Named("RESCUER"));
            MoteMaker.ThrowText(p.DrawPos + new Vector3(0f, 0f, 0.8f), map, line, Color.white, 6f);
            p.equipment?.DropAllEquipment(p.Position, forbid: false);
            if (p.MentalState != null) p.MentalState.RecoverFromState();
            Faction oldFaction = p.Faction;
            InteractionWorker_RecruitAttempt.DoRecruit(rescuer, p, out _, out _, useAudiovisualEffects: true, sendLetter: false);
            Find.LetterStack.ReceiveLetter("OldScores_Spared_LetterLabel".Translate(p.LabelShort),
                "OldScores_Spared_LetterText".Translate(p.LabelShort, rescuer.LabelShort, oldFaction?.Name ?? "?", line, p.Named("PAWN"), rescuer.Named("RESCUER")),
                LetterDefOf.PositiveEvent, p);
            spared.Remove(rec);
            Unpin(p);
            Log.Message($"[Old Scores] {p.LabelShort} recognised {rescuer.LabelShort} and joined the colony");
        }

        void CleanupSpared()
        {
            for (int i = spared.Count - 1; i >= 0; i--)
            {
                SparedRecord rec = spared[i];
                Pawn p = rec.pawn;
                bool gone = p == null || p.Dead || p.Discarded || p.Faction == null || p.Faction.IsPlayer || p.IsSlave
                            || rec.rescuer == null || rec.rescuer.Dead || rec.rescuer.Discarded || rec.rescuer.Faction != Faction.OfPlayer;
                if (!gone) continue;
                spared.RemoveAt(i);
                Unpin(p);
            }
        }

        /// <summary>Kill postfix: a remembered pawn died.</summary>
        void Notify_SparedDied(Pawn p)
        {
            SparedRecord rec = GetSpared(p);
            if (rec == null) return;
            spared.Remove(rec);
            Unpin(p);
        }
    }
}
