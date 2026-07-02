using System;
using System.Collections.Generic;
using TightBeam.Config;
using UnityEngine;

namespace TightBeam.Lighting
{
    /// <summary>
    /// The single flashlight: owns one Spot Light that follows the local camera, composites its live parameters from
    /// three layers each frame - BASE (player focus/intensity/colour) -> OVERRIDE STACK (per-field, for other mods) ->
    /// TRANSIENT EFFECTS (Blink/Flicker/Pulse) - and writes the result onto the Light. Plain C# singleton (no
    /// MonoBehaviour, no IL2CPP type registration); driven from Core's Unity callbacks.
    /// </summary>
    internal sealed class FlashlightController
    {
        public static FlashlightController Instance { get; } = new FlashlightController();

        private GameObject _go;
        private Light _light;
        private Camera _cam;

        // Persistent player state
        private bool _isOn;
        private float _focus;        // DISPLAYED focus that drives the light (eased toward _focusTarget each frame)
        private float _focusTarget;  // where the player wants it (moves instantly on scroll)
        private float _scrollVelEma;  // smoothed scroll speed estimate for the velocity-sensitive step
        private float _intensity;
        private Color _color = Color.white;
        private float? _manualRange, _manualAngle; // API SetRange/SetSpotAngle pins; cleared when the player scrolls focus

        // Override stack (one entry per BeginOverride token). Newest-per-field wins; null field = fall through.
        private sealed class Ov { public int Token; public string Owner; public float? I, R, A; public Color? C; public float Expiry; }
        private readonly List<Ov> _overrides = new List<Ov>();
        private int _nextToken = 1;

        // Transient effects
        private enum FxMode { None, Flicker, Pulse }
        private FxMode _fx = FxMode.None;
        private float _fxStrength, _fxFreq, _fxAmp, _fxPeriod, _fxEnd, _fxSeed;
        private int _blinkLeft;        // remaining half-cycles
        private float _blinkInterval, _blinkNext; private bool _blinkDark;

        public event Action<bool> Toggled;

        public bool IsOn => _isOn;
        public bool HasLight => _light != null;

        public void InitFromPrefs()
        {
            _focus = TightBeamPreferences.DefaultFocus;
            _focusTarget = _focus; _scrollVelEma = 0f;
            _intensity = TightBeamPreferences.DefaultIntensity;
            _color = TightBeamPreferences.Color;
            _isOn = TightBeamPreferences.StartOn;
        }

        // ----- rig -----------------------------------------------------------------------------------------------

        /// <summary>Create the light (once) and keep it following the camera. Safe to call every frame - it lazily
        /// (re)acquires Camera.main only when needed, and the light survives scene loads (DontDestroyOnLoad), so it is
        /// never torn down and rebuilt the way Flashlizzy did.</summary>
        public void EnsureRig()
        {
            if (_light == null)
            {
                _go = new GameObject("TightBeamLight");
                UnityEngine.Object.DontDestroyOnLoad(_go);
                _light = _go.AddComponent<Light>();
                _light.type = LightType.Spot;
                _light.renderMode = LightRenderMode.Auto;
                _light.shadows = TightBeamPreferences.CastShadows ? LightShadows.Soft : LightShadows.None;
                _light.shadowBias = 0.05f;
                _light.shadowNormalBias = 0.4f;
                _light.enabled = false;
            }
            if (_cam == null) _cam = Camera.main; // refetched only when null (e.g. after a scene load)
        }

        /// <summary>Turn the beam off while outside gameplay (menu/loading), without destroying the rig.</summary>
        public void DisableRig()
        {
            if (_light != null) _light.enabled = false;
            VanillaLightSync.Restore(); // never leave vanilla lights captured across a scene change
        }

        /// <summary>Snap the light to the camera (position + a small chest/hand-held offset + aim). Called each frame.</summary>
        public void Follow()
        {
            if (_light == null) return;
            if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
            var t = _cam.transform;
            _go.transform.position = t.position + t.forward * 0.25f + t.right * 0.15f - t.up * 0.2f;
            _go.transform.rotation = t.rotation;
        }

        // ----- per-frame composition ------------------------------------------------------------------------------

        public void Tick(float dt)
        {
            if (_light == null) return;

            // Ease the DISPLAYED focus toward the target (frame-rate-independent low-pass, cannot overshoot). The
            // epsilon-snap stops rewriting range/angle by an imperceptible residue forever once it has settled.
            float kEase = 1f - Mathf.Exp(-dt / TightBeamPreferences.FocusEaseTau);
            _focus = Mathf.Lerp(_focus, _focusTarget, kEase);
            if (Mathf.Abs(_focusTarget - _focus) < 0.0005f) _focus = _focusTarget;

            // BASE from focus: narrow focus -> long range + tight angle; wide focus -> short range + broad angle.
            // A manual API SetRange/SetSpotAngle pin takes precedence until the player scrolls focus again.
            float baseRange = _manualRange ?? Mathf.Lerp(TightBeamPreferences.RangeNarrow, TightBeamPreferences.RangeWide, _focus);
            float baseAngle = _manualAngle ?? Mathf.Lerp(TightBeamPreferences.AngleNarrow, TightBeamPreferences.AngleWide, _focus);
            float baseInt = _intensity;
            Color baseCol = _color;

            // OVERRIDE STACK: newest entry with a non-null value for each field wins; expired temp entries removed.
            PruneOverrides();
            float range = baseRange, angle = baseAngle, intensity = baseInt; Color col = baseCol;
            for (int i = _overrides.Count - 1; i >= 0; i--)
            {
                var o = _overrides[i];
                if (o.I.HasValue && intensity == baseInt) intensity = o.I.Value;
                if (o.R.HasValue && range == baseRange) range = o.R.Value;
                if (o.A.HasValue && angle == baseAngle) angle = o.A.Value;
                if (o.C.HasValue && col == baseCol) col = o.C.Value;
            }

            // TRANSIENT EFFECTS: a final multiplicative modulation on intensity (never toggles Light.enabled).
            intensity *= EffectMultiplier(dt);

            _light.intensity = Mathf.Max(0f, intensity);
            _light.range = Mathf.Clamp(range, 2f, 60f);
            _light.spotAngle = Mathf.Clamp(angle, 8f, 90f);
            _light.innerSpotAngle = _light.spotAngle * 0.6f; // soft falloff band under URP (harmless no-op otherwise)
            _light.color = col;
            _light.enabled = _isOn;

            // Vanilla-light sync: when the beam is nominally ON but an override/effect holds it dark (e.g. a horror
            // mod's blackout), the game's own flashlight lights (equipped item + phone lamp) go dark WITH it -
            // otherwise the room stays lit by the vanilla point light and the blackout reads broken.
            VanillaLightSync.Apply(_isOn && intensity <= 0.05f);
        }

        private float EffectMultiplier(float dt)
        {
            float mul = 1f;
            if (_fx == FxMode.Flicker)
            {
                if (Time.time >= _fxEnd) _fx = FxMode.None;
                else
                {
                    float n = Mathf.PerlinNoise(Time.time * _fxFreq + _fxSeed, _fxSeed * 0.37f); // 0..1, smooth
                    mul *= Mathf.Lerp(1f - _fxStrength, 1f, n);
                }
            }
            else if (_fx == FxMode.Pulse)
            {
                if (Time.time >= _fxEnd) _fx = FxMode.None;
                else mul *= 1f + _fxAmp * Mathf.Sin(Time.time * (2f * Mathf.PI / Mathf.Max(0.05f, _fxPeriod)) + _fxSeed);
            }

            if (_blinkLeft > 0)
            {
                if (Time.time >= _blinkNext) { _blinkDark = !_blinkDark; _blinkLeft--; _blinkNext = Time.time + _blinkInterval; }
                if (_blinkDark) mul *= 0.04f;
            }
            return Mathf.Max(0f, mul);
        }

        private void PruneOverrides()
        {
            for (int i = _overrides.Count - 1; i >= 0; i--)
                if (_overrides[i].Expiry > 0f && Time.time > _overrides[i].Expiry) _overrides.RemoveAt(i);
        }

        // ----- player controls ------------------------------------------------------------------------------------

        public void SetOn(bool on) { if (on == _isOn) return; _isOn = on; try { Toggled?.Invoke(on); } catch { } }
        public void Toggle() => SetOn(!_isOn);

        public void NudgeIntensity(float delta)
        {
            _intensity = Mathf.Clamp(_intensity + delta, TightBeamPreferences.MinIntensity, TightBeamPreferences.MaxIntensity);
        }

        private void MoveFocusTarget(float delta) { _focusTarget = Mathf.Clamp01(_focusTarget + delta); _manualRange = null; _manualAngle = null; }
        private void SnapFocusTarget(float value) { _focusTarget = Mathf.Clamp01(value); _manualRange = null; _manualAngle = null; }

        /// <summary>Nudge the focus TARGET (the eased display value chases it). Kept for API stability.</summary>
        public void NudgeFocus(float delta) => MoveFocusTarget(delta);
        public float Focus => _focus;

        /// <summary>Per-frame focus scroll input (called every frame while the modifier is held). Slow scrolling = fine
        /// FocusSensitivity steps; faster scrolling ramps the step up on a super-linear curve; a genuine fast FLICK
        /// (scroll-speed EMA past the flick threshold) snaps the target straight to the nearest extreme. The displayed
        /// focus still eases in smoothly via Tick, so a flick "races" the beam to the extreme rather than popping.</summary>
        public void UpdateFocusScroll(float scrollDelta, float dt)
        {
            if (Mathf.Abs(scrollDelta) > 0.0001f)
            {
                float rate = Mathf.Abs(scrollDelta) / Mathf.Max(dt, 1f / 240f);
                float kAttack = 1f - Mathf.Exp(-dt / TightBeamPreferences.FocusVelAttackTau);
                _scrollVelEma = Mathf.Lerp(_scrollVelEma, rate, kAttack);

                float refRate = TightBeamPreferences.FocusVelocityRefRate;
                float flickRate = refRate * TightBeamPreferences.FocusFlickRateMultiplier;
                float dir = TightBeamPreferences.InvertFocusScroll ? -1f : 1f;
                float sign = Mathf.Sign(scrollDelta) * dir;

                if (_scrollVelEma >= flickRate)
                    SnapFocusTarget(sign > 0f ? 1f : 0f);
                else
                {
                    float t = Mathf.Clamp01((_scrollVelEma - refRate) / Mathf.Max(0.0001f, flickRate - refRate));
                    float step = Mathf.Lerp(TightBeamPreferences.FocusSensitivity, TightBeamPreferences.FocusMaxStep,
                                            Mathf.Pow(t, TightBeamPreferences.FocusVelocityGamma));
                    MoveFocusTarget(sign * step);
                }
            }
            else
            {
                float kDecay = 1f - Mathf.Exp(-dt / TightBeamPreferences.FocusVelDecayTau);
                _scrollVelEma = Mathf.Lerp(_scrollVelEma, 0f, kDecay);
            }
        }

        /// <summary>Called when the focus modifier is released - the scroll gesture is over.</summary>
        public void ResetFocusScrollVelocity()
        {
            _scrollVelEma = 0f;
        }

        // ----- base setters (API) ---------------------------------------------------------------------------------

        public void SetIntensity(float v) => _intensity = Mathf.Clamp(v, TightBeamPreferences.MinIntensity, TightBeamPreferences.MaxIntensity);
        public void SetColor(Color c) => _color = c;
        public void SetBaseRange(float v) => _manualRange = Mathf.Clamp(v, 2f, 60f);
        public void SetBaseSpotAngle(float v) => _manualAngle = Mathf.Clamp(v, 8f, 90f);
        public float CurrentIntensity => _light != null ? _light.intensity : 0f;
        public float CurrentRange => _light != null ? _light.range : 0f;
        public float CurrentSpotAngle => _light != null ? _light.spotAngle : 0f;
        public Color CurrentColor => _light != null ? _light.color : Color.black;

        // ----- override stack (API) -------------------------------------------------------------------------------

        public int BeginOverride(string owner) { var o = new Ov { Token = _nextToken++, Owner = owner }; _overrides.Add(o); return o.Token; }
        public void EndOverride(int token) { for (int i = 0; i < _overrides.Count; i++) if (_overrides[i].Token == token) { _overrides.RemoveAt(i); return; } }
        private Ov Find(int token) { for (int i = 0; i < _overrides.Count; i++) if (_overrides[i].Token == token) return _overrides[i]; return null; }

        public void OverrideIntensity(int token, float v) { var o = Find(token); if (o != null) o.I = v; }
        public void OverrideRange(int token, float v) { var o = Find(token); if (o != null) o.R = v; }
        public void OverrideSpotAngle(int token, float v) { var o = Find(token); if (o != null) o.A = v; }
        public void OverrideColor(int token, Color c) { var o = Find(token); if (o != null) o.C = c; }
        public void ClearOverrideIntensity(int token) { var o = Find(token); if (o != null) o.I = null; }
        public void ClearOverrideRange(int token) { var o = Find(token); if (o != null) o.R = null; }
        public void ClearOverrideSpotAngle(int token) { var o = Find(token); if (o != null) o.A = null; }
        public void ClearOverrideColor(int token) { var o = Find(token); if (o != null) o.C = null; }

        /// <summary>Fire-and-forget momentary intensity override that auto-restores after `seconds`.</summary>
        public void TemporaryIntensity(float v, float seconds)
        {
            _overrides.Add(new Ov { Token = _nextToken++, Owner = "$temp", I = v, Expiry = Time.time + Mathf.Max(0.05f, seconds) });
        }
        public void TemporaryColor(float r, float g, float b, float a, float seconds)
        {
            _overrides.Add(new Ov { Token = _nextToken++, Owner = "$temp", C = new Color(r, g, b, a), Expiry = Time.time + Mathf.Max(0.05f, seconds) });
        }

        // ----- transient effects (API) ----------------------------------------------------------------------------

        public void Flicker(float strength01, float durationSeconds, float freqHz)
        {
            _fx = FxMode.Flicker; _fxStrength = Mathf.Clamp01(strength01); _fxFreq = Mathf.Max(0.1f, freqHz);
            _fxEnd = Time.time + Mathf.Max(0.05f, durationSeconds); _fxSeed = UnityEngine.Random.value * 100f;
        }
        public void Pulse(float amp01, float periodSeconds, float durationSeconds)
        {
            _fx = FxMode.Pulse; _fxAmp = Mathf.Clamp01(amp01); _fxPeriod = Mathf.Max(0.1f, periodSeconds);
            _fxEnd = float.IsInfinity(durationSeconds) ? float.MaxValue : Time.time + Mathf.Max(0.05f, durationSeconds);
            _fxSeed = UnityEngine.Random.value * 6.28f;
        }
        public void StopFlicker() { if (_fx == FxMode.Flicker) _fx = FxMode.None; }
        public void StopPulse() { if (_fx == FxMode.Pulse) _fx = FxMode.None; }

        public void Blink(int times, float intervalSeconds)
        {
            if (!_isOn || times <= 0) return;
            _blinkLeft = times * 2; _blinkInterval = Mathf.Max(0.02f, intervalSeconds); _blinkNext = Time.time; _blinkDark = false;
        }
    }
}
