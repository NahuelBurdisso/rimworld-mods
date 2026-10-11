using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Gesta
{
    /// <summary>
    /// Brothers in Arms: two colonists who both hurt the enemy in the same fight, three fights over,
    /// become brothers (or sisters) in arms: a relation, a matching sash, a deeper grief.
    /// See DESIGN.md "Feature 7".
    /// </summary>
    public class BrothersTracker : GameComponent
    {
        public const int FightsNeeded = 3;

        // saved: "minId_maxId" -> shared fights
        Dictionary<string, int> sharedFights = new Dictionary<string, int>();
        // not saved: map id -> colonists who hurt a hostile in the current engagement
        readonly Dictionary<int, HashSet<Pawn>> participants = new Dictionary<int, HashSet<Pawn>>();
        static readonly List<Pawn> tmp = new List<Pawn>();

        static BrothersTracker instance;
        public static BrothersTracker Instance => instance;
        public BrothersTracker(Game game) { instance = this; }

        static string Key(Pawn a, Pawn b)
        {
            int x = a.thingIDNumber, y = b.thingIDNumber;
            return x < y ? x + "_" + y : y + "_" + x;
        }

        public int SharedFights(Pawn a, Pawn b) => sharedFights.TryGetValue(Key(a, b), out int n) ? n : 0;

        public static bool AreBrothers(Pawn a, Pawn b) =>
            a?.relations != null && b != null && a.relations.DirectRelationExists(GestaDefOf.Gesta_BrotherInArms, b);

        /// <summary>From the Pawn.PostApplyDamage postfix.</summary>
        public void Notify_Hit(Pawn victim, Pawn attacker, float damage)
        {
            if (!GestaMod.Settings.brothers || damage <= 0f || attacker == null || !attacker.IsFreeNonSlaveColonist) return;
            if (!victim.HostileTo(Faction.OfPlayer) || victim.Faction == Faction.OfPlayer) return;
            Map map = attacker.MapHeld;
            if (map == null || EngagementTracker.Instance?.IsActive(map) != true) return;
            if (!participants.TryGetValue(map.uniqueID, out HashSet<Pawn> set))
                participants[map.uniqueID] = set = new HashSet<Pawn>();
            set.Add(attacker);
        }

        public void Notify_EngagementStarted(Map map) => participants.Remove(map.uniqueID);

        public void Notify_EngagementEnded(Map map, bool forced)
        {
            if (!participants.TryGetValue(map.uniqueID, out HashSet<Pawn> set)) return;
            participants.Remove(map.uniqueID);
            if (forced || !GestaMod.Settings.brothers) return;
            tmp.Clear();
            tmp.AddRange(set.Where(p => p != null && !p.Dead));
            for (int i = 0; i < tmp.Count; i++)
            for (int j = i + 1; j < tmp.Count; j++)
            {
                Pawn a = tmp[i], b = tmp[j];
                string key = Key(a, b);
                sharedFights.TryGetValue(key, out int n);
                sharedFights[key] = ++n;
                if (n >= FightsNeeded && !AreBrothers(a, b)) Bond(a, b);
            }
            tmp.Clear();
        }

        public void Bond(Pawn a, Pawn b)
        {
            a.relations.AddDirectRelation(GestaDefOf.Gesta_BrotherInArms, b);   // reflexive: both sides
            GiveSash(a);
            GiveSash(b);
            Find.LetterStack.ReceiveLetter("Gesta_Brothers_LetterLabel".Translate(),
                "Gesta_Brothers_LetterText".Translate(a.LabelShort, b.LabelShort, FightsNeeded),
                LetterDefOf.PositiveEvent, new LookTargets(a, b));
            Log.Message($"[Gesta] {a.LabelShort} and {b.LabelShort} are now brothers in arms");
        }

        static void GiveSash(Pawn p)
        {
            Hediff mourning = p.health.hediffSet.GetFirstHediffOfDef(GestaDefOf.Gesta_BrotherSashMourning);
            if (mourning != null) p.health.RemoveHediff(mourning);
            if (!p.health.hediffSet.HasHediff(GestaDefOf.Gesta_BrotherSash)) p.health.AddHediff(GestaDefOf.Gesta_BrotherSash);
        }

        /// <summary>From the Pawn.Kill postfix: surviving brothers grieve harder and wear black.</summary>
        public void Notify_Died(Pawn dead)
        {
            if (dead.relations == null) return;
            foreach (Pawn other in dead.relations.DirectRelations
                         .Where(r => r.def == GestaDefOf.Gesta_BrotherInArms).Select(r => r.otherPawn).ToList())
            {
                if (other == null || other.Dead || other.health == null) continue;
                bool stillHasLivingBrother = other.relations.DirectRelations
                    .Any(r => r.def == GestaDefOf.Gesta_BrotherInArms && r.otherPawn != dead && r.otherPawn != null && !r.otherPawn.Dead);
                if (!stillHasLivingBrother)
                {
                    Hediff sash = other.health.hediffSet.GetFirstHediffOfDef(GestaDefOf.Gesta_BrotherSash);
                    if (sash != null) other.health.RemoveHediff(sash);
                    if (!other.health.hediffSet.HasHediff(GestaDefOf.Gesta_BrotherSashMourning))
                        other.health.AddHediff(GestaDefOf.Gesta_BrotherSashMourning);
                }
            }
        }

        /// <summary>From the MakeDowned postfix: a brother going down hurts to watch.</summary>
        public void Notify_Downed(Pawn downed)
        {
            if (downed.relations == null || downed.MapHeld == null) return;
            foreach (DirectPawnRelation r in downed.relations.DirectRelations)
            {
                if (r.def != GestaDefOf.Gesta_BrotherInArms) continue;
                Pawn other = r.otherPawn;
                if (other == null || other.Dead || other.MapHeld != downed.MapHeld) continue;
                other.needs?.mood?.thoughts?.memories?.TryGainMemory(GestaDefOf.Gesta_BrotherDowned, downed);
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref sharedFights, "sharedFights", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) sharedFights ??= new Dictionary<string, int>();
        }
    }
}
