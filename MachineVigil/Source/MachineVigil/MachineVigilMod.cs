using HarmonyLib;
using UnityEngine;
using Verse;

namespace MachineVigil
{
    public class MachineVigilMod : Mod
    {
        public MachineVigilMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Machine Vigil] loaded");
        }
    }
}
