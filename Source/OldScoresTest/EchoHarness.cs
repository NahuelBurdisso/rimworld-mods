using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using OldScores;
using RimWorld;
using UnityEngine;
using Verse;

namespace OldScoresTest
{
    /// <summary>
    /// Feature 4 — Raid Echoes: three real raids (two walk-ins from the same edge, one drop-pod raid), the
    /// non-raids that must leave no echo, the overlay draw counters, a Scribe roundtrip and forgetting.
    /// </summary>
    public class EchoHarness : GameComponent
    {
        const int WalkTicks = 600, BreachWaitTicks = 150, EndWaitTicks = 200, PodLandTimeout = 900;

        bool active, done;
        int ticks, phase, since;
        Map map;
        Faction enemy;
        Pawn killer;
        OldScoresMapComponent comp;
        IntVec3 firstArrival = IntVec3.Invalid;
        int raidStartTick, oldRoadTickBefore;
        readonly List<Pawn> raidPawns = new List<Pawn>();
        readonly List<IntVec3> painted = new List<IntVec3>();
        int linesBefore, labelsBefore, frames = -1;
        int guiProbe;   // 1: measure the play-settings row in the next OnGUI repaint; 2: done
        float rowOn = -1, rowOff = -1, worldOn = -1, worldOff = -1;
        string guiError;

        public EchoHarness(Game game) { }

        /// <summary>The real DoPlaySettingsGlobalControls, called from OnGUI with fresh rows: how wide is the icon row?</summary>
        public override void GameComponentOnGUI()
        {
            if (!active || done || guiProbe != 1 || Event.current.type != EventType.Repaint) return;
            guiProbe = 2;
            bool old = OldScoresMod.Settings.echoes;
            try
            {
                rowOn = RowWidth(true, false);
                rowOff = RowWidth(false, false);
                worldOn = RowWidth(true, true);
                worldOff = RowWidth(false, true);
            }
            catch (System.Exception ex) { guiError = ex.Message; }
            finally { OldScoresMod.Settings.echoes = old; }
        }

        static float RowWidth(bool feature, bool worldView)
        {
            OldScoresMod.Settings.echoes = feature;
            var row = new WidgetRow(0f, 0f, UIDirection.RightThenUp);
            Find.PlaySettings.DoPlaySettingsGlobalControls(row, worldView);
            return row.FinalX;
        }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (TestUtil.Prelude(ticks, TestUtil.EchoesStart, "echoes")) return;
            try { Step(); }
            catch (System.Exception ex) { Kit.Check(false, "echo harness threw: " + ex); Finish(); }
        }

        /// <summary>Draw-counter checks need rendered frames, which keep coming while the game is unpaused.</summary>
        public override void GameComponentUpdate()
        {
            if (!active || done || frames < 0) return;
            frames++;
        }

        void Next(int p) { phase = p; since = ticks; }
        int Waited => ticks - since;

        void Step()
        {
            switch (phase)
            {
                case 0: Setup(); break;
                case 1: if (Waited >= WalkTicks) Walked(); break;
                case 2: if (Waited >= BreachWaitTicks) Breached(); break;
                case 3: WaitEnd(4, "first walk-in raid"); break;
                case 4: FirstEcho(); break;
                case 5: if (Waited >= 300) { KillRaid(); Next(6); } break;
                case 6: WaitEnd(7, "second walk-in raid"); break;
                case 7: SecondEcho(); break;
                case 8: PodsLanding(); break;
                case 9: if (Waited >= 120) { KillRaid(); Next(10); } break;
                case 10: WaitEnd(11, "drop-pod raid"); break;
                case 11: PodEcho(); break;
                case 12: OverlayOff(); break;
                case 13: OverlayOn(); break;
                case 14: RoundtripAndForget(); Finish(); break;
            }
        }

        // --- 0: negatives, then the first raid ----------------------------------------------------

        void Setup()
        {
            map = Find.CurrentMap;
            comp = OldScoresMapComponent.For(map);
            Kit.Check(comp != null, "setup: the map has the Old Scores map component");
            if (comp == null) { Finish(); return; }
            comp.ClearEchoes();
            OldScoresTracker.Instance.showEchoes = false;
            enemy = TestUtil.HostileFaction();
            killer = map.mapPawns.FreeColonistsSpawned.FirstOrDefault() ?? TestUtil.NewColonist(map, map.Center);
            Kit.Check(enemy != null, $"setup: a hostile faction to raid us ({enemy?.Name ?? "none"})");
            if (enemy == null) { Finish(); return; }

            Helpers();
            Negatives();

            // feature off: a real raid is not tracked
            OldScoresMod.Settings.echoes = false;
            List<Pawn> offRaid = Raid(enemy, PawnsArrivalModeDefOf.EdgeWalkIn, IntVec3.Invalid, out bool offOk);
            OldScoresMod.Settings.echoes = true;
            Kit.Check(offOk && comp.ActiveRaids.Count == 0, $"with the feature off a raid is not tracked (raid fired {offOk}, {offRaid.Count} raiders)");
            KillAll(offRaid);

            // raid 1: walk-in, the arrival mode picks the edge
            raidPawns.Clear();
            raidPawns.AddRange(Raid(enemy, PawnsArrivalModeDefOf.EdgeWalkIn, IntVec3.Invalid, out bool ok));
            raidStartTick = Find.TickManager.TicksGame;
            ActiveRaid raid = comp.ActiveRaids.FirstOrDefault();
            Kit.Check(ok && raid != null && comp.ActiveRaids.Count == 1, $"a real walk-in raid of {enemy.Name} is tracked (fired {ok}, active raids {comp.ActiveRaids.Count})");
            if (raid == null) { Finish(); return; }
            Kit.Check(raid.tracks.Count == raidPawns.Count && raid.walkIn && raid.factionId == enemy.loadID,
                $"...one track per raider ({raid.tracks.Count}/{raidPawns.Count}), walk-in, faction id {raid.factionId}");
            Kit.Check(raid.arrival.IsValid, $"...its arrival cell is the raid's spawn centre ({raid.arrival})");
            firstArrival = raid.arrival;
            int nearEdge = raid.tracks.Count(t => t.points.Count == 1 && EdgeDist(t.points[0]) <= 12);
            Kit.Check(nearEdge == raid.tracks.Count, $"...every road starts at the map edge where they stepped on ({nearEdge}/{raid.tracks.Count} within 12 cells)");
            Next(1);
        }

        void Helpers()
        {
            var line = Enumerable.Range(0, 10).Select(i => new IntVec3(10 + i * 3, 0, 20)).ToList();
            List<IntVec3> simple = OldScoresMapComponent.Simplify(line, 1f);
            Kit.Check(simple.Count == 2 && simple[0] == line[0] && simple[1] == line[9], $"a straight road simplifies to its two ends ({simple.Count} points)");
            var bent = new List<IntVec3> { new IntVec3(0, 0, 0), new IntVec3(5, 0, 0), new IntVec3(10, 0, 0), new IntVec3(10, 0, 5), new IntVec3(10, 0, 10) };
            Kit.Check(OldScoresMapComponent.Simplify(bent, 1f).Count == 3, "a road with one corner keeps the corner");
            var many = Enumerable.Range(0, 64).Select(i => new IntVec3(i, 0, 0)).ToList();
            OldScoresMapComponent.Decimate(many);
            Kit.Check(many.Count == 33 && many[0].x == 0 && many[32].x == 63, $"a full track (64 points) is halved keeping both ends ({many.Count}, last x {many.Last().x})");

            // the old road: same faction within 20 cells; not 25 cells off, not another faction, not an old pod raid
            var walk = new RaidEcho { factionId = 9001, walkIn = true, arrival = new IntVec3(50, 0, 0) };
            walk.laneStarts.Add(0); walk.points.Add(new IntVec3(50, 0, 1)); walk.points.Add(new IntVec3(55, 0, 20));
            var pod = new RaidEcho { factionId = 9002, walkIn = false, arrival = new IntVec3(100, 0, 0) };
            pod.laneStarts.Add(0); pod.points.Add(new IntVec3(100, 0, 1)); pod.points.Add(new IntVec3(100, 0, 20));
            var roads = new List<RaidEcho> { walk, pod };
            Kit.Check(OldScoresMapComponent.MatchesOldRoad(roads, 9001, new IntVec3(65, 0, 0)), "old road: the same faction 15 cells from where it came before matches");
            Kit.Check(!OldScoresMapComponent.MatchesOldRoad(roads, 9001, new IntVec3(75, 0, 0)), "old road: 25 cells off does not");
            Kit.Check(!OldScoresMapComponent.MatchesOldRoad(roads, 9003, new IntVec3(50, 0, 0)), "old road: another faction at the very same spot does not");
            Kit.Check(!OldScoresMapComponent.MatchesOldRoad(roads, 9002, new IntVec3(100, 0, 0)), "old road: an old drop-pod raid is no road");

            // loitering: no farther from the road's start for 8 samples -> no points until he pushes on
            var t = new RaidTrack();
            t.points.Add(new IntVec3(10, 0, 10));
            bool walking = !OldScoresMapComponent.Loitering(t, new IntVec3(20, 0, 10));
            int quiet = 0;
            for (int i = 0; i < 10; i++) if (OldScoresMapComponent.Loitering(t, new IntVec3(i % 2 == 0 ? 17 : 19, 0, 12))) quiet++;
            bool pushes = !OldScoresMapComponent.Loitering(t, new IntVec3(26, 0, 10));
            Kit.Check(walking && quiet == 10 - OldScoresTuning.EchoStallSamples + 1 && pushes,
                $"a raider milling around (siege camp) stops adding points after {OldScoresTuning.EchoStallSamples} samples and resumes when he pushes on (walking {walking}, quiet samples {quiet}, resumed {pushes})");

            // fading
            int now = Find.TickManager.TicksGame;
            int fresh = OldScoresMapComponent.AlphaStep(new RaidEcho { endTick = now }, now);
            int yearOld = OldScoresMapComponent.AlphaStep(new RaidEcho { endTick = now - OldScoresTuning.EchoFadeTicks }, now);
            int older = OldScoresMapComponent.AlphaStep(new RaidEcho { endTick = now - OldScoresTuning.EchoFadeTicks * 2 }, now);
            Kit.Check(fresh >= 8 && yearOld == 3 && older == 3, $"echoes fade with age: alpha {fresh / 10f} new, {yearOld / 10f} at a year, {older / 10f} after");
        }

        void Negatives()
        {
            // hostiles that walk in without a raid incident
            var loose = new List<Pawn> { Kit.SpawnHostile(map, map.Center, null, enemy), Kit.SpawnHostile(map, map.Center, null, enemy) };
            Kit.Check(comp.ActiveRaids.Count == 0, "hostiles spawned without a raid incident leave no echo");
            KillAll(loose);
            FireNeutral(IncidentDefOf.VisitorGroup, "a visitor group");
            FireNeutral(IncidentDefOf.TraderCaravanArrival, "a trader caravan");
            FireNeutral(IncidentDefOf.RaidFriendly, "a friendly raid (military aid)");
            Kit.Check(comp.Echoes.Count == 0, "...and nothing was remembered");
        }

        void FireNeutral(IncidentDef def, string what)
        {
            var before = new HashSet<Pawn>(map.mapPawns.AllPawns);
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            parms.forced = true;
            parms.points = Mathf.Max(parms.points, 300f);
            if (def == IncidentDefOf.RaidFriendly)
            {
                // name an ally that can send fighters, walking in, so the worker has no reason to refuse
                parms.faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f =>
                    !f.IsPlayer && !f.Hidden && !f.defeated && f.def.humanlikeFaction && !f.HostileTo(Faction.OfPlayer)
                    && f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Any(g => g.kindDef == PawnGroupKindDefOf.Combat));
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
                if (parms.faction == null) { Log.Message($"[Test] SKIP {what}: no non-hostile faction with fighters on this world"); return; }
            }
            bool ok = Execute(def, parms);
            List<Pawn> arrived = map.mapPawns.AllPawns.Where(p => !before.Contains(p)).ToList();
            if (!ok)
            {
                Log.Message($"[Test] SKIP {what}: the incident did not fire on this world");
                return;
            }
            // asserted whenever it fired, even if its pawns are still in drop pods
            Kit.Check(comp.ActiveRaids.Count == 0, $"{what} ({arrived.Count} pawns of {arrived.FirstOrDefault()?.Faction?.Name ?? parms.faction?.Name}) creates no echo");
            foreach (Pawn p in arrived) if (!p.Destroyed) p.Destroy(DestroyMode.Vanish);   // out of the way of the raids
        }

        // --- 1-4: the first raid walks, breaks in, dies -------------------------------------------

        void Walked()
        {
            ActiveRaid raid = comp.ActiveRaids.FirstOrDefault();
            if (raid == null) { Kit.Check(false, "the first raid is still on after 600 ticks of walking (it ended early)"); Next(3); return; }
            ComponentRoundtrip(raid);
            RaidTrack lead = raid.tracks.Where(t => t.pawn != null && t.pawn.Spawned).OrderByDescending(t => t.points.Count).FirstOrDefault();
            int moving = raid.tracks.Count(t => t.points.Count >= 2);
            float went = lead == null ? 0f : lead.points[0].DistanceTo(lead.points[lead.points.Count - 1]);
            Kit.Check(moving > 0 && went > 6f, $"after {WalkTicks} ticks the roads are sampled ({moving} tracks with points, lead walked {went:0.0} cells over {lead?.points.Count} points)");
            Kit.Check(raid.tracks.All(t => t.points.Count <= OldScoresTuning.EchoMaxPointsPerTrack), "...no track over the point cap");
            Kit.Check(raid.tracks.All(t => t.points.Zip(t.points.Skip(1), (a, b) => (a - b).LengthHorizontalSquared >= 9 || t.breach == b).All(x => x)),
                "...consecutive points are at least 3 cells apart");
            if (lead != null && lead.recording)
            {
                // the colony's Home area under him: the next sample is his breach
                IntVec3 at = lead.pawn.Position;
                foreach (IntVec3 c in CellRect.CenteredOn(at, 4).ClipInsideMap(map))
                    if (!map.areaManager.Home[c]) { map.areaManager.Home[c] = true; painted.Add(c); }
                Log.Message($"[Test] (painted Home area around the lead raider at {at})");
            }
            Next(2);
        }

        void Breached()
        {
            ActiveRaid raid = comp.ActiveRaids.FirstOrDefault();
            RaidTrack b = raid?.tracks.FirstOrDefault(t => t.breach.IsValid);
            Kit.Check(b != null && !b.recording && b.points.Last() == b.breach && map.areaManager.Home[b.breach],
                $"stepping into the Home area is a breach and ends that road ({b?.breach.ToString() ?? "none"})");

            // a raider carried off while downed is still on the map (MapHeld): his track stays, and his death counts
            RaidTrack carriedTrack = raid?.tracks.FirstOrDefault(t => t != b && t.pawn != null && t.pawn.Spawned && !t.pawn.Downed);
            if (carriedTrack != null && raid.tracks.Count(t => t.pawn != null && t.pawn.Spawned && !t.pawn.Downed) >= 2)
            {
                Pawn r = carriedTrack.pawn;
                Kit.Down(r);
                bool carried = r.Downed && !r.Dead && killer.carryTracker.TryStartCarry(r, 1, reserve: false) == 1;   // the overload carrying jobs use: despawns him
                comp.SampleRaidsNow();
                Kit.Check(carried && !r.Spawned && r.MapHeld == map && carriedTrack.pawn == r && comp.ActiveRaids.Contains(raid),
                    $"a downed raider carried by someone is still the raid's (carried {carried}, spawned {r.Spawned}, track kept {carriedTrack.pawn == r}, raid on {comp.ActiveRaids.Contains(raid)})");
                int deathsBefore = raid.deaths.Count;
                if (!r.Dead) r.Kill(new DamageInfo(DamageDefOf.Bullet, 40f, 0f, -1f, killer));
                Kit.Check(raid.deaths.Count == deathsBefore + 1 && raid.deaths.Last() == killer.Position,
                    $"...and when he dies in someone's arms his death is remembered where he was held ({raid.deaths.LastOrDefault()} vs {killer.Position})");
                if (killer.carryTracker.CarriedThing != null) killer.carryTracker.TryDropCarriedThing(killer.Position, ThingPlaceMode.Near, out Thing _);
            }
            else Log.Message("[Test] SKIP carried-raider check: fewer than two raiders still standing");
            KillRaid();
            Next(3);
        }

        void KillRaid()
        {
            foreach (Pawn p in raidPawns)
                if (p != null && !p.Dead && p.Spawned) Kit.KillBy(p, killer);
        }

        void WaitEnd(int nextPhase, string what)
        {
            if (comp.ActiveRaids.Count == 0)
            {
                Kit.Check(true, $"the {what} ends within {Waited} ticks of the last raider falling");
                Next(nextPhase);
            }
            else if (Waited > EndWaitTicks)
            {
                ActiveRaid r = comp.ActiveRaids[0];
                Kit.Check(false, $"the {what} ends (still on after {Waited} ticks; tracks {r.tracks.Count}, live {r.tracks.Count(t => t.pawn != null)})");
                comp.ClearEchoes();
                Next(nextPhase);
            }
        }

        void FirstEcho()
        {
            RaidEcho e = comp.Echoes.LastOrDefault();
            Kit.Check(comp.Echoes.Count == 1 && e != null && e.factionId == enemy.loadID && e.factionName == enemy.Name,
                $"the ended raid became an echo of {enemy.Name} (echoes {comp.Echoes.Count})");
            if (e == null) { Finish(); return; }
            bool lanesOk = e.laneStarts.Count >= 1 && e.laneStarts.Count <= OldScoresTuning.EchoMaxLanes
                           && Enumerable.Range(0, e.laneStarts.Count).All(l => e.LaneEnd(l) - e.laneStarts[l] >= 2)
                           && e.points.All(c => c.InBounds(map));
            Kit.Check(lanesOk && e.IsWellFormed, $"...with {e.laneStarts.Count} lane(s) of {string.Join("/", Enumerable.Range(0, e.laneStarts.Count).Select(l => e.LaneEnd(l) - e.laneStarts[l]))} points, all on the map");
            Kit.Check(e.points[0].DistanceTo(e.arrival) <= 15f, $"...the first lane starts at the arrival ({e.points[0]} vs {e.arrival})");
            Kit.Check(e.breaches.Count >= 1 && e.breaches.Count <= OldScoresTuning.EchoMaxBreaches, $"...a breach is remembered ({string.Join(", ", e.breaches)})");
            int dead = raidPawns.Count(p => p.Dead);
            Kit.Check(e.deaths.Count == Mathf.Min(dead, OldScoresTuning.EchoMaxDeaths) && e.deaths.All(c => c.InBounds(map)),
                $"...one death cell per raider who fell ({e.deaths.Count} cells, {dead} dead)");
            Kit.Check(e.walkIn && e.raiders == raidPawns.Count && e.endTick > e.startTick, $"...walk-in, {e.raiders} raiders, {e.endTick - e.startTick} ticks long");
            List<string> labels = comp.EchoLabelsForTest();
            string want = $"{enemy.Name} - 1 raid, broke in here";
            Kit.Check(labels.Any(l => l.StartsWith(want)), $"the overlay label reads \"{want}\" ({string.Join(" | ", labels)})");

            // raid 2: same faction, same edge
            oldRoadTickBefore = comp.lastOldRoadTick;
            raidPawns.Clear();
            raidPawns.AddRange(Raid(enemy, PawnsArrivalModeDefOf.EdgeWalkIn, firstArrival, out bool ok));
            Kit.Check(ok && comp.ActiveRaids.Count == 1, $"a second walk-in raid from the same place ({firstArrival}) is tracked");
            string msg = comp.lastOldRoadText;
            Kit.Check(comp.lastOldRoadTick == Find.TickManager.TicksGame && msg != null && msg.Contains("old road") && msg.Contains(enemy.Name),
                $"...and the colony notices: \"{msg}\"");
            var live = Traverse.Create(typeof(Messages)).Field("liveMessages").GetValue<List<Message>>();
            Kit.Check(live != null && live.Any(m => m.text == msg), "...as a real message on screen");
            Next(5);
        }

        void SecondEcho()
        {
            Kit.Check(comp.Echoes.Count == 2 && comp.Echoes.All(x => x.factionId == enemy.loadID), $"two raids, two echoes ({comp.Echoes.Count})");
            List<string> labels = comp.EchoLabelsForTest();
            string want = $"{enemy.Name} - 2 raids, broke in here";
            Kit.Check(labels.Any(l => l.StartsWith(want)), $"the label counts both: \"{want}\" ({string.Join(" | ", labels)})");

            // raid 3: drop pods, same faction: tracked once landed, never "the old road"
            oldRoadTickBefore = comp.lastOldRoadTick;
            raidPawns.Clear();
            raidPawns.AddRange(Raid(enemy, PawnsArrivalModeDefOf.EdgeDrop, IntVec3.Invalid, out bool ok));
            ActiveRaid raid = comp.ActiveRaids.FirstOrDefault();
            Kit.Check(ok && raid != null && !raid.walkIn, $"a drop-pod raid is tracked as pods (fired {ok}, walkIn {raid?.walkIn})");
            int pending = raid?.tracks.Count(t => !t.seen) ?? 0;
            Log.Message($"[Test] (drop-pod raid: {raid?.tracks.Count} raiders, {pending} still in their pods)");
            Kit.Check(comp.lastOldRoadTick == oldRoadTickBefore, "...and pods never get the old-road message");
            if (raid == null) { Next(12); return; }
            Next(8);
        }

        void PodsLanding()
        {
            ActiveRaid raid = comp.ActiveRaids.FirstOrDefault();
            if (raid == null) { Kit.Check(false, $"the drop-pod raid waited for its pods (ended after {Waited} ticks)"); Next(12); return; }
            if (raid.tracks.All(t => t.seen || t.pawn == null))
            {
                Kit.Check(raid.tracks.Any(t => t.points.Count >= 1), $"the pod raiders are tracked once they land ({Waited} ticks)");
                Next(9);
            }
            else if (Waited > PodLandTimeout)
            {
                Kit.Check(false, $"the pod raiders land within {PodLandTimeout} ticks ({raid.tracks.Count(t => !t.seen)} still unseen)");
                Next(9);
            }
        }

        void PodEcho()
        {
            RaidEcho e = comp.Echoes.LastOrDefault();
            Kit.Check(comp.Echoes.Count == 3 && e != null && !e.walkIn, $"the drop-pod raid became an echo without walk-in (echoes {comp.Echoes.Count}, walkIn {e?.walkIn})");
            Next(12);
        }

        // --- 12-13: the overlay -------------------------------------------------------------------

        void OverlayOff()
        {
            if (frames < 0)
            {
                Patches info = Harmony.GetPatchInfo(AccessTools.Method(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls)));
                Kit.Check(info != null && info.Postfixes.Any(p => p.owner == "nahuel.oldscores"), "the play-settings icon row carries the Raid Echoes toggle postfix");
                guiProbe = 1;
                RaidEcho first = comp.Echoes.FirstOrDefault();
                IntVec3 look = first == null ? map.Center : first.breaches.Count > 0 ? first.breaches[0] : first.points[first.LaneEnd(0) - 1];
                Find.CameraDriver.SetRootPosAndSize(look.ToVector3Shifted(), 24f);
                OldScoresTracker.Instance.showEchoes = false;
                linesBefore = EchoStats.Lines;
                labelsBefore = EchoStats.Labels;
                frames = 0;
                return;
            }
            if ((frames < 20 || guiProbe == 1) && Waited < 3000) return;
            Kit.Check(guiProbe == 2 && guiError == null && rowOn - rowOff >= 20f && rowOn - rowOff <= 32f,
                $"the map-view icon row gets one more icon with the feature on (row width {rowOn} vs {rowOff} off{(guiError == null ? "" : ", error " + guiError)})");
            Kit.Check(guiProbe == 2 && Mathf.Abs(worldOn - worldOff) < 0.01f, $"...and none in the world view ({worldOn} vs {worldOff})");
            Kit.Check(EchoStats.Lines == linesBefore && EchoStats.Labels == labelsBefore,
                $"overlay off: nothing drawn over {frames} frames (lines +{EchoStats.Lines - linesBefore}, labels +{EchoStats.Labels - labelsBefore})");
            OldScoresTracker.Instance.showEchoes = true;
            linesBefore = EchoStats.Lines;
            labelsBefore = EchoStats.Labels;
            frames = 0;
            Next(13);
        }

        void OverlayOn()
        {
            if (frames < 20 && Waited < 3000) return;
            Kit.Check(EchoStats.Lines > linesBefore, $"overlay on: echo lines drawn ({EchoStats.Lines - linesBefore} line draws over {frames} frames)");
            Kit.Check(EchoStats.Labels > labelsBefore, $"overlay on: echo labels drawn ({EchoStats.Labels - labelsBefore} over {frames} frames)");
            Kit.Check((EchoStats.Lines - linesBefore) / Mathf.Max(1, frames) <= OldScoresTuning.EchoMaxDrawsPerFrame, "...within the per-frame draw cap");
            OldScoresTracker.Instance.showEchoes = false;
            frames = -1;
            Next(14);
        }

        /// <summary>
        /// The whole map component through Scribe while a raid is on: tracks keep their live pawns (the test
        /// registers them with the loader, as a real game load would have them deep-loaded), points and flags
        /// survive; and a component saved before this feature existed (no nodes) loads empty, not null.
        /// </summary>
        void ComponentRoundtrip(ActiveRaid raid)
        {
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "OldScoresEchoComponent.xml");
            OldScoresMapComponent saved = comp, loaded = null, empty = null;
            try
            {
                Scribe.saver.InitSaving(path, "echoTest");
                Scribe.saver.savingForDebug = true;   // the pawns are deep-saved by the map in a real save
                Scribe_Deep.Look(ref saved, "comp", map);
                Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loaded, "comp", map);
                var dir = (LoadedObjectDirectory)AccessTools.Field(typeof(CrossRefHandler), "loadedObjectDirectory").GetValue(Scribe.loader.crossRefs);
                foreach (Pawn p in raid.tracks.Select(t => t.pawn).Where(p => p != null).Distinct()) dir.RegisterLoaded(p);
                Scribe.loader.FinalizeLoading();

                File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<echoTest><comp Class=\"OldScores.OldScoresMapComponent\" /></echoTest>");
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref empty, "comp", map);
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                if (File.Exists(path)) File.Delete(path);
            }
            ActiveRaid back = loaded?.ActiveRaids.FirstOrDefault();
            bool same = back != null && loaded.ActiveRaids.Count == comp.ActiveRaids.Count && back.factionId == raid.factionId
                        && back.arrival == raid.arrival && back.walkIn == raid.walkIn && back.startTick == raid.startTick
                        && back.tracks.Count == raid.tracks.Count
                        && back.tracks.Zip(raid.tracks, (a, b) => a.pawn == b.pawn && a.points.SequenceEqual(b.points) && a.breach == b.breach
                                                                 && a.recording == b.recording && a.seen == b.seen && a.stall == b.stall).All(x => x);
            Kit.Check(same, $"save/load mid-raid: the map component comes back with its active raid, live pawns and roads ({back?.tracks.Count ?? 0} tracks, {back?.tracks.Count(t => t.pawn != null) ?? 0} pawns)");
            Kit.Check(empty != null && empty.Echoes != null && empty.ActiveRaids != null && empty.Echoes.Count == 0 && empty.ActiveRaids.Count == 0,
                "an old save without Raid Echoes data loads with empty lists");
        }

        // --- 14: save/load and forgetting ---------------------------------------------------------

        void RoundtripAndForget()
        {
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "OldScoresEchoRoundtrip.xml");
            List<RaidEcho> saved = comp.Echoes.ToList();
            List<RaidEcho> loaded = null;
            try
            {
                Scribe.saver.InitSaving(path, "echoTest");
                Scribe_Collections.Look(ref saved, "echoes", LookMode.Deep);
                Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loaded, "echoes", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                if (File.Exists(path)) File.Delete(path);
            }
            bool same = loaded != null && loaded.Count == saved.Count && saved.Count > 0;
            for (int i = 0; same && i < saved.Count; i++)
            {
                RaidEcho a = saved[i], b = loaded[i];
                same = a.factionId == b.factionId && a.factionName == b.factionName && a.startTick == b.startTick && a.endTick == b.endTick
                       && a.arrival == b.arrival && a.walkIn == b.walkIn && a.raiders == b.raiders
                       && a.points.SequenceEqual(b.points) && a.laneStarts.SequenceEqual(b.laneStarts)
                       && a.breaches.SequenceEqual(b.breaches) && a.deaths.SequenceEqual(b.deaths) && b.IsWellFormed;
            }
            Kit.Check(same, $"save/load: {saved.Count} echoes written with Scribe and read back are identical");

            // forgetting: a copy dated two years and a day ago goes, the real ones stay
            RaidEcho old = loaded?.FirstOrDefault();
            if (old != null)
            {
                int n = comp.Echoes.Count;
                old.endTick = Find.TickManager.TicksGame - OldScoresTuning.EchoForgetTicks - GenDate.TicksPerDay;
                comp.AddEcho(old);
                Kit.Check(comp.Echoes.Count == n + 1, "setup: an echo two years old added");
                comp.ForgetOldEchoesNow();
                Kit.Check(comp.Echoes.Count == n && !comp.Echoes.Contains(old), $"an echo older than two years is forgotten, the recent {n} stay");
            }

            // a faction that never broke in (and is gone from the world): label at the end of its road, saved name
            var never = new RaidEcho { factionId = 987654, factionName = "The Lost Patrol", endTick = Find.TickManager.TicksGame, walkIn = true, raiders = 3 };
            IntVec3 from = new IntVec3(5, 0, map.Size.z / 2), to = new IntVec3(25, 0, map.Size.z / 2 + 4);
            never.laneStarts.Add(0);
            never.points.Add(from);
            never.points.Add(to);
            never.arrival = from;
            comp.AddEcho(never);
            List<string> labels = comp.EchoLabelsForTest();
            Kit.Check(labels.Contains("The Lost Patrol - 1 raid @ " + to), $"a faction that never broke in is labelled where its road ended, by its saved name ({string.Join(" | ", labels)})");
        }

        // --- helpers ------------------------------------------------------------------------------

        List<Pawn> Raid(Faction f, PawnsArrivalModeDef mode, IntVec3 center, out bool ok)
        {
            var before = new HashSet<Pawn>(map.mapPawns.AllPawns);
            ok = false;
            // vanilla raid generation can come back empty at random (e.g. Biotech's children-only age
            // restriction finding no pawns); a few tries
            for (int attempt = 0; attempt < 4 && !ok; attempt++)
            {
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
                parms.faction = f;
                parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
                parms.raidArrivalMode = mode;
                parms.points = 250f;
                parms.forced = true;
                if (center.IsValid) parms.spawnCenter = center;
                ok = Execute(IncidentDefOf.RaidEnemy, parms);
                if (!ok) Log.Message($"[Test] (raid attempt {attempt + 1} of {f.Name} via {mode.defName} did not fire)");
            }
            // pod raiders are not spawned yet: take them from the active raid when there is one
            var pawns = map.mapPawns.AllPawns.Where(p => !before.Contains(p) && p.Faction == f).ToList();
            ActiveRaid raid = comp.ActiveRaids.LastOrDefault();
            if (raid != null && raid.startTick == Find.TickManager.TicksGame)
                foreach (RaidTrack t in raid.tracks) if (t.pawn != null && !pawns.Contains(t.pawn)) pawns.Add(t.pawn);
            return pawns;
        }

        static bool Execute(IncidentDef def, IncidentParms parms)
        {
            TimeSpeed speed = Find.TickManager.CurTimeSpeed;
            bool ok = def.Worker.TryExecute(parms);
            // the raid letter (ThreatBig) pauses the game under the default auto-pause setting
            if (Find.TickManager.CurTimeSpeed == TimeSpeed.Paused && speed != TimeSpeed.Paused) Find.TickManager.CurTimeSpeed = speed;
            return ok;
        }

        int EdgeDist(IntVec3 c) => Mathf.Min(Mathf.Min(c.x, map.Size.x - 1 - c.x), Mathf.Min(c.z, map.Size.z - 1 - c.z));

        static void KillAll(IEnumerable<Pawn> pawns)
        {
            foreach (Pawn p in pawns.ToList())
                if (p != null && !p.Dead && !p.Destroyed) { if (p.Spawned) p.Kill(null); else p.Destroy(DestroyMode.Vanish); }
        }

        void Finish()
        {
            OldScoresMod.Settings.echoes = true;
            if (OldScoresTracker.Instance != null) OldScoresTracker.Instance.showEchoes = false;
            if (map != null) foreach (IntVec3 c in painted) map.areaManager.Home[c] = false;
            frames = -1;
            KillAll(raidPawns);
            comp?.ClearEchoes();   // nothing of the test (e.g. "The Lost Patrol") stays on the map
            Kit.Summary("OldScores/Echoes");
            TestUtil.MarkFinished("Echoes");
            done = true;
        }
    }
}
