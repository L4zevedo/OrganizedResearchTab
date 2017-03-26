/* Lucas Azevedo
 * 2017-03-25
 */

using System;

using Harmony;

//using UnityEngine;
//using RimWorld;
using Verse;

namespace OrganizedResearch
{
    [StaticConstructorOnStartup]
    static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            var harmony = HarmonyInstance.Create("rimworld.lazevedo.organizedresearchtab.main");

            // no longer needed, but might be useful in the future
            //harmony.Patch(
            //    AccessTools.Method(typeof(MainTabWindow_Research), "DrawRightRect"), // original
            //    new HarmonyMethod(typeof(OrganizedResearch), "DrawRightRectPrefix"), // prefix
            //    null); // no postfix
        }
    }
}
