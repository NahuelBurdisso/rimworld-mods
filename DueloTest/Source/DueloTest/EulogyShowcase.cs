using System.Collections.Generic;
using System.Linq;
using Duelo;
using RimWorld;
using UnityEngine;
using Verse;

namespace DueloTest
{
    /// <summary>
    /// Visual check for Embers of the Eulogy (showcase.flag contains "eulogy"). A colonist with a real
    /// life (records, a marriage tale, a spouse) dies, is laid in a grave a few cells from the colony,
    /// and the colony holds a funeral (Ideology) - or, without Ideology, the burial reads the eulogy.
    /// Captures: lines rising with embers (three moments), the grave's inspect pane with the Read eulogy
    /// button, and the full eulogy window.
    /// </summary>
    public class EulogyShowcase : GameComponent
    {
        bool active, started, funeralMode;
        int ticks, shotsLines, endTick = -1;
        Building_Grave grave;
        Pawn deceased;
        int linesAtStart, lettersAtStart;
        float lastLineAt = -1f;
        int seenEmitted;
        readonly ShowcaseCamera cam = new ShowcaseCamera();
        int stage;   // 0 lines, 1 inspect, 2 dialog, 3 done

        public EulogyShowcase(Game game) { }

        public override void FinalizeInit()
        {
            active = ShowcaseFlag.Wants("eulogy");
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
            if (cam.Busy || stage >= 3) return;

            int emitted = MapComponent_Duelo.LinesEmitted - linesAtStart;
            if (stage == 0)
            {
                float now = Time.realtimeSinceStartup;
                if (emitted != seenEmitted) { seenEmitted = emitted; lastLineAt = now; }
                // shoot 1.4 s after lines 2, 4 and the last appear
                int want = Mathf.Min(2 * (shotsLines + 1), TotalLines);
                if (emitted >= want && now - lastLineAt >= 1.4f)
                {
                    shotsLines++;
                    cam.Shoot($"eulogy-{shotsLines}-lines", funeralMode ? TimeSpeed.Superfast : TimeSpeed.Normal);
                    if (shotsLines >= 3 || want >= TotalLines) stage = 1;
                }
                return;
            }
            if (stage == 1 && MapComponent_Duelo.LettersSent > lettersAtStart)
            {
                if (endTick < 0) endTick = ticks;
                if (ticks - endTick < 60) return;
                Find.Selector.ClearSelection();
                Find.Selector.Select(grave);
                cam.Shoot("eulogy-4-inspect", TimeSpeed.Paused);
                stage = 2;
                return;
            }
            if (stage == 2)
            {
                CompDueloGrave comp = grave.GetComp<CompDueloGrave>();
                var cmd = comp.CompGetGizmosExtra().OfType<Command_Action>().FirstOrDefault();
                cmd?.action();
                cam.Shoot("eulogy-5-read", TimeSpeed.Paused);
                stage = 3;
                Log.Message("[Showcase] eulogy done; grave text: " + comp.FullText.Replace("\n\n", " | "));
            }
        }

        int TotalLines => grave?.GetComp<CompDueloGrave>()?.eulogyLines?.Count
                          ?? GameComponent_DueloMemory.Instance?.Find(deceased?.ThingID)?.lines.Count ?? 6;

        void Begin()
        {
            started = true;
            Map map = Find.CurrentMap;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned.ToList();
            if (colonists.Count < 2) { Log.Error("[Showcase] need 2 colonists"); active = false; return; }
            deceased = colonists[0];
            Pawn spouse = colonists[1];

            if (!deceased.relations.DirectRelationExists(PawnRelationDefOf.Spouse, spouse))
                deceased.relations.AddDirectRelation(PawnRelationDefOf.Spouse, spouse);
            TaleRecorder.RecordTale(TaleDefOf.Marriage, deceased, spouse);
            deceased.records.AddTo(RecordDefOf.KillsHumanlikes, 9);
            deceased.records.AddTo(RecordDefOf.MealsCooked, 84);
            deceased.records.AddTo(RecordDefOf.ThingsConstructed, 61);

            IntVec3 near = colonists.Last().Position;
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(near + new IntVec3(0, 0, 4), map, 6, c =>
                GenConstruct.CanPlaceBlueprintAt(ThingDefOf.Grave, c, Rot4.South, map).Accepted
                && GenAdj.OccupiedRect(c, Rot4.South, ThingDefOf.Grave.size).Cells.All(x => x.Standable(map) && x.GetFirstPawn(map) == null && !x.Roofed(map)));
            grave = (Building_Grave)ThingMaker.MakeThing(ThingDefOf.Grave);
            grave.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(grave, cell, map, Rot4.South);

            Pawn raider = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Pirate,
                Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate), forceGenerateNewPawn: true));
            deceased.Kill(new DamageInfo(DamageDefOf.Bullet, 40f, instigator: raider), null);
            Corpse corpse = deceased.Corpse;
            if (corpse.Spawned) corpse.DeSpawn();
            grave.TryAcceptThing(corpse);

            linesAtStart = MapComponent_Duelo.LinesEmitted;
            lettersAtStart = MapComponent_Duelo.LettersSent;
            Find.CameraDriver.SetRootPosAndSize(grave.TrueCenter() + new Vector3(0f, 0f, 1.8f), 14f);
            Find.Selector.ClearSelection();

            Precept_Ritual funeral = null;
            if (ModsConfig.IdeologyActive && Faction.OfPlayer.ideos != null)
                foreach (Ideo ideo in Faction.OfPlayer.ideos.AllIdeos)
                    foreach (Precept pr in ideo.PreceptsListForReading)
                        if (pr is Precept_Ritual r && EulogyUtility.IsFuneral(r)) funeral = r;
            if (funeral != null)
            {
                funeralMode = true;
                var target = new TargetInfo(grave);
                RitualObligation obligation = funeral.activeObligations?.FirstOrDefault(o => o.targetA.Thing == deceased || (o.targetA.Thing is Corpse c && c.InnerPawn == deceased));
                RitualRoleAssignments assignments = Dialog_BeginRitual.CreateRitualRoleAssignments(funeral, target, map, null, null, null, null);
                assignments.FillPawns(null, target);
                funeral.behavior.TryExecuteOn(target, null, funeral, obligation, assignments, playerForced: true);
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            }
            else
            {
                DueloMod.Settings.eulogyAtBurialEvenWithFuneral = true;
                MapComponent_Duelo.For(map).TryReadAtBurial(grave);
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            }
            Log.Message($"[Showcase] {deceased.LabelShort} buried at {grave.Position}; funeral {funeralMode}; hour {GenLocalDate.HourOfDay(map)}");
        }
    }
}
