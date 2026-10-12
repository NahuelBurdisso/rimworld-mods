using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Aftermath;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace AftermathTest
{
    /// <summary>Feature 2 — Trophy Standards: a real raid, its leader, the drop, installing it, pride, negatives.</summary>
    public class StandardHarness : HarnessBase
    {
        public StandardHarness(Game game) { }

        protected override int StartTick => TestUtil.StandardsStart;
        protected override string Name => "Standards";

        static readonly MethodInfo Observe = AccessTools.Method(typeof(PawnObserver), "ObserveSurroundingThings");

        Faction enemy;
        List<Pawn> raiders = new List<Pawn>(), raiders2 = new List<Pawn>();
        Lord lord;
        Pawn hero, onlooker;
        IntVec3 root;
        int standardsBefore;

        StandardTracker T => StandardTracker.For(map);

        protected override void Step()
        {
            switch (phase)
            {
                case 0:
                    Next();
                    Setup();
                    break;
                case 1:
                    // the tracker scans every 250 ticks: wait (bounded) for it to name a leader
                    if (InPhase % 30 != 0) break;
                    Pawn leader = T.LeaderOf(lord);
                    if (leader == null && InPhase < 400) break;
                    Kit.Check(leader != null && lord.ownedPawns.Contains(leader),
                        $"within {InPhase} ticks the tracker names a leader of the raid ({leader?.LabelShort ?? "none"}, {leader?.kindDef?.defName})");
                    if (leader == null) { Finish(); break; }
                    Next();
                    Fall(leader);
                    break;
                case 2:
                    Next();
                    Install();
                    break;
                case 3:
                    Next();
                    FeatureOff();
                    Finish();
                    break;
            }
        }

        void Setup()
        {
            enemy = TestUtil.HostileFaction();
            Kit.Check(enemy != null, $"setup: a hostile humanlike faction that raids ({enemy?.Name ?? "none"})");
            root = TestUtil.PrepareStrip(map, TestUtil.Anchor(map, 14, 10), 7, 5, Name);
            Kit.Check(root.IsValid, "setup: a clear strip for the standard");
            if (enemy == null || !root.IsValid) { Finish(); return; }
            hero = TestUtil.NewColonist(map, root + new IntVec3(1, 0, 1));
            hero.drafter.Drafted = true;
            standardsBefore = Standards().Count;
            raiders = TestUtil.Raid(map, enemy, 500f);
            Kit.Check(raiders.Count >= 2, $"setup: a real raid of {enemy.Name} arrived ({raiders.Count} raiders)");
            lord = raiders.FirstOrDefault()?.GetLord();
            Kit.Check(lord != null, "setup: the raiders have a lord");
            if (raiders.Count < 2 || lord == null) Finish();
        }

        void Fall(Pawn leader)
        {
            // negative first: another raider dies, nothing drops
            Pawn other = lord.ownedPawns.FirstOrDefault(p => p != leader && p.RaceProps.Humanlike);
            if (other != null)
            {
                Kit.KillBy(other, hero);
                Kit.Check(Standards().Count == standardsBefore, "killing a raider who is not the leader drops no standard");
            }
            else Log.Message("[Test] (raid has a single member left; skipped the non-leader check)");

            string leaderName = AftermathUtil.FoeName(leader);
            TestUtil.DownBy(leader, hero);
            Kit.Check(leader.Downed || leader.Dead, $"setup: the leader went down to {hero.LabelShort}'s blows");
            List<MinifiedThing> now = Standards();
            Kit.Check(now.Count == standardsBefore + 1, $"bringing the leader down drops exactly one standard ({now.Count - standardsBefore})");
            MinifiedThing mini = now.FirstOrDefault(m => ((Building_CapturedStandard)m.InnerThing).leaderName == leaderName);
            var s = mini?.InnerThing as Building_CapturedStandard;
            Kit.Check(s != null && s.factionName == enemy.Name && s.killerName == hero.LabelShort && s.clothColor == enemy.Color,
                $"...naming {enemy.Name}, {leaderName} and {hero.LabelShort}, in the faction's colour ({s?.factionName}, {s?.leaderName}, {s?.killerName}, {s?.clothColor})");
            Kit.Check(s != null && s.veterans.Contains(hero.thingIDNumber), "...with the one who brought him down among its veterans");
            Kit.Check(mini != null && mini.Position.DistanceTo(leader.PositionHeld) < 6f, "...on the ground near where he fell");

            // the rest of the raid dies: still one
            TestUtil.KillAll(raiders);
            Kit.Check(Standards().Count == standardsBefore + 1, "the rest of the raid dying drops no second standard");
        }

        void Install()
        {
            MinifiedThing mini = Standards().LastOrDefault();
            var s = mini?.InnerThing as Building_CapturedStandard;
            if (s == null) { Kit.Check(false, "setup: a standard to install"); return; }
            mini.GetDirectlyHeldThings().Remove(s);
            mini.Destroy();
            IntVec3 at = root + new IntVec3(3, 0, 3);
            GenSpawn.Spawn(s, at, map);
            s.SetFaction(Faction.OfPlayer);
            Kit.Check(s.Spawned && s.LabelNoCount.Contains(enemy.Name), $"setup: the standard installed ({s.LabelNoCount})");
            string inspect = s.GetInspectString();
            Kit.Check(inspect.Contains(enemy.Name) && inspect.Contains(s.leaderName) && inspect.Contains(hero.LabelShort),
                $"its inspect pane tells the story: \"{inspect.Replace("\n", " | ")}\"");

            // the emblem: the faction icon is chosen and printed clearly above the cloth (PrintPlane lifts the
            // top vertices of every plane by 0.01; the icon must clear that, or the cloth hides it)
            Texture2D icon = s.EmblemTexture;
            bool hasIcon = !enemy.def.factionIconPath.NullOrEmpty() || !enemy.def.settlementTexturePath.NullOrEmpty();
            float lift = (StandardGraphics.IconLift - StandardGraphics.ClothLift) * Altitudes.AltInc;
            Kit.Check((!hasIcon || (icon != null && icon != BaseContent.BadTex)) && lift > 0.012f,
                $"the standard carries {enemy.Name}'s emblem ({icon?.name ?? "none"}; icon path {enemy.def.factionIconPath}), printed {lift:0.000} above the cloth (> 0.01 vertex tilt)");

            onlooker = TestUtil.NewColonist(map, at + new IntVec3(-2, 0, 0), p => p.needs?.mood != null && !PawnUtility.IsBiologicallyOrArtificiallyBlind(p));
            TestUtil.Teleport(onlooker, at + new IntVec3(-2, 0, 0));
            onlooker.drafter.Drafted = true;
            TestUtil.Teleport(hero, at + new IntVec3(2, 0, 0));
            hero.drafter.Drafted = true;
            Kit.Check(Observe != null, "setup: found private PawnObserver.ObserveSurroundingThings");
            if (Observe == null) return;
            Observe.Invoke(onlooker.needs.mood.observer, null);
            Observe.Invoke(hero.needs.mood.observer, null);
            var mem = onlooker.needs.mood.thoughts.memories;
            Kit.Check(mem.GetFirstMemoryOfDef(AftermathDefOf.Aftermath_TrophyStandard) != null && mem.GetFirstMemoryOfDef(AftermathDefOf.Aftermath_TrophyStandardVeteran) == null,
                $"a colonist who sees it (vanilla PawnObserver) is proud: {AftermathDefOf.Aftermath_TrophyStandard.defName}");
            var memHero = hero.needs.mood.thoughts.memories;
            Kit.Check(memHero.GetFirstMemoryOfDef(AftermathDefOf.Aftermath_TrophyStandardVeteran) != null,
                "the veteran who brought the leader down gets the stronger veteran memory");
        }

        void FeatureOff()
        {
            Faction f2 = TestUtil.HostileFaction(not: null);
            AftermathMod.Settings.trophyStandards = false;
            int before = Standards().Count;
            raiders2 = TestUtil.Raid(map, f2, 400f);
            Lord l2 = raiders2.FirstOrDefault()?.GetLord();
            Pawn top = raiders2.OrderByDescending(p => p.kindDef.combatPower).FirstOrDefault();
            if (top != null) TestUtil.DownBy(top, hero);
            foreach (Pawn p in raiders2.Where(p => p != top)) Kit.KillBy(p, hero);
            Kit.Check(raiders2.Count > 0 && Standards().Count == before && (l2 == null || T.Get(l2) == null),
                $"with the feature off, a second raid's leader and members fall and nothing drops ({raiders2.Count} raiders)");
            AftermathMod.Settings.trophyStandards = true;
        }

        List<MinifiedThing> Standards() =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.MinifiedThing).OfType<MinifiedThing>()
                .Where(m => m.InnerThing is Building_CapturedStandard).ToList();

        protected override string Facts() =>
            $"lord {lord?.loadID}, members {lord?.ownedPawns.Count}, raiders {raiders.Count}";

        protected override void Cleanup()
        {
            AftermathMod.Settings.trophyStandards = true;
            TestUtil.KillAll(raiders);
            TestUtil.KillAll(raiders2);
            if (hero?.drafter != null && hero.Spawned) hero.drafter.Drafted = false;
            if (onlooker?.drafter != null && onlooker.Spawned) onlooker.drafter.Drafted = false;
        }
    }
}
