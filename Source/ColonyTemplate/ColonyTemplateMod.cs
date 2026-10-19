using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyTemplate
{
    public class ColonyTemplateMod : Mod
    {
        public static ColonyTemplateSettings Settings;

        public ColonyTemplateMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ColonyTemplateSettings>();
            new Harmony(content.PackageId).PatchAll();
        }

        public override string SettingsCategory() => "Colony Template";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            l.CheckboxLabeled("ColonyTemplate_AutoApply".Translate(), ref Settings.autoApplyOnNewGame, "ColonyTemplate_AutoApplyTip".Translate());
            l.GapLine();
            l.CheckboxLabeled("ColonyTemplate_DoSchedule".Translate(), ref Settings.applySchedule);
            if (Settings.applySchedule)
            {
                l.Label("ColonyTemplate_ScheduleHelp".Translate());
                string edited = l.TextEntry(Settings.schedule);
                if (edited.Length <= 24) Settings.schedule = edited.ToUpperInvariant();
                if (!ColonyTemplateSettings.ValidSchedule(Settings.schedule))
                    l.Label("ColonyTemplate_ScheduleInvalid".Translate().Colorize(ColorLibrary.RedReadable));
                if (l.ButtonText("ColonyTemplate_ScheduleReset".Translate()))
                    Settings.schedule = ColonyTemplateSettings.DefaultSchedule;
            }
            l.GapLine();
            l.CheckboxLabeled("ColonyTemplate_DoPriorities".Translate(), ref Settings.applyPriorities, "ColonyTemplate_DoPrioritiesTip".Translate());
            l.CheckboxLabeled("ColonyTemplate_DoPolicies".Translate(), ref Settings.applyPolicies, "ColonyTemplate_DoPoliciesTip".Translate());
            if (Settings.applyPolicies)
            {
                if (l.ButtonTextLabeled("ColonyTemplate_MedCare".Translate(), Settings.medCare.GetLabel()))
                {
                    var opts = new System.Collections.Generic.List<FloatMenuOption>();
                    foreach (MedicalCareCategory c in Enum.GetValues(typeof(MedicalCareCategory)))
                        opts.Add(new FloatMenuOption(c.GetLabel(), () => Settings.medCare = c));
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
                l.CheckboxLabeled("ColonyTemplate_SelfTend".Translate(), ref Settings.selfTend);
            }
            l.End();
        }
    }

    public class ColonyTemplateSettings : ModSettings
    {
        // sleep 22-6, work 6-19, recreation 19-22
        public const string DefaultSchedule = "SSSSSSWWWWWWWWWWWWWJJJSS";

        public bool autoApplyOnNewGame;
        public bool applySchedule = true;
        public string schedule = DefaultSchedule;
        public bool applyPriorities = true;
        public bool applyPolicies = true;
        public MedicalCareCategory medCare = MedicalCareCategory.NormalOrWorse;
        public bool selfTend = true;

        public static bool ValidSchedule(string s)
        {
            if (s == null || s.Length != 24) return false;
            foreach (char c in s)
                if (ColonySetup.Assignment(c) == null) return false;
            return true;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref autoApplyOnNewGame, "autoApplyOnNewGame", false);
            Scribe_Values.Look(ref applySchedule, "applySchedule", true);
            Scribe_Values.Look(ref schedule, "schedule", DefaultSchedule);
            Scribe_Values.Look(ref applyPriorities, "applyPriorities", true);
            Scribe_Values.Look(ref applyPolicies, "applyPolicies", true);
            Scribe_Values.Look(ref medCare, "medCare", MedicalCareCategory.NormalOrWorse);
            Scribe_Values.Look(ref selfTend, "selfTend", true);
        }
    }

    // Runs the colony setup once on the first tick of a new game when enabled in the settings.
    public class ColonyTemplateGameComponent : GameComponent
    {
        bool pending;

        public ColonyTemplateGameComponent(Game game) { }

        public override void StartedNewGame()
        {
            pending = ColonyTemplateMod.Settings.autoApplyOnNewGame;
        }

        public override void GameComponentTick()
        {
            if (!pending) return;
            pending = false;
            Map map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (map == null) return;
            int n = ColonySetup.Apply(map);
            Messages.Message("ColonyTemplate_SetupDone".Translate(n), MessageTypeDefOf.NeutralEvent, historical: false);
        }
    }
}
