using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Duelo
{
    /// <summary>
    /// Gives every grave def (thingClass Building_Grave or a subclass: Grave, Sarcophagus, modded graves)
    /// a CompDueloGrave. Runs after all defs are loaded, before any game exists.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class GraveCompInjector
    {
        public static readonly List<ThingDef> Injected = new List<ThingDef>();

        static GraveCompInjector()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                Type cls = def.thingClass;
                if (cls == null || !typeof(Building_Grave).IsAssignableFrom(cls) || def.HasComp(typeof(CompDueloGrave))) continue;
                if (def.comps == null) def.comps = new List<CompProperties>();
                def.comps.Add(new CompProperties_DueloGrave());
                Injected.Add(def);
            }
            if (Prefs.DevMode) Log.Message("[Duelo] grave comp added to " + Injected.Count + " grave defs");
        }
    }
}
