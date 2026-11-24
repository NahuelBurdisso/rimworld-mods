using HarmonyLib;
using UnityEngine;
using Verse;

namespace TheDogKnowsTest
{
    public class TheDogKnowsTestMod : Mod
    {
        public TheDogKnowsTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[The Dog Knows Test] loaded");
        }
    }
}
