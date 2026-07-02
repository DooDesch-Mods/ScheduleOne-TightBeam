using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Phone;
using UnityEngine;

namespace TightBeam.Lighting
{
    /// <summary>
    /// Keeps the game's OWN flashlight lights out of the way while TightBeam is lit. TightBeam IS the player's
    /// flashlight, so whenever its beam is on we disable the vanilla flashlight lights and show TightBeam's cone
    /// instead (and they stay dark through a mod blackout too). Everything restores the moment the beam goes off.
    ///
    /// Two vanilla sources, both purely local (no RPC / player state touched):
    /// - the equipped Flashlight ITEM: first-person viewmodel under PlayerInventory.equippable; its lights sit on
    ///   OptimizedLight carriers whose Enabled gate survives the camera-movement rewrite events (writing
    ///   Light.enabled directly would be undone by the next UpdateLightState).
    /// - the PHONE lamp: Phone.PhoneFlashlight's ACTIVE state is rewritten every frame by Phone.Update, but a
    ///   disabled Light COMPONENT under it survives that SetActive, so we disable components, never the GO.
    ///
    /// Captured references die with unequip/scene loads - every pass revalidates, re-asserts the disable each
    /// frame, and rescans ~10x/sec to pick up a just-equipped item or a phone lamp toggled on mid-beam.
    /// </summary>
    internal static class VanillaLightSync
    {
        private static readonly List<OptimizedLight> _opt = new List<OptimizedLight>();
        private static readonly List<Light> _bare = new List<Light>();
        private static bool _dark;
        private static float _nextRescan;

        /// <summary>Called once per frame from the controller with the effective darkness state.</summary>
        public static void Apply(bool dark)
        {
            if (dark)
            {
                if (!_dark) { _dark = true; _nextRescan = 0f; }
                // Re-assert the disable every frame. The game can flip a captured light back on between rescans
                // (an equip, the phone re-activating its lamp, an OptimizedLight state rewrite); without this it
                // flashes back on until the next scan. Allocation-free, so it is cheap to run per frame.
                ReassertDisabled();
                // Scan for NEWLY appeared lights (a mid-blackout equip / phone-lamp toggle) at a tight cadence,
                // so a fresh vanilla light goes dark within ~0.1s instead of lingering up to a second.
                if (Time.time >= _nextRescan)
                {
                    _nextRescan = Time.time + 0.1f;
                    Capture();
                }
            }
            else if (_dark)
            {
                Restore();
            }
        }

        // Re-disable any already-captured light the game turned back on. Guarded so it only writes on a real
        // flip (no redundant UpdateLightState calls) and stays null-safe across unequip / scene teardown.
        private static void ReassertDisabled()
        {
            for (int i = 0; i < _opt.Count; i++)
            {
                var ol = _opt[i];
                if (ol != null && ol.Enabled) { ol.Enabled = false; ol.UpdateLightState(); }
            }
            for (int i = 0; i < _bare.Count; i++)
            {
                var l = _bare[i];
                if (l != null && l.enabled) l.enabled = false;
            }
        }

        private static void Capture()
        {
            try
            {
                if (PlayerSingleton<PlayerInventory>.InstanceExists)
                {
                    var eq = PlayerSingleton<PlayerInventory>.Instance.equippable;
                    if (eq != null && eq.TryCast<Il2CppScheduleOne.Tools.Flashlight>() != null)
                        DisableUnder(eq.gameObject);
                }
                if (PlayerSingleton<Phone>.InstanceExists)
                {
                    var pf = PlayerSingleton<Phone>.Instance.PhoneFlashlight;
                    if (pf != null) DisableUnder(pf);
                }
            }
            catch (System.Exception e) { Core.Log?.Warning("VanillaLightSync capture failed: " + e.Message); }
        }

        private static void DisableUnder(GameObject root)
        {
            foreach (var ol in root.GetComponentsInChildren<OptimizedLight>(true))
            {
                if (ol == null || !ol.Enabled) continue;
                ol.Enabled = false;
                ol.UpdateLightState(); // covers both field- and property-backed Enabled across game versions
                _opt.Add(ol);
            }
            foreach (var l in root.GetComponentsInChildren<Light>(true))
            {
                if (l == null || !l.enabled) continue;
                if (l.GetComponent<OptimizedLight>() != null) continue; // that one is owned by the Enabled gate
                l.enabled = false;
                _bare.Add(l);
            }
        }

        /// <summary>Re-enable everything still alive and forget. Also called on scene exit as a safety net.</summary>
        public static void Restore()
        {
            _dark = false;
            foreach (var ol in _opt)
            {
                try { if (ol != null) { ol.Enabled = true; ol.UpdateLightState(); } } catch { }
            }
            foreach (var l in _bare)
            {
                try { if (l != null) l.enabled = true; } catch { }
            }
            _opt.Clear();
            _bare.Clear();
        }
    }
}
