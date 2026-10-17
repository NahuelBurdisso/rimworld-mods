using Verse;

namespace OverTheBody
{
    /// <summary>One animal standing over one fallen colonist.</summary>
    public class GuardRecord : IExposable
    {
        public Pawn animal;
        public Pawn master;
        public int startTick;
        public int lastGuardTick = -99999;
        public int ticksGuarded;
        /// <summary>The animal went down while guarding: if it dies of it, it died guarding.</summary>
        public bool fellGuarding;

        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal", saveDestroyedThings: true);
            Scribe_References.Look(ref master, "master", saveDestroyedThings: true);
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref lastGuardTick, "lastGuardTick", -99999);
            Scribe_Values.Look(ref ticksGuarded, "ticksGuarded");
            Scribe_Values.Look(ref fellGuarding, "fellGuarding");
        }
    }
}
