using HarmonyLib;
using UnityEngine;
using Verse;

namespace AftermathTest
{
    public class AftermathTestMod : Mod
    {
        public AftermathTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Aftermath Test] loaded");
        }
    }
}
