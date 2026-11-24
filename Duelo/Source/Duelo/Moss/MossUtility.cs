using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Duelo
{
    /// <summary>How much the living still love the one in the grave, as a moss level 0..1.</summary>
    public static class MossUtility
    {
        public const float MinLove = 5f;
        public const float LoveScale = 90f;
        public const float StepPerRecompute = 0.04f;   // ~25 recomputes (about one day) from bare to full
        public const int RecomputeEveryRareTicks = 10;  // graves tick Rare (250): ~2500 ticks

        static readonly List<Pawn> tmpLiving = new List<Pawn>();

        /// <summary>Sum of the positive opinions living free colonists hold of <paramref name="deceased"/>.</summary>
        public static float LoveFor(Pawn deceased)
        {
            if (deceased == null || deceased.RaceProps == null || !deceased.RaceProps.Humanlike) return 0f;
            // Only people the colony knew: one of us, or kin/partners of a living colonist. A stranger or a
            // raider gets opinion from looks alone (Pretty/Beautiful), which is not love.
            bool oneOfUs = deceased.Faction != null && deceased.Faction.IsPlayer;
            // copy: OpinionOf runs thought workers that may use PawnsFinder's shared result lists
            tmpLiving.Clear();
            tmpLiving.AddRange(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep);
            float love = 0f;
            for (int i = 0; i < tmpLiving.Count; i++)
            {
                Pawn p = tmpLiving[i];
                if (p == deceased || p.relations == null || !p.RaceProps.Humanlike) continue;
                if (!oneOfUs && !p.GetRelations(deceased).Any()) continue;
                int op = EulogyComposer.SafeOpinion(p, deceased);
                if (op > 0) love += op;
            }
            tmpLiving.Clear();
            return love;
        }

        public static float TargetForLove(float love)
        {
            if (love < MinLove) return 0f;
            return 1f - Mathf.Exp(-love / LoveScale);
        }
    }
}
