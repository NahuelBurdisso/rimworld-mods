using System;
using System.Collections.Generic;
using System.Linq;
using AfterTheRain;
using RimWorld;
using UnityEngine;
using Verse;

namespace AfterTheRainTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N`. Drives WeatherManager.TransitionTo through every rule of DESIGN.md,
    /// then lets a real rainbow run with three drafted colonists (two under open sky, one under a roof).
    /// Stages: tick 60 (rules + start), 760 (witnesses, inspiration, drawing, rain returns), 1500 (ended, tick path).
    /// </summary>
    public class RainHarness : GameComponent
    {
        bool active;
        int ticks;
        Map map;
        RainbowWatcher watcher;
        GameCondition_Rainbow rainbow;
        Pawn a, b, c;
        IntVec3 roofCell;
        bool roofAdded;
        readonly Dictionary<Pawn, bool> inspiredBefore = new Dictionary<Pawn, bool>();
        RainbowSettings saved;

        public RainHarness(Game game) { }

        public override void FinalizeInit() => active = Kit.HarnessArmed;

        static WeatherDef W(string name) => DefDatabase<WeatherDef>.GetNamedSilentFail(name);
        static WeatherDef Rain => W("Rain");

        public override void GameComponentTick()
        {
            if (!active) return;
            ticks++;
            try
            {
                if (ticks == 60) StageRules();
                else if (ticks == 760) StageWitnesses();
                else if (ticks == 1500) { StageEnded(); Finish(); }
            }
            catch (Exception ex)
            {
                Kit.Check(false, $"harness threw at tick {ticks}: {ex}");
                Finish();
            }
        }

        void Finish()
        {
            if (!active) return;
            active = false;
            RainbowWatcher.SunGlowSource = GenCelestial.CurCelestialSunGlow;
            if (saved != null)
            {
                var s = AfterTheRainMod.Settings;
                s.enabled = saved.enabled; s.chance = saved.chance; s.inspirationChance = saved.inspirationChance;
                s.showMessage = saved.showMessage; s.opacity = saved.opacity;
            }
            if (roofAdded) map.roofGrid.SetRoof(roofCell, null);
            foreach (Pawn p in new[] { a, b, c })
                if (p?.drafter != null && p.Spawned) p.drafter.Drafted = false;
            Kit.Summary("After the Rain");
        }

        /// <summary>Rain (fully settled) -> to, with the new weather at the given age.</summary>
        void SetTransition(WeatherDef from, WeatherDef to, int age)
        {
            WeatherManager wm = map.weatherManager;
            wm.TransitionTo(from);
            wm.curWeatherAge = 5000;
            wm.TransitionTo(to);
            wm.curWeatherAge = age;
        }

        void StageRules()
        {
            map = Find.CurrentMap;
            var st = AfterTheRainMod.Settings;
            saved = new RainbowSettings { enabled = st.enabled, chance = st.chance, inspirationChance = st.inspirationChance, showMessage = st.showMessage, opacity = st.opacity };
            st.enabled = true;
            st.showMessage = true;
            RainbowWatcher.SunGlowSource = _ => 0.6f;   // pin the sun so the early-fade check does not depend on the start hour

            watcher = map.GetComponent<RainbowWatcher>();
            Kit.Check(watcher != null, "RainbowWatcher map component exists");
            Kit.Check(RainbowDrawer.Ready && RainbowDrawer.Tex != null && RainbowDrawer.Tex.width == 1024, "rainbow texture and both materials loaded");
            Log.Message($"[Test] start: hour {GenLocalDate.HourFloat(map):0.00}, dayPercent {GenLocalDate.DayPercent(map):0.000}, real sun glow {GenCelestial.CurCelestialSunGlow(map):0.00}, weather {map.weatherManager.curWeather.defName}");

            // ---- R1 weather classification
            Kit.Check(RainbowWatcher.IsRain(Rain), "Rain counts as rain");
            if (ModsConfig.OdysseyActive)
                Kit.Check(RainbowWatcher.IsRain(W("TorrentialRain")) && RainbowWatcher.IsRain(W("ToxRain")), "Odyssey torrential and tox rain count as rain");
            Kit.Check(!RainbowWatcher.IsRain(W("RainyThunderstorm")), "a rainy thunderstorm is not rain (lightning)");
            Kit.Check(!RainbowWatcher.IsRain(W("FoggyRain")), "foggy rain is not rain (fog)");
            Kit.Check(!RainbowWatcher.IsRain(W("SnowGentle")) && !RainbowWatcher.IsRain(W("SnowHard")), "snow is not rain (vanilla snow has rainRate 1)");
            if (ModsConfig.AnomalyActive) Kit.Check(!RainbowWatcher.IsRain(W("BloodRain")), "blood rain is excluded");
            Kit.Check(!RainbowWatcher.IsRain(WeatherDefOf.Clear), "clear is not rain");
            Kit.Check(RainbowWatcher.IsClearSky(WeatherDefOf.Clear), "Clear is a clear sky");
            Kit.Check(!RainbowWatcher.IsClearSky(W("Fog")), "Fog is not a clear sky");
            if (ModsConfig.OdysseyActive)
                Kit.Check(RainbowWatcher.IsClearSky(W("Windy")) && !RainbowWatcher.IsClearSky(W("Overcast")), "Odyssey: Windy is a clear sky, Overcast is not");
            Kit.Check(!RainbowWatcher.IsClearSky(Rain) && !RainbowWatcher.IsClearSky(W("DryThunderstorm")), "Rain and a dry thunderstorm are not a clear sky");

            // ---- R2 sun window
            Kit.Check(RainbowWatcher.SunWindow(0.35f, 0.6f) && RainbowWatcher.SunWindow(0.65f, 0.6f), "morning and afternoon with a mid sun are in the window");
            Kit.Check(!RainbowWatcher.SunWindow(0.5f, 0.9f), "midday is outside the window");
            Kit.Check(!RainbowWatcher.SunWindow(0.1f, 0f) && !RainbowWatcher.SunWindow(0.9f, 0f), "night is outside the window");
            Kit.Check(!RainbowWatcher.SunWindow(0.3f, 0.1f), "a dawn sun still too low (glow 0.1) is outside the window");
            Kit.Check(!RainbowWatcher.SunWindow(0.45f, 1f) && !RainbowWatcher.SunWindow(0.55f, 1f), "late morning and early afternoon (sun high) are outside the window");
            Kit.Check(RainbowWatcher.InGraceWindow(0.44f) && !RainbowWatcher.InGraceWindow(0.47f) && !RainbowWatcher.InGraceWindow(0.85f),
                "a rainbow already up may run 1 hour past the window, then it fades");
            Kit.Check(GameCondition_Rainbow.SideFor(0.3f) == -1f && GameCondition_Rainbow.SideFor(0.7f) == 1f, "morning rainbows stand left (west), afternoon ones right (east)");
            // the real sun on this map, hour by hour today: the windows must actually open (vanilla glow saturates at 1)
            int dayStart = Find.TickManager.TicksAbs - (int)(GenLocalDate.HourFloat(map) * 2500f);
            bool morningOpens = false, afternoonOpens = false;
            var table = new System.Text.StringBuilder();
            for (int h = 0; h < 24; h++)
            {
                float glow = GenCelestial.CelestialSunGlow(map, dayStart + h * 2500 + 1250);
                float dp = (h + 0.5f) / 24f;
                bool open = RainbowWatcher.SunWindow(dp, glow);
                if (open && dp < 0.5f) morningOpens = true;
                if (open && dp > 0.5f) afternoonOpens = true;
                table.Append($"{h}:{glow:0.00}{(open ? "*" : "")} ");
            }
            Log.Message("[Test] sun glow by hour today (* = window open): " + table);
            Kit.Check(morningOpens && afternoonOpens, "on this map's real sun, both the morning and the afternoon window open today");

            // ---- R3 sky
            Kit.Check(RainbowWatcher.HasSky(map) && !RainbowWatcher.Suppressed(map), "the quicktest map has a sky and nothing suppresses rainbows");

            // ---- R4 negatives on the real weather manager
            Kit.Check(RainbowWatcher.ActiveRainbow(map) == null, "no rainbow at start");
            SetTransition(W("RainyThunderstorm"), WeatherDefOf.Clear, 2500);
            Kit.Check(watcher.Evaluate(0.35f, 0.6f, 1f) == RainbowResult.NotTransition, "thunderstorm -> clear: no rainbow");
            SetTransition(W("FoggyRain"), WeatherDefOf.Clear, 2500);
            Kit.Check(watcher.Evaluate(0.35f, 0.6f, 1f) == RainbowResult.NotTransition, "foggy rain -> clear: no rainbow");
            SetTransition(Rain, W("Fog"), 2500);
            Kit.Check(watcher.Evaluate(0.35f, 0.6f, 1f) == RainbowResult.NotTransition, "rain -> fog: no rainbow");
            SetTransition(Rain, WeatherDefOf.Clear, 1000);
            Kit.Check(watcher.Evaluate(0.35f, 0.6f, 1f) == RainbowResult.NotTransition, "rain -> clear, still mid-fade (age 1000): not yet");
            SetTransition(Rain, WeatherDefOf.Clear, 9000);
            Kit.Check(watcher.Evaluate(0.35f, 0.6f, 1f) == RainbowResult.NotTransition, "rain -> clear more than 3 hours ago (age 9000): too late");

            AfterTheRainMod.Settings.enabled = false;
            SetTransition(Rain, WeatherDefOf.Clear, 2100);
            Kit.Check(watcher.Evaluate(0.35f, 0.6f, 1f) == RainbowResult.Disabled, "setting off: no rainbow");
            AfterTheRainMod.Settings.enabled = true;

            GameCondition eclipse = GameConditionMaker.MakeCondition(GameConditionDefOf.Eclipse, 60000);
            map.gameConditionManager.RegisterCondition(eclipse);
            Kit.Check(watcher.Evaluate(0.35f, 0.6f, 1f) == RainbowResult.Suppressed, "eclipse active: suppressed");
            eclipse.End();

            SetTransition(Rain, WeatherDefOf.Clear, 2200);
            Kit.Check(watcher.Evaluate(0.5f, 0.9f, 1f) == RainbowResult.OutOfWindow, "rain -> clear at midday: out of window");
            Kit.Check(watcher.Evaluate(0.1f, 0f, 1f) == RainbowResult.OutOfWindow, "rain -> clear at night: out of window");
            Kit.Check(watcher.Evaluate(0.35f, 0.6f, 0f) == RainbowResult.RollFailed, "same transition later in the morning with chance 0: rolled and failed (window misses do not consume the roll)");
            Kit.Check(watcher.Evaluate(0.36f, 0.6f, 1f) == RainbowResult.AlreadyRolled, "same transition again with chance 1: already rolled, one roll per transition");
            Kit.Check(RainbowWatcher.ActiveRainbow(map) == null, "no rainbow after the failed roll");

            // ---- witnesses: A and C under open sky, B under a roof, all drafted so they stay awake and put
            List<Pawn> cols = Kit.Colonists(map);
            if (cols.Count < 3) { Kit.Check(false, "need three colonists, have " + cols.Count); return; }
            a = cols[0]; b = cols[1]; c = cols[2];
            IntVec3 centre = a.Position;
            IntVec3 open1 = FindCell(centre, roofed: false, avoid: IntVec3.Invalid);
            IntVec3 open2 = FindCell(open1 + new IntVec3(3, 0, 0), roofed: false, avoid: open1);
            roofCell = FindCell(open1 + new IntVec3(-3, 0, 0), roofed: false, avoid: open1, avoid2: open2);
            if (!open1.IsValid || !open2.IsValid || !roofCell.IsValid) { Kit.Check(false, "found three free unroofed cells"); return; }
            map.roofGrid.SetRoof(roofCell, RoofDefOf.RoofConstructed);
            roofAdded = true;
            foreach (var (p, cell) in new[] { (a, open1), (b, roofCell), (c, open2) })
            {
                p.drafter.Drafted = true;
                Kit.Teleport(p, cell);
                p.drafter.Drafted = true;
                if (p.Inspired) p.mindState.inspirationHandler.EndInspiration(p.mindState.inspirationHandler.CurState);
                inspiredBefore[p] = p.Inspired;
            }
            Kit.Check(!a.Position.Roofed(map) && b.Position.Roofed(map) && !c.Position.Roofed(map) && a.Awake() && b.Awake() && c.Awake(),
                "setup: A and C awake under open sky, B awake under a roof");
            Log.Message($"[Test] witnesses: A={a.LabelShort} inspirations available {(a.mindState.inspirationHandler.GetRandomAvailableInspirationDef() != null)}, C={c.LabelShort} {(c.mindState.inspirationHandler.GetRandomAvailableInspirationDef() != null)}");

            // ---- R5 a real rainbow
            AfterTheRainMod.Settings.inspirationChance = 1f;
            SetTransition(Rain, WeatherDefOf.Clear, 2400);
            RainbowResult r = watcher.Evaluate(0.35f, 0.6f, 1f);
            rainbow = RainbowWatcher.ActiveRainbow(map);
            Kit.Check(r == RainbowResult.Started && rainbow != null, "rain -> clear in the morning with chance 1: a rainbow starts (" + r + ")");
            if (rainbow == null) return;
            Kit.Check(rainbow.Duration >= 2500 && rainbow.Duration <= 5000, $"it lasts 1-2 hours ({rainbow.Duration} ticks)");
            Kit.Check(rainbow.side == GameCondition_Rainbow.SideFor(GenLocalDate.DayPercent(map)), $"it stands on the anti-solar side for the real hour (side {rainbow.side} at {GenLocalDate.HourFloat(map):0.0}h)");
            Kit.Check(rainbow.Strength < 0.05f, $"it starts invisible and fades in (strength {rainbow.Strength:0.00})");
            SetTransition(Rain, WeatherDefOf.Clear, 2500);
            Kit.Check(watcher.Evaluate(0.35f, 0.6f, 1f) == RainbowResult.AlreadyActive
                      && map.gameConditionManager.ActiveConditions.Count(x => x is GameCondition_Rainbow) == 1, "a second transition while one is up: still a single rainbow");
            // leave the weather settled clear so neither the watcher nor the early fade react until stage 2
            map.weatherManager.curWeatherAge = RainbowWatcher.MaxAge + 1000;
        }

        IntVec3 FindCell(IntVec3 near, bool roofed, IntVec3 avoid, IntVec3? avoid2 = null)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(near, 12f, true))
            {
                if (!cell.InBounds(map) || !cell.Standable(map) || cell.Roofed(map) != roofed || cell.Fogged(map)) continue;
                if (cell == avoid || (avoid2.HasValue && cell == avoid2.Value)) continue;
                if (cell.GetFirstPawn(map) != null) continue;
                return cell;
            }
            return IntVec3.Invalid;
        }

        void StageWitnesses()
        {
            if (rainbow == null) { Kit.Check(false, "stage 2 skipped: no rainbow"); return; }
            Kit.Check(map.gameConditionManager.ActiveConditions.Contains(rainbow) && rainbow.Strength >= 0.99f, $"700 ticks later it is fully up (strength {rainbow.Strength:0.00})");
            Memory(a, out bool memA); Memory(b, out bool memB); Memory(c, out bool memC);
            Kit.Check(memA && memC, "A and C, under open sky, remember seeing the rainbow");
            Kit.Check(!memB && !rainbow.HasSeen(b), "B, under a roof, did not see it");
            Thought_Memory t = a.needs.mood.thoughts.memories.GetFirstMemoryOfDef(RainbowDefOf.AfterTheRain_SawRainbow);
            Kit.Check(t != null && Mathf.Approximately(t.MoodOffset(), 2f), $"the memory is worth +2 mood ({t?.MoodOffset()})");
            int newlyInspired = new[] { a, c }.Count(p => p.Inspired && !inspiredBefore[p]);
            bool anyAvailable = new[] { a, c }.Any(p => p.Inspired || p.mindState.inspirationHandler.GetRandomAvailableInspirationDef() != null);
            if (!anyAvailable && newlyInspired == 0) Log.Warning("[Test] SKIP inspiration: neither witness had an inspiration available");
            else Kit.Check(newlyInspired == 1 && rainbow.inspiredOne, $"with inspiration chance 100%, exactly one witness was inspired ({newlyInspired}; A {a.mindState.inspirationHandler.CurStateDef?.defName ?? "-"}, C {c.mindState.inspirationHandler.CurStateDef?.defName ?? "-"}; any available {anyAvailable})");
            Kit.Check(!b.Inspired, "B, who did not see it, was not inspired");
            int again = rainbow.CheckWitnesses(map);
            Kit.Check(again == 0, "a second look adds no second memory");
            Kit.Check(GameCondition_Rainbow.DrawCount > 0, $"the arc was drawn ({GameCondition_Rainbow.DrawCount} frames)");
            Kit.Check(rainbow.SkyGazeChanceFactor(map) >= 3f, "skygazing is more likely while it is up");

            // rain comes back: the rainbow fades out early
            map.weatherManager.TransitionTo(Rain);
            rainbow.CheckFadeEarly(map);
            Kit.Check(rainbow.endingEarly && rainbow.TicksLeft <= GameCondition_Rainbow.Fade, $"rain again: it starts fading out now ({rainbow.TicksLeft} ticks left)");
        }

        static void Memory(Pawn p, out bool has) => has = p.needs.mood.thoughts.memories.GetFirstMemoryOfDef(RainbowDefOf.AfterTheRain_SawRainbow) != null;

        void StageEnded()
        {
            Kit.Check(rainbow == null || !map.gameConditionManager.ActiveConditions.Contains(rainbow), "after the fade-out the rainbow is gone");
            Kit.Check(watcher.LastEvaluateTick > 1000, $"the watcher's own tick keeps evaluating (last at {watcher.LastEvaluateTick}, result {watcher.LastResult})");
            Kit.Check(watcher.LastResult == RainbowResult.NotTransition || watcher.LastResult == RainbowResult.AlreadyRolled, "while it rains again the watcher does not start another");
        }
    }
}
