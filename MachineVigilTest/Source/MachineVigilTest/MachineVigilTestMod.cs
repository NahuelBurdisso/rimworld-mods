using HarmonyLib;
using UnityEngine;
using Verse;

namespace MachineVigilTest
{
    public class MachineVigilTestMod : Mod
    {
        public MachineVigilTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Machine Vigil Test] loaded");
        }
    }
}
