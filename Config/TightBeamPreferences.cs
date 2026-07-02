using MelonLoader;
using UnityEngine;

namespace TightBeam.Config
{
    /// <summary>
    /// MelonPreferences for TightBeam (category "TightBeam"). The headline control is the FOCUS ("Pegel"): a single
    /// 0..1 axis the player scrolls with ALT + mouse wheel that trades a wide short-range flood (focus 1) for a narrow
    /// long-range throw (focus 0). Range and spot angle are BOTH derived from focus between the endpoints below, so a
    /// low focus can light a distant spot without becoming Flashlizzy's map-wide floodlight.
    /// </summary>
    internal static class TightBeamPreferences
    {
        private static MelonPreferences_Category _cat;

        private static MelonPreferences_Entry<bool> _enabled;
        private static MelonPreferences_Entry<string> _toggleKey;
        private static MelonPreferences_Entry<string> _focusModifierKey;
        private static MelonPreferences_Entry<string> _intensityUpKey;
        private static MelonPreferences_Entry<string> _intensityDownKey;
        private static MelonPreferences_Entry<float> _defaultIntensity;
        private static MelonPreferences_Entry<float> _minIntensity;
        private static MelonPreferences_Entry<float> _maxIntensity;
        private static MelonPreferences_Entry<float> _defaultFocus;
        private static MelonPreferences_Entry<float> _focusSensitivity;
        private static MelonPreferences_Entry<float> _focusEaseTau;
        private static MelonPreferences_Entry<float> _focusMaxStep;
        private static MelonPreferences_Entry<float> _focusVelocityGamma;
        private static MelonPreferences_Entry<float> _focusVelocityRefRate;
        private static MelonPreferences_Entry<float> _focusFlickRateMultiplier;
        private static MelonPreferences_Entry<float> _focusVelAttackTau;
        private static MelonPreferences_Entry<float> _focusVelDecayTau;
        private static MelonPreferences_Entry<bool> _invertFocusScroll;
        private static MelonPreferences_Entry<float> _rangeWide;
        private static MelonPreferences_Entry<float> _rangeNarrow;
        private static MelonPreferences_Entry<float> _angleWide;
        private static MelonPreferences_Entry<float> _angleNarrow;
        private static MelonPreferences_Entry<string> _colorHex;
        private static MelonPreferences_Entry<bool> _castShadows;
        private static MelonPreferences_Entry<bool> _startOn;

        public static bool Enabled => _enabled.Value;
        public static KeyCode ToggleKey => ParseKey(_toggleKey.Value, KeyCode.F);
        public static KeyCode FocusModifierKey => ParseKey(_focusModifierKey.Value, KeyCode.LeftAlt);
        public static KeyCode IntensityUpKey => ParseKey(_intensityUpKey.Value, KeyCode.RightBracket);
        public static KeyCode IntensityDownKey => ParseKey(_intensityDownKey.Value, KeyCode.LeftBracket);

        public static float DefaultIntensity => Mathf.Clamp(_defaultIntensity.Value, MinIntensity, MaxIntensity);
        public static float MinIntensity => Mathf.Max(0.05f, _minIntensity.Value);
        public static float MaxIntensity => Mathf.Max(MinIntensity + 0.1f, _maxIntensity.Value);
        public static float DefaultFocus => Mathf.Clamp01(_defaultFocus.Value);
        public static float FocusSensitivity => Mathf.Clamp(_focusSensitivity.Value, 0.005f, 1f);
        public static float FocusEaseTau => Mathf.Clamp(_focusEaseTau.Value, 0.02f, 0.15f);
        public static float FocusMaxStep => Mathf.Clamp(_focusMaxStep.Value, FocusSensitivity, 1f);
        public static float FocusVelocityGamma => Mathf.Clamp(_focusVelocityGamma.Value, 1f, 4f);
        public static float FocusVelocityRefRate => Mathf.Clamp(_focusVelocityRefRate.Value, 0.5f, 30f);
        public static float FocusFlickRateMultiplier => Mathf.Clamp(_focusFlickRateMultiplier.Value, 1.5f, 10f);
        public static float FocusVelAttackTau => Mathf.Clamp(_focusVelAttackTau.Value, 0.01f, 0.2f);
        public static float FocusVelDecayTau => Mathf.Clamp(_focusVelDecayTau.Value, 0.05f, 0.4f);
        public static bool InvertFocusScroll => _invertFocusScroll.Value;

        /// <summary>Range at full-WIDE focus (Pegel high): a short, near flood. Clamped sane.</summary>
        public static float RangeWide => Mathf.Clamp(_rangeWide.Value, 3f, 25f);
        /// <summary>Range at full-NARROW focus (Pegel low): a long, far throw. Clamped sane (never Flashlizzy's 60).</summary>
        public static float RangeNarrow => Mathf.Clamp(_rangeNarrow.Value, 15f, 45f);
        /// <summary>Cone angle at full-WIDE focus (broad).</summary>
        public static float AngleWide => Mathf.Clamp(_angleWide.Value, 40f, 90f);
        /// <summary>Cone angle at full-NARROW focus (tight throw).</summary>
        public static float AngleNarrow => Mathf.Clamp(_angleNarrow.Value, 8f, 35f);

        public static Color Color => ParseColor(_colorHex.Value, new Color(0.90f, 0.95f, 1.00f));
        public static bool CastShadows => _castShadows.Value;
        public static bool StartOn => _startOn.Value;

        public static void Initialize()
        {
            _cat = MelonPreferences.CreateCategory("TightBeam");

            _enabled = _cat.CreateEntry("Enabled", true, description: "Master switch for the whole mod.");
            _toggleKey = _cat.CreateEntry("ToggleKey", "F", description: "KeyCode name to toggle the flashlight on/off.");
            _focusModifierKey = _cat.CreateEntry("FocusModifierKey", "LeftAlt",
                description: "Hold this key and scroll the mouse wheel to adjust FOCUS (Pegel): wide near flood <-> narrow far throw.");
            _intensityUpKey = _cat.CreateEntry("IntensityUpKey", "RightBracket", description: "Increase brightness while lit.");
            _intensityDownKey = _cat.CreateEntry("IntensityDownKey", "LeftBracket", description: "Decrease brightness while lit.");
            _defaultIntensity = _cat.CreateEntry("DefaultIntensity", 7f, description: "Base beam brightness (clamped to Min/Max).");
            _minIntensity = _cat.CreateEntry("MinIntensity", 1f, description: "Hard floor for brightness.");
            _maxIntensity = _cat.CreateEntry("MaxIntensity", 20f, description: "Hard ceiling for brightness (prevents the Flashlizzy 'hold the key, go blinding' bug).");
            _defaultFocus = _cat.CreateEntry("DefaultFocus", 0.5f,
                description: "Starting focus/Pegel 0..1. 1 = wide short-range flood, 0 = narrow long-range throw, 0.5 = a balanced ~mid beam.");
            _focusSensitivity = _cat.CreateEntry("FocusSensitivity", 0.08f, description: "Focus change per notch at SLOW/precise scroll speed (the fine-adjustment floor step).");
            _focusEaseTau = _cat.CreateEntry("FocusEaseTau", 0.05f, description: "Seconds for the displayed beam to ease toward the target focus after a scroll (settle ~4x this). 0.02 = near-instant/steppy, 0.15 = very smooth.");
            _focusMaxStep = _cat.CreateEntry("FocusMaxStep", 0.35f, description: "Largest per-notch focus step from a fast (but not flick-threshold) scroll.");
            _focusVelocityGamma = _cat.CreateEntry("FocusVelocityGamma", 2.0f, description: "Curve exponent shaping how step size ramps up with scroll speed (higher = more ease-in, slow stays fine longer).");
            _focusVelocityRefRate = _cat.CreateEntry("FocusVelocityRefRate", 5f, description: "Scroll rate (units/sec) at/below which focus changes by plain FocusSensitivity per notch (slow single ticks read ~2-4).");
            _focusFlickRateMultiplier = _cat.CreateEntry("FocusFlickRateMultiplier", 2.0f, description: "Flick threshold = FocusVelocityRefRate * this (default 5*2=10); crossing it races focus to the nearest extreme. Fast flicks read ~27+.");
            _focusVelAttackTau = _cat.CreateEntry("FocusVelAttackTau", 0.05f, description: "How quickly the scroll-speed estimate reacts to a new, faster scroll.");
            _focusVelDecayTau = _cat.CreateEntry("FocusVelDecayTau", 0.15f, description: "How quickly the scroll-speed estimate cools down once scrolling pauses/stops.");
            _invertFocusScroll = _cat.CreateEntry("InvertFocusScroll", false, description: "Flip which scroll direction widens the beam.");
            _rangeWide = _cat.CreateEntry("RangeWide", 8f, description: "Beam range (m) at full WIDE focus - a short near flood.");
            _rangeNarrow = _cat.CreateEntry("RangeNarrow", 34f, description: "Beam range (m) at full NARROW focus - a long far throw.");
            _angleWide = _cat.CreateEntry("AngleWide", 66f, description: "Cone angle (deg) at full WIDE focus.");
            _angleNarrow = _cat.CreateEntry("AngleNarrow", 16f, description: "Cone angle (deg) at full NARROW focus.");
            _colorHex = _cat.CreateEntry("ColorHex", "#E6F2FF", description: "Beam colour (hex). Cool white by default so it reads against warm/sickly environments.");
            _castShadows = _cat.CreateEntry("CastShadows", true, description: "Cast soft shadows so the beam is blocked by walls (turn off on low-end machines).");
            _startOn = _cat.CreateEntry("StartOn", false, description: "Whether the flashlight begins switched on.");
        }

        private static KeyCode ParseKey(string s, KeyCode fallback)
            => System.Enum.TryParse<KeyCode>(s, ignoreCase: true, out var k) ? k : fallback;

        private static Color ParseColor(string input, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(input)) return fallback;
            if (ColorUtility.TryParseHtmlString(input.StartsWith("#") ? input : ("#" + input), out Color c)) return c;
            return fallback;
        }
    }
}
