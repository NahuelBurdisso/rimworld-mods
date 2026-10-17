using HarmonyLib;
using UnityEngine;
using Verse;

namespace UnlikelyHeroTest
{
    public class UnlikelyHeroTestMod : Mod
    {
        public UnlikelyHeroTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Unlikely Hero Test] loaded");
        }
    }
}
