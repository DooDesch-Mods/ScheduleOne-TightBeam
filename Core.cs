using MelonLoader;
using TightBeam.Bridge;
using TightBeam.Config;
using TightBeam.Lighting;
using TightBeam.Net;
using UnityEngine;
#if SNITCH
using Snitch.Api;
#endif

[assembly: MelonInfo(typeof(TightBeam.Core), "TightBeam", "2.1.0", "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-TightBeam")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace TightBeam
{
    /// <summary>
    /// TightBeam entry point. A limited-range handheld flashlight with a cross-mod control API. It IS the player's
    /// flashlight: on/off follows the game's own flashlight state, and ALT + mouse wheel sets FOCUS/Pegel (wide near flood
    /// &lt;-&gt; narrow far throw). A camera-following Spot Light with a single Harmony patch for the hotbar scroll.
    /// </summary>
    public sealed class Core : MelonMod
    {
        public static MelonLogger.Instance Log { get; private set; }
        private bool _inMain;
        private bool _patched;
        private bool _wasEnabled = true;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            TightBeamPreferences.Initialize();
            FlashlightController.Instance.InitFromPrefs();
            BridgeHost.Install(); // expose the reflection API to consumer mods immediately (load-order-proof)
            BeamPostBuilder.Start(); // subscribe to the controller's effect events before anything can fire
            Log.Msg($"TightBeam initialized. Enabled={TightBeamPreferences.Enabled}. On/off follows the game flashlight; " +
                    $"hold {TightBeamPreferences.FocusModifierKey} + mouse wheel = focus/Pegel.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            _inMain = sceneName == "Main";
            // Apply the hotbar ALT+scroll guard lazily on the first gameplay scene - never at the menu (patching
            // gameplay methods while the main menu is up can hard-crash the game).
            if (_inMain && !_patched)
            {
                HarmonyInstance.PatchAll(typeof(Core).Assembly);
                _patched = true;
                Log.Msg("TightBeam: hotbar ALT+scroll guard applied.");
            }
            // A scene change ends the session: take our notice down (Tick keeps retrying until it is gone) and
            // clear a breaker tripped in the old one so it cannot follow the player into the next.
            BeamBoard.EndSession();
            RemoteBeams.NewSession();
        }

        public override void OnUpdate()
        {
            if (!TightBeamPreferences.Enabled || !_inMain) return;
            var c = FlashlightController.Instance;

            // Beam on/off = the game's own flashlight state (single source of truth); no separate TightBeam toggle.
            c.SyncOnFromGame();
            if (!c.IsOn) return;

            // ALT + mouse wheel -> focus / Pegel. Call the controller EVERY frame while the modifier is held (even on
            // zero-scroll frames, so the scroll-speed estimate can decay); reset it when released. Same GameInput
            // source as the hotbar-suppression guard, so the two stay frame-perfectly in sync.
            if (Input.GetKey(TightBeamPreferences.FocusModifierKey))
                c.UpdateFocusScroll(Il2CppScheduleOne.GameInput.MouseScrollDelta, Time.deltaTime);
            else
                c.ResetFocusScrollVelocity();
        }

        public override void OnLateUpdate()
        {
            var c = FlashlightController.Instance;
            if (!TightBeamPreferences.Enabled)
            {
                // Flipping the master switch off mid-session has to hand everything back: our own rig, and every
                // remote player's vanilla flashlight light that we darkened on their behalf.
                if (_wasEnabled) { _wasEnabled = false; c.DisableRig(); RemoteBeams.DisableAll(); }
                // Still pump the board while switched off: a disabled mod has to take its own notice down, or the
                // other players keep drawing a beam nobody is maintaining any more.
                BeamBoard.Tick(false);
                return;
            }
            _wasEnabled = true;
            // Outside gameplay the board still gets pumped, so a notice left over from the last session drains
            // instead of sitting in a lobby that outlived the scene.
            if (!_inMain) { c.DisableRig(); RemoteBeams.DisableAll(); BeamBoard.Tick(false); return; }
#if SNITCH
            using (Profiler.Sample("TightBeam.Frame"))
            {
                c.EnsureRig();
                c.Follow();
                c.Tick(Time.deltaTime);
            }
            using (Profiler.Sample("TightBeam.Remote"))
            {
                BeamBoard.Tick(true);
                RemoteBeams.Tick(Time.deltaTime);
            }
#else
            c.EnsureRig();
            c.Follow();
            c.Tick(Time.deltaTime);
            // Post our own shape (only when it actually changed), then draw everyone else's. Remote beams run
            // even while our own is off - we are drawing their flashlight, not ours.
            BeamBoard.Tick(true);
            RemoteBeams.Tick(Time.deltaTime);
#endif
        }
    }
}
