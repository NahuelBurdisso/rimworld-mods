using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace ColonyTemplate
{
    // Templates live next to the game's own save data: <SaveData>/ColonyTemplates/<name>.xml
    public static class TemplateFiles
    {
        public static string Folder
        {
            get
            {
                string dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "ColonyTemplates");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string PathFor(string name) => Path.Combine(Folder, name + ".xml");

        public static List<string> Names() =>
            Directory.GetFiles(Folder, "*.xml").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();

        public static bool IsValidName(string name) =>
            !name.NullOrEmpty() && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        public static void Save(string name, CrewTemplate template)
        {
            try
            {
                SafeSaver.Save(PathFor(name), "colonyTemplate", () =>
                {
                    ScribeMetaHeaderUtility.WriteMetaHeader();
                    Scribe_Deep.Look(ref template, "crew");
                });
            }
            catch (Exception ex)
            {
                Log.Error("[Colony Template] could not save " + name + ": " + ex);
            }
        }

        public static CrewTemplate Load(string name)
        {
            CrewTemplate template = null;
            try
            {
                Scribe.loader.InitLoading(PathFor(name));
                try
                {
                    Scribe_Deep.Look(ref template, "crew");
                    Scribe.loader.FinalizeLoading();
                }
                catch
                {
                    Scribe.ForceStop();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Colony Template] could not load " + name + ": " + ex);
                return null;
            }
            return template;
        }
    }
}
