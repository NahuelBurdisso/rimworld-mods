using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Gesta
{
    /// <summary>
    /// The Breaking Blow: when the last enemy of a big raid falls, the camera finds the one who did
    /// it, time slows for a breath, black bars close in and the moment is named. See DESIGN.md "Feature 9".
    /// </summary>
    public class Killcam : GameComponent
    {
        public const float SlowMoRealSeconds = 2.2f, SlowMoFactor = 0.3f, BarsRealSeconds = 4.5f;
        public const int MinGapTicks = GenDate.TicksPerDay / 2;

        // saved: raid lord loadID -> raid points
        Dictionary<int, float> raidPoints = new Dictionary<int, float>();
        int lastFinaleTick = -999999;

        // presentation (not saved)
        float startReal = -999f;
        bool pendingCamera;
        Vector3 focus;
        string caption;
        public Pawn LastShooter { get; private set; }
        public int FinalesPlayed { get; private set; }

        static Killcam instance;
        public static Killcam Instance => instance;
        public Killcam(Game game) { instance = this; }

        public static bool SlowMoActive => instance != null && Time.realtimeSinceStartup - instance.startReal < SlowMoRealSeconds;
        public bool BarsActive => Time.realtimeSinceStartup - startReal < BarsRealSeconds;
        public float PointsOf(Lord lord) => lord != null && raidPoints.TryGetValue(lord.loadID, out float p) ? p : 0f;

        /// <summary>RaidStrategyWorker.MakeLords postfix: remember how big each raid was.</summary>
        public void Notify_RaidLords(IncidentParms parms, List<Pawn> pawns)
        {
            foreach (Pawn p in pawns)
            {
                Lord lord = p?.GetLord();
                if (lord != null) raidPoints[lord.loadID] = parms.points;
            }
        }

        /// <summary>Lord.Notify_PawnLost postfix.</summary>
        public void Notify_PawnLost(Lord lord, Pawn pawn, PawnLostCondition cond, DamageInfo? dinfo)
        {
            if (!GestaMod.Settings.killcam || lord == null || !raidPoints.TryGetValue(lord.loadID, out float points)) return;
            if (lord.ownedPawns.Count > 0) return;
            raidPoints.Remove(lord.loadID);
            if (cond != PawnLostCondition.Killed && cond != PawnLostCondition.Incapped) return;
            if (points < GestaMod.Settings.killcamMinPoints) return;
            Pawn shooter = dinfo?.Instigator as Pawn;
            Map map = pawn.MapHeld;
            if (shooter == null || !shooter.IsFreeNonSlaveColonist || map == null || !map.IsPlayerHome) return;
            if (CombatUtil.ActiveHostilePawns(map) > 0) return;   // another raid group is still fighting
            int now = Find.TickManager.TicksGame;
            if (now - lastFinaleTick < MinGapTicks) return;
            lastFinaleTick = now;
            Play(shooter, pawn, lord.faction, map);
        }

        void Play(Pawn shooter, Pawn victim, Faction faction, Map map)
        {
            LastShooter = shooter;
            FinalesPlayed++;
            Vector3 a = shooter.DrawPos, b = victim.PositionHeld.ToVector3Shifted();
            if (shooter.Spawned && a.ToIntVec3().InBounds(map) && b.ToIntVec3().InBounds(map))
                FleckMaker.ConnectingLine(a, b, GestaDefOf.Gesta_Fleck_Tracer, map, 0.35f);
            string factionName = faction?.Name ?? "Gesta_Killcam_TheEnemy".Translate().ToString();
            caption = "Gesta_Killcam_Caption".Translate(shooter.LabelShort, factionName);
            Messages.Message(caption, new LookTargets(shooter), MessageTypeDefOf.PositiveEvent);
            Log.Message($"[Gesta] breaking blow: {shooter.LabelShort} ended the raid of {factionName}");
            if (Find.CurrentMap != map) return;
            focus = (a + b) / 2f;
            pendingCamera = true;
            startReal = Time.realtimeSinceStartup;
        }

        public override void GameComponentUpdate()
        {
            if (!pendingCamera) return;
            pendingCamera = false;
            if (GestaMod.Settings.killcamCamera) Find.CameraDriver.PanToMapLocAndSize(focus, 12f, 1.2f);
            if (Find.TickManager.CurTimeSpeed > TimeSpeed.Normal) Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        }

        public override void GameComponentOnGUI()
        {
            if (!BarsActive || Event.current.type != EventType.Repaint || WorldRendererUtility.WorldSelected) return;
            float t = Time.realtimeSinceStartup - startReal;
            float k = Mathf.Min(1f, t / 0.35f) * Mathf.Min(1f, (BarsRealSeconds - t) / 0.6f);
            float h = UI.screenHeight * 0.085f * k;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, UI.screenWidth, h), BaseContent.WhiteTex);
            GUI.DrawTexture(new Rect(0f, UI.screenHeight - h, UI.screenWidth, h), BaseContent.WhiteTex);
            if (caption != null && k > 0.5f)
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(1f, 0.9f, 0.7f, (k - 0.5f) * 2f);
                Widgets.Label(new Rect(0f, UI.screenHeight - h, UI.screenWidth, h), caption);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
            GUI.color = Color.white;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref raidPoints, "raidPoints", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref lastFinaleTick, "lastFinaleTick", -999999);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) raidPoints ??= new Dictionary<int, float>();
        }
    }

    /// <summary>RaidStrategyWorker.MakeLords(IncidentParms parms, List&lt;Pawn&gt; pawns).</summary>
    [HarmonyPatch(typeof(RaidStrategyWorker), nameof(RaidStrategyWorker.MakeLords))]
    public static class Patch_MakeLords
    {
        public static void Postfix(IncidentParms parms, List<Pawn> pawns) => Killcam.Instance?.Notify_RaidLords(parms, pawns);
    }

    /// <summary>Lord.Notify_PawnLost(Pawn pawn, PawnLostCondition cond, DamageInfo? dinfo = null).</summary>
    [HarmonyPatch(typeof(Lord), nameof(Lord.Notify_PawnLost))]
    public static class Patch_LordPawnLost
    {
        public static void Postfix(Lord __instance, Pawn pawn, PawnLostCondition cond, DamageInfo? dinfo) =>
            Killcam.Instance?.Notify_PawnLost(__instance, pawn, cond, dinfo);
    }

    /// <summary>TickManager.TickRateMultiplier: a breath of slow motion while the finale plays.</summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
    public static class Patch_TickRateMultiplier
    {
        public static void Postfix(ref float __result)
        {
            if (__result > 0f && Killcam.SlowMoActive && GestaMod.Settings.killcamSlowMo)
                __result = Mathf.Min(__result, Killcam.SlowMoFactor);
        }
    }
}
