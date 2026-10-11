using HarmonyLib;
using UnityEngine;
using Verse;

namespace OldScoresTest
{
    public class OldScoresTestMod : Mod
    {
        public OldScoresTestMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before defs are parsed - patches apply to def loading too.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Old Scores Test] loaded");
        }
    }
}
