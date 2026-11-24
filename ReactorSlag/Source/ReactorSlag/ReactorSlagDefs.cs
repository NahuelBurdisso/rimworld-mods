using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ReactorSlag
{
    [DefOf]
    public static class ReactorSlagDefOf
    {
        public static ThingDef ReactorSlag_Core;
        public static ThingDef ReactorSlag_CoreLarge;
        public static ThingDef ReactorSlag_Scar;

        static ReactorSlagDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ReactorSlagDefOf)); }
    }

    /// <summary>Size and light of one kind of core (on the core ThingDefs).</summary>
    public class ReactorSlagExtension : DefModExtension
    {
        /// <summary>Cells within this distance of the centre form the pool (1.5 = 3x3, 2.5 = 5x5 disc).</summary>
        public float poolRadius = 1.5f;
        /// <summary>Light radius while white-hot.</summary>
        public float glowRadius = 6f;
        /// <summary>Halo diameter in cells while white-hot.</summary>
        public float haloSize = 4.5f;
        /// <summary>Slag pieces printed over the pool.</summary>
        public int pieces = 9;
        /// <summary>With Odyssey: cooled lava on the centre only (false) or the centre and its 4 neighbours (true).</summary>
        public bool wideGroundScar;
    }

    /// <summary>Added in code to every mechanoid race (see SlagInjector).</summary>
    public class CompProperties_ReactorSlag : CompProperties
    {
        public CompProperties_ReactorSlag() { compClass = typeof(CompReactorSlag); }
    }

    /// <summary>Gives every mechanoid race the comp that spills its core on death. Runs once, after defs load.</summary>
    [StaticConstructorOnStartup]
    public static class SlagInjector
    {
        public static int Injected;

        static SlagInjector()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race == null || !def.race.IsMechanoid) continue;
                if (def.comps == null) def.comps = new List<CompProperties>();
                if (def.HasComp(typeof(CompReactorSlag))) continue;
                var props = new CompProperties_ReactorSlag();
                props.ResolveReferences(def);
                def.comps.Add(props);
                Injected++;
            }
        }
    }
}
