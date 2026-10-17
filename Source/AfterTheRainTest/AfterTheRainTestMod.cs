using HarmonyLib;
using UnityEngine;
using Verse;

namespace AfterTheRainTest
{
    public class AfterTheRainTestMod : Mod
    {
        public AfterTheRainTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[After the Rain Test] loaded");
        }
    }
}
