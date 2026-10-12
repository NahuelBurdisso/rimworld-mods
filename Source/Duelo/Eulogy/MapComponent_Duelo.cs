using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Duelo
{
    /// <summary>A eulogy being read at a grave right now. Transient: not saved.</summary>
    public class EulogyReading
    {
        public Building_Grave grave;
        public Pawn deceased;
        public string pawnName;
        public List<string> lines;
        public LordJob_Ritual ritual;    // null: read at a burial
        public int startTick;
        public int next;
        public float lastLineReal = -999f;
        public bool ritualFinished;
        public bool AtFuneral => ritual != null;
    }

    /// <summary>
    /// Finds funerals at graves, reads eulogies (text lines + embers), and sends the letter.
    /// Cost: a lord scan every 250 ticks; readings tick every 30 ticks only while one is active.
    /// </summary>
    public class MapComponent_Duelo : MapComponent
    {
        public const int FuneralScanInterval = 250;
        public const int ReadingInterval = 30;

        /// <summary>Real seconds between two lines so they do not overlap at high speed. Tests set it to 0.</summary>
        public static float MinRealSecondsBetweenLines = 3.2f;

        public readonly List<EulogyReading> readings = new List<EulogyReading>();
        public readonly List<CompDueloGrave> graves = new List<CompDueloGrave>();

        // test/debug counters
        public static int LinesEmitted;
        public static int LettersSent;
        public static int ReadingsStarted;
        public static int FuneralReadingsStarted;
        public static int BurialsDeferredToFuneral;
        public static Mote_EulogyLine LastLine;

        public MapComponent_Duelo(Map map) : base(map) { }

        public static MapComponent_Duelo For(Map map) => map?.GetComponent<MapComponent_Duelo>();

        public void RegisterGrave(CompDueloGrave comp)
        {
            if (!graves.Contains(comp)) graves.Add(comp);
            comp.registered = true;
        }

        public void DeregisterGrave(CompDueloGrave comp)
        {
            graves.Remove(comp);
            comp.registered = false;
        }

        /// <summary>Moss of the beloved: only graves in view, only with moss. Current map only (MapInterface).</summary>
        public override void MapComponentDraw()
        {
            if (!DueloMod.Settings.moss || graves.Count == 0 || map != Find.CurrentMap) return;
            CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(3);
            float night = 1f - map.skyManager.CurSkyGlow;
            float brightness = DueloMod.Settings.mossBrightness;
            for (int i = 0; i < graves.Count; i++)
            {
                CompDueloGrave g = graves[i];
                if (g.mossLevel < 0.01f || !g.parent.Spawned || !view.Contains(g.parent.Position)) continue;
                MossDrawer.Draw(g, night, brightness);
            }
        }

        public bool IsReadingAt(Building_Grave grave)
        {
            for (int i = 0; i < readings.Count; i++)
                if (readings[i].grave == grave) return true;
            return false;
        }

        public override void MapComponentTick()
        {
            int t = Find.TickManager.TicksGame;
            try
            {
                if (t % FuneralScanInterval == 0 && DueloMod.Settings.eulogy && ModsConfig.IdeologyActive) ScanFunerals();
                if (readings.Count > 0 && t % ReadingInterval == 0) TickReadings();
            }
            catch (System.Exception e)
            {
                Log.ErrorOnce("[Duelo] eulogy reading failed: " + e, 0x5D0E4);
                readings.Clear();
            }
        }

        // ---------------------------------------------------------------- triggers

        void ScanFunerals()
        {
            List<Lord> lords = map.lordManager.lords;
            for (int i = 0; i < lords.Count; i++)
            {
                if (!(lords[i].LordJob is LordJob_Ritual lj) || !EulogyUtility.IsFuneral(lj.Ritual)) continue;
                if (!(lj.selectedTarget.Thing is Building_Grave grave) || !grave.Spawned || grave.Map != map) continue;
                Pawn deceased = grave.Corpse?.InnerPawn;
                if (!EulogyComposer.Qualifies(deceased) || IsReadingAt(grave)) continue;
                CompDueloGrave comp = grave.GetComp<CompDueloGrave>();
                if (comp == null || comp.SpokenFor(deceased)) continue;   // already read at the burial
                StartReading(grave, deceased, lj);
            }
        }

        /// <summary>Called from the burial patch.</summary>
        public void TryReadAtBurial(Building_Grave grave)
        {
            if (!DueloMod.Settings.eulogy || !grave.Spawned) return;
            Pawn deceased = grave.Corpse?.InnerPawn;
            if (!EulogyComposer.Qualifies(deceased) || IsReadingAt(grave)) return;
            CompDueloGrave comp = grave.GetComp<CompDueloGrave>();
            if (comp == null || comp.SpokenFor(deceased)) return;
            if (!DueloMod.Settings.eulogyAtBurialEvenWithFuneral && EulogyUtility.FuneralPending(deceased))
            {
                BurialsDeferredToFuneral++;
                return;
            }
            if (EulogyUtility.CountMourners(grave, deceased) == 0) return;
            StartReading(grave, deceased, null);
        }

        public EulogyReading StartReading(Building_Grave grave, Pawn deceased, LordJob_Ritual ritual)
        {
            EulogyRecord rec = EulogyUtility.GetOrCompose(deceased);
            var reading = new EulogyReading
            {
                grave = grave,
                deceased = deceased,
                pawnName = deceased.LabelShortCap,
                lines = new List<string>(rec.lines),
                ritual = ritual,
                startTick = Find.TickManager.TicksGame,
            };
            // A burial cannot be called off: the grave keeps the words at once. A funeral keeps them only
            // when it completes, so a funeral called off by a raid can be held (and read) again.
            if (ritual == null) grave.GetComp<CompDueloGrave>()?.SetEulogy(deceased, rec.lines, false);
            readings.Add(reading);
            ReadingsStarted++;
            if (ritual != null) FuneralReadingsStarted++;
            EulogyUtility.ThrowEmbers(map, grave.TrueCenter(), 10, 0.45f);
            if (Prefs.DevMode)
                Log.Message($"[Duelo] eulogy for {reading.pawnName} at {(ritual != null ? "funeral" : "burial")}: {reading.lines.Count} lines");
            return reading;
        }

        // ---------------------------------------------------------------- reading

        void TickReadings()
        {
            for (int i = readings.Count - 1; i >= 0; i--)
            {
                EulogyReading r = readings[i];
                if (r.grave == null || r.grave.Destroyed || !r.grave.Spawned || r.grave.Map != map)
                {
                    readings.RemoveAt(i);
                    continue;
                }

                Vector3 center = r.grave.TrueCenter();
                EulogyUtility.ThrowEmbers(map, center, Rand.RangeInclusive(1, 3), 0.5f);

                bool mayEmit;
                if (r.AtFuneral && !r.ritualFinished)
                {
                    Lord lord = r.ritual.lord;
                    if (lord != null && map.lordManager.lords.Contains(lord))
                    {
                        // spread the lines over the first 90% of the ceremony
                        mayEmit = r.next < r.lines.Count && r.ritual.Progress >= (r.next + 0.5f) / r.lines.Count * 0.9f;
                    }
                    else if (r.ritual.Ritual != null && r.ritual.Ritual.lastFinishedTick >= r.startTick)
                    {
                        r.ritualFinished = true;   // completed: say what is left, then the letter
                        mayEmit = true;
                    }
                    else
                    {
                        readings.RemoveAt(i);      // called off: stop quietly
                        continue;
                    }
                }
                else
                {
                    mayEmit = true;
                }

                if (mayEmit && r.next < r.lines.Count && Time.realtimeSinceStartup - r.lastLineReal >= MinRealSecondsBetweenLines)
                    EmitLine(r, center);

                if (r.next >= r.lines.Count && (!r.AtFuneral || r.ritualFinished))
                {
                    Complete(r);
                    readings.RemoveAt(i);
                }
            }
        }

        void EmitLine(EulogyReading r, Vector3 center)
        {
            string line = r.lines[r.next++];
            r.lastLineReal = Time.realtimeSinceStartup;
            Vector3 at = center + new Vector3(0f, 0f, 0.7f);
            if (!at.ToIntVec3().InBounds(map)) at = center;
            var mote = (Mote_EulogyLine)ThingMaker.MakeThing(DueloDefOf.Duelo_Mote_EulogyLine);
            mote.text = line;
            mote.exactPosition = at;
            mote.SetVelocity(0f, Mote_EulogyLine.RiseSpeed);
            GenSpawn.Spawn(mote, at.ToIntVec3(), map);
            LastLine = mote;
            LinesEmitted++;
            EulogyUtility.ThrowEmbers(map, center, 16, 0.55f);
        }

        void Complete(EulogyReading r)
        {
            if (r.AtFuneral && r.deceased != null) r.grave.GetComp<CompDueloGrave>()?.SetEulogy(r.deceased, r.lines, true);
            if (!DueloMod.Settings.eulogyLetter) return;
            string intro = (r.AtFuneral ? "Duelo_EulogyLetterFuneral" : "Duelo_EulogyLetterBurial").Translate(r.pawnName);
            var text = new System.Text.StringBuilder(intro);
            for (int i = 0; i < r.lines.Count; i++) text.Append("\n\n").Append(r.lines[i]);
            Find.LetterStack.ReceiveLetter("Duelo_EulogyLetterLabel".Translate(r.pawnName), text.ToString(), LetterDefOf.NeutralEvent, r.grave);
            LettersSent++;
        }
    }
}
