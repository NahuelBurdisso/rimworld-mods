using RimWorld;
using Verse;

namespace Aftermath
{
    /// <summary>
    /// Adds the door and grave comps to every matching ThingDef after defs load. There is no common
    /// XML parent for doors or graves (mods add their own), so this is done by class, not XPath.
    /// Old saves get the comps too: ThingWithComps.ExposeData calls InitializeComps on LoadingVars.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CompInjector
    {
        static CompInjector()
        {
            int doors = 0, graves = 0;
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (d.thingClass == null) continue;
                if (d.IsDoor && !typeof(Building_VacBarrier).IsAssignableFrom(d.thingClass) && !d.HasComp(typeof(CompBracedDoor)))
                {
                    d.comps.Add(new CompProperties(typeof(CompBracedDoor)));
                    doors++;
                }
                if (typeof(Building_Grave).IsAssignableFrom(d.thingClass) && !d.HasComp(typeof(CompHonoredFoe)))
                {
                    d.comps.Add(new CompProperties(typeof(CompHonoredFoe)));
                    graves++;
                }
            }
            Log.Message($"[Aftermath] comps added to {doors} door defs and {graves} grave defs");
        }
    }
}
