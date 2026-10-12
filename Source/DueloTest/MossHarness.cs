using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Duelo;
using RimWorld;
using Verse;

namespace DueloTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N` (N >= 9000). Moss of the beloved:
    ///  - a colonist loved by three living colonists (spouse, child, friend) is laid in a grave: target > 0.5,
    ///    the moss grows one step per ~2500 ticks through the real CompTickRare path;
    ///  - a stranger nobody knew: target 0, no moss (negative); an empty grave: no moss (negative);
    ///  - the lovers die: love and target drop, and the moss steps back down;
    ///  - CompDueloGrave fields survive a Scribe round trip.
    /// </summary>
    public class MossHarness : GameComponent
    {
        const int StartTick = 360;

        bool active, done;
        int ticks, step, stepTick;
        Map map;
        Pawn beloved, spouse, child, friend, stranger;
        Building_Grave graveBeloved, graveStranger, graveEmpty;
        CompDueloGrave cb, cs, ce;
        float targetBefore, loveBefore;
        int recomputesSeen;
        float firstLevel;

        public MossHarness(Game game) { }

        public override void FinalizeInit()
        {
            active = GenCommandLine.TryGetCommandLineArg("rwtools-ticks", out _);
        }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (!EulogyHarness.Finished) return;   // run after the eulogy harness: our deaths would call off its funeral
            if (ticks < StartTick || ticks % 30 != 0) return;
            try
            {
                Step();
            }
            catch (System.Exception e)
            {
                Check(false, "moss harness threw at step " + step + ": " + e);
                done = true;
            }
        }

        void Next() { step++; stepTick = ticks; }
        int InStep => ticks - stepTick;

        void Step()
        {
            switch (step)
            {
                case 0: Setup(); Next(); break;
                case 1: WaitGrowth(); break;
                case 2: Forget(); Next(); break;
                case 3: WaitFade(); break;
                case 4: RoundTrip(); Next(); break;
                default: done = true; break;
            }
        }

        Pawn NewColonist(IntVec3 near)
        {
            var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true,
                canGeneratePawnRelations: false, fixedIdeo: Faction.OfPlayer.ideos?.PrimaryIdeo, forbidAnyTitle: true);
            Pawn p = PawnGenerator.GeneratePawn(req);
            GenSpawn.Spawn(p, LandCellNear(near, 6), map);
            return p;
        }

        /// <summary>A standable, reachable cell near <paramref name="near"/>; quicktest maps can put water anywhere.</summary>
        IntVec3 LandCellNear(IntVec3 near, int radius)
        {
            IntVec3 anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center;
            if (CellFinder.TryFindRandomCellNear(near, map, radius, c => c.Standable(map) && !c.Fogged(map) && map.reachability.CanReach(anchor, c, Verse.AI.PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)), out IntVec3 cell))
                return cell;
            return CellFinder.StandableCellNear(anchor, map, 8f);
        }

        IntVec3 GraveCellNear(IntVec3 near, System.Predicate<IntVec3> ok)
        {
            for (int r = 4; r <= 24; r += 4)
                if (CellFinder.TryFindRandomCellNear(near, map, r, c => ok(c) && c.Standable(map) && !c.Fogged(map), out IntVec3 cell)) return cell;
            return LandCellNear(near, 10);
        }

        Building_Grave NewGrave(IntVec3 near)
        {
            IntVec3 cell = GraveCellNear(near, c =>
                GenConstruct.CanPlaceBlueprintAt(ThingDefOf.Grave, c, Rot4.South, map).Accepted
                && GenAdj.OccupiedRect(c, Rot4.South, ThingDefOf.Grave.size).Cells.All(x => x.Standable(map) && x.GetFirstPawn(map) == null));
            var g = (Building_Grave)ThingMaker.MakeThing(ThingDefOf.Grave);
            g.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(g, cell, map, Rot4.South);
            return g;
        }

        static void Inter(Pawn into, Building_Grave grave)
        {
            Corpse corpse = into.Corpse;
            if (corpse.Spawned) corpse.DeSpawn();
            grave.TryAcceptThing(corpse);
        }

        void Setup()
        {
            map = Find.CurrentMap;
            DueloMod.Settings.moss = true;
            IntVec3 at = LandCellNear(map.mapPawns.FreeColonistsSpawned.First().Position + new IntVec3(0, 0, 22), 10);
            beloved = NewColonist(at);
            spouse = NewColonist(at);
            child = NewColonist(at);
            friend = NewColonist(at);
            beloved.relations.AddDirectRelation(PawnRelationDefOf.Spouse, spouse);
            child.relations.AddDirectRelation(PawnRelationDefOf.Parent, beloved);
            for (int i = 0; i < 5; i++)
                friend.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.RescuedMe, beloved);

            stranger = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, null, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(stranger, LandCellNear(at, 6), map);

            graveBeloved = NewGrave(at + new IntVec3(-4, 0, 0));
            graveStranger = NewGrave(at + new IntVec3(0, 0, 0));
            graveEmpty = NewGrave(at + new IntVec3(4, 0, 0));
            cb = graveBeloved.GetComp<CompDueloGrave>();
            cs = graveStranger.GetComp<CompDueloGrave>();
            ce = graveEmpty.GetComp<CompDueloGrave>();
            Check(cb != null && cs != null && ce != null, "every grave has the Duelo comp");
            Check(MapComponent_Duelo.For(map).graves.Contains(cb), "spawned graves register for moss drawing");

            Log.Message($"[Test] opinions of the beloved: spouse {EulogyComposer.SafeOpinion(spouse, beloved)}, child {EulogyComposer.SafeOpinion(child, beloved)}, friend {EulogyComposer.SafeOpinion(friend, beloved)}");
            beloved.Kill(null, null);
            stranger.Kill(null, null);
            Inter(beloved, graveBeloved);
            Inter(stranger, graveStranger);
            Check(cb.Buried == beloved && cs.Buried == stranger, "corpses lie in their graves");
        }

        void WaitGrowth()
        {
            // the real path: CompTickRare every 250 ticks, a recompute every 10th
            if (cb.mossTarget >= 0f && recomputesSeen == 0) { recomputesSeen = 1; firstLevel = cb.mossLevel; }
            if (cb.mossLevel > 0f || InStep > 6000)
            {
                Log.Message($"[Test] beloved: love {cb.lastLove:F0}, target {cb.mossTarget:F2}, level {cb.mossLevel:F2}; stranger: love {cs.lastLove:F0}, target {cs.mossTarget:F2}; empty: target {ce.mossTarget:F2}");
                Check(cb.lastLove >= 60f && cb.mossTarget > 0.5f, $"beloved grave: love {cb.lastLove:F0} gives target {cb.mossTarget:F2} > 0.5");
                Check(firstLevel == 0f, "first recompute after spawn only sets the target (no free growth)");
                Check(cb.mossLevel > 0f && cb.mossLevel <= MossUtility.StepPerRecompute * 2 + 0.001f,
                    $"moss grows slowly through CompTickRare (level {cb.mossLevel:F2} after {InStep} ticks)");
                Check(cs.mossTarget == 0f && cs.mossLevel == 0f, "stranger nobody knew: no moss");
                Check(ce.mossTarget == 0f && ce.mossLevel == 0f, "empty grave: no moss");
                Check(!cb.CompInspectStringExtra().NullOrEmpty() || cb.mossLevel < 0.05f, "inspect line once the moss shows");
                Next();
            }
        }

        void Forget()
        {
            // speed the test up: grown moss, then the people who loved them die
            cb.mossLevel = 0.6f;
            loveBefore = cb.lastLove;
            targetBefore = cb.mossTarget;
            string insp = cb.CompInspectStringExtra() ?? "";
            Check(insp.ToLowerInvariant().Contains("Duelo_MossLush".Translate().ToString().ToLowerInvariant()), "inspect shows the moss: " + insp);
            spouse.Kill(null, null);
            child.Kill(null, null);
        }

        void WaitFade()
        {
            if (InStep < 2700) return;
            Log.Message($"[Test] after the spouse and child died: love {cb.lastLove:F0} (was {loveBefore:F0}), target {cb.mossTarget:F2} (was {targetBefore:F2}), level {cb.mossLevel:F2}");
            Check(cb.lastLove < loveBefore && cb.mossTarget < targetBefore, "when those who loved them die, the love and the target drop");
            Check(cb.mossLevel < 0.6f, "the moss steps back down toward the new target");
            Next();
        }

        void RoundTrip()
        {
            cb.mossLevel = 0.37f;
            string xml = Scribe.saver.DebugOutputFor(graveBeloved);
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            XmlNode node = doc.SelectSingleNode("//dueloMossLevel");
            Check(node != null && float.TryParse(node.InnerText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float saved)
                  && System.Math.Abs(saved - 0.37f) < 0.0001f, "moss level is written to the save: " + (node?.OuterXml ?? "missing"));
            var fresh = new CompDueloGrave { parent = graveBeloved };
            Scribe.loader.curXmlParent = doc.DocumentElement;
            Scribe.mode = LoadSaveMode.LoadingVars;
            try
            {
                fresh.PostExposeData();
            }
            finally
            {
                Scribe.loader.curXmlParent = null;
                Scribe.mode = LoadSaveMode.Inactive;
            }
            Check(System.Math.Abs(fresh.mossLevel - 0.37f) < 0.0001f && fresh.mossTarget < 0f, $"moss level loads back ({fresh.mossLevel:F2}); target recomputed later");
        }

        static void Check(bool ok, string what)
        {
            if (ok) Log.Message("[Test] PASS " + what);
            else Log.Error("[Test] FAIL " + what);
        }
    }
}
