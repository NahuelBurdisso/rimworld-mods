using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyTemplate
{
    // Pawns -> CrewTemplate. Works on the starting-pawns page (possessions come from
    // GameInitData) and on a map (possessions come from the pawn's inventory).
    public static class CrewCapture
    {
        public static CrewTemplate Capture(List<Pawn> pawns)
        {
            var crew = new CrewTemplate();
            foreach (Pawn p in pawns)
                crew.pawns.Add(CapturePawn(p, pawns));
            return crew;
        }

        static PawnTemplate CapturePawn(Pawn p, List<Pawn> group)
        {
            var t = new PawnTemplate { gender = p.gender };
            if (p.Name is NameTriple n)
            {
                t.first = n.First;
                t.nick = n.Nick;
                t.last = n.Last;
            }
            else
            {
                t.nick = p.Name?.ToStringShort;
            }
            t.bioAge = p.ageTracker.AgeBiologicalYearsFloat;
            t.chronoAge = p.ageTracker.AgeChronologicalYearsFloat;
            if (ModsConfig.BiotechActive && p.genes != null)
            {
                if (p.genes.UniqueXenotype)
                    Log.Warning("[Colony Template] " + p.LabelShort + " has a custom xenotype; only its base xenotype is saved");
                t.xenotype = p.genes.Xenotype.defName;
            }

            Pawn_StoryTracker story = p.story;
            if (story != null)
            {
                t.childhood = story.Childhood?.defName;
                t.adulthood = story.Adulthood?.defName;
                foreach (Trait tr in story.traits.allTraits)
                {
                    // traits granted by genes come back with the xenotype
                    if (tr.sourceGene != null) continue;
                    t.traits.Add(new TraitEntry { def = tr.def.defName, degree = tr.Degree });
                }
                t.bodyType = story.bodyType?.defName;
                t.headType = story.headType?.defName;
                t.hair = story.hairDef?.defName;
                t.hairColor = story.HairColor;
                t.skinColor = story.skinColorOverride;
                t.favoriteColor = story.favoriteColor?.defName;
            }
            t.beard = p.style?.beardDef?.defName;

            if (p.skills != null)
                foreach (SkillRecord s in p.skills.skills)
                    t.skills.Add(new SkillEntry { def = s.def.defName, level = s.levelInt, passion = s.passion });

            if (p.apparel != null)
                foreach (Apparel ap in p.apparel.WornApparel)
                    t.apparel.Add(Gear(ap));
            if (p.equipment?.Primary != null)
                t.weapon = Gear(p.equipment.Primary);

            if (Current.ProgramState == ProgramState.Entry
                && Find.GameInitData.startingPossessions.TryGetValue(p, out List<ThingDefCount> owned))
            {
                foreach (ThingDefCount tc in owned)
                    t.possessions.Add(new GearEntry { def = tc.ThingDef.defName, count = tc.Count });
            }
            else if (p.inventory != null)
            {
                foreach (Thing th in p.inventory.innerContainer)
                    t.possessions.Add(Gear(th));
            }

            if (p.relations != null)
            {
                foreach (DirectPawnRelation rel in p.relations.DirectRelations)
                {
                    int other = group.IndexOf(rel.otherPawn);
                    if (other < 0) continue;
                    t.relations.Add(new RelationEntry { def = rel.def.defName, other = other });
                }
            }
            return t;
        }

        static GearEntry Gear(Thing th)
        {
            var g = new GearEntry { def = th.def.defName, stuff = th.Stuff?.defName, count = th.stackCount };
            if (th.TryGetQuality(out QualityCategory q))
            {
                g.hasQuality = true;
                g.quality = q;
            }
            return g;
        }

        public static List<Pawn> MapColonists(Map map) =>
            map.mapPawns.FreeColonistsSpawned.Where(p => p.RaceProps.Humanlike).ToList();
    }
}
