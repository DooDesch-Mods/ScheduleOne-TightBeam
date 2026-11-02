#if DEBUG
using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Phone;
using TightBeam.Lighting;
using UnityEngine;

namespace TightBeam.DevTools
{
    /// <summary>
    /// tbflashprobe: toggles the game flashlight a number of times and samples, after TightBeam's LateUpdate (the
    /// state the frame renders with), whether any of the game's own flashlight lights is lit. While TightBeam is
    /// enabled none of them may ever be lit; a frame where one is, is the vanilla lamp flashing up before the cone.
    /// The toggle is issued from LateUpdate, so the next frame's Phone.Update activates the lamp in its own order
    /// relative to the mod's OnUpdate, the same way a key press does.
    /// </summary>
    internal static class FlashProbe
    {
        private const int FramesPerPhase = 20;

        private static int _cyclesLeft;
        private static int _frame;
        private static int _cycles;
        private static int _samples;
        private static int _vanillaFrames;
        private static int _vanillaWithoutBeam;
        private static readonly List<string> _hits = new List<string>();

        internal static bool Running => _cyclesLeft > 0;

        internal static void Start(int cycles)
        {
            _cyclesLeft = Mathf.Clamp(cycles, 1, 50);
            _cycles = _cyclesLeft;
            _frame = 0; _samples = 0; _vanillaFrames = 0; _vanillaWithoutBeam = 0;
            _hits.Clear();
            FlashlightController.Instance.SetOn(false);
            Core.Log.Msg($"[Debug] tbflashprobe: started, {_cycles} on/off cycles of {FramesPerPhase} frames each");
        }

        /// <summary>Called at the end of Core.OnLateUpdate.</summary>
        internal static void Tick()
        {
            if (_cyclesLeft <= 0) return;
            var c = FlashlightController.Instance;

            // Sample first: this is the state the current frame renders with.
            if (_frame > 0)
            {
                _samples++;
                string lit = LitVanillaLight();
                if (lit != null)
                {
                    _vanillaFrames++;
                    if (!c.BeamLit) _vanillaWithoutBeam++;
                    if (_hits.Count < 8) _hits.Add($"f{Time.frameCount}:{lit}:beam={(c.BeamLit ? "on" : "off")}");
                }
            }

            int phaseFrame = _frame % (FramesPerPhase * 2);
            if (phaseFrame == 0) c.SetOn(true);
            else if (phaseFrame == FramesPerPhase) c.SetOn(false);
            else if (phaseFrame == FramesPerPhase * 2 - 1) _cyclesLeft--;
            _frame++;

            if (_cyclesLeft > 0) return;
            c.SetOn(false);
            bool pass = _vanillaFrames == 0;
            Core.Log.Msg($"[Debug] tbflashprobe: {(pass ? "PASS" : "FAIL")} cycles={_cycles} samples={_samples} " +
                         $"vanillaLitFrames={_vanillaFrames} vanillaLitWithoutBeam={_vanillaWithoutBeam}" +
                         (_hits.Count > 0 ? " hits=" + string.Join(",", _hits) : ""));
        }

        // Name of the first lit vanilla flashlight light (phone lamp or equipped flashlight item), or null.
        private static string LitVanillaLight()
        {
            if (PlayerSingleton<Phone>.InstanceExists)
            {
                var pf = PlayerSingleton<Phone>.Instance.PhoneFlashlight;
                string hit = pf != null ? LitUnder(pf, "phone") : null;
                if (hit != null) return hit;
            }
            if (PlayerSingleton<PlayerInventory>.InstanceExists)
            {
                var eq = PlayerSingleton<PlayerInventory>.Instance.Equippable;
                if (eq != null && eq.TryCast<Il2CppScheduleOne.Tools.Flashlight>() != null) return LitUnder(eq.gameObject, "item");
            }
            return null;
        }

        private static string LitUnder(GameObject root, string tag)
        {
            if (!root.activeInHierarchy) return null;
            foreach (var l in root.GetComponentsInChildren<Light>(false))
                if (l != null && l.enabled && l.intensity > 0f) return tag + "/" + l.gameObject.name;
            return null;
        }
    }
}
#endif
