using HarmonyLib;
using UnityEngine;

namespace WorldMap
{
    [HarmonyPatch(typeof(mainscript), "Update")]
    internal static class MainCursorPatch
    {
        private static void Postfix(mainscript __instance)
        {
            if (!MapSession.IsOpen) return;
            __instance.crsrLocked = false;
            __instance.SetCursorVisible(true);
        }
    }

    [HarmonyPatch(typeof(fpscontroller), "Update")]
    internal static class PlayerCursorPatch
    {
        private static void Prefix()
        {
            if (MapSession.IsOpen && mainscript.s != null) mainscript.s.crsrLocked = false;
        }
    }

    [HarmonyPatch(typeof(mainscript), "SetCursorVisible")]
    internal static class CursorVisibilityPatch
    {
        private static void Prefix(ref bool _state)
        {
            if (MapSession.IsOpen) _state = true;
        }
    }

    [HarmonyPatch(typeof(mainscript), "PressedEscape")]
    internal static class EscapeMapPatch
    {
        private static bool Prefix()
        {
            if (!MapSession.IsOpen) return true;
            MapSession.Close();
            return false;
        }
    }
}
