using HarmonyLib;
using UnityEngine;
using Verse;

namespace PackHowlsTest
{
    public class PackHowlsTestMod : Mod
    {
        public PackHowlsTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Pack Howls Test] loaded");
        }
    }
}
