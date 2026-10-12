using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimMcpGovTest
{
    public class RimMcpGovTestMod : Mod
    {
        public RimMcpGovTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[RimMcp Gov Test] loaded");
        }
    }
}
