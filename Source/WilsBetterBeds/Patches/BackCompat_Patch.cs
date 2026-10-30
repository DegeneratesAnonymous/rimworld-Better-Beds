using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace WilsBetterBeds.Patches
{
    /// <summary>
    /// The mod used to be called Smarter Beds, and everything it put in a save carried that name: its
    /// saved data (locks and cooldowns) and the mood memories it gave. These two patches teach the game
    /// that the old names now mean the new ones, so a save made before the rename loads cleanly and
    /// keeps what it had. They only ever fill in an answer the game could not find; once the old names
    /// have been saved over they never fire.
    /// </summary>
    internal static class BackCompatNames
    {
        public const string OldPrefix = "SmarterBeds";
        public const string NewPrefix = "WilsBetterBeds";

        public static MethodBase Find(string name) =>
            typeof(BackCompatibility).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == name);
    }

    /// <summary>A saved class name such as "SmarterBeds.GameComponent_BedNegotiation" becomes the new one.</summary>
    [HarmonyPatch]
    public static class BackCompat_Type_Patch
    {
        static MethodBase TargetMethod() => BackCompatNames.Find("GetBackCompatibleType");
        static bool Prepare() => TargetMethod() != null;

        static void Postfix(string providedClassName, ref Type __result)
        {
            if (__result != null || providedClassName == null || !providedClassName.StartsWith(BackCompatNames.OldPrefix + ".", StringComparison.Ordinal)) return;
            __result = typeof(BackCompat_Type_Patch).Assembly.GetType(BackCompatNames.NewPrefix + providedClassName.Substring(BackCompatNames.OldPrefix.Length));
        }
    }

    /// <summary>A saved def name such as "SmarterBeds_GotBetterRoom" becomes the new one.</summary>
    [HarmonyPatch]
    public static class BackCompat_DefName_Patch
    {
        static MethodBase TargetMethod() => BackCompatNames.Find("BackCompatibleDefName");
        static bool Prepare() => TargetMethod() != null;

        static void Postfix(string defName, ref string __result)
        {
            if (__result == null || !__result.StartsWith(BackCompatNames.OldPrefix + "_", StringComparison.Ordinal)) return;
            __result = BackCompatNames.NewPrefix + __result.Substring(BackCompatNames.OldPrefix.Length);
        }
    }
}
