using System.Collections.Generic;
using Verse;

namespace UnlikelyHero
{
    /// <summary>
    /// The pawns that already had their moment (thingIDNumber), saved with the game.
    /// A plain IExposable so the save round trip can be tested on its own.
    /// </summary>
    public class HeroRoll : IExposable
    {
        public HashSet<int> ids = new HashSet<int>();

        public bool Contains(Pawn p) => p != null && ids.Contains(p.thingIDNumber);
        public bool Add(Pawn p) => p != null && ids.Add(p.thingIDNumber);

        public void ExposeData()
        {
            Scribe_Collections.Look(ref ids, "ids", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) ids ??= new HashSet<int>();
        }
    }

    public class HeroLedger : GameComponent
    {
        HeroRoll roll = new HeroRoll();

        static HeroLedger instance;
        public static HeroLedger Instance => instance;

        public HeroLedger(Game game)
        {
            instance = this;
            HeroMoment.ResetStatics();
        }

        public HeroRoll Roll => roll;
        public bool IsHero(Pawn p) => roll.Contains(p);
        public bool MarkHero(Pawn p) => roll.Add(p);

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref roll, "heroes");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) roll ??= new HeroRoll();
        }
    }
}
