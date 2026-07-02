using HarmonyLib;
using Il2CppScheduleOne;
using Il2CppScheduleOne.PlayerScripts;
using TightBeam.Config;
using UnityEngine;

namespace TightBeam.Patches
{
    /// <summary>
    /// While the focus modifier (default Left Alt) is held AND the mouse wheel is moving, TightBeam owns the scroll for
    /// beam focus - so suppress the vanilla hotbar's wheel slot-switching on exactly those frames. Number keys 1-0 and
    /// plain (no-modifier) wheel scrolling of the hotbar are untouched, and the vanilla guards (typing / paused /
    /// hotbar-disabled) still run on every other frame because the prefix only ever returns false, never forces the
    /// method to run. The flashlight reads Unity's own <c>Input</c> (not <c>GameInput</c>), so its focus scroll still
    /// works while this only starves the game's hotbar reader.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.UpdateHotbarSelection))]
    internal static class HotbarScrollGuard
    {
        private static bool Prefix()
        {
            if (!TightBeamPreferences.Enabled) return true;
            // Check the SAME scroll source the game itself reads (GameInput's new-input value), not the legacy
            // Input.GetAxis - the two disagree on some frames, which let the odd scroll leak through to the hotbar.
            if (Input.GetKey(TightBeamPreferences.FocusModifierKey) && GameInput.MouseScrollDelta != 0f)
                return false; // this ALT+scroll frame belongs to the flashlight focus - skip vanilla hotbar cycling
            return true;
        }
    }
}
