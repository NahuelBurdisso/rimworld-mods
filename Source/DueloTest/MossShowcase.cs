using System.Collections.Generic;
using System.Linq;
using Duelo;
using RimWorld;
using UnityEngine;
using Verse;

namespace DueloTest
{
    /// <summary>
    /// Visual check for Moss of the Beloved (showcase.flag contains "moss"). Three graves side by side:
    /// left, someone the whole colony loved (spouse, child, friend alive); middle, someone one colonist
    /// liked; right, a stranger. Showcase shortcut: the moss is set straight to its target instead of
    /// growing for a day. Captures by day, then at night (time jumped forward), then with the loved
    /// grave selected.
    /// </summary>
    public class MossShowcase : GameComponent
    {
        bool active, started;
        int ticks, stage, waitUntil;
        readonly ShowcaseCamera cam = new ShowcaseCamera();
        readonly List<Building_Grave> graves = new List<Building_Grave>();

        public MossShowcase(Game game) { }

        public override void FinalizeInit()
        {
            active = ShowcaseFlag.Wants("moss");
        }

        public override void GameComponentTick()
        {
            if (!active) return;
            ticks++;
            if (!started && ticks == 120) Begin();
        }

        public override void GameComponentUpdate()
        {
            if (!active || !started) return;
            cam.Update();
            if (cam.Busy || ticks < waitUntil) return;
            Map map = Find.CurrentMap;
            switch (stage)
            {
                case 0:
                    Log.Message($"[Showcase] day: hour {GenLocalDate.HourOfDay(map)}, sky glow {map.skyManager.CurSkyGlow:F2}");
                    cam.Shoot("moss-1-day", TimeSpeed.Normal);
                    stage = 1;
                    break;
                case 1:
                {
                    int hour = GenLocalDate.HourOfDay(map);
                    int toNight = ((23 - hour) % 24 + 24) % 24;
                    Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + toNight * GenDate.TicksPerHour);
                    waitUntil = ticks + 180;   // let the sky settle
                    stage = 2;
                    break;
                }
                case 2:
                    Log.Message($"[Showcase] night: hour {GenLocalDate.HourOfDay(map)}, sky glow {map.skyManager.CurSkyGlow:F2}");
                    cam.Shoot("moss-2-night", TimeSpeed.Normal);
                    stage = 3;
                    break;
                case 3:
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(graves[0]);
                    cam.Shoot("moss-3-night-inspect", TimeSpeed.Paused);
                    stage = 4;
                    foreach (Building_Grave g in graves)
                    {
                        CompDueloGrave c = g.GetComp<CompDueloGrave>();
                        Log.Message($"[Showcase] {c.Buried?.LabelShort}: love {c.lastLove:F0}, moss {c.mossLevel:F2}");
                    }
                    break;
            }
        }

        Pawn NewPawn(Map map, IntVec3 near, Faction faction)
        {
            var req = new PawnGenerationRequest(faction != null ? PawnKindDefOf.Colonist : PawnKindDefOf.Villager, faction,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, fixedIdeo: faction != null ? Faction.OfPlayer.ideos?.PrimaryIdeo : null);
            Pawn p = PawnGenerator.GeneratePawn(req);
            GenSpawn.Spawn(p, CellFinder.StandableCellNear(near, map, 3f), map);
            return p;
        }

        void Begin()
        {
            started = true;
            Map map = Find.CurrentMap;
            List<Pawn> living = map.mapPawns.FreeColonistsSpawned.ToList();
            if (living.Count < 3) { Log.Error("[Showcase] need 3 colonists"); active = false; return; }

            IntVec3 origin = living[0].Position + new IntVec3(0, 0, 6);
            IntVec3 start = CellFinder.RandomClosewalkCellNear(origin, map, 8, c =>
                Enumerable.Range(0, 3).All(i => CanGrave(map, c + new IntVec3(i * 3, 0, 0))));
            Pawn loved = NewPawn(map, start, Faction.OfPlayer);
            Pawn liked = NewPawn(map, start, Faction.OfPlayer);
            Pawn stranger = NewPawn(map, start, null);

            loved.relations.AddDirectRelation(PawnRelationDefOf.Spouse, living[0]);
            living[1].relations.AddDirectRelation(PawnRelationDefOf.Parent, loved);
            for (int i = 0; i < 5; i++)
            {
                living[2].needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.RescuedMe, loved);
                living[0].needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.RescuedMe, loved);
            }
            living[2].relations.AddDirectRelation(PawnRelationDefOf.Lover, liked);   // one lover who still remembers
            for (int i = 0; i < 3; i++) living[2].needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.RescuedMe, liked);

            Pawn[] dead = { loved, liked, stranger };
            for (int i = 0; i < 3; i++)
            {
                var g = (Building_Grave)ThingMaker.MakeThing(ThingDefOf.Grave);
                g.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(g, start + new IntVec3(i * 3, 0, 0), map, Rot4.South);
                graves.Add(g);
                dead[i].Kill(null, null);
                Corpse corpse = dead[i].Corpse;
                if (corpse.Spawned) corpse.DeSpawn();
                g.TryAcceptThing(corpse);
                CompDueloGrave c = g.GetComp<CompDueloGrave>();
                c.RecomputeMoss();
                c.mossLevel = c.mossTarget;   // showcase shortcut: skip a day of growth
            }
            Find.CameraDriver.SetRootPosAndSize(graves[1].TrueCenter() + new Vector3(0f, 0f, 0.5f), 11f);
            Find.Selector.ClearSelection();
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            waitUntil = ticks + 60;
            Log.Message($"[Showcase] moss graves at {start}: {loved.LabelShort} (loved), {liked.LabelShort} (liked), {stranger.LabelShort} (stranger)");
        }

        static bool CanGrave(Map map, IntVec3 c)
        {
            return c.InBounds(map) && GenConstruct.CanPlaceBlueprintAt(ThingDefOf.Grave, c, Rot4.South, map).Accepted
                && GenAdj.OccupiedRect(c, Rot4.South, ThingDefOf.Grave.size).Cells.All(x => x.InBounds(map) && x.Standable(map) && x.GetFirstPawn(map) == null && !x.Roofed(map));
        }
    }
}
