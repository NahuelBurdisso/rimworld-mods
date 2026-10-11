using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gesta
{
    /// <summary>
    /// War Stories: after a fight, the colonists who scored kills tell the tale in social time, and
    /// the number of raiders grows with every telling. Listeners who were there roll their eyes;
    /// listeners who were not are inspired. See DESIGN.md "Feature 10".
    /// </summary>
    public class WarStories : GameComponent
    {
        public const int StoryShelfDays = 15, MaxTellings = 6;

        public class Story : IExposable
        {
            public int realKills, endTick, timesTold;
            public List<int> witnesses = new List<int>();   // thingIDNumbers of colonists who fought in it
            public void ExposeData()
            {
                Scribe_Values.Look(ref realKills, "realKills");
                Scribe_Values.Look(ref endTick, "endTick");
                Scribe_Values.Look(ref timesTold, "timesTold");
                Scribe_Collections.Look(ref witnesses, "witnesses", LookMode.Value);
                if (Scribe.mode == LoadSaveMode.PostLoadInit) witnesses ??= new List<int>();
            }
        }

        // saved
        Dictionary<int, Story> storyByPawn = new Dictionary<int, Story>();
        Dictionary<string, int> toldNumbers = new Dictionary<string, int>();   // "initiatorId_ticksAbs" -> number shown in the log
        // not saved: map id -> kills and fighters of the current engagement
        readonly Dictionary<int, Dictionary<int, int>> kills = new Dictionary<int, Dictionary<int, int>>();
        readonly Dictionary<int, HashSet<int>> fighters = new Dictionary<int, HashSet<int>>();

        static WarStories instance;
        public static WarStories Instance => instance;
        public WarStories(Game game) { instance = this; }

        public Story StoryOf(Pawn p) => p != null && storyByPawn.TryGetValue(p.thingIDNumber, out Story s) ? s : null;

        public void Notify_EngagementStarted(Map map) { kills.Remove(map.uniqueID); fighters.Remove(map.uniqueID); }

        /// <summary>Kill postfix: a colonist killed a hostile during a fight.</summary>
        public void Notify_Killed(Pawn victim, KillContext ctx)
        {
            Pawn killer = ctx.killer;
            if (killer == null || !killer.IsFreeNonSlaveColonist || !ctx.victimWasHostile) return;
            if (EngagementTracker.Instance?.IsActive(ctx.map) != true) return;
            if (!kills.TryGetValue(ctx.map.uniqueID, out var k)) kills[ctx.map.uniqueID] = k = new Dictionary<int, int>();
            k.TryGetValue(killer.thingIDNumber, out int n);
            k[killer.thingIDNumber] = n + 1;
            Notify_Fought(killer, ctx.map);
        }

        /// <summary>PostApplyDamage: a colonist hurt a hostile (they were there).</summary>
        public void Notify_Fought(Pawn colonist, Map map)
        {
            if (map == null || colonist == null || !colonist.IsFreeNonSlaveColonist || EngagementTracker.Instance?.IsActive(map) != true) return;
            if (!fighters.TryGetValue(map.uniqueID, out var f)) fighters[map.uniqueID] = f = new HashSet<int>();
            f.Add(colonist.thingIDNumber);
        }

        public void Notify_EngagementEnded(Map map, bool forced)
        {
            kills.TryGetValue(map.uniqueID, out var k);
            fighters.TryGetValue(map.uniqueID, out var f);
            kills.Remove(map.uniqueID);
            fighters.Remove(map.uniqueID);
            if (forced || k == null || !GestaMod.Settings.warStories) return;
            int now = Find.TickManager.TicksGame;
            var witnesses = f?.ToList() ?? new List<int>();
            foreach (var kv in k)
                storyByPawn[kv.Key] = new Story { realKills = kv.Value, endTick = now, witnesses = witnesses };
        }

        public bool HasFreshStory(Pawn p)
        {
            Story s = StoryOf(p);
            return s != null && s.realKills > 0 && s.timesTold < MaxTellings
                   && Find.TickManager.TicksGame - s.endTick <= StoryShelfDays * GenDate.TicksPerDay;
        }

        /// <summary>Each telling adds 1-3 raiders per previous telling: 3... 5... 9.</summary>
        public int Tell(Pawn teller)
        {
            Story s = StoryOf(teller);
            if (s == null) return 0;
            int shown = s.realKills;
            for (int i = 0; i < s.timesTold; i++) shown += Rand.RangeInclusive(1, 3);
            s.timesTold++;
            string key = teller.thingIDNumber + "_" + GenTicks.TicksAbs;
            toldNumbers[key] = shown;
            if (toldNumbers.Count > 400)
                foreach (string old in toldNumbers.Keys.Take(100).ToList()) toldNumbers.Remove(old);
            return shown;
        }

        public bool TryGetTold(Pawn teller, int ticksAbs, out int n) =>
            toldNumbers.TryGetValue(teller.thingIDNumber + "_" + ticksAbs, out n);

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref storyByPawn, "storyByPawn", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref toldNumbers, "toldNumbers", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                storyByPawn ??= new Dictionary<int, Story>();
                toldNumbers ??= new Dictionary<string, int>();
            }
        }
    }

    public class InteractionWorker_WarStory : InteractionWorker
    {
        public const float BaseWeight = 0.12f, InspireChance = 0.02f;

        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (!GestaMod.Settings.warStories || WarStories.Instance == null) return 0f;
            if (!initiator.IsFreeNonSlaveColonist || !recipient.IsFreeNonSlaveColonist) return 0f;
            if (EngagementTracker.Instance?.IsActive(initiator.MapHeld) == true) return 0f;   // not mid-fight
            return WarStories.Instance.HasFreshStory(initiator) ? BaseWeight : 0f;
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null; letterLabel = null; letterDef = null; lookTargets = null;
            WarStories ws = WarStories.Instance;
            WarStories.Story story = ws?.StoryOf(initiator);
            if (story == null || !ws.HasFreshStory(initiator)) return;   // forced by another mod or an old save: nothing new to tell
            ws.Tell(initiator);
            var memories = recipient.needs?.mood?.thoughts?.memories;
            if (story.witnesses.Contains(recipient.thingIDNumber))
                memories?.TryGainMemory(GestaDefOf.Gesta_NotHowItHappened, initiator);
            else
            {
                memories?.TryGainMemory(GestaDefOf.Gesta_InspiredByWarStory, initiator);
                if (Rand.Chance(InspireChance) && recipient.mindState?.inspirationHandler != null)
                {
                    InspirationDef def = recipient.mindState.inspirationHandler.GetRandomAvailableInspirationDef();
                    if (def != null) recipient.mindState.inspirationHandler.TryStartInspiration(def, "Gesta_WarStory_InspiredBy".Translate(initiator.LabelShort));
                }
            }
            if (initiator.Spawned)
                for (int i = 0; i < 3; i++) FleckMaker.ThrowMetaIcon(initiator.Position, initiator.Map, GestaDefOf.Gesta_Fleck_WarStory);
        }
    }

    /// <summary>
    /// PlayLogEntry_Interaction.ToGameStringFromPOV_Worker(Thing pov, bool forceLog): vanilla always
    /// builds its own log entry, so the exaggerated number is put in afterwards.
    /// </summary>
    [HarmonyPatch(typeof(PlayLogEntry_Interaction), "ToGameStringFromPOV_Worker")]
    public static class Patch_WarStoryLogText
    {
        const string Token = "GESTAKILLS";
        static readonly AccessTools.FieldRef<PlayLogEntry_Interaction, InteractionDef> IntDef =
            AccessTools.FieldRefAccess<PlayLogEntry_Interaction, InteractionDef>("intDef");
        static readonly AccessTools.FieldRef<PlayLogEntry_Interaction, Pawn> Initiator =
            AccessTools.FieldRefAccess<PlayLogEntry_Interaction, Pawn>("initiator");

        public static void Postfix(PlayLogEntry_Interaction __instance, ref string __result)
        {
            if (__result == null || !__result.Contains(Token) || IntDef(__instance) != GestaDefOf.Gesta_RecountsBattle) return;
            Pawn teller = Initiator(__instance);
            string n = teller != null && WarStories.Instance != null && WarStories.Instance.TryGetTold(teller, __instance.Timestamp, out int shown)
                ? shown.ToString() : "Gesta_WarStory_AFew".Translate().ToString();
            __result = __result.Replace(Token, n);
        }
    }
}
