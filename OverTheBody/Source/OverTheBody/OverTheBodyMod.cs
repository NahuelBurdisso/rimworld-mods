using Verse;

namespace OverTheBody
{
    /// <summary>No Harmony: behaviour comes from a ThinkTreeDef inserted at Animal_PreMain and a GameComponent.</summary>
    public class OverTheBodyMod : Mod
    {
        public OverTheBodyMod(ModContentPack content) : base(content)
        {
            Log.Message("[Over the Body] loaded");
        }
    }
}
