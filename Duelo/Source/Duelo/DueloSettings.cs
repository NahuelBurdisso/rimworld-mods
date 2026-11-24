using Verse;

namespace Duelo
{
    public class DueloSettings : ModSettings
    {
        public bool lastLight = true;
        public bool eulogy = true;
        public bool eulogyLetter = true;
        public bool eulogyAtBurialEvenWithFuneral = false;
        public bool moss = true;
        public float mossBrightness = 1f;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref lastLight, "lastLight", true);
            Scribe_Values.Look(ref eulogy, "eulogy", true);
            Scribe_Values.Look(ref eulogyLetter, "eulogyLetter", true);
            Scribe_Values.Look(ref eulogyAtBurialEvenWithFuneral, "eulogyAtBurialEvenWithFuneral", false);
            Scribe_Values.Look(ref moss, "moss", true);
            Scribe_Values.Look(ref mossBrightness, "mossBrightness", 1f);
        }
    }
}
