using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace OldScores
{
    /// <summary>
    /// Per-map timing and drawing. The state of the first three features lives in <see cref="OldScoresTracker"/>;
    /// Raid Echoes (Echoes.cs, EchoesDraw.cs) keeps its per-map records here and saves them with the map.
    /// </summary>
    public partial class OldScoresMapComponent : MapComponent
    {
        public OldScoresMapComponent(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            try { EchoTick(now); }
            catch (System.Exception ex) { Log.ErrorOnce("[Old Scores] raid echo tick failed: " + ex, 0x0D5C16); }
            OldScoresTracker tracker = OldScoresTracker.Instance;
            if (tracker == null) return;
            if (now % OldScoresTuning.SparedCheckInterval == 0 && tracker.AnyReturnedSpared) tracker.SparedCheck(map);
            if (now % OldScoresTuning.DreadInterval == 0 && tracker.AnyNickname) tracker.DreadCheck(map);
        }

        public override void MapComponentOnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (OldScoresMod.Settings.beasts) DrawBeastLabels();
            DrawEchoLabels();
        }

        void DrawBeastLabels()
        {
            OldScoresTracker tracker = OldScoresTracker.Instance;
            if (tracker == null) return;
            IReadOnlyList<BeastRecord> beasts = tracker.Beasts;
            for (int i = 0; i < beasts.Count; i++)
            {
                BeastRecord rec = beasts[i];
                Pawn b = rec.beast;
                if (rec.stage != BeastStage.Returned || b == null || !b.Spawned || b.Map != map || b.Position.Fogged(map)) continue;
                Vector2 pos = GenMapUI.LabelDrawPosFor(b, -0.6f);
                pos.y += 14f;
                GenMapUI.DrawThingLabel(pos, tracker.BeastLabel(rec), OldScoresTracker.GrudgeRed);
            }
        }
    }
}
