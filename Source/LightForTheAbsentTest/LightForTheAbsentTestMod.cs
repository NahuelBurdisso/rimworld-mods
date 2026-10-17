using HarmonyLib;
using UnityEngine;
using Verse;

namespace LightForTheAbsentTest
{
    public class LightForTheAbsentTestMod : Mod
    {
        public LightForTheAbsentTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Light for the Absent Test] loaded");
        }
    }
}
