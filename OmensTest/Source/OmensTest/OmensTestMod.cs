using HarmonyLib;
using UnityEngine;
using Verse;

namespace OmensTest
{
    public class OmensTestMod : Mod
    {
        public OmensTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Omens Test] loaded");
        }
    }
}
