using System.Collections.Generic;
using RimWorld;
using Verse;

namespace UnlikelyHero
{
    /// <summary>Who counts as a non-combatant and what the rare bonus is. One instance: UnlikelyHero_Config.</summary>
    public class HeroConfigDef : Def
    {
        public List<TraitDef> violentTraits = new List<TraitDef>();
        public List<SkillDef> gentleSkills = new List<SkillDef>();
        /// <summary>Fire and burns: fire keeps its instigator for minutes, so a burning raider is never a duel.</summary>
        public List<DamageDef> excludedDamage = new List<DamageDef>();
        public TraitDef bonusTrait;
        public int bonusTraitDegree;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (gentleSkills.NullOrEmpty()) yield return "gentleSkills is empty";
        }
    }

    [DefOf]
    public static class HeroDefOf
    {
        public static HeroConfigDef UnlikelyHero_Config;
        public static ThoughtDef UnlikelyHero_FoundOut;
        public static ThoughtDef UnlikelyHero_SawStandUp;
        public static TaleDef UnlikelyHero_Tale;
        public static FleckDef UnlikelyHero_Fleck_Beam;
        public static FleckDef UnlikelyHero_Fleck_Glow;

        static HeroDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(HeroDefOf)); }
    }
}
