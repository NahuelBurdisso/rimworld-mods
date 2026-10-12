using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Aftermath
{
    /// <summary>
    /// Injected into every Building_Turret def (CompInjector). Keeps the turret's service record; names it
    /// once it has held the line through enough battles; leaves named remains and mourning when it is
    /// destroyed. Nothing ticks: everything is driven by damage events (SentinelUtility) and Notify_Killed.
    /// </summary>
    public class CompOldSentinel : ThingComp
    {
        public static readonly Color NameColor = new Color(0.96f, 0.84f, 0.52f);

        public SentinelRecord rec;   // null until the turret first engages a hostile

        public bool Named => rec != null && rec.Named;
        public string SentinelName => rec?.name;
        public int Takedowns => rec?.takedowns ?? 0;
        public int Battles => rec?.battles ?? 0;

        static bool On => AftermathMod.Settings.oldSentinels;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            SentinelTracker.For(parent.Map)?.Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            SentinelTracker.For(map)?.Unregister(this);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref rec, "aftermathSentinel");
        }

        // ------------------------------------------------------------------ the record

        SentinelRecord Rec()
        {
            if (rec == null)
                rec = new SentinelRecord { firstTick = Find.TickManager.TicksGame, longLat = AftermathUtil.LongLat(parent.MapHeld) };
            return rec;
        }

        /// <summary>It fought a hostile now. More than BattleGapTicks after the last time: a new battle.</summary>
        public void Engage()
        {
            SentinelRecord r = Rec();
            int now = Find.TickManager.TicksGame;
            if (r.lastEngagedTick < 0 || now - r.lastEngagedTick > AftermathTuning.BattleGapTicks) r.battles++;
            r.lastEngagedTick = now;
        }

        /// <summary>A foe brought down by this turret. False if that victim was already counted.</summary>
        public bool CreditTakedown(Pawn victim)
        {
            Engage();
            SentinelRecord r = rec;
            if (r.recentVictims.Contains(victim.thingIDNumber)) return false;
            r.recentVictims.Add(victim.thingIDNumber);
            while (r.recentVictims.Count > AftermathTuning.SentinelRecentVictimsKept) r.recentVictims.RemoveAt(0);
            r.takedowns++;
            if (!r.Named && r.battles >= AftermathTuning.SentinelMinBattles && r.takedowns >= AftermathTuning.SentinelMinTakedowns)
                GiveName();
            return true;
        }

        public void AddComrade(Pawn p)
        {
            if (p == null) return;
            SentinelRecord r = Rec();
            if (r.HasComrade(p.thingIDNumber) || r.comradeIds.Count >= AftermathTuning.SentinelMaxComrades) return;
            r.comradeIds.Add(p.thingIDNumber);
            r.comradeNames.Add(p.LabelShort);
        }

        public bool RecentlyEngaged(int now) => rec != null && rec.lastEngagedTick >= 0 && now - rec.lastEngagedTick <= AftermathTuning.SentinelComradeWindowTicks;

        void GiveName()
        {
            rec.name = NameGenerator.GenerateName(AftermathDefOf.Aftermath_NamerSentinel, SentinelUtility.NameFree, appendNumberIfNameUsed: true);
            rec.namedTick = Find.TickManager.TicksGame;
            if (!parent.Spawned) return;
            Messages.Message("Aftermath_SentinelNamed".Translate(rec.name, parent.def.label, rec.takedowns, rec.battles),
                parent, MessageTypeDefOf.PositiveEvent);
            MoteMaker.ThrowText(parent.DrawPos + new Vector3(0f, 0f, 0.6f), parent.Map, rec.name, NameColor, 6f);
        }

        // ------------------------------------------------------------------ shown

        public override string TransformLabel(string label)
        {
            if (!On || !Named) return label;
            return "Aftermath_SentinelLabel".Translate(rec.name, label);
        }

        public override string CompInspectStringExtra()
        {
            if (!On || rec == null || rec.takedowns <= 0) return null;
            if (!rec.Named) return "Aftermath_SentinelTallyUnnamed".Translate(rec.takedowns, rec.battles);
            var sb = new StringBuilder();
            sb.Append("Aftermath_SentinelTally".Translate(rec.name, AftermathUtil.Date(rec.firstTick, rec.longLat), rec.takedowns, rec.battles));
            if (rec.comradeNames.Count > 0)
            {
                sb.AppendLine();
                sb.Append("Aftermath_SentinelOldHands".Translate(AftermathUtil.Join(rec.comradeNames)));
            }
            return sb.ToString();
        }

        public override void DrawGUIOverlay()
        {
            if (rec == null || !rec.Named || !On) return;
            if (Find.CameraDriver.CurrentZoom > CameraZoomRange.Close) return;
            GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(parent, -0.4f), OverlayText, NameColor);
        }

        string overlayCache;
        int overlayTakedowns = -1;

        /// <summary>"Old Bess · 14", rebuilt only when the tally changes (DrawGUIOverlay runs every frame).</summary>
        public string OverlayText
        {
            get
            {
                if (overlayCache == null || overlayTakedowns != rec.takedowns)
                {
                    overlayTakedowns = rec.takedowns;
                    overlayCache = "Aftermath_SentinelOverlay".Translate(rec.name, rec.takedowns);
                }
                return overlayCache;
            }
        }

        // ------------------------------------------------------------------ the fall

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            try { SentinelUtility.Fall(this, prevMap, dinfo); }
            catch (System.Exception ex) { Log.ErrorOnce("[Aftermath] sentinel fall failed: " + ex, 0x0AF7E20); }
        }
    }

    /// <summary>The player's turrets on a map, for the "fought beside it" check. Not saved: rebuilt on spawn.</summary>
    public class SentinelTracker : MapComponent
    {
        public readonly List<CompOldSentinel> sentinels = new List<CompOldSentinel>();

        public SentinelTracker(Map map) : base(map) { }

        public static SentinelTracker For(Map map) => map?.GetComponent<SentinelTracker>();

        public void Register(CompOldSentinel c) { if (!sentinels.Contains(c)) sentinels.Add(c); }

        public void Unregister(CompOldSentinel c) => sentinels.Remove(c);
    }
}
