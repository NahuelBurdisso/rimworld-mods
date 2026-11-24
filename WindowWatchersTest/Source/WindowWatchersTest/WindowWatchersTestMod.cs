using HarmonyLib;
using UnityEngine;
using Verse;

namespace WindowWatchersTest
{
    public class WindowWatchersTestMod : Mod
    {
        public WindowWatchersTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Window Watchers Test] loaded");
        }
    }
}
