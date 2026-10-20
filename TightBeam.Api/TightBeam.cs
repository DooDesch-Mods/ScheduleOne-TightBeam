using System;
using System.Collections.Generic;
using System.Reflection;

namespace TightBeam.Api
{
    /// <summary>
    /// TightBeam's cross-mod control API for the player's flashlight. Reference TightBeam.Api.dll OR drop this single
    /// file into your mod. Drive the beam from any mod: on/off, brightness, range, colour, plus Blink/Flicker/Pulse and
    /// scoped per-field overrides (e.g. dim it in a dark room, flicker it near power).
    ///
    /// Every call is a zero-overhead no-op when TightBeam is not installed and lights up automatically when it is, so
    /// you can ship this unconditionally with no hard dependency. All calls MUST be on the Unity main thread.
    ///
    /// <code>
    ///   using TightBeam.Api;
    ///   Beam.Blink(3);                                   // event blink
    ///   var ov = Beam.BeginOverride("MyMod");            // scoped, per-field override
    ///   ov.SetIntensity(1f).SetSpotAngle(28f);           //   dim + narrow while in a zone
    ///   ov.Dispose();                                    // release -> beam returns to the player's own settings
    /// </code>
    /// </summary>
    /// <remarks>
    /// Intensity is the Unity light intensity. The player's base rests at 7 and is held between MinIntensity (1) and
    /// MaxIntensity (20) from the TightBeam preferences. Range is in metres and the drawn beam stays between 2 and 60.
    /// The spot angle is the full cone angle in degrees and stays between 8 and 90. Without a pinned value both follow the
    /// player's focus: 34 m at 16 degrees fully narrow, 8 m at 66 degrees fully wide, with the default preferences.
    /// Colour channels run from 0 to 1; the default beam colour is #E6F2FF. Times are in seconds of game time.
    /// Effects (blink, flicker, pulse) scale the intensity on top of the base and the overrides.
    /// </remarks>
    /// <example>
    /// A red, flickering beam for three seconds after an explosion:
    /// <code>
    /// Beam.SetTemporaryColor(1f, 0.2f, 0.2f, 3f);
    /// Beam.Flicker(0.8f, 3f);
    /// </code>
    /// </example>
    public static class Beam
    {
        private static bool _bound;
        private static int _probeAttempts;
        private static readonly List<Action> _pending = new List<Action>();

        private static Func<bool> _isOn;
        private static Func<float> _getIntensity, _getRange, _getSpotAngle;
        private static Func<float[]> _getColor;
        private static Action<bool> _setOn;
        private static Action _toggle, _stopFlicker, _stopPulse;
        private static Action<float> _setIntensity, _setRange, _setSpotAngle;
        private static Action<float, float, float, float> _setColor;
        private static Action<int, float> _blink;
        private static Action<float, float, float> _flicker, _pulse;
        private static Action<float, float> _tempIntensity;
        private static Action<float, float, float, float, float> _tempColor;
        private static Func<string, int> _beginOverride;
        private static Action<int> _endOverride, _clrI, _clrR, _clrA, _clrC;
        private static Action<int, float> _ovI, _ovR, _ovA;
        private static Action<int, float, float, float, float> _ovC;
        private static Action<Action<bool>> _registerToggled;

        private static int _abi;
        private static Func<bool> _isMultiplayer;
        private static Func<ulong> _getLocalSteamId;
        private static Func<ulong[]> _getRemoteIds;
        private static Func<ulong, bool> _remoteHasTightBeam, _isRemoteRendered;
        private static Func<ulong, float[]> _getRemoteBeam, _getRemoteBeamPose;
        private static Action<Action<ulong, bool>> _registerRemoteToggled;
        private static readonly ulong[] _noIds = new ulong[0];

        /// <summary>Fires whenever the flashlight actually transitions on/off (player key or any mod's SetOn/Toggle).</summary>
        public static event Action<bool> OnToggled;

        /// <summary>Fires when ANOTHER player's beam goes on or off: (steamId, isOn).</summary>
        public static event Action<ulong, bool> OnRemoteToggled;

        /// <summary>True only when the TightBeam host is installed AND bound. You rarely need this - the API is a safe no-op when absent.</summary>
        public static bool Available { get { EnsureBound(); return _bound; } }

        /// <summary>ABI version of the host that is installed: 0 when absent, 1 for local-only builds, 2 and up once
        /// the other players' beams can be read.</summary>
        public static int AbiVersion { get { EnsureBound(); return _abi; } }

        /// <summary>Whether the local flashlight is on.</summary>
        /// <value>Follows the game's own phone flashlight, so it is false while the player sits in a vehicle. False when TightBeam is absent.</value>
        public static bool IsOn { get { EnsureBound(); return _isOn != null && _isOn(); } }
        /// <summary>The intensity the beam shows this frame.</summary>
        /// <value>The Unity light intensity, with overrides and effects applied, e.g. 7 at rest. 0 when TightBeam is absent.</value>
        public static float Intensity { get { EnsureBound(); return _getIntensity?.Invoke() ?? 0f; } }
        /// <summary>The range the beam shows this frame.</summary>
        /// <value>In metres, from 2 to 60. 0 when TightBeam is absent.</value>
        public static float Range { get { EnsureBound(); return _getRange?.Invoke() ?? 0f; } }
        /// <summary>The cone angle the beam shows this frame.</summary>
        /// <value>The full cone angle in degrees, from 8 to 90. 0 when TightBeam is absent.</value>
        public static float SpotAngle { get { EnsureBound(); return _getSpotAngle?.Invoke() ?? 0f; } }
        /// <summary>The colour the beam shows this frame.</summary>
        /// <param name="r">Red, 0 to 1.</param>
        /// <param name="g">Green, 0 to 1.</param>
        /// <param name="b">Blue, 0 to 1.</param>
        /// <param name="a">Alpha, 0 to 1. All four are 0 when TightBeam is absent.</param>
        public static void GetColor(out float r, out float g, out float b, out float a)
        {
            EnsureBound();
            var c = _getColor?.Invoke();
            if (c != null && c.Length >= 4) { r = c[0]; g = c[1]; b = c[2]; a = c[3]; } else { r = g = b = a = 0f; }
        }

        /// <summary>Turns the flashlight on, like <c>SetOn(true)</c>.</summary>
        public static void TurnOn() => SetOn(true);
        /// <summary>Turns the flashlight off, like <c>SetOn(false)</c>.</summary>
        public static void TurnOff() => SetOn(false);
        /// <summary>Switches the game's flashlight to the other state, with its sound.</summary>
        public static void Toggle() => Do(() => _toggle?.Invoke());
        /// <summary>Switches the game's own flashlight, with its sound. It does nothing when the state already matches.</summary>
        /// <param name="on">true turns it on, false off.</param>
        public static void SetOn(bool on) => Do(() => _setOn?.Invoke(on));

        /// <summary>Sets the base intensity the beam rests at.</summary>
        /// <param name="value">The Unity light intensity, e.g. 7. It is clamped to MinIntensity (1) and MaxIntensity (20) from the preferences.</param>
        public static void SetIntensity(float value) => Do(() => _setIntensity?.Invoke(value));
        /// <summary>Pins the base range. The pin holds until the player changes the focus with the scroll wheel.</summary>
        /// <param name="meters">The range in metres, e.g. 20. It is clamped to 2 to 60.</param>
        public static void SetRange(float meters) => Do(() => _setRange?.Invoke(meters));
        /// <summary>Pins the base cone angle. The pin holds until the player changes the focus with the scroll wheel.</summary>
        /// <param name="degrees">The full cone angle in degrees, e.g. 30. It is clamped to 8 to 90.</param>
        public static void SetSpotAngle(float degrees) => Do(() => _setSpotAngle?.Invoke(degrees));
        /// <summary>Sets the base beam colour.</summary>
        /// <param name="r">Red, 0 to 1.</param>
        /// <param name="g">Green, 0 to 1.</param>
        /// <param name="b">Blue, 0 to 1.</param>
        /// <param name="a">Alpha, 0 to 1. The default is 1.</param>
        public static void SetColor(float r, float g, float b, float a = 1f) => Do(() => _setColor?.Invoke(r, g, b, a));
        /// <summary>Sets the base beam colour from a hex string.</summary>
        /// <param name="hex">"#RRGGBB" or "#RRGGBBAA", the "#" optional, e.g. "#E6F2FF". Without AA the alpha is 1. Null, fewer than 6 digits or a value that is not hex changes nothing.</param>
        public static void SetColorHex(string hex) { if (TryHex(hex, out var r, out var g, out var b, out var a)) SetColor(r, g, b, a); }

        /// <summary>Blinks the beam dark and back. It does nothing while the beam is off, and a started blink cannot be stopped.</summary>
        /// <param name="times">How many blinks, from 1 to 20. Higher counts blink 20 times; 0 or less does nothing.</param>
        /// <param name="intervalSeconds">Seconds for each dark and each lit half, at least 0.02. The default is 0.12.</param>
        public static void Blink(int times, float intervalSeconds = 0.12f) => Do(() => _blink?.Invoke(times, intervalSeconds));
        /// <summary>Flickers the beam with smooth noise. A new flicker or a pulse replaces the running one.</summary>
        /// <param name="strength01">0 to 1. The intensity drops by up to this share: 0.8 dims it down to 20 percent at the lowest.</param>
        /// <param name="durationSeconds">How long it runs, at least 0.05 seconds.</param>
        /// <param name="frequencyHz">How fast the noise changes, at least 0.1. The default is 14.</param>
        public static void Flicker(float strength01, float durationSeconds, float frequencyHz = 14f) => Do(() => _flicker?.Invoke(strength01, durationSeconds, frequencyHz));
        public static void StopFlicker() => Do(() => _stopFlicker?.Invoke());
        /// <summary>Swells the beam brighter and darker along a sine wave. A new pulse or a flicker replaces the running one.</summary>
        /// <param name="amplitude01">0 to 1. The intensity swings by this share both ways: 0.3 runs from 70 to 130 percent.</param>
        /// <param name="periodSeconds">Seconds for one full swing, at least 0.1.</param>
        /// <param name="durationSeconds">How long it runs, at least 0.05 seconds. <c>float.PositiveInfinity</c> runs until <see cref="StopPulse"/>.</param>
        public static void Pulse(float amplitude01, float periodSeconds, float durationSeconds) => Do(() => _pulse?.Invoke(amplitude01, periodSeconds, durationSeconds));
        public static void StopPulse() => Do(() => _stopPulse?.Invoke());

        /// <summary>Overrides the intensity for a while, then the beam returns to its own value.</summary>
        /// <param name="value">The Unity light intensity. It is not held to MinIntensity and MaxIntensity; below 0 shows as 0.</param>
        /// <param name="seconds">How long it holds, at least 0.05.</param>
        /// <param name="fadeSeconds">Not passed to TightBeam; the value switches without a fade.</param>
        public static void SetTemporaryIntensity(float value, float seconds, float fadeSeconds = 0.25f) => Do(() => _tempIntensity?.Invoke(value, seconds));
        /// <summary>Overrides the colour for a while, then the beam returns to its own colour.</summary>
        /// <param name="r">Red, 0 to 1.</param>
        /// <param name="g">Green, 0 to 1.</param>
        /// <param name="b">Blue, 0 to 1.</param>
        /// <param name="seconds">How long it holds, at least 0.05.</param>
        /// <param name="a">Alpha, 0 to 1. The default is 1.</param>
        /// <param name="fadeSeconds">Not passed to TightBeam; the colour switches without a fade.</param>
        public static void SetTemporaryColor(float r, float g, float b, float seconds, float a = 1f, float fadeSeconds = 0.25f) => Do(() => _tempColor?.Invoke(r, g, b, a, seconds));

        // ----- other players' beams (host ABI 2+) -----------------------------------------------------------------
        // Read-only by design. Every player is the sole author of their own beam, so you drive the LOCAL beam as
        // usual and the state replicates on its own - a blackout you apply here is what the other players see.
        // Players are identified by SteamID64. Everything degrades to "nothing there" against an older host.

        /// <summary>True while in a session with at least one other player.</summary>
        public static bool IsMultiplayer { get { EnsureBound(); return _isMultiplayer != null && _isMultiplayer(); } }

        /// <summary>The local player's SteamID64, or 0 when it is not known yet.</summary>
        public static ulong LocalSteamId { get { EnsureBound(); return _getLocalSteamId?.Invoke() ?? 0UL; } }

        /// <summary>Every other player whose beam is being tracked. Never null; empty when alone or absent.</summary>
        public static ulong[] RemoteIds { get { EnsureBound(); return _getRemoteIds?.Invoke() ?? _noIds; } }

        /// <summary>Whether this player is running TightBeam and sharing their beam. False means TryGetRemote still
        /// answers, but with local defaults rather than their real settings.</summary>
        /// <param name="steamId">The player's SteamID64, e.g. from <see cref="RemoteIds"/>.</param>
        public static bool RemoteHasTightBeam(ulong steamId)
        {
            EnsureBound();
            return _remoteHasTightBeam != null && _remoteHasTightBeam(steamId);
        }

        /// <summary>Whether that player's beam is actually being drawn, as opposed to lit but culled by distance or
        /// the beam cap.</summary>
        /// <param name="steamId">The player's SteamID64, e.g. from <see cref="RemoteIds"/>.</param>
        public static bool IsRemoteRendered(ulong steamId)
        {
            EnsureBound();
            return _isRemoteRendered != null && _isRemoteRendered(steamId);
        }

        /// <summary>One player's beam as it is being drawn. False when that player is unknown.</summary>
        /// <param name="steamId">The player's SteamID64, e.g. from <see cref="RemoteIds"/>.</param>
        /// <param name="state">The beam; default when the method returns false.</param>
        public static bool TryGetRemote(ulong steamId, out RemoteBeamState state)
        {
            EnsureBound();
            state = default;
            var v = _getRemoteBeam?.Invoke(steamId);
            if (v == null || v.Length < 8) return false;
            state = new RemoteBeamState
            {
                IsOn = v[0] != 0f,
                Intensity = v[1], Range = v[2], SpotAngle = v[3],
                R = v[4], G = v[5], B = v[6], A = v[7],
            };
            return true;
        }

        /// <summary>Where one player's beam starts and which way it points, for questions like "is that beam on me".
        /// False when the beam is not currently drawn.</summary>
        /// <param name="steamId">The player's SteamID64, e.g. from <see cref="RemoteIds"/>.</param>
        /// <param name="px">World x of the beam's start, in metres.</param>
        /// <param name="py">World y of the beam's start, in metres.</param>
        /// <param name="pz">World z of the beam's start, in metres.</param>
        /// <param name="fx">World x of the unit direction the beam points.</param>
        /// <param name="fy">World y of the unit direction the beam points.</param>
        /// <param name="fz">World z of the unit direction the beam points.</param>
        /// <example>
        /// Whether another player's beam points at a spot within 10 degrees:
        /// <code>
        /// if (Beam.TryGetRemotePose(id, out var px, out var py, out var pz, out var fx, out var fy, out var fz))
        /// {
        ///     var to = (target - new Vector3(px, py, pz)).normalized;
        ///     bool onMe = Vector3.Angle(new Vector3(fx, fy, fz), to) &lt; 10f;
        /// }
        /// </code>
        /// </example>
        public static bool TryGetRemotePose(ulong steamId,
            out float px, out float py, out float pz, out float fx, out float fy, out float fz)
        {
            EnsureBound();
            px = py = pz = fx = fy = fz = 0f;
            var v = _getRemoteBeamPose?.Invoke(steamId);
            if (v == null || v.Length < 6) return false;
            px = v[0]; py = v[1]; pz = v[2]; fx = v[3]; fy = v[4]; fz = v[5];
            return true;
        }

        /// <summary>Begin a scoped, per-field override for one owner. Dispose() (or scope exit) releases it.</summary>
        /// <param name="ownerId">A name for your override, e.g. "MyMod". Null becomes "?".</param>
        /// <returns>The handle to set and clear fields on. Each call opens a new override; the newest one with a value for a field wins.</returns>
        public static OverrideHandle BeginOverride(string ownerId)
        {
            EnsureBound();
            int token = _beginOverride?.Invoke(ownerId ?? "?") ?? 0;
            return new OverrideHandle(token);
        }

        internal static void OvIntensity(int t, float v) => Do(() => _ovI?.Invoke(t, v));
        internal static void OvRange(int t, float v) => Do(() => _ovR?.Invoke(t, v));
        internal static void OvSpotAngle(int t, float v) => Do(() => _ovA?.Invoke(t, v));
        internal static void OvColor(int t, float r, float g, float b, float a) => Do(() => _ovC?.Invoke(t, r, g, b, a));
        internal static void ClrIntensity(int t) => Do(() => _clrI?.Invoke(t));
        internal static void ClrRange(int t) => Do(() => _clrR?.Invoke(t));
        internal static void ClrSpotAngle(int t) => Do(() => _clrA?.Invoke(t));
        internal static void ClrColor(int t) => Do(() => _clrC?.Invoke(t));
        internal static void EndOverride(int t) => Do(() => _endOverride?.Invoke(t));

        private static void Do(Action a) { EnsureBound(); if (_bound) a(); else _pending.Add(a); }
        private static void NotifyToggled(bool on) { try { OnToggled?.Invoke(on); } catch { } }
        private static void NotifyRemoteToggled(ulong id, bool on) { try { OnRemoteToggled?.Invoke(id, on); } catch { } }

        private static void EnsureBound()
        {
            if (_bound) return;
            try
            {
                Type t = FindBridge((_probeAttempts++ % 30) == 0);
                if (t == null) return;
                object abi = t.GetField("AbiVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (abi is int v && v < 1) return;
                _abi = abi is int av ? av : 1;

                _isOn = G<Func<bool>>(t, "IsOn");
                _getIntensity = G<Func<float>>(t, "GetIntensity");
                _getRange = G<Func<float>>(t, "GetRange");
                _getSpotAngle = G<Func<float>>(t, "GetSpotAngle");
                _getColor = G<Func<float[]>>(t, "GetColor");
                _setOn = G<Action<bool>>(t, "SetOn");
                _toggle = G<Action>(t, "Toggle");
                _setIntensity = G<Action<float>>(t, "SetIntensity");
                _setRange = G<Action<float>>(t, "SetRange");
                _setSpotAngle = G<Action<float>>(t, "SetSpotAngle");
                _setColor = G<Action<float, float, float, float>>(t, "SetColor");
                _blink = G<Action<int, float>>(t, "Blink");
                _flicker = G<Action<float, float, float>>(t, "Flicker");
                _stopFlicker = G<Action>(t, "StopFlicker");
                _pulse = G<Action<float, float, float>>(t, "Pulse");
                _stopPulse = G<Action>(t, "StopPulse");
                _tempIntensity = G<Action<float, float>>(t, "TempIntensity");
                _tempColor = G<Action<float, float, float, float, float>>(t, "TempColor");
                _beginOverride = G<Func<string, int>>(t, "BeginOverride");
                _endOverride = G<Action<int>>(t, "EndOverride");
                _ovI = G<Action<int, float>>(t, "OvIntensity");
                _ovR = G<Action<int, float>>(t, "OvRange");
                _ovA = G<Action<int, float>>(t, "OvSpotAngle");
                _ovC = G<Action<int, float, float, float, float>>(t, "OvColor");
                _clrI = G<Action<int>>(t, "ClrIntensity");
                _clrR = G<Action<int>>(t, "ClrRange");
                _clrA = G<Action<int>>(t, "ClrSpotAngle");
                _clrC = G<Action<int>>(t, "ClrColor");
                _registerToggled = G<Action<Action<bool>>>(t, "RegisterToggledListener");

                // ABI 2 additions. They resolve to null against an older host, which is exactly what makes every
                // member above degrade to "nothing there" instead of throwing.
                _isMultiplayer = G<Func<bool>>(t, "IsMultiplayer");
                _getLocalSteamId = G<Func<ulong>>(t, "GetLocalSteamId");
                _getRemoteIds = G<Func<ulong[]>>(t, "GetRemoteBeamIds");
                _remoteHasTightBeam = G<Func<ulong, bool>>(t, "RemoteHasTightBeam");
                _getRemoteBeam = G<Func<ulong, float[]>>(t, "GetRemoteBeam");
                _getRemoteBeamPose = G<Func<ulong, float[]>>(t, "GetRemoteBeamPose");
                _isRemoteRendered = G<Func<ulong, bool>>(t, "IsRemoteBeamRendered");
                _registerRemoteToggled = G<Action<Action<ulong, bool>>>(t, "RegisterRemoteToggledListener");

                // Gate on a v1 field. Keying this on a v2 field would make this shim refuse to bind to a v1 host
                // and silently break every consumer that ships against an older TightBeam.
                if (_setOn == null) return; // partial table - retry next call
                _bound = true;
                _registerToggled?.Invoke(NotifyToggled);
                _registerRemoteToggled?.Invoke(NotifyRemoteToggled);
                for (int i = 0; i < _pending.Count; i++) { try { _pending[i](); } catch { } }
                _pending.Clear();
            }
            catch { }
        }

        private static T G<T>(Type t, string field) where T : class
            => t.GetField(field, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as T;

        private static Type FindBridge(bool scan)
        {
            Type t = Type.GetType("TightBeam.Bridge.FlashlightBridge, TightBeam", false);
            if (t != null || !scan) return t;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType("TightBeam.Bridge.FlashlightBridge", false); if (t != null) return t; } catch { }
            }
            return null;
        }

        private static bool TryHex(string hex, out float r, out float g, out float b, out float a)
        {
            r = g = b = a = 1f;
            if (string.IsNullOrEmpty(hex)) return false;
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length < 6) return false;
            try
            {
                r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
                g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
                b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
                a = hex.Length >= 8 ? Convert.ToInt32(hex.Substring(6, 2), 16) / 255f : 1f;
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>Another player's beam as it is being drawn on this machine.</summary>
    /// <remarks>Units match <see cref="Beam"/>: intensity as a Unity light intensity, range in metres, angle in degrees, colour channels from 0 to 1.</remarks>
    public struct RemoteBeamState
    {
        /// <summary>Whether that player's beam is on.</summary>
        public bool IsOn;
        /// <summary>Intensity with running effects applied, range in metres and full cone angle in degrees.</summary>
        public float Intensity, Range, SpotAngle;
        /// <summary>Beam colour, each channel from 0 to 1.</summary>
        public float R, G, B, A;
    }

    /// <summary>A scoped, per-field override on the flashlight for one owner. Set fields to drive the beam; Clear or
    /// Dispose to release them and let the player's own settings show through. Safe no-op if TightBeam is absent.</summary>
    /// <example>
    /// Dim and narrow the beam while the player is inside a zone:
    /// <code>
    /// OverrideHandle zone = Beam.BeginOverride("MyMod").SetIntensity(1f).SetSpotAngle(28f);
    /// // on leaving the zone
    /// zone.Dispose();
    /// </code>
    /// </example>
    public struct OverrideHandle : IDisposable
    {
        private readonly int _token;
        internal OverrideHandle(int token) { _token = token; }
        /// <summary>Overrides the intensity until it is cleared.</summary>
        /// <param name="v">The Unity light intensity, e.g. 1. It is not held to MinIntensity and MaxIntensity; below 0 shows as 0.</param>
        public OverrideHandle SetIntensity(float v) { Beam.OvIntensity(_token, v); return this; }
        /// <summary>Overrides the range until it is cleared.</summary>
        /// <param name="m">The range in metres. The drawn beam stays between 2 and 60.</param>
        public OverrideHandle SetRange(float m) { Beam.OvRange(_token, m); return this; }
        /// <summary>Overrides the cone angle until it is cleared.</summary>
        /// <param name="deg">The full cone angle in degrees, e.g. 28. The drawn beam stays between 8 and 90.</param>
        public OverrideHandle SetSpotAngle(float deg) { Beam.OvSpotAngle(_token, deg); return this; }
        /// <summary>Overrides the colour until it is cleared.</summary>
        /// <param name="r">Red, 0 to 1.</param>
        /// <param name="g">Green, 0 to 1.</param>
        /// <param name="b">Blue, 0 to 1.</param>
        /// <param name="a">Alpha, 0 to 1. The default is 1.</param>
        public OverrideHandle SetColor(float r, float g, float b, float a = 1f) { Beam.OvColor(_token, r, g, b, a); return this; }
        public OverrideHandle ClearIntensity() { Beam.ClrIntensity(_token); return this; }
        public OverrideHandle ClearRange() { Beam.ClrRange(_token); return this; }
        public OverrideHandle ClearSpotAngle() { Beam.ClrSpotAngle(_token); return this; }
        public OverrideHandle ClearColor() { Beam.ClrColor(_token); return this; }
        public void Dispose() { Beam.EndOverride(_token); }
    }
}
