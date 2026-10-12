using HarmonyLib;
using UnityEngine;
using Verse;

namespace DueloTest
{
    public class DueloTestMod : Mod
    {
        public DueloTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Duelo Test] loaded");
        }
    }
}
