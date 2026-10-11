using Verse;

namespace AftermathTest
{
    /// <summary>
    /// A harness armed by `rw launch --ticks N`: waits for its start tick (counted from game start), then runs
    /// Step() every tick until Finish(). Exceptions and the global deadline become FAILs, never hangs.
    /// </summary>
    public abstract class HarnessBase : GameComponent
    {
        protected bool active, done;
        protected int ticks, phase, phaseTick;
        protected Map map;

        protected abstract int StartTick { get; }
        protected abstract string Name { get; }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (ticks < StartTick) return;
            if (ticks > TestUtil.Deadline)
            {
                Kit.Check(false, $"{Name}: still waiting in phase {phase} at the deadline ({Facts()})");
                Finish();
                return;
            }
            try
            {
                if (map == null)
                {
                    map = Find.CurrentMap;
                    Log.Message($"[Test] {Name} harness starting at tick {ticks}");
                }
                Step();
            }
            catch (System.Exception ex)
            {
                Kit.Check(false, $"{Name} harness threw in phase {phase}: {ex}");
                Finish();
            }
        }

        protected void Next()
        {
            phase++;
            phaseTick = ticks;
        }

        protected int InPhase => ticks - phaseTick;

        protected abstract void Step();

        /// <summary>State for FAIL messages.</summary>
        protected virtual string Facts() => "";

        protected virtual void Cleanup() { }

        protected void Finish()
        {
            if (done) return;
            done = true;
            try { Cleanup(); }
            catch (System.Exception ex) { Kit.Check(false, $"{Name} cleanup threw: {ex}"); }
            Kit.Summary("Aftermath/" + Name);
            TestUtil.MarkFinished(Name);
        }
    }
}
