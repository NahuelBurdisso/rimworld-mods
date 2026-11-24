using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace UnlikelyHero
{
    /// <summary>
    /// Verse.Pawn.DoKillSideEffects(DamageInfo? dinfo, Hediff exactCulprit, bool spawned) is private and the
    /// only caller of RecordsUtility.Notify_PawnKilled (decomp Pawn.cs:2028). Postfix: the victim is still
    /// spawned and alive-flagged, its lord is still assigned and the killer's records already count this death.
    /// RecordsUtility.Notify_PawnKilled itself is not patched because it has no DamageInfo.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), "DoKillSideEffects")]
    public static class Patch_Pawn_DoKillSideEffects
    {
        public static void Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            if (dinfo?.Instigator is not Pawn) return;   // cheap exit for most deaths
            try
            {
                HeroMoment.TryTrigger(__instance, dinfo);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Unlikely Hero] failed on a death: " + ex, 0x5A11E0);
            }
        }
    }

    public static class HeroMoment
    {
        public const string GestaPackageId = "nahuel.gesta";
        public static readonly Color TextColor = new Color(1f, 0.88f, 0.62f);

        // counters for the test harness
        public static int Triggered, VisualsPlayed, TalesRecorded, BonusesGiven;
        public static Pawn LastHero;
        public static string LastBonus;

        public static bool GestaQuiet => UnlikelyHeroMod.Settings.quietWithGesta && ModsConfig.IsActive(GestaPackageId);

        public static void TryTrigger(Pawn victim, DamageInfo? dinfo)
        {
            HeroLedger ledger = HeroLedger.Instance;
            if (ledger == null) return;
            string why = HeroRules.Check(victim, dinfo, out Pawn hero);
            if (why != null)
            {
                HeroRules.LastRejection = why;
                return;
            }
            HeroRules.LastRejection = null;
            // marked first: whatever fails below, the moment never repeats
            ledger.MarkHero(hero);
            Triggered++;
            LastHero = hero;
            Play(hero, victim, dinfo.Value);
        }

        static void Play(Pawn hero, Pawn victim, DamageInfo dinfo)
        {
            Map map = hero.Map;
            bool quiet = GestaQuiet;
            string bonus = null;

            // each step on its own: the hero is already marked, so a failure must not cost the rest of the moment
            Step("hero thought", () => hero.needs.mood.thoughts.memories.TryGainMemory(HeroDefOf.UnlikelyHero_FoundOut));
            Step("witnesses", () => GiveWitnessMemories(hero, map));
            if (!quiet)
            {
                Step("tale", () => { if (TaleRecorder.RecordTale(HeroDefOf.UnlikelyHero_Tale, hero, victim) != null) TalesRecorded++; });
                Step("visual", () => PlayVisual(hero, map));
            }
            Step("bonus", () => bonus = RollBonus(hero, dinfo));
            Step("message", () =>
            {
                if (!UnlikelyHeroMod.Settings.showMessage || !PawnUtility.ShouldSendNotificationAbout(hero)) return;
                TaggedString text = "UnlikelyHero_Message".Translate(hero.Named("HERO"), victim.Named("VICTIM"));
                if (bonus != null) text += " " + "UnlikelyHero_MessageBonus".Translate(hero.Named("HERO"), bonus);
                Messages.Message(text, hero, MessageTypeDefOf.PositiveEvent);
            });
            Log.Message($"[Unlikely Hero] {hero.LabelShort} killed {victim.LabelShort}" +
                        (quiet ? " (quiet: Gesta is active)" : "") + (bonus != null ? $", bonus: {bonus}" : ""));
        }

        static void Step(string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.ErrorOnce($"[Unlikely Hero] {what} failed: {ex}", ("UnlikelyHero" + what).GetHashCode()); }
        }

        static void GiveWitnessMemories(Pawn hero, Map map)
        {
            // a copy: a memory handler patched by another mod could change the pawn list while we iterate
            var pawns = new List<Pawn>(map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer));
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn w = pawns[i];
                if (w == hero || w.Dead || !w.RaceProps.Humanlike || w.needs?.mood == null) continue;
                if (!ThoughtUtility.Witnessed(w, hero)) continue;
                w.needs.mood.thoughts.memories.TryGainMemory(HeroDefOf.UnlikelyHero_SawStandUp, hero);
            }
        }

        /// <summary>Called when a game is created or loaded: drop references into the previous game.</summary>
        public static void ResetStatics()
        {
            LastHero = null;
            LastBonus = null;
            HeroRules.LastRejection = null;
        }

        public static void PlayVisual(Pawn hero, Map map)
        {
            if (map == null || !hero.Spawned) return;
            Vector3 feet = hero.DrawPos;
            // the beam's texture is anchored at its centre: lift it so its foot sits at the pawn
            map.flecks.CreateFleck(FleckMaker.GetDataStatic(feet + new Vector3(0f, 0f, 3.0f), map, HeroDefOf.UnlikelyHero_Fleck_Beam));
            map.flecks.CreateFleck(FleckMaker.GetDataStatic(feet, map, HeroDefOf.UnlikelyHero_Fleck_Glow, 2.4f));
            // below the pawn label: over the beam a warm text on a warm core cannot be read
            MoteMaker.ThrowText(feet + new Vector3(0f, 0f, -1.15f), map, "UnlikelyHero_FloatText".Translate(), TextColor, 3.5f);
            VisualsPlayed++;
        }

        /// <summary>Rare permanent bonus: the config trait (steadfast) if nothing conflicts, else a minor passion in the skill used.</summary>
        public static string RollBonus(Pawn hero, DamageInfo dinfo)
        {
            LastBonus = null;
            float chance = UnlikelyHeroMod.Settings.bonusChance;
            if (chance <= 0f || !Rand.Chance(chance)) return null;
            string got = TryGiveTrait(hero) ?? TryGivePassion(hero, dinfo.Def != null && dinfo.Def.isRanged ? SkillDefOf.Shooting : SkillDefOf.Melee);
            if (got != null) BonusesGiven++;
            else Log.Message($"[Unlikely Hero] bonus rolled for {hero.LabelShort} but nothing could be given");
            LastBonus = got;
            return got;
        }

        static string TryGiveTrait(Pawn hero)
        {
            HeroConfigDef cfg = HeroDefOf.UnlikelyHero_Config;
            TraitDef def = cfg.bonusTrait;
            TraitSet traits = hero.story?.traits;
            if (def == null || traits == null) return null;
            List<Trait> all = traits.allTraits;
            if (all.Count >= 4) return null;
            // HasTrait ignores suppressed traits; a suppressed Nerves must still block a second one
            for (int i = 0; i < all.Count; i++)
                if (all[i].def == def || def.ConflictsWith(all[i])) return null;
            var trait = new Trait(def, cfg.bonusTraitDegree);
            traits.GainTrait(trait);
            return traits.HasTrait(def, cfg.bonusTraitDegree) ? trait.Label : null;
        }

        static string TryGivePassion(Pawn hero, SkillDef skill)
        {
            SkillRecord rec = hero.skills?.GetSkill(skill);
            if (rec == null || rec.TotallyDisabled || rec.passion != Passion.None) return null;
            List<Trait> all = hero.story.traits.allTraits;
            for (int i = 0; i < all.Count; i++)
                if (all[i].def.ConflictsWithPassion(skill)) return null;
            rec.passion = Passion.Minor;
            return "UnlikelyHero_PassionBonus".Translate(skill.label);
        }
    }
}
