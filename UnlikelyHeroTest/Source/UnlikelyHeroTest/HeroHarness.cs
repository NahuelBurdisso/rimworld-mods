using System;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using UnlikelyHero;
using Verse;

namespace UnlikelyHeroTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N`. Generates colonists with controlled skills and traits on an
    /// open patch of the quicktest map and drives real Pawn.Kill calls through every rule of DESIGN.md.
    /// </summary>
    public class HeroHarness : GameComponent
    {
        bool active, done;
        int ticks;
        Map map;
        IntVec3 c;   // arena centre: hero stands here, the victim at +2x

        static TraitDef NervesDef => DefDatabase<TraitDef>.GetNamed("Nerves");

        public HeroHarness(Game game) { }

        public override void FinalizeInit() => active = Kit.HarnessArmed;

        public override void GameComponentTick()
        {
            if (!active || done || ++ticks != 60) return;
            done = true;
            float savedChance = UnlikelyHeroMod.Settings.bonusChance;
            try { Run(); }
            catch (Exception ex) { Kit.Check(false, "harness threw: " + ex); }
            finally { UnlikelyHeroMod.Settings.bonusChance = savedChance; }
            Kit.Summary("Unlikely Hero");
        }

        void Run()
        {
            map = Find.CurrentMap;
            Kit.Check(HeroLedger.Instance != null, "HeroLedger game component exists");
            if (!FindArena()) { Kit.Check(false, "found an open 11x11 arena near the colonists"); return; }
            bool quiet = HeroMoment.GestaQuiet;
            Log.Message($"[Test] mode: Gesta active={ModsConfig.IsActive("nahuel.gesta")}, quiet={quiet}");

            // ---- P1: a non-combatant cook kills a raider; witnesses with and without line of sight
            Pawn a = MakeColonist("A", shooting: 2, melee: 2, best: SkillDefOf.Cooking, bestLevel: 6);
            Pawn w = MakeColonist("W", 5, 5, null, 0);
            Pawn blind = MakeColonist("Blind", 5, 5, null, 0);
            Place(a, c);
            Place(w, c + new IntVec3(-3, 0, 2));
            Place(blind, c + new IntVec3(0, 0, -4));
            for (int x = -2; x <= 2; x++)
                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog), c + new IntVec3(x, 0, -2), map);
            Kit.Check(GenSight.LineOfSight(c, w.Position, map) && !GenSight.LineOfSight(c, blind.Position, map),
                "setup: W sees the hero, Blind is behind a wall");

            UnlikelyHeroMod.Settings.bonusChance = 1f;
            int trig = HeroMoment.Triggered, vis = HeroMoment.VisualsPlayed, tales = HeroMoment.TalesRecorded;
            Pawn p1 = Hostile(c + new IntVec3(2, 0, 0));
            Kit.KillBy(p1, a, DamageDefOf.Bullet);
            Kit.Check(p1.Dead, "P1 raider died");
            Kit.Check(HeroMoment.Triggered == trig + 1 && HeroMoment.LastHero == a,
                $"P1 cook (Shooting 2, Melee 2) killing a raider triggers the moment (rejection: {HeroRules.LastRejection ?? "none"})");
            Kit.Check(HeroLedger.Instance.IsHero(a), "P1 the cook is recorded in the save ledger");
            Thought_Memory own = Memory(a, HeroDefOf.UnlikelyHero_FoundOut, null);
            Kit.Check(own != null, $"P1 hero has 'didn't know I had it in me' ({own?.LabelCap}, mood {own?.MoodOffset():+0;-0})");
            Thought_Memory seen = Memory(w, HeroDefOf.UnlikelyHero_SawStandUp, a);
            Kit.Check(seen != null && seen.LabelCap.ToString().Contains(a.LabelShort),
                $"P1 witness with line of sight got '{seen?.LabelCap}' about the hero (mood {seen?.MoodOffset():+0;-0}, opinion {w.relations.OpinionOf(a)})");
            Kit.Check(seen != null && Mathf.Approximately(seen.MoodOffset(), 3f) && seen is Thought_MemorySocial ms && Mathf.Approximately(ms.OpinionOffset(), 10f),
                "P1 witness memory gives +3 mood and +10 opinion of the hero");
            Kit.Check(seen != null && !seen.Description.NullOrEmpty() && seen.Description.Contains(a.LabelShort),
                "P1 witness memory description names the hero");
            Kit.Check(Memory(blind, HeroDefOf.UnlikelyHero_SawStandUp, a) == null, "P1 colonist behind the wall got no witness memory");
            Kit.Check(Memory(a, HeroDefOf.UnlikelyHero_SawStandUp, a) == null, "P1 the hero is not a witness of themself");

            Tale tale = Find.TaleManager.AllTalesListForReading.LastOrDefault(t => t.def == HeroDefOf.UnlikelyHero_Tale && t.DominantPawn == a);
            if (quiet)
            {
                Kit.Check(HeroMoment.VisualsPlayed == vis, "P1 quiet (Gesta active): no column of light / floating text");
                Kit.Check(HeroMoment.TalesRecorded == tales && tale == null, "P1 quiet (Gesta active): no tale");
            }
            else
            {
                Kit.Check(HeroMoment.VisualsPlayed == vis + 1, "P1 column of light and floating text played");
                Kit.Check(HeroMoment.TalesRecorded == tales + 1 && tale != null, "P1 tale recorded with the hero as first pawn");
                string art = tale == null ? null : (string)TaleTextGenerator.GenerateTextFromTale(TextGenerationPurpose.ArtDescription, tale, 42, RulePackDefOf.ArtDescription_Sculpture);
                Kit.Check(!art.NullOrEmpty() && !art.Contains("ERR"), $"P1 tale makes an art description: \"{art}\"");
            }
            Kit.Check(a.story.traits.HasTrait(NervesDef, 1) && HeroMoment.LastBonus != null,
                $"P1 bonus at chance 1: gained steadfast ({HeroMoment.LastBonus ?? "nothing"})");

            // ---- P2: only once per pawn
            trig = HeroMoment.Triggered;
            Pawn p2 = Hostile(c + new IntVec3(2, 0, 0));
            Kit.KillBy(p2, a, DamageDefOf.Bullet);
            Kit.Check(HeroMoment.Triggered == trig && HeroRules.LastRejection == "killer already had the moment",
                $"P2 second kill by the same hero does nothing ({HeroRules.LastRejection})");

            // ---- P3: negatives, each with a fresh non-combatant (every kill raises the killer's records)
            Pawn downed = Hostile(c + new IntVec3(2, 0, 0));
            Kit.Down(downed);
            Kit.Check(downed.Downed && !downed.Dead, "setup: raider downed");
            Negative(downed, Fresh("B1"), DamageDefOf.Bullet, "victim was already down", "P3a finishing off a downed raider");

            Pawn prisoner = Hostile(c + new IntVec3(2, 0, 0));
            prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            Negative(prisoner, Fresh("B2"), DamageDefOf.Cut, "victim is a prisoner", "P3b killing a prisoner");

            Negative(Hostile(c + new IntVec3(2, 0, 0)), Fresh("B3"), DamageDefOf.ExecutionCut, "execution", "P3c an execution cut");
            Negative(Hostile(c + new IntVec3(2, 0, 0)), Fresh("B4"), DamageDefOf.Flame, "fire", "P3d a death by fire");
            Negative(Hostile(c + new IntVec3(2, 0, 0)), Fresh("B5"), DamageDefOf.Bomb, "explosion", "P3e a death by explosion");
            Negative(Hostile(c + new IntVec3(2, 0, 0)), Fresh("B6"), DefDatabase<DamageDef>.GetNamed("AcidBurn"), "fire", "P3f a death by acid burn (child of Flame)");

            Pawn pet = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"), Faction.OfPlayer);
            GenSpawn.Spawn(pet, c + new IntVec3(2, 0, 0), map);
            Negative(pet, Fresh("B7"), DamageDefOf.Bullet, "victim belongs to the colony", "P3g killing a colony animal");

            Pawn stranger = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, null);
            GenSpawn.Spawn(stranger, c + new IntVec3(2, 0, 0), map);
            Negative(stranger, Fresh("B8"), DamageDefOf.Bullet, "victim was not an active threat", "P3h killing a non-hostile stranger");

            Pawn berserker = Fresh("B9");
            berserker.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Berserk, forced: true, forceWake: true);
            Kit.Check(berserker.InMentalState, "setup: B9 is berserk");
            Negative(Hostile(c + new IntVec3(2, 0, 0)), berserker, DamageDefOf.Bullet, "killer is in a mental state", "P3i a berserk colonist's kill");
            berserker.MentalState?.RecoverFromState();

            // ---- P4: a fighter; P5: killed before; P6: violent trait
            Pawn fighter = MakeColonist("C", 12, 10, null, 0);
            Place(fighter, c);
            Negative(Hostile(c + new IntVec3(2, 0, 0)), fighter, DamageDefOf.Bullet, "killer is a fighter", "P4 a colonist with Shooting 12");

            Pawn veteran = MakeColonist("D", 2, 2, null, 0);
            veteran.records.Increment(RecordDefOf.KillsHumanlikes);
            Place(veteran, c);
            Negative(Hostile(c + new IntVec3(2, 0, 0)), veteran, DamageDefOf.Bullet, "killer has killed before", "P5 a non-combatant who already killed a person");

            Pawn brawler = MakeColonist("E", 2, 2, null, 0);
            brawler.story.traits.GainTrait(new Trait(TraitDefOf.Brawler));
            Place(brawler, c);
            Negative(Hostile(c + new IntVec3(2, 0, 0)), brawler, DamageDefOf.Cut, "killer has a violent trait: Brawler", "P6 a brawler");

            // ---- P7: a nervous doctor kills a manhunting boar in melee; Nervous blocks steadfast -> Melee passion
            Pawn b = Fresh("B");
            b.story.traits.GainTrait(new Trait(NervesDef, -1));
            Pawn beast = PawnGenerator.GeneratePawn(PawnKindDef.Named("WildBoar"), null);
            GenSpawn.Spawn(beast, c + new IntVec3(1, 0, 0), map);
            beast.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, forced: true, forceWake: true);
            Kit.Check(beast.InAggroMentalState, "setup: boar is manhunting");
            trig = HeroMoment.Triggered;
            UnlikelyHeroMod.Settings.bonusChance = 1f;
            Kit.KillBy(beast, b, DamageDefOf.Cut);
            Kit.Check(HeroMoment.Triggered == trig + 1 && HeroMoment.LastHero == b,
                $"P7 doctor killing a manhunting boar in melee triggers the moment (rejection: {HeroRules.LastRejection ?? "none"})");
            Kit.Check(b.story.traits.GetTrait(NervesDef)?.Degree == -1 && b.story.traits.allTraits.Count(t => t.def == NervesDef) == 1,
                "P7 Nervous kept: steadfast not added over a conflicting trait");
            Kit.Check(b.skills.GetSkill(SkillDefOf.Melee).passion == Passion.Minor,
                $"P7 bonus fell back to a minor Melee passion ({HeroMoment.LastBonus ?? "nothing"})");

            // ---- P10: real wounds, not a bare Kill: a head shot (consciousness 0 before Kill runs)
            UnlikelyHeroMod.Settings.bonusChance = 0f;
            Pawn sniper = Fresh("I");
            Pawn target = Hostile(c + new IntVec3(2, 0, 0));
            BodyPartRecord brain = target.health.hediffSet.GetBrain();
            trig = HeroMoment.Triggered;
            for (int k = 0; k < 10 && !target.Dead; k++)
                target.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 40f, 2f, -1f, sniper, brain, sniper.equipment?.Primary?.def));
            Kit.Check(target.Dead, "P10 raider died from a real bullet to the brain");
            Kit.Check(HeroMoment.Triggered == trig + 1 && HeroMoment.LastHero == sniper,
                $"P10 a real head shot triggers the moment (rejection: {HeroRules.LastRejection ?? "none"})");

            // ---- P11: vanilla death-on-downed from real blows counts (Kill runs before MakeDowned)
            Pawn smith = Fresh("J");
            Pawn tough = Hostile(c + new IntVec3(2, 0, 0));
            tough.health.overrideDeathOnDownedChance = 1f;
            trig = HeroMoment.Triggered;
            Kit.DownBy(tough, smith);
            Kit.Check(tough.Dead, "P11 raider died from blows at the moment it would have gone down");
            Kit.Check(HeroMoment.Triggered == trig + 1 && HeroMoment.LastHero == smith,
                $"P11 death on downed counts (rejection: {HeroRules.LastRejection ?? "none"})");

            // ---- P8: rule B (main craft) and its limits
            UnlikelyHeroMod.Settings.bonusChance = 0f;
            Pawn cook = MakeColonist("F", 6, 5, SkillDefOf.Cooking, 12);
            Place(cook, c);
            trig = HeroMoment.Triggered;
            Kit.KillBy(Hostile(c + new IntVec3(2, 0, 0)), cook, DamageDefOf.Bullet);
            Kit.Check(HeroMoment.Triggered == trig + 1 && HeroMoment.LastHero == cook,
                $"P8 head cook (Cooking 12, Shooting 6, Melee 5) counts as a non-combatant (rejection: {HeroRules.LastRejection ?? "none"})");
            Kit.Check(HeroMoment.LastBonus == null && cook.story.traits.allTraits.Count == 0, "P8 bonus chance 0 gives nothing");

            Pawn armedCook = MakeColonist("G", 9, 5, SkillDefOf.Cooking, 12);
            Place(armedCook, c);
            Negative(Hostile(c + new IntVec3(2, 0, 0)), armedCook, DamageDefOf.Bullet, "killer is a fighter", "P8b cook with Shooting 9 (over the cap of 8)");
            Pawn builder = MakeColonist("H", 6, 5, SkillDefOf.Construction, 12);
            Place(builder, c);
            Negative(Hostile(c + new IntVec3(2, 0, 0)), builder, DamageDefOf.Bullet, "killer is a fighter", "P8c builder (best skill not a gentle one) with Shooting 6");

            // ---- P9: the ledger survives a save round trip
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "unlikelyhero-ledger-test.xml");
            HeroRoll roll = HeroLedger.Instance.Roll;
            Scribe.saver.InitSaving(path, "test");
            Scribe_Deep.Look(ref roll, "heroes");
            Scribe.saver.FinalizeSaving();
            HeroRoll back = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref back, "heroes");
            Scribe.loader.FinalizeLoading();
            Kit.Check(back != null && back.ids.SetEquals(roll.ids) && back.Contains(a) && back.Contains(b) && back.Contains(cook) && back.Contains(sniper) && !back.Contains(fighter),
                $"P9 hero ledger saved and loaded ({back?.ids.Count ?? 0} heroes)");
        }

        void Negative(Pawn victim, Pawn killer, DamageDef def, string reason, string what)
        {
            int trig = HeroMoment.Triggered;
            Kit.KillBy(victim, killer, def);
            Kit.Check(victim.Dead, $"{what}: victim died");
            Kit.Check(HeroMoment.Triggered == trig && HeroRules.LastRejection == reason && !HeroLedger.Instance.IsHero(killer),
                $"{what} does not trigger (\"{HeroRules.LastRejection}\")");
        }

        /// <summary>A new non-combatant (Shooting 3, Melee 3, Medicine 7) standing at the arena centre.</summary>
        Pawn Fresh(string tag)
        {
            Pawn p = MakeColonist(tag, 3, 3, SkillDefOf.Medicine, 7);
            Place(p, c);
            return p;
        }

        static Thought_Memory Memory(Pawn p, ThoughtDef def, Pawn other) =>
            p.needs.mood.thoughts.memories.Memories.FirstOrDefault(m => m.def == def && (other == null || m.otherPawn == other));

        Pawn Hostile(IntVec3 cell)
        {
            Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Pirate, Faction.OfPirates ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false));
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>Adult baseliner colonist, no traits, every skill 3 except the ones given; no skill disabled.</summary>
        Pawn MakeColonist(string tag, int shooting, int melee, SkillDef best, int bestLevel)
        {
            Pawn p = null;
            for (int i = 0; i < 20; i++)
            {
                p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                    forceGenerateNewPawn: true, mustBeCapableOfViolence: true, forcedXenotype: XenotypeDefOf.Baseliner,
                    developmentalStages: DevelopmentalStage.Adult));
                if (p.skills.skills.All(s => !s.TotallyDisabled)) break;
            }
            foreach (Trait t in p.story.traits.allTraits.ToList()) p.story.traits.RemoveTrait(t);
            foreach (SkillRecord s in p.skills.skills) { s.Level = 3; s.passion = Passion.None; }
            p.skills.GetSkill(SkillDefOf.Shooting).Level = shooting;
            p.skills.GetSkill(SkillDefOf.Melee).Level = melee;
            if (best != null) p.skills.GetSkill(best).Level = bestLevel;
            Log.Message($"[Test] made {tag}: {p.LabelShort} Shooting {p.skills.GetSkill(SkillDefOf.Shooting).Level} Melee {p.skills.GetSkill(SkillDefOf.Melee).Level}" +
                        (best != null ? $" {best.defName} {p.skills.GetSkill(best).Level}" : ""));
            return p;
        }

        void Place(Pawn p, IntVec3 cell)
        {
            if (p.Spawned) Teleport(p, cell);
            else GenSpawn.Spawn(p, cell, map);
        }

        void Teleport(Pawn p, IntVec3 cell) => Kit.Teleport(p, cell);

        bool FindArena()
        {
            IntVec3 start = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(start, 40f, true))
            {
                CellRect r = CellRect.CenteredOn(cell, 5);
                if (!r.InBounds(map)) continue;
                bool ok = true;
                foreach (IntVec3 x in r)
                    if (!x.Standable(map) || x.GetEdifice(map) != null || x.Fogged(map) || x.GetFirstPawn(map) != null || x.GetFirstItem(map) != null)
                    { ok = false; break; }
                if (!ok) continue;
                foreach (IntVec3 x in r)
                    foreach (Thing t in x.GetThingList(map).ToList())
                        if (t is Plant) t.Destroy();
                c = cell;
                Log.Message($"[Test] arena at {c}");
                return true;
            }
            return false;
        }
    }
}
