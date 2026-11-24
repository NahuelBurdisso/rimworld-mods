using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Grammar;

namespace Gesta
{
    public enum TauntOutcome { Hit, Backfire, Break }

    /// <summary>One raid's side of the shouting match. Keyed by Lord.loadID.</summary>
    public class TrashTalkLordState : IExposable
    {
        public int lordId;
        public float morale = TrashTalk.StartMorale;
        public int exchanges;
        public int lastTick = -999999;
        public bool over;

        public void ExposeData()
        {
            Scribe_Values.Look(ref lordId, "lordId");
            Scribe_Values.Look(ref morale, "morale", TrashTalk.StartMorale);
            Scribe_Values.Look(ref exchanges, "exchanges");
            Scribe_Values.Look(ref lastTick, "lastTick", -999999);
            Scribe_Values.Look(ref over, "over");
        }
    }

    /// <summary>What the last exchange did (for tests and logs).</summary>
    public class TauntRecord
    {
        public Lord lord;
        public Pawn taunter, target, fled;
        public TauntOutcome outcome;
        public float moraleBefore, moraleAfter;
        public int tick;
        public PlayLogEntry_Interaction entry;
    }

    /// <summary>
    /// Trash Talk: during a standoff a colonist shouts insults across the field at a raid and the
    /// raiders shout back. Hits wear down the raid's morale; at zero its weakest raider breaks and
    /// runs; a bad taunt backfires. See DESIGN.md "Feature 12".
    /// </summary>
    public class TrashTalk : GameComponent
    {
        public const int ScanInterval = 250, ScanOffset = 131, PruneInterval = 2500;
        public const int MapGapTicks = 600, LordCooldownTicks = 1200, ColonistCooldownTicks = 2500;
        public const int MaxExchanges = 3, ReplyDelay = 120, MinSocial = 8;
        public const float MinRange = 8f, MaxRange = 40f, RollChance = 0.4f;
        public const float StartMorale = 100f, BackfireMoraleGain = 15f, XpPerTaunt = 10f;

        class PendingReply
        {
            public Pawn raider, colonist;
            public int due;
            public bool mock;
        }

        // saved
        List<TrashTalkLordState> lordStates = new List<TrashTalkLordState>();
        Dictionary<int, int> colonistLastTaunt = new Dictionary<int, int>();
        // not saved
        readonly Dictionary<int, int> mapLastExchange = new Dictionary<int, int>();
        readonly List<PendingReply> pending = new List<PendingReply>();

        static readonly List<Lord> tmpLords = new List<Lord>();
        static readonly List<Pawn> tmpTaunters = new List<Pawn>();
        static readonly List<Pawn> tmpTargets = new List<Pawn>();
        static readonly List<Pawn> tmpInRange = new List<Pawn>();
        static readonly List<Pawn> tmpFleers = new List<Pawn>();
        static readonly List<float> tmpPowers = new List<float>();

        /// <summary>Tests: forces the scan's per-lord chance.</summary>
        public static float? DebugChance;
        /// <summary>Tests: forces the backfire roll (0 = always backfires when it can, 0.99 = never).</summary>
        public static float? DebugRoll;

        public TauntRecord LastExchange { get; private set; }
        public int RepliesDelivered { get; private set; }
        public PlayLogEntry_Interaction LastReplyEntry { get; private set; }
        public int PendingReplies => pending.Count;

        static TrashTalk instance;
        public static TrashTalk Instance => instance;
        public TrashTalk(Game game) { instance = this; TrashTalkBubbles.Clear(); }

        // ---------------------------------------------------------------- ticking

        public override void GameComponentTick()
        {
            if (!GestaMod.Settings.trashTalk)
            {
                if (pending.Count > 0) pending.Clear();
                TrashTalkBubbles.Clear();
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (pending.Count > 0) DeliverReplies(now);
            if (now % ScanInterval == ScanOffset) ScanAll(now);
            if (now % PruneInterval == ScanOffset) Prune();
        }

        public override void GameComponentUpdate() => TrashTalkBubbles.Update();

        public override void GameComponentOnGUI() => TrashTalkBubbles.OnGUI();

        /// <summary>Tests: run the scan now.</summary>
        public int ScanNow() => ScanAll(Find.TickManager.TicksGame);

        int ScanAll(int now)
        {
            if (!GestaMod.Settings.trashTalk) return 0;
            int made = 0;
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (!CombatUtil.IsPlayerHome(map) || map.lordManager.lords.Count == 0) continue;
                if (mapLastExchange.TryGetValue(map.uniqueID, out int last) && now - last < MapGapTicks) continue;
                tmpLords.Clear();
                tmpLords.AddRange(map.lordManager.lords);
                for (int i = 0; i < tmpLords.Count; i++)
                {
                    Lord lord = tmpLords[i];
                    if (WhyNotLord(lord, now) != null) continue;
                    if (!Rand.Chance(DebugChance ?? RollChance)) continue;
                    if (!TryPickPair(lord, now, out Pawn taunter, out Pawn target)) continue;
                    Exchange(lord, taunter, target, now);
                    made++;
                    break;   // one exchange per map per scan
                }
                tmpLords.Clear();
            }
            return made;
        }

        // ---------------------------------------------------------------- rules

        public TrashTalkLordState StateFor(Lord lord, bool create)
        {
            if (lord == null) return null;
            for (int i = 0; i < lordStates.Count; i++) if (lordStates[i].lordId == lord.loadID) return lordStates[i];
            if (!create) return null;
            var s = new TrashTalkLordState { lordId = lord.loadID };
            lordStates.Add(s);
            return s;
        }

        public static bool IsStandoffToil(LordToil t) =>
            t is LordToil_Stage || t is LordToil_Siege || t is LordToil_AssaultColony
            || t is LordToil_AssaultColonyBreaching || t is LordToil_AssaultColonySappers;

        /// <summary>Null if this raid can be taunted now, else why not.</summary>
        public string WhyNotLord(Lord lord, int now)
        {
            if (!GestaMod.Settings.trashTalk) return "feature off";
            if (lord == null || lord.Map == null || !CombatUtil.IsPlayerHome(lord.Map)) return "not on a player home map";
            Faction f = lord.faction;
            if (f == null || f.IsPlayer || f.def == null || !f.def.humanlikeFaction || !f.HostileTo(Faction.OfPlayer)) return "not a hostile humanlike faction";
            if (!IsStandoffToil(lord.CurLordToil)) return "not staging or attacking";
            if (ChampionsChallenge.Instance?.For(lord) != null) return "champion's challenge active";
            TrashTalkLordState s = StateFor(lord, false);
            if (s != null)
            {
                if (s.over) return "trash talk over";
                if (s.exchanges >= MaxExchanges) return "max exchanges";
                if (now - s.lastTick < LordCooldownTicks) return "lord cooldown";
            }
            return null;
        }

        public static bool QualifiesByPersonality(Pawn p)
        {
            if (SocialLevel(p) >= MinSocial) return true;
            TraitSet t = p.story?.traits;
            return t != null && (t.HasTrait(TraitDefOf.Abrasive) || t.HasTrait(TraitDefOf.Bloodlust) || t.HasTrait(TraitDefOf.Brawler));
        }

        static int SocialLevel(Pawn p)
        {
            SkillRecord s = p.skills?.GetSkill(SkillDefOf.Social);
            return s == null || s.TotallyDisabled ? 0 : s.Level;
        }

        /// <summary>P = Social + 4 (Abrasive) + 2 (Bloodlust or Brawler).</summary>
        public static float Power(Pawn p)
        {
            float power = SocialLevel(p);
            TraitSet t = p.story?.traits;
            if (t != null)
            {
                if (t.HasTrait(TraitDefOf.Abrasive)) power += 4f;
                if (t.HasTrait(TraitDefOf.Bloodlust) || t.HasTrait(TraitDefOf.Brawler)) power += 2f;
            }
            return power;
        }

        public static float BackfireChance(float power) => Mathf.Clamp(0.45f - 0.035f * power, 0.05f, 0.45f);

        public static float MoraleDamage(float power) => 15f + 2.5f * power;

        static bool CanSpeak(Pawn p) =>
            p.Spawned && !p.Dead && !p.Downed && !p.InMentalState && p.Awake() && !p.IsMutant
            && p.RaceProps.Humanlike && p.DevelopmentalStage.Adult()
            && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking);

        /// <summary>Null if this colonist may shout now, else why not.</summary>
        public string WhyNotTaunter(Pawn p, int now)
        {
            if (p == null || !p.IsFreeNonSlaveColonist || p.IsQuestLodger()) return "not a free colonist";
            if (!p.Spawned || p.Dead || p.Downed) return "not standing";
            if (!p.RaceProps.Humanlike || !p.DevelopmentalStage.Adult() || p.IsMutant) return "not a humanlike adult";
            if (p.InMentalState) return "in a mental state";
            if (!p.Awake()) return "asleep";
            if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Talking)) return "cannot talk";
            if (p.skills?.GetSkill(SkillDefOf.Social)?.TotallyDisabled != false) return "incapable of social";
            if (!QualifiesByPersonality(p)) return "not sharp-tongued enough";
            if (colonistLastTaunt.TryGetValue(p.thingIDNumber, out int last) && now - last < ColonistCooldownTicks) return "colonist cooldown";
            return null;
        }

        /// <summary>Null if this raider can be taunted as part of the lord, else why not.</summary>
        public static string WhyNotTarget(Pawn r, Lord lord)
        {
            if (r == null || !r.Spawned || r.Dead) return "not spawned";
            if (r.IsPrisoner) return "prisoner";
            if (lord == null || r.GetLord() != lord) return "not part of the raid";
            if (!r.RaceProps.Humanlike || !r.DevelopmentalStage.Adult() || r.IsMutant) return "not a humanlike adult";
            if (r.Downed) return "downed";
            if (r.InMentalState) return "in a mental state";
            if (!r.Awake()) return "asleep";
            if (r.Fogged()) return "fogged";
            if (!r.health.capacities.CapableOf(PawnCapacityDefOf.Hearing)) return "cannot hear";
            if (r.ThreatDisabled(null)) return "not a threat";
            return null;
        }

        /// <summary>Range, sight and melee between a colonist and a raider.</summary>
        public static string WhyNotPair(Pawn c, Pawn r)
        {
            if (c.Map != r.Map) return "different maps";
            float d2 = (c.Position - r.Position).LengthHorizontalSquared;
            if (d2 < MinRange * MinRange) return "too close";
            if (d2 > MaxRange * MaxRange) return "too far";
            if (InMelee(c) || InMelee(r)) return "in melee";
            if (!GenSight.LineOfSight(c.Position, r.Position, c.Map)) return "no line of sight";
            return null;
        }

        static bool InMelee(Pawn p)
        {
            if (p.CurJobDef == JobDefOf.AttackMelee) return true;
            Map map = p.Map;
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(p))
            {
                if (!c.InBounds(map)) continue;
                List<Thing> things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                    if (things[i] is Pawn o && o != p && !o.Downed && !o.Dead && o.HostileTo(p)) return true;
            }
            return false;
        }

        bool TryPickPair(Lord lord, int now, out Pawn taunter, out Pawn target)
        {
            taunter = null; target = null;
            Map map = lord.Map;
            tmpTargets.Clear();
            List<Pawn> owned = lord.ownedPawns;
            for (int i = 0; i < owned.Count; i++) if (WhyNotTarget(owned[i], lord) == null) tmpTargets.Add(owned[i]);
            if (tmpTargets.Count == 0) return false;
            tmpTaunters.Clear();
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++) if (WhyNotTaunter(colonists[i], now) == null) tmpTaunters.Add(colonists[i]);
            // the sharpest tongue speaks first; ids break ties so the pick is stable
            tmpTaunters.Sort((a, b) =>
            {
                int c = Power(b).CompareTo(Power(a));
                return c != 0 ? c : a.thingIDNumber.CompareTo(b.thingIDNumber);
            });
            for (int i = 0; i < tmpTaunters.Count && target == null; i++)
            {
                Pawn c = tmpTaunters[i];
                tmpInRange.Clear();
                for (int k = 0; k < tmpTargets.Count; k++)
                {
                    float d2 = (c.Position - tmpTargets[k].Position).LengthHorizontalSquared;
                    if (d2 >= MinRange * MinRange && d2 <= MaxRange * MaxRange) tmpInRange.Add(tmpTargets[k]);
                }
                IntVec3 from = c.Position;
                tmpInRange.Sort((a, b) => (a.Position - from).LengthHorizontalSquared.CompareTo((b.Position - from).LengthHorizontalSquared));
                for (int k = 0; k < tmpInRange.Count; k++)
                {
                    if (WhyNotPair(c, tmpInRange[k]) != null) continue;
                    taunter = c;
                    target = tmpInRange[k];
                    break;
                }
            }
            tmpTargets.Clear(); tmpTaunters.Clear(); tmpInRange.Clear();
            return target != null;
        }

        /// <summary>
        /// Tests and other features: run one exchange between this colonist and raider if every rule
        /// except the scan's chance and its per-map spacing allows it. Returns why not, or null when it happened.
        /// </summary>
        public string TryExchange(Lord lord, Pawn taunter, Pawn target)
        {
            int now = Find.TickManager.TicksGame;
            string why = WhyNotLord(lord, now) ?? WhyNotTaunter(taunter, now) ?? WhyNotTarget(target, lord) ?? WhyNotPair(taunter, target);
            if (why != null) return why;
            Exchange(lord, taunter, target, now);
            return null;
        }

        // ---------------------------------------------------------------- the exchange

        void Exchange(Lord lord, Pawn taunter, Pawn target, int now)
        {
            Map map = lord.Map;
            TrashTalkLordState s = StateFor(lord, true);
            float power = Power(taunter);
            float before = s.morale;
            float roll = DebugRoll ?? Rand.Value;
            TauntOutcome outcome;
            Pawn fled = null;
            if (roll < BackfireChance(power))
            {
                outcome = TauntOutcome.Backfire;
                s.morale = Mathf.Min(StartMorale, s.morale + BackfireMoraleGain);
                taunter.needs?.mood?.thoughts?.memories?.TryGainMemory(GestaDefOf.Gesta_TauntBackfired);
            }
            else
            {
                outcome = TauntOutcome.Hit;
                s.morale -= MoraleDamage(power);
                if (s.morale <= 0f)
                {
                    s.morale = 0f;
                    s.over = true;
                    fled = TryBreak(lord, target, taunter);
                    if (fled != null) outcome = TauntOutcome.Break;
                }
            }
            s.exchanges++;
            s.lastTick = now;
            colonistLastTaunt[taunter.thingIDNumber] = now;
            mapLastExchange[map.uniqueID] = now;
            taunter.skills?.Learn(SkillDefOf.Social, XpPerTaunt);

            RulePackDef sentence = outcome == TauntOutcome.Backfire ? GestaDefOf.Gesta_TrashTalk_Backfire
                : outcome == TauntOutcome.Break ? GestaDefOf.Gesta_TrashTalk_Break : GestaDefOf.Gesta_TrashTalk_Hit;
            var entry = new PlayLogEntry_Interaction(GestaDefOf.Gesta_TrashTalk, taunter, target, new List<RulePackDef> { sentence });
            Find.PlayLog.Add(entry);
            Shout(taunter, target, GestaDefOf.Gesta_TrashTalk, GestaDefOf.Gesta_TrashTalk_Shouts, colonistSide: true);

            if (outcome == TauntOutcome.Break)
                Messages.Message("Gesta_TrashTalk_Broke".Translate(fled.LabelShort, taunter.LabelShort), fled, MessageTypeDefOf.PositiveEvent);
            else if (target.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                pending.Add(new PendingReply { raider = target, colonist = taunter, due = now + ReplyDelay, mock = outcome == TauntOutcome.Backfire });

            LastExchange = new TauntRecord
            {
                lord = lord, taunter = taunter, target = target, fled = fled, outcome = outcome,
                moraleBefore = before, moraleAfter = s.morale, tick = now, entry = entry
            };
            Log.Message($"[Gesta] trash talk: {taunter.LabelShort} -> {target.LabelShort} ({lord.faction?.Name}): {outcome}, morale {before:0} -> {s.morale:0}"
                        + (fled != null ? $", {fled.LabelShort} flees" : ""));
        }

        /// <summary>
        /// Morale hit zero: the target runs if he is one of the weaker raiders, else the weakest
        /// able raider does. Never the faction leader or a boss. Returns who fled.
        /// </summary>
        static Pawn TryBreak(Lord lord, Pawn target, Pawn taunter)
        {
            tmpFleers.Clear();
            tmpPowers.Clear();
            Pawn leader = lord.faction?.leader;
            foreach (Pawn p in lord.ownedPawns)
            {
                if (WhyNotTarget(p, lord) != null || p == leader || p.kindDef == null || p.kindDef.isBoss || p.kindDef.factionLeader) continue;
                tmpFleers.Add(p);
                tmpPowers.Add(p.kindDef.combatPower);
            }
            Pawn fled = null;
            if (tmpFleers.Count > 0)
            {
                tmpPowers.Sort();
                float median = tmpPowers[(tmpPowers.Count - 1) / 2];
                tmpFleers.Sort((a, b) =>
                {
                    int c = a.kindDef.combatPower.CompareTo(b.kindDef.combatPower);
                    return c != 0 ? c : a.health.summaryHealth.SummaryHealthPercent.CompareTo(b.health.summaryHealth.SummaryHealthPercent);
                });
                if (tmpFleers.Contains(target) && target.kindDef.combatPower <= median)
                {
                    tmpFleers.Remove(target);
                    tmpFleers.Insert(0, target);
                }
                string reason = "Gesta_TrashTalk_FleeReason".Translate(taunter.LabelShort);
                for (int i = 0; i < tmpFleers.Count && fled == null; i++)
                    if (tmpFleers[i].mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.PanicFlee, reason, otherPawn: taunter))
                        fled = tmpFleers[i];
            }
            tmpFleers.Clear();
            tmpPowers.Clear();
            return fled;
        }

        void DeliverReplies(int now)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingReply r = pending[i];
                if (now < r.due) continue;
                pending.RemoveAt(i);
                Pawn raider = r.raider, colonist = r.colonist;
                if (raider == null || colonist == null || !CanSpeak(raider) || !colonist.Spawned || colonist.Dead || colonist.Map != raider.Map) continue;
                InteractionDef def = r.mock ? GestaDefOf.Gesta_TrashTalkMock : GestaDefOf.Gesta_TrashTalkReply;
                LastReplyEntry = new PlayLogEntry_Interaction(def, raider, colonist, null);
                Find.PlayLog.Add(LastReplyEntry);
                Shout(raider, colonist, def, r.mock ? GestaDefOf.Gesta_TrashTalk_Mockery : GestaDefOf.Gesta_TrashTalk_Retorts, colonistSide: false);
                RepliesDelivered++;
            }
        }

        static void Shout(Pawn speaker, Pawn target, InteractionDef def, RulePackDef lines, bool colonistSide)
        {
            if (!speaker.Spawned) return;
            MoteMaker.MakeInteractionBubble(speaker, target, def.interactionMote, def.GetSymbol(speaker.Faction, speaker.Ideo), def.GetSymbolColor(speaker.Faction));
            if (!GestaMod.Settings.trashTalkBubbles) return;
            string text = ResolveLine(lines, speaker, target, colonistSide ? target : speaker);
            if (!text.NullOrEmpty()) TrashTalkBubbles.Add(speaker, text, colonistSide);
        }

        public static string ResolveLine(RulePackDef pack, Pawn speaker, Pawn target, Pawn raider)
        {
            GrammarRequest req = default;
            req.Includes.Add(pack);
            req.Rules.AddRange(GrammarUtility.RulesForPawn("SPEAKER", speaker, req.Constants, addRelationInfoSymbol: false, addTags: false));
            req.Rules.AddRange(GrammarUtility.RulesForPawn("TARGET", target, req.Constants, addRelationInfoSymbol: false, addTags: false));
            req.Constants["raiderTribal"] = (raider?.Faction?.def != null && raider.Faction.def.techLevel <= TechLevel.Neolithic).ToString();
            return GrammarResolver.Resolve("r_shout", req, "Gesta trash talk");
        }

        // ---------------------------------------------------------------- housekeeping

        /// <summary>Tests: forget every cooldown (lords keep their morale and counts).</summary>
        public void DebugResetCooldowns()
        {
            colonistLastTaunt.Clear();
            mapLastExchange.Clear();
            foreach (TrashTalkLordState s in lordStates) s.lastTick = -999999;
        }

        void Prune()
        {
            var live = new HashSet<int>();
            foreach (Map map in Find.Maps)
                foreach (Lord l in map.lordManager.lords) live.Add(l.loadID);
            lordStates.RemoveAll(s => !live.Contains(s.lordId));
            int now = Find.TickManager.TicksGame;
            var stale = new List<int>();
            foreach (var kv in colonistLastTaunt) if (now - kv.Value >= ColonistCooldownTicks) stale.Add(kv.Key);
            foreach (int id in stale) colonistLastTaunt.Remove(id);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref lordStates, "lordStates", LookMode.Deep);
            Scribe_Collections.Look(ref colonistLastTaunt, "colonistLastTaunt", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                lordStates ??= new List<TrashTalkLordState>();
                lordStates.RemoveAll(s => s == null);
                colonistLastTaunt ??= new Dictionary<int, int>();
            }
        }
    }
}
