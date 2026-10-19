using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyTemplate
{
    // "Save crew" / "Load crew" on the Create Characters page.
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DoWindowContents))]
    public static class Page_ConfigureStartingPawns_DoWindowContents_Patch
    {
        const float ButtonWidth = 170f, ButtonHeight = 32f;

        public static void Postfix(Rect rect)
        {
            float x = rect.center.x - ButtonWidth - 4f;
            if (Widgets.ButtonText(new Rect(x, rect.y, ButtonWidth, ButtonHeight), "ColonyTemplate_SaveCrew".Translate()))
                TemplateUI.SaveDialog(Find.GameInitData.startingAndOptionalPawns);
            if (Widgets.ButtonText(new Rect(x + ButtonWidth + 8f, rect.y, ButtonWidth, ButtonHeight), "ColonyTemplate_LoadCrew".Translate()))
                TemplateUI.LoadMenu();
        }
    }

    public static class TemplateUI
    {
        public static void SaveDialog(List<Pawn> pawns)
        {
            CrewTemplate crew = CrewCapture.Capture(pawns);
            Find.WindowStack.Add(new Dialog_NameTemplate(name =>
            {
                TemplateFiles.Save(name, crew);
                Messages.Message("ColonyTemplate_Saved".Translate(name, crew.pawns.Count), MessageTypeDefOf.PositiveEvent, historical: false);
            }));
        }

        public static void LoadMenu()
        {
            List<string> names = TemplateFiles.Names();
            if (names.Count == 0)
            {
                Messages.Message("ColonyTemplate_NoTemplates".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            var opts = new List<FloatMenuOption>();
            foreach (string name in names)
                opts.Add(new FloatMenuOption(name, () => Load(name)));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        static void Load(string name)
        {
            CrewTemplate crew = TemplateFiles.Load(name);
            if (crew == null || crew.pawns.Count == 0)
            {
                Messages.Message("ColonyTemplate_LoadFailed".Translate(name), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            var problems = new StringBuilder();
            CrewApply.ApplyToStartingPawns(crew, problems);
            Messages.Message("ColonyTemplate_Loaded".Translate(name), MessageTypeDefOf.PositiveEvent, historical: false);
            if (problems.Length > 0)
                Find.WindowStack.Add(new Dialog_MessageBox("ColonyTemplate_Problems".Translate() + "\n\n" + problems));
        }
    }

    public class Dialog_NameTemplate : Window
    {
        readonly System.Action<string> onAccept;
        string name = "";
        bool focused;

        public override Vector2 InitialSize => new Vector2(320f, 175f);

        public Dialog_NameTemplate(System.Action<string> onAccept)
        {
            this.onAccept = onAccept;
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnClickedOutside = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            bool enter = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "ColonyTemplate_NamePrompt".Translate());
            GUI.SetNextControlName("ColonyTemplateName");
            name = Widgets.TextField(new Rect(0f, 35f, inRect.width, 35f), name);
            if (!focused)
            {
                UI.FocusControl("ColonyTemplateName", this);
                focused = true;
            }
            bool exists = TemplateFiles.IsValidName(name) && TemplateFiles.Names().Contains(name);
            if (exists)
                Widgets.Label(new Rect(0f, 72f, inRect.width, 24f), "ColonyTemplate_WillOverwrite".Translate().Colorize(ColorLibrary.Yellow));
            if (Widgets.ButtonText(new Rect(inRect.width / 2f - 70f, inRect.height - 35f, 140f, 35f), "OK".Translate()) || enter)
            {
                if (!TemplateFiles.IsValidName(name))
                {
                    Messages.Message("ColonyTemplate_BadName".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                }
                else
                {
                    onAccept(name);
                    Close();
                }
                Event.current.Use();
            }
        }
    }

    // Bottom-bar button on a map: colony setup now, or save the current colonists as a template.
    public class MainButtonWorker_ColonyTemplate : MainButtonWorker
    {
        public override void Activate()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("ColonyTemplate_RunSetup".Translate(), () =>
                {
                    int n = ColonySetup.Apply(map);
                    Messages.Message("ColonyTemplate_SetupDone".Translate(n), MessageTypeDefOf.PositiveEvent, historical: false);
                }),
                new FloatMenuOption("ColonyTemplate_SaveColonists".Translate(), () =>
                    TemplateUI.SaveDialog(CrewCapture.MapColonists(map))),
                new FloatMenuOption("ColonyTemplate_OpenSettings".Translate(), () =>
                    Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<ColonyTemplateMod>()))),
            };
            Find.WindowStack.Add(new FloatMenu(opts));
        }
    }
}
