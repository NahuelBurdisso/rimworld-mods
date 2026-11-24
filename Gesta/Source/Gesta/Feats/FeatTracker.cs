using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gesta
{
    /// <summary>
    /// Feats of Arms: legendary moments in a fight earn a colonist a title, a tale and a golden
    /// column of light. Each feat once per pawn. See DESIGN.md "Feature 2".
    /// </summary>
    public class FeatTracker : GameComponent
    {
        public const int TripleKillWindow = 600;
        public const float BigBeastBodySize = 2.5f;
        public const float BigBeastCombatPower = 300f;
        public const float LifelineRadius = 15f;
        const int LifelineInterval = 60, LifelineChecksNeeded = 4;

        // saved: pawn id -> feat defNames joined by ','; pawn id -> last title this mod gave
        Dictionary<int, string> featsByPawn = new Dictionary<int, string>();
        Dictionary<int, string> epithetByPawn = new Dictionary<int, string>();

        // not saved
        readonly Dictionary<int, List<int>> recentKillTicks = new Dictionary<int, List<int>>();
        // pawn id -> (consecutive checks under fire, tick of the last one)
        readonly Dictionary<int, (int count, int tick)> lifelineChecks = new Dictionary<int, (int, int)>();
        static readonly List<FeatDef> tmpEarned = new List<FeatDef>();

        static FeatTracker instance;
        public static FeatTracker Instance => instance;

        public FeatTracker(Game game) { instance = this; }

        public bool Has(Pawn p, FeatDef feat) =>
            featsByPawn.TryGetValue(p.thingIDNumber, out string s) && (',' + s + ',').Contains(',' + feat.defName + ',');

        public void Notify_Killed(Pawn victim, KillContext ctx)
        {
            if (!GestaMod.Settings.feats) return;
            Pawn killer = ctx.killer;
            if (!CanEarn(killer) || !ctx.victimWasHostile || ctx.ByFire) return;

            int now = Find.TickManager.TicksGame;
            if (!recentKillTicks.TryGetValue(killer.thingIDNumber, out List<int> ticks))
                recentKillTicks[killer.thingIDNumber] = ticks = new List<int>();
            ticks.RemoveAll(t => now - t > TripleKillWindow);
            ticks.Add(now);
            // one kill can earn several feats: collected so the pawn gets one title and one message
            tmpEarned.Clear();
            if (ticks.Count >= 3) tmpEarned.Add(GestaDefOf.Gesta_Feat_TripleKill);
            if (ctx.Melee && (ctx.victimBodySize >= BigBeastBodySize || ctx.victimCombatPower >= BigBeastCombatPower))
                tmpEarned.Add(GestaDefOf.Gesta_Feat_BeastBreaker);
            if (ctx.victimWasLeader) tmpEarned.Add(GestaDefOf.Gesta_Feat_Kingslayer);
            if (tmpEarned.Count > 0) TryAward(killer, tmpEarned);
            tmpEarned.Clear();
        }

        /// <summary>Real colonists only: no slaves, no quest lodgers, no corpses.</summary>
        public static bool CanEarn(Pawn p) => p != null && !p.Dead && p.IsFreeNonSlaveColonist && !p.IsQuestLodger();

        public override void GameComponentTick()
        {
            if (!GestaMod.Settings.feats || Find.TickManager.TicksGame % LifelineInterval != 0) return;
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                List<Pawn> colonists = maps[m].mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++) CheckLifeline(colonists[i]);
            }
        }

        void CheckLifeline(Pawn carrier)
        {
            if (!(carrier.carryTracker?.CarriedThing is Pawn carried)) return;
            int id = carrier.thingIDNumber;
            bool underFire = CanEarn(carrier)
                             && carried.Downed && carried.Faction == Faction.OfPlayer && carried.RaceProps.Humanlike
                             && !carried.DevelopmentalStage.Baby()   // carrying a baby to safety is parenting, not a feat
                             && HostileInSight(carrier, LifelineRadius);
            if (!underFire)
            {
                lifelineChecks.Remove(id);
                return;
            }
            int now = Find.TickManager.TicksGame;
            // a gap (left the map, stopped carrying for a while) starts the count again
            int n = lifelineChecks.TryGetValue(id, out var prev) && now - prev.tick <= LifelineInterval ? prev.count + 1 : 1;
            lifelineChecks[id] = (n, now);
            if (n >= LifelineChecksNeeded)
            {
                lifelineChecks.Remove(id);
                tmpEarned.Clear();
                tmpEarned.Add(GestaDefOf.Gesta_Feat_Lifeline);
                TryAward(carrier, tmpEarned);
                tmpEarned.Clear();
            }
        }

        static bool HostileInSight(Pawn p, float radius)
        {
            float r2 = radius * radius;
            foreach (IAttackTarget t in p.Map.attackTargetsCache.TargetsHostileToColony)
            {
                if (t.Thing is Pawn h && !h.Downed && (h.Position - p.Position).LengthHorizontalSquared <= r2
                    && GenHostility.IsActiveThreatToPlayer(t) && GenSight.LineOfSight(h.Position, p.Position, p.Map))
                    return true;
            }
            return false;
        }

        public bool TryAward(Pawn pawn, FeatDef feat)
        {
            var one = new List<FeatDef> { feat };
            return TryAward(pawn, one);
        }

        /// <summary>
        /// Awards every new feat in the list at once: one tale per feat, but a single beam, title,
        /// memory and message (the last feat in the list names the pawn).
        /// </summary>
        public bool TryAward(Pawn pawn, List<FeatDef> candidates)
        {
            int id = pawn.thingIDNumber;
            FeatDef naming = null;
            string labels = null;
            foreach (FeatDef feat in candidates)
            {
                if (feat == null || Has(pawn, feat)) continue;
                featsByPawn[id] = featsByPawn.TryGetValue(id, out string s) && !s.NullOrEmpty() ? s + "," + feat.defName : feat.defName;
                if (feat.tale != null) TaleRecorder.RecordTale(feat.tale, pawn);
                naming = feat;
                labels = labels == null ? feat.LabelCap.ToString() : labels + ", " + feat.LabelCap;
            }
            if (naming == null) return false;

            string epithet = naming.epithets.RandomElement();
            bool titled = GestaMod.Settings.featTitles && TryGiveTitle(pawn, epithet);
            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(GestaDefOf.Gesta_DidAFeat);
            FeatEffects.Play(pawn, labels);

            string text = titled ? "Gesta_Feat_EarnedTitle".Translate(pawn.LabelShort, labels, epithet)
                                 : "Gesta_Feat_Earned".Translate(pawn.LabelShort, labels);
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
                Messages.Message(text, pawn, MessageTypeDefOf.PositiveEvent);
            Log.Message($"[Gesta] feat {labels} for {pawn.LabelShort} (title: {(titled ? epithet : "kept")})");
            return true;
        }

        /// <summary>Only replaces the backstory title or a title this mod gave; never one the player typed.</summary>
        bool TryGiveTitle(Pawn pawn, string epithet)
        {
            Pawn_StoryTracker story = pawn.story;
            if (story == null) return false;
            string current = story.Title;
            bool ours = epithetByPawn.TryGetValue(pawn.thingIDNumber, out string given) && current == given;
            if (current != story.TitleDefault && !ours) return false;
            story.Title = epithet;
            epithetByPawn[pawn.thingIDNumber] = epithet;
            return true;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref featsByPawn, "featsByPawn", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref epithetByPawn, "epithetByPawn", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                featsByPawn ??= new Dictionary<int, string>();
                epithetByPawn ??= new Dictionary<int, string>();
            }
        }
    }

    public static class FeatEffects
    {
        public static readonly Color Gold = new Color(1f, 0.84f, 0.4f);

        public static void Play(Pawn pawn, string label)
        {
            Map map = pawn.MapHeld;
            if (map == null || !pawn.PositionHeld.InBounds(map)) return;
            Vector3 feet = pawn.DrawPos;
            if (!pawn.Spawned) feet = pawn.PositionHeld.ToVector3Shifted();

            // the beam's texture is anchored at its centre: lift it so its foot sits at the pawn
            var beam = FleckMaker.GetDataStatic(feet + new Vector3(0f, 0f, 2.6f), map, GestaDefOf.Gesta_Fleck_FeatBeam);
            map.flecks.CreateFleck(beam);
            map.flecks.CreateFleck(FleckMaker.GetDataStatic(feet, map, GestaDefOf.Gesta_Fleck_FeatBloom, 2.2f));
            MoteMaker.ThrowText(feet + new Vector3(0f, 0f, 1.1f), map, label, Gold, 3.5f);
        }
    }
}
