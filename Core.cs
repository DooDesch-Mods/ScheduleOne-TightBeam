using MelonLoader;
using TightBeam.Bridge;
using TightBeam.Config;
using TightBeam.Lighting;
using UnityEngine;

[assembly: MelonInfo(typeof(TightBeam.Core), "TightBeam", "1.0.0", "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-TightBeam")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace TightBeam
{
    /// <summary>
    /// TightBeam entry point. A believable limited-range handheld flashlight with a cross-mod control API. Player
    /// controls: toggle key (default F), '['/']' brightness, and ALT + mouse wheel for FOCUS/Pegel (wide near flood
    /// &lt;-&gt; narrow far throw). No Harmony, no game types - just a camera-following Spot Light.
    /// </summary>
    public sealed class Core : MelonMod
    {
        public static MelonLogger.Instance Log { get; private set; }
        private bool _inMain;
        private bool _patched;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            TightBeamPreferences.Initialize();
            FlashlightController.Instance.InitFromPrefs();
            BridgeHost.Install(); // expose the reflection API to consumer mods immediately (load-order-proof)
            Log.Msg($"TightBeam initialized. Enabled={TightBeamPreferences.Enabled}. Toggle={TightBeamPreferences.ToggleKey}; " +
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
        }

        public override void OnUpdate()
        {
            if (!TightBeamPreferences.Enabled || !_inMain) return;
            var c = FlashlightController.Instance;

            if (Input.GetKeyDown(TightBeamPreferences.ToggleKey)) c.Toggle();
            if (!c.IsOn) return;

            if (Input.GetKeyDown(TightBeamPreferences.IntensityUpKey)) c.NudgeIntensity(1f);
            else if (Input.GetKeyDown(TightBeamPreferences.IntensityDownKey)) c.NudgeIntensity(-1f);

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
            if (!TightBeamPreferences.Enabled) return;
            var c = FlashlightController.Instance;
            if (!_inMain) { c.DisableRig(); return; }
            c.EnsureRig();
            c.Follow();
            c.Tick(Time.deltaTime);
        }
    }
}
