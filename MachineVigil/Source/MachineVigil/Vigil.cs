using System.Collections.Generic;
using Verse;

namespace MachineVigil
{
    /// <summary>One dead mechanitor and the uncontrolled mechs keeping vigil over them.</summary>
    public class Vigil : IExposable
    {
        public Pawn mechanitor;
        public Map lastMap;
        public IntVec3 lastCenter = IntVec3.Invalid;
        public int startTick;
        public bool announced;
        public List<Pawn> mechs = new List<Pawn>();
        public List<int> witnessIds = new List<int>();

        /// <summary>
        /// Where the ring goes: the corpse (or the grave holding it) if it is on a map, else the last
        /// place it was seen. False when the corpse is off-map (caravan) or nothing valid is known.
        /// </summary>
        public bool TryGetCenter(out IntVec3 center, out Map map)
        {
            Corpse corpse = mechanitor?.Corpse;
            if (corpse != null && !corpse.Destroyed)
            {
                Map held = corpse.MapHeld;
                if (held == null)
                {
                    center = IntVec3.Invalid;
                    map = null;
                    return false;
                }
                lastMap = held;
                lastCenter = corpse.PositionHeld;
            }
            if (lastMap != null && !Find.Maps.Contains(lastMap)) lastMap = null;
            center = lastCenter;
            map = lastMap;
            return map != null && center.IsValid && center.InBounds(map);
        }

        public int SlotOf(Pawn mech) => mechs.IndexOf(mech);

        /// <summary>Drops mechs that can no longer keep vigil (and could be discarded by the world pawn GC).</summary>
        public void PruneMechs() =>
            mechs.RemoveAll(m => m == null || m.Destroyed || m.Dead || m.Faction != RimWorld.Faction.OfPlayer);

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                PruneMechs();
                if (lastMap != null && !Find.Maps.Contains(lastMap)) lastMap = null;
            }
            Scribe_References.Look(ref mechanitor, "mechanitor", saveDestroyedThings: true);
            Scribe_References.Look(ref lastMap, "lastMap");
            Scribe_Values.Look(ref lastCenter, "lastCenter", IntVec3.Invalid);
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref announced, "announced");
            Scribe_Collections.Look(ref mechs, "mechs", LookMode.Reference);
            Scribe_Collections.Look(ref witnessIds, "witnessIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                mechs ??= new List<Pawn>();
                mechs.RemoveAll(m => m == null);
                witnessIds ??= new List<int>();
            }
        }
    }
}
