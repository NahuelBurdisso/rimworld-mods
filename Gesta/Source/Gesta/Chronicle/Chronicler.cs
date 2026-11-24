using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace Gesta
{
    /// <summary>
    /// The Chronicler: an offline, Darkest-Dungeon-style narrator. On the rare decisive beats of a
    /// fight it writes one line across the bottom of the screen, built from the pawn's real history
    /// with a RulePack grammar (tone follows the storyteller). See DESIGN.md "Feature 8".
    /// </summary>
    public class Chronicler : GameComponent
    {
        public enum Beat { FirstDown, Death, Kingslayer, Recovered, Victory }

        public const int CooldownTicks = 5000;      // ~2 in-game hours between ordinary lines
        public const int DeathGapTicks = 600;       // deaths always speak, but not on top of each other
        const int RememberLines = 10;

        // saved
        Dictionary<int, int> timesDowned = new Dictionary<int, int>();
        int lastLineTick = -999999;

        // not saved
        readonly Dictionary<int, bool> firstDownDone = new Dictionary<int, bool>();
        readonly HashSet<int> downedInCombat = new HashSet<int>();   // so waking from surgery is not "getting back up"
        readonly Dictionary<int, Dictionary<Pawn, int>> killsThisFight = new Dictionary<int, Dictionary<Pawn, int>>();
        readonly Queue<string> recent = new Queue<string>();
        public string LastLine { get; private set; }
        public Beat? LastBeat { get; private set; }
        public int LinesSpoken { get; private set; }

        static Chronicler instance;
        public static Chronicler Instance => instance;
        public Chronicler(Game game) { instance = this; }

        public int TimesDowned(Pawn p) => timesDowned.TryGetValue(p.thingIDNumber, out int n) ? n : 0;

        static bool InFight(Map map) => map != null && EngagementTracker.Instance?.IsActive(map) == true;

        // ---- hooks ---------------------------------------------------------------------------

        public void Notify_EngagementStarted(Map map)
        {
            firstDownDone.Remove(map.uniqueID);
            killsThisFight.Remove(map.uniqueID);
        }

        public void Notify_EngagementEnded(Map map, bool forced)
        {
            killsThisFight.TryGetValue(map.uniqueID, out Dictionary<Pawn, int> kills);
            firstDownDone.Remove(map.uniqueID);
            killsThisFight.Remove(map.uniqueID);
            if (forced || kills == null) return;
            int total = kills.Values.Sum();
            Pawn hero = kills.Where(kv => kv.Key != null && !kv.Key.Dead).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
            if (total >= 2 && hero != null && CombatUtil.CountFighters(map) > 0)
                Speak(Beat.Victory, hero, null, map, total);
        }

        /// <param name="attacker">The instigator of the downing blow (null for surgery, hunger, ...).</param>
        public void Notify_ColonistDowned(Pawn pawn, Pawn attacker)
        {
            if (!pawn.IsFreeNonSlaveColonist || attacker == null) return;   // only combat downs count
            timesDowned[pawn.thingIDNumber] = TimesDowned(pawn) + 1;
            downedInCombat.Add(pawn.thingIDNumber);
            Map map = pawn.MapHeld;
            if (!InFight(map) || firstDownDone.ContainsKey(map.uniqueID)) return;
            firstDownDone[map.uniqueID] = true;
            Speak(Beat.FirstDown, pawn, Hostile(attacker), map);
        }

        public void Notify_ColonistRecovered(Pawn pawn)
        {
            if (!downedInCombat.Remove(pawn.thingIDNumber)) return;
            if (!pawn.IsFreeNonSlaveColonist || !InFight(pawn.MapHeld)) return;
            Speak(Beat.Recovered, pawn, null, pawn.MapHeld);
        }

        /// <summary>Only an enemy gets named: friendly fire and berserk friends are not "that face".</summary>
        static Pawn Hostile(Pawn p) => p != null && p.HostileTo(Faction.OfPlayer) && p.Faction != Faction.OfPlayer ? p : null;

        /// <summary>From the Kill postfix.</summary>
        public void Notify_Killed(Pawn victim, KillContext ctx)
        {
            Map map = ctx.map;
            if (victim.IsFreeNonSlaveColonist || (victim.Faction == Faction.OfPlayer && victim.RaceProps.Humanlike && !victim.IsSlave && !victim.IsMutant))
            {
                downedInCombat.Remove(victim.thingIDNumber);
                Pawn enemy = Hostile(ctx.attributedKiller);
                if (InFight(map) || enemy != null)
                    Speak(Beat.Death, victim, enemy, map);
                return;
            }
            Pawn killer = ctx.killer;
            if (killer == null || !killer.IsFreeNonSlaveColonist || !ctx.victimWasHostile) return;
            if (InFight(map))
            {
                if (!killsThisFight.TryGetValue(map.uniqueID, out Dictionary<Pawn, int> kills))
                    killsThisFight[map.uniqueID] = kills = new Dictionary<Pawn, int>();
                kills.TryGetValue(killer, out int n);
                kills[killer] = n + 1;
            }
            if (ctx.victimWasLeader) Speak(Beat.Kingslayer, killer, victim, map);
        }

        // ---- speaking ------------------------------------------------------------------------

        bool CanSpeak(Beat beat)
        {
            int now = Find.TickManager.TicksGame;
            int gap = now - lastLineTick;
            return beat == Beat.Death ? gap >= DeathGapTicks : gap >= CooldownTicks;
        }

        /// <summary>Public for tests: force=true ignores the cooldown.</summary>
        public string Speak(Beat beat, Pawn pawn, Pawn other, Map map, int count = 0, bool force = false)
        {
            if (!GestaMod.Settings.chronicler || pawn == null) return null;
            if (!force && !CanSpeak(beat)) return null;
            string line = Compose(beat, pawn, other, count);
            if (line.NullOrEmpty()) return null;
            lastLineTick = Find.TickManager.TicksGame;
            LastLine = line;
            LastBeat = beat;
            LinesSpoken++;
            if (map != null && Find.CurrentMap == map) ChronicleOverlay.Show(line, Killcam.Instance?.BarsSecondsLeft ?? 0f);
            Log.Message($"[Gesta] chronicle ({beat}): {line}");
            return line;
        }

        public string Compose(Beat beat, Pawn pawn, Pawn other, int count)
        {
            RulePackDef pack = DefDatabase<RulePackDef>.GetNamedSilentFail("Gesta_Chronicle_" + beat);
            if (pack == null) return null;
            string best = null;
            for (int attempt = 0; attempt < 6; attempt++)   // avoid repeating a recent line
            {
                GrammarRequest req = default;
                req.Includes.Add(pack);
                req.Rules.AddRange(GrammarUtility.RulesForPawn("PAWN", pawn, req.Constants, addRelationInfoSymbol: false));
                if (other != null) req.Rules.AddRange(GrammarUtility.RulesForPawn("OTHER", other, req.Constants, addRelationInfoSymbol: false));
                req.Constants["hasOther"] = (other != null).ToString();
                req.Constants["storyteller"] = Find.Storyteller?.def?.defName ?? "Cassandra";
                req.Constants["kills"] = (pawn.records?.GetAsInt(RecordDefOf.Kills) ?? 0).ToString();
                req.Constants["timesDowned"] = TimesDowned(pawn).ToString();
                req.Constants["count"] = count.ToString();
                req.Constants["hasLover"] = (LovePartnerRelationUtility.ExistingMostLikedLovePartner(pawn, false) != null).ToString();
                req.Constants["veteran"] = ((pawn.records?.GetAsInt(RecordDefOf.Kills) ?? 0) >= 10).ToString();
                req.Rules.Add(new Rule_String("kills", req.Constants["kills"]));
                req.Rules.Add(new Rule_String("timesDowned", req.Constants["timesDowned"]));
                req.Rules.Add(new Rule_String("count", req.Constants["count"]));
                string line = GrammarResolver.Resolve("r_line", req, "Gesta chronicle " + beat);
                best = line;
                if (!recent.Contains(line)) break;
            }
            recent.Enqueue(best);
            while (recent.Count > RememberLines) recent.Dequeue();
            return best;
        }

        public override void GameComponentOnGUI() => ChronicleOverlay.Draw();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref timesDowned, "timesDowned", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref lastLineTick, "lastLineTick", -999999);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) timesDowned ??= new Dictionary<int, int>();
        }
    }

    /// <summary>A letterbox caption across the bottom of the screen, in real time (also while paused).</summary>
    public static class ChronicleOverlay
    {
        const float FadeIn = 0.8f, Hold = 5.5f, FadeOut = 1.4f, BandHeight = 64f, BottomMargin = 48f;
        static string text;
        static float start = -999f;

        /// <param name="delay">Real seconds to wait (e.g. until the finale's bars are gone).</param>
        public static void Show(string line, float delay = 0f)
        {
            text = line;
            start = Time.realtimeSinceStartup + Mathf.Max(0f, delay);
        }

        public static void Draw()
        {
            if (text == null || Event.current.type != EventType.Repaint || WorldRendererUtility.WorldSelected) return;
            float age = Time.realtimeSinceStartup - start;
            if (age < 0f) return;   // waiting for its turn
            if (age > FadeIn + Hold + FadeOut) { text = null; return; }
            float a = age < FadeIn ? age / FadeIn : age < FadeIn + Hold ? 1f : 1f - (age - FadeIn - Hold) / FadeOut;

            var band = new Rect(0f, UI.screenHeight - BottomMargin - BandHeight, UI.screenWidth, BandHeight);
            GUI.color = new Color(0f, 0f, 0f, 0.74f * a);
            GUI.DrawTexture(band, BaseContent.WhiteTex);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 0.95f, 0.86f, a);
            Widgets.Label(band.ContractedBy(40f, 4f), "<i>" + text + "</i>");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }
}
