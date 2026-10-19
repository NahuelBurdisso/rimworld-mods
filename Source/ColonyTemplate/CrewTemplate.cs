using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyTemplate
{
    // Defs are stored by defName so a template survives a mod being removed:
    // a missing def is skipped with a warning instead of breaking the load.
    public class CrewTemplate : IExposable
    {
        public List<PawnTemplate> pawns = new List<PawnTemplate>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pawns == null)
                pawns = new List<PawnTemplate>();
        }
    }

    public class PawnTemplate : IExposable
    {
        public string first, nick, last;
        public Gender gender = Gender.Male;
        public float bioAge = 25f, chronoAge = 25f;
        public string xenotype;
        public string childhood, adulthood;
        public List<TraitEntry> traits = new List<TraitEntry>();
        public List<SkillEntry> skills = new List<SkillEntry>();
        public List<GearEntry> apparel = new List<GearEntry>();
        public GearEntry weapon;
        public List<GearEntry> possessions = new List<GearEntry>();
        public List<RelationEntry> relations = new List<RelationEntry>();

        // appearance
        public string bodyType, headType, hair, beard, favoriteColor;
        public Color hairColor = Color.white;
        public Color? skinColor;

        public string Label => nick.NullOrEmpty() ? first + " " + last : nick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref first, "first");
            Scribe_Values.Look(ref nick, "nick");
            Scribe_Values.Look(ref last, "last");
            Scribe_Values.Look(ref gender, "gender", Gender.Male);
            Scribe_Values.Look(ref bioAge, "bioAge", 25f);
            Scribe_Values.Look(ref chronoAge, "chronoAge", 25f);
            Scribe_Values.Look(ref xenotype, "xenotype");
            Scribe_Values.Look(ref childhood, "childhood");
            Scribe_Values.Look(ref adulthood, "adulthood");
            Scribe_Collections.Look(ref traits, "traits", LookMode.Deep);
            Scribe_Collections.Look(ref skills, "skills", LookMode.Deep);
            Scribe_Collections.Look(ref apparel, "apparel", LookMode.Deep);
            Scribe_Deep.Look(ref weapon, "weapon");
            Scribe_Collections.Look(ref possessions, "possessions", LookMode.Deep);
            Scribe_Collections.Look(ref relations, "relations", LookMode.Deep);
            Scribe_Values.Look(ref bodyType, "bodyType");
            Scribe_Values.Look(ref headType, "headType");
            Scribe_Values.Look(ref hair, "hair");
            Scribe_Values.Look(ref beard, "beard");
            Scribe_Values.Look(ref favoriteColor, "favoriteColor");
            Scribe_Values.Look(ref hairColor, "hairColor", Color.white);
            Scribe_Values.Look(ref skinColor, "skinColor");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                traits = traits ?? new List<TraitEntry>();
                skills = skills ?? new List<SkillEntry>();
                apparel = apparel ?? new List<GearEntry>();
                possessions = possessions ?? new List<GearEntry>();
                relations = relations ?? new List<RelationEntry>();
            }
        }
    }

    public class TraitEntry : IExposable
    {
        public string def;
        public int degree;

        public void ExposeData()
        {
            Scribe_Values.Look(ref def, "def");
            Scribe_Values.Look(ref degree, "degree", 0);
        }
    }

    public class SkillEntry : IExposable
    {
        public string def;
        public int level;
        public Passion passion;

        public void ExposeData()
        {
            Scribe_Values.Look(ref def, "def");
            Scribe_Values.Look(ref level, "level", 0);
            Scribe_Values.Look(ref passion, "passion", Passion.None);
        }
    }

    public class GearEntry : IExposable
    {
        public string def, stuff;
        public bool hasQuality;
        public QualityCategory quality = QualityCategory.Normal;
        public int count = 1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref def, "def");
            Scribe_Values.Look(ref stuff, "stuff");
            Scribe_Values.Look(ref hasQuality, "hasQuality", false);
            Scribe_Values.Look(ref quality, "quality", QualityCategory.Normal);
            Scribe_Values.Look(ref count, "count", 1);
        }
    }

    // A relation from this pawn to another pawn of the same template, by index.
    public class RelationEntry : IExposable
    {
        public string def;
        public int other;

        public void ExposeData()
        {
            Scribe_Values.Look(ref def, "def");
            Scribe_Values.Look(ref other, "other", 0);
        }
    }
}
