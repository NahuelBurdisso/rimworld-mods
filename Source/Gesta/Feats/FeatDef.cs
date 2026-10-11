using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Gesta
{
    /// <summary>A feat of arms: what it is called, the names it can earn and the tale it leaves.</summary>
    public class FeatDef : Def
    {
        /// <summary>Titles the colony may give for it ("Kingslayer"). One is picked at random.</summary>
        public List<string> epithets = new List<string>();
        public TaleDef tale;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (epithets.NullOrEmpty()) yield return "no epithets";
        }
    }
}
