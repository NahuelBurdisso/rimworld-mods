using HarmonyLib;
using UnityEngine;
using Verse;

namespace OverTheBodyTest
{
    public class OverTheBodyTestMod : Mod
    {
        public OverTheBodyTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Over the Body Test] loaded");
        }
    }
}
