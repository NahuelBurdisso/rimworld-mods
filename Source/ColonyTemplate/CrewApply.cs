using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace ColonyTemplate
{
    // CrewTemplate -> the starting pawns of the new-game page. Each pawn is regenerated
    // through the page's own path (gender, age and xenotype fixed in the generation
    // request, so body and head fit), then the rest is written over it.
    public static class CrewApply
    {
        public static void ApplyToStartingPawns(CrewTemplate crew, StringBuilder problems)
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            int n = System.Math.Min(crew.pawns.Count, pawns.Count);
            if (crew.pawns.Count > pawns.Count)
                problems.AppendLine("ColonyTemplate_TooManyPawns".Translate(crew.pawns.Count, pawns.Count));

            for (int i = 0; i < n; i++)
            {
                PawnTemplate t = crew.pawns[i];
                PawnGenerationRequest original = StartingPawnUtility.GetGenerationRequest(i);
                PawnGenerationRequest req = original;
                req.FixedGender = t.gender;
                req.FixedBiologicalAge = t.bioAge;
                req.FixedChronologicalAge = System.Math.Max(t.chronoAge, t.bioAge);
                req.ExcludeBiologicalAgeRange = null;
                req.BiologicalAgeRange = null;
                req.AllowedDevelopmentalStages = t.bioAge < 3f ? DevelopmentalStage.Baby
                    : t.bioAge < 13f ? DevelopmentalStage.Child : DevelopmentalStage.Adult;
                req.CanGeneratePawnRelations = false;
                XenotypeDef xeno = Def<XenotypeDef>(t.xenotype, problems);
                if (xeno != null && ModsConfig.BiotechActive)
                {
                    req.ForcedXenotype = xeno;
                    req.ForcedCustomXenotype = null;
                }
                StartingPawnUtility.SetGenerationRequest(i, req);
                Pawn p = StartingPawnUtility.RandomizeInPlace(pawns[i]);
                // later "randomize" clicks go back to what the page had
                StartingPawnUtility.SetGenerationRequest(i, original);

                ApplyDetails(p, t, problems);
                List<ThingDefCount> owned = Find.GameInitData.startingPossessions[p];
                owned.Clear();
                foreach (GearEntry g in t.possessions)
                {
                    ThingDef def = Def<ThingDef>(g.def, problems);
                    if (def != null) owned.Add(new ThingDefCount(def, g.count));
                }
            }

            for (int i = 0; i < n; i++)
            {
                Pawn p = pawns[i];
                foreach (RelationEntry r in crew.pawns[i].relations)
                {
                    if (r.other < 0 || r.other >= n || r.other == i) continue;
                    PawnRelationDef def = Def<PawnRelationDef>(r.def, problems);
                    if (def == null || def.implied) continue;
                    Pawn other = pawns[r.other];
                    if (!p.relations.DirectRelationExists(def, other))
                        p.relations.AddDirectRelation(def, other);
                }
            }
            for (int i = 0; i < n; i++)
                Refresh(pawns[i]);
        }

        static void ApplyDetails(Pawn p, PawnTemplate t, StringBuilder problems)
        {
            if (!t.first.NullOrEmpty() || !t.last.NullOrEmpty())
                p.Name = new NameTriple(t.first ?? "", t.nick.NullOrEmpty() ? t.first : t.nick, t.last ?? "");

            Pawn_StoryTracker story = p.story;
            BackstoryDef child = Def<BackstoryDef>(t.childhood, problems);
            if (child != null) story.Childhood = child;
            if (t.adulthood.NullOrEmpty()) story.Adulthood = null;
            else
            {
                BackstoryDef adult = Def<BackstoryDef>(t.adulthood, problems);
                if (adult != null) story.Adulthood = adult;
            }

            for (int i = story.traits.allTraits.Count - 1; i >= 0; i--)
            {
                Trait tr = story.traits.allTraits[i];
                if (tr.sourceGene == null) story.traits.RemoveTrait(tr);
            }
            foreach (TraitEntry te in t.traits)
            {
                TraitDef def = Def<TraitDef>(te.def, problems);
                if (def != null && !story.traits.HasTrait(def))
                    story.traits.GainTrait(new Trait(def, te.degree, forced: true));
            }

            BodyTypeDef body = Def<BodyTypeDef>(t.bodyType, problems);
            if (body != null) story.bodyType = body;
            HeadTypeDef head = Def<HeadTypeDef>(t.headType, problems);
            if (head != null) story.headType = head;
            HairDef hair = Def<HairDef>(t.hair, problems);
            if (hair != null) story.hairDef = hair;
            story.HairColor = t.hairColor;
            if (t.skinColor.HasValue) story.skinColorOverride = t.skinColor;
            ColorDef fav = Def<ColorDef>(t.favoriteColor, problems);
            if (fav != null) story.favoriteColor = fav;
            if (p.style != null)
            {
                BeardDef beard = Def<BeardDef>(t.beard, problems);
                if (beard != null) p.style.beardDef = beard;
            }

            foreach (SkillEntry s in t.skills)
            {
                SkillDef def = Def<SkillDef>(s.def, problems);
                if (def == null) continue;
                SkillRecord rec = p.skills.GetSkill(def);
                rec.Level = s.level;
                rec.xpSinceLastLevel = 0f;
                rec.passion = s.passion;
            }
            p.skills.Notify_SkillDisablesChanged();
            p.Notify_DisabledWorkTypesChanged();

            p.apparel?.DestroyAll();
            foreach (GearEntry g in t.apparel)
            {
                if (!(Make(g, problems) is Apparel ap)) continue;
                if (ApparelUtility.HasPartsToWear(p, ap.def) && p.apparel.CanWearWithoutDroppingAnything(ap.def))
                    p.apparel.Wear(ap, dropReplacedApparel: false);
                else
                {
                    problems.AppendLine("ColonyTemplate_CannotWear".Translate(p.LabelShort, ap.LabelCap));
                    ap.Destroy();
                }
            }
            p.equipment?.DestroyAllEquipment();
            if (t.weapon != null && Make(t.weapon, problems) is ThingWithComps eq && eq.def.equipmentType == EquipmentType.Primary)
                p.equipment.AddEquipment(eq);
        }

        static Thing Make(GearEntry g, StringBuilder problems)
        {
            ThingDef def = Def<ThingDef>(g.def, problems);
            if (def == null) return null;
            ThingDef stuff = def.MadeFromStuff ? Def<ThingDef>(g.stuff, problems) ?? GenStuff.DefaultStuffFor(def) : null;
            Thing th = ThingMaker.MakeThing(def, stuff);
            if (g.hasQuality) th.TryGetComp<CompQuality>()?.SetQuality(g.quality, ArtGenerationContext.Colony);
            th.stackCount = System.Math.Max(1, System.Math.Min(g.count, def.stackLimit));
            return th;
        }

        static void Refresh(Pawn p)
        {
            p.Drawer.renderer.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(p);
        }

        // Missing defs (a mod was removed) are reported once each, not fatal.
        public static T Def<T>(string defName, StringBuilder problems) where T : Def
        {
            if (defName.NullOrEmpty()) return null;
            T def = DefDatabase<T>.GetNamedSilentFail(defName);
            if (def == null)
            {
                string line = "ColonyTemplate_MissingDef".Translate(typeof(T).Name, defName);
                if (!problems.ToString().Contains(line)) problems.AppendLine(line);
            }
            return def;
        }
    }
}
