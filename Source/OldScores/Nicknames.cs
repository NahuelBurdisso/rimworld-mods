using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Grammar;

namespace OldScores
{
    /// <summary>Feature 1 — Enemy Nickname. See DESIGN.md.</summary>
    public partial class OldScoresTracker
    {
        static readonly string[] NicknameTraits =
        {
            "Tough", "Brawler", "Pyromaniac", "Nimble", "Wimp", "Bloodlust", "Psychopath",
            "Kind", "Cannibal", "Masochist", "Gourmand", "Ascetic",
        };


        public NicknameRecord GetNickname(Pawn colonist, Faction faction)
        {
            for (int i = 0; i < nicknames.Count; i++)
            {
                NicknameRecord r = nicknames[i];
                if (r.colonistId == colonist.thingIDNumber && r.faction == faction) return r;
            }
            return null;
        }

        public void Notify_Kill(Pawn victim, KillContext ctx)
        {
            if (!OldScoresMod.Settings.nicknames) return;
            Pawn killer = ctx.killer;
            if (killer == null || killer.Dead || !killer.IsFreeNonSlaveColonist || killer.IsQuestLodger()) return;
            Faction faction = ctx.victimFaction;
            if (!ctx.victimHumanlike || faction == null || faction.IsPlayer || !ctx.victimHostile || ctx.victimDowned) return;

            NicknameRecord rec = GetNickname(killer, faction);
            if (rec == null)
            {
                rec = new NicknameRecord { colonistId = killer.thingIDNumber, faction = faction };
                nicknames.Add(rec);
            }
            rec.colonistName = killer.LabelShort;
            if (rec.Named) return;
            rec.kills++;
            if (ctx.victimWasLeader || rec.kills >= OldScoresTuning.KillsForNickname)
                GiveNickname(killer, rec, ctx.victimWasLeader ? victim : null);
        }

        void GiveNickname(Pawn colonist, NicknameRecord rec, Pawn killedLeader)
        {
            rec.nickname = GenerateNickname(colonist);
            rec.namedTickAbs = Find.TickManager.TicksAbs;
            string factionName = rec.faction.Name;
            TaggedString text = killedLeader != null
                ? "OldScores_Nickname_LetterTextLeader".Translate(factionName, colonist.LabelShort, rec.nickname, killedLeader.LabelShort, colonist.Named("PAWN"))
                : "OldScores_Nickname_LetterText".Translate(factionName, colonist.LabelShort, rec.nickname, rec.kills, colonist.Named("PAWN"));
            Find.LetterStack.ReceiveLetter("OldScores_Nickname_LetterLabel".Translate(colonist.LabelShort), text,
                LetterDefOf.NeutralEvent, colonist, rec.faction);
            Log.Message($"[Old Scores] {factionName} named {colonist.LabelShort} \"{rec.nickname}\"");
        }

        /// <summary>"the Unkillable Toaster": lower-case article, words capitalised by the rule pack.</summary>
        public static string GenerateNickname(Pawn colonist)
        {
            var request = new GrammarRequest();
            request.Includes.Add(OldScoresDefOf.OldScores_Namer_EnemyNickname);
            Dictionary<string, string> constants = request.Constants;
            request.Rules.AddRange(GrammarUtility.RulesForPawn("PAWN", colonist, constants, addRelationInfoSymbol: false, addTags: false));
            ThingWithComps weapon = colonist.equipment?.Primary;
            constants["weapon"] = weapon == null ? "none" : weapon.def.IsMeleeWeapon ? "melee" : "ranged";
            if (weapon != null) request.Rules.Add(new Rule_String("WEAPON_label", Find.ActiveLanguageWorker.ToTitleCase(weapon.def.label)));
            constants["trait"] = "none";
            if (colonist.story?.traits != null)
            {
                foreach (string t in NicknameTraits)
                {
                    TraitDef def = DefDatabase<TraitDef>.GetNamedSilentFail(t);
                    if (def != null && colonist.story.traits.HasTrait(def)) { constants["trait"] = t; break; }
                }
            }
            string result = null;
            try { result = GrammarResolver.Resolve("r_name", request, "OldScores nickname", capitalizeFirstSentence: false); }
            catch (System.Exception ex) { Log.ErrorOnce("[Old Scores] nickname grammar failed: " + ex, 0x0D5C01); }
            if (result.NullOrEmpty() || result.Contains("[") || result.Contains("Error")) result = "OldScores_Nickname_Fallback".Translate();
            return result.Trim();
        }

        /// <summary>"the Unkillable Toaster (Pirates of X), the Revolver Ghost (Y)", or null.</summary>
        public string NicknamesLine(Pawn colonist)
        {
            StringBuilder sb = null;
            for (int i = 0; i < nicknames.Count; i++)
            {
                NicknameRecord r = nicknames[i];
                if (r.colonistId != colonist.thingIDNumber || !r.Named || r.faction == null) continue;
                sb ??= new StringBuilder();
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(r.nickname).Append(" (").Append(r.faction.Name).Append(')');
            }
            return sb?.ToString();
        }

        public bool AnyNickname => nicknames.Count > 0;

        static readonly List<Pawn> tmpRaiders = new List<Pawn>();

        /// <summary>Every 250 ticks from the map component: raiders who see her may break and run.</summary>
        public void DreadCheck(Map map)
        {
            if (!OldScoresMod.Settings.nicknames || nicknames.Count == 0) return;
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int c = 0; c < colonists.Count; c++) DreadFrom(colonists[c]);
        }

        /// <summary>
        /// One colonist's share of the dread check: every raider of a faction that nicknamed her and can
        /// see her rolls. Returns how many fell into dread. Public so the harness can attribute results
        /// to one colonist when other nicknamed colonists are on the map.
        /// </summary>
        public int DreadFrom(Pawn colonist)
        {
            if (!OldScoresMod.Settings.nicknames || nicknames.Count == 0) return 0;
            if (colonist == null || !colonist.Spawned || colonist.Downed || colonist.Dead || colonist.Faction != Faction.OfPlayer) return 0;
            Map map = colonist.Map;
            int started = 0;
            for (int i = 0; i < nicknames.Count; i++)
            {
                NicknameRecord rec = nicknames[i];
                if (!rec.Named || rec.colonistId != colonist.thingIDNumber || rec.faction == null) continue;
                tmpRaiders.Clear();
                tmpRaiders.AddRange(map.mapPawns.SpawnedPawnsInFaction(rec.faction));
                for (int k = 0; k < tmpRaiders.Count; k++)
                {
                    Pawn raider = tmpRaiders[k];
                    if (WhyNoDread(raider, colonist, rec.faction) != null) continue;
                    if (!Rand.Chance(OldScoresTuning.DreadChance)) continue;
                    if (raider.mindState.mentalStateHandler.TryStartMentalState(OldScoresDefOf.OldScores_Dread,
                            "OldScores_Dread_Reason".Translate(colonist.LabelShort, rec.nickname), forced: true, otherPawn: colonist))
                    {
                        started++;
                        // two icons a beat apart drift up from him (a text mote was one pixel at normal zoom)
                        FleckMaker.ThrowMetaIcon(raider.Position, map, OldScoresDefOf.OldScores_Fleck_Dread);
                        FleckMaker.ThrowMetaIcon(raider.Position, map, OldScoresDefOf.OldScores_Fleck_Dread, 0.25f);
                    }
                }
                tmpRaiders.Clear();
            }
            return started;
        }

        /// <summary>Null if the raider can be frightened by the colonist now, else why not (for logs and tests).</summary>
        public static string WhyNoDread(Pawn raider, Pawn colonist, Faction faction)
        {
            if (raider == null || !raider.Spawned || raider.Dead) return "not spawned";
            if (raider.Map != colonist.Map) return "other map";
            if (raider.Downed) return "downed";
            if (!raider.RaceProps.Humanlike) return "not humanlike";
            if (raider.Faction != faction) return "faction " + (raider.Faction?.Name ?? "none");
            if (raider.IsPrisoner) return "prisoner";
            if (raider.MentalStateDef != null)
                return "already in " + raider.MentalStateDef.defName + (raider.MentalState?.causedByPawn != null ? " (caused by " + raider.MentalState.causedByPawn.LabelShort + ")" : "");
            if (!raider.HostileTo(Faction.OfPlayer)) return "not hostile";
            float r = OldScoresTuning.DreadRadius;
            if ((raider.Position - colonist.Position).LengthHorizontalSquared > r * r) return "too far";
            if (!GenSight.LineOfSight(colonist.Position, raider.Position, colonist.Map)) return "no line of sight";
            return null;
        }

        void CleanupNicknames()
        {
            nicknames.RemoveAll(r => r.faction == null || !Find.FactionManager.AllFactionsListForReading.Contains(r.faction));
        }
    }
}
