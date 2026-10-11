using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using RimWorld.Planet;

namespace Gesta
{
    /// <summary>
    /// The Last One Standing: when a single fighter is left on their feet against 3+ hostiles,
    /// the game stops to watch them. See DESIGN.md.
    /// </summary>
    public class LastStandTracker : GameComponent
    {
        public const int MinHostiles = 3;
        public const int CooldownTicks = GenDate.TicksPerYear;
        public static readonly Color Gold = new Color(1f, 0.82f, 0.32f);

        // saved
        Pawn hero;
        int heroMapId = -1;
        int startTick;
        List<Pawn> downedDuring = new List<Pawn>();
        Dictionary<int, int> lastStandTickByPawnId = new Dictionary<int, int>();

        // presentation, not saved
        float captionStartRealTime = -999f;
        string caption;

        static LastStandTracker instance;
        public static LastStandTracker Instance => instance;
        public static Pawn ActiveHero => instance?.hero;

        // requested from sim code, played from GameComponentUpdate (UI side, per client)
        bool cinematicPending;

        public Pawn Hero => hero;
        public bool HasCooldown(Pawn p) => lastStandTickByPawnId.TryGetValue(p.thingIDNumber, out int t)
                                           && Find.TickManager.TicksGame - t < CooldownTicks;

        public LastStandTracker(Game game) { instance = this; }

        public void Notify_EngagementStarted(Map map) { }

        public void Evaluate(Map map)
        {
            if (!GestaMod.Settings.lastStand || hero != null) return;
            if (!CombatUtil.IsPlayerHome(map)) return;
            EngagementTracker.Engagement e = EngagementTracker.Instance?.Get(map);
            if (e == null || !e.active || e.standHappened || e.maxFighters < 2) return;

            Pawn candidate = CombatUtil.SoleFighter(map);
            if (candidate == null || HasCooldown(candidate)) return;
            if (CombatUtil.ActiveHostilePawns(map) < MinHostiles) return;

            Begin(candidate, map);
        }

        void Begin(Pawn pawn, Map map)
        {
            hero = pawn;
            heroMapId = map.uniqueID;
            startTick = Find.TickManager.TicksGame;
            lastStandTickByPawnId[pawn.thingIDNumber] = startTick;
            EngagementTracker.Instance.Get(map).standHappened = true;
            downedDuring.Clear();
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                if (p != pawn && p.Downed && !p.IsSlave) downedDuring.Add(p);

            pawn.health.AddHediff(GestaDefOf.Gesta_LastStand);
            cinematicPending = true;
            Log.Message($"[Gesta] last stand: {pawn.LabelShort} against {CombatUtil.ActiveHostilePawns(map)} hostiles");
        }

        public void Notify_PawnDowned(Pawn pawn)
        {
            if (hero == null) return;
            if (pawn == hero) { End(false); return; }
            if (pawn.MapHeld?.uniqueID == heroMapId && !pawn.IsSlave && !downedDuring.Contains(pawn)) downedDuring.Add(pawn);
        }

        public void Notify_PawnDied(Pawn pawn)
        {
            if (pawn != hero) return;
            TaleRecorder.RecordTale(GestaDefOf.Gesta_FellAloneTale, pawn);
            End(false);
        }

        /// <param name="forced">The engagement timed out with enemies still around: no reward.</param>
        public void Notify_EngagementEnded(Map map, bool forced)
        {
            if (hero == null || map.uniqueID != heroMapId) return;
            bool survived = !forced && !hero.Dead && !hero.Downed;
            if (survived)
            {
                hero.needs?.mood?.thoughts?.memories?.TryGainMemory(GestaDefOf.Gesta_StoodAlone);
                TaleRecorder.RecordTale(GestaDefOf.Gesta_StoodAloneTale, hero);
                foreach (Pawn saved in downedDuring)
                {
                    if (saved != null && !saved.Dead)
                        saved.needs?.mood?.thoughts?.memories?.TryGainMemory(GestaDefOf.Gesta_SavedByLastStand, hero);
                }
                Messages.Message("Gesta_LastStand_Survived".Translate(hero.LabelShort), hero, MessageTypeDefOf.PositiveEvent);
            }
            End(survived);
        }

        void End(bool survived)
        {
            Log.Message($"[Gesta] last stand of {hero?.LabelShort} ended (survived: {survived})");
            hero = null;
            heroMapId = -1;
            downedDuring.Clear();
        }

        public override void GameComponentTick()
        {
            // the hero can leave the map, be kidnapped or stop being a colonist without dying
            if (hero != null && Find.TickManager.TicksGame % 60 == 0 && (!hero.Spawned || hero.Faction != Faction.OfPlayer))
                End(false);
        }

        public override void GameComponentUpdate()
        {
            if (!cinematicPending) return;
            cinematicPending = false;
            if (hero == null || !hero.Spawned || Find.CurrentMap != hero.Map) return;
            if (GestaMod.Settings.lastStandCamera)
                Find.CameraDriver.PanToMapLocAndSize(hero.DrawPos, 14f, 1.5f);
            if (GestaMod.Settings.lastStandSlowDown && Find.TickManager.CurTimeSpeed > TimeSpeed.Normal)
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            if (GestaMod.Settings.lastStandMusic)
                Find.MusicManagerPlay?.ForceFadeoutAndSilenceFor(25f, 2f, preventDangerTransition: true);
            caption = "Gesta_LastStand_Caption".Translate(hero.LabelShort);
            captionStartRealTime = Time.realtimeSinceStartup;
        }

        public override void GameComponentOnGUI()
        {
            if (hero == null || Event.current.type != EventType.Repaint) return;
            if (Find.CurrentMap == null || Find.CurrentMap.uniqueID != heroMapId || WorldRendererUtility.WorldSelected) return;
            LastStandOverlay.Draw(hero, caption, captionStartRealTime);
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref hero, "hero");
            Scribe_Values.Look(ref heroMapId, "heroMapId", -1);
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Collections.Look(ref downedDuring, "downedDuring", LookMode.Reference);
            Scribe_Collections.Look(ref lastStandTickByPawnId, "lastStandTickByPawnId", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                downedDuring ??= new List<Pawn>();
                downedDuring.RemoveAll(p => p == null);
                lastStandTickByPawnId ??= new Dictionary<int, int>();
                if (hero == null) heroMapId = -1;
            }
        }
    }
}
