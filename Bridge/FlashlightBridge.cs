using System;

namespace TightBeam.Bridge
{
    /// <summary>
    /// The reflection ABI contract between the TightBeam host and any consumer mod's vendored <c>Beam</c> shim. ONLY
    /// plain BCL delegate/primitive types cross this boundary (never a UnityEngine.Color or a custom type), so the shim
    /// stays completely Unity-free. ADDITIVE-ONLY: never rename or remove a field once shipped - bump <see cref="AbiVersion"/>
    /// for additions. Colours are marshalled as raw r,g,b,a floats.
    /// </summary>
    public static class FlashlightBridge
    {
        public const int AbiVersion = 2;

        // state getters
        public static Func<bool> IsOn;
        public static Func<float> GetIntensity;
        public static Func<float> GetRange;
        public static Func<float> GetSpotAngle;
        public static Func<float[]> GetColor;                 // [r,g,b,a]

        // persistent switch + base config
        public static Action<bool> SetOn;
        public static Action Toggle;
        public static Action<float> SetIntensity;
        public static Action<float> SetRange;
        public static Action<float> SetSpotAngle;
        public static Action<float, float, float, float> SetColor;

        // transient effects
        public static Action<int, float> Blink;               // times, interval
        public static Action<float, float, float> Flicker;    // strength01, duration, freqHz
        public static Action StopFlicker;
        public static Action<float, float, float> Pulse;      // amp01, period, duration
        public static Action StopPulse;

        // fire-and-forget temporary overrides
        public static Action<float, float> TempIntensity;                    // value, seconds
        public static Action<float, float, float, float, float> TempColor;   // r,g,b,a, seconds

        // scoped override stack
        public static Func<string, int> BeginOverride;        // owner -> token
        public static Action<int> EndOverride;
        public static Action<int, float> OvIntensity;
        public static Action<int, float> OvRange;
        public static Action<int, float> OvSpotAngle;
        public static Action<int, float, float, float, float> OvColor;
        public static Action<int> ClrIntensity;
        public static Action<int> ClrRange;
        public static Action<int> ClrSpotAngle;
        public static Action<int> ClrColor;

        // push notification
        public static Action<Action<bool>> RegisterToggledListener;

        // ----- v2: OTHER players' beams --------------------------------------------------------------------------
        // Read-only on purpose. Every player is the sole author of their own beam, so a consumer drives its LOCAL
        // beam and the state replicates by itself - which is also why a mod-driven blackout now reads correctly to
        // everyone else. Remote setters would introduce an authority question that this design does not have.
        // Players are named by SteamID64, the same value the game replicates as Player.PlayerCode.

        public static Func<bool> IsMultiplayer;                 // in a session with at least one other player
        public static Func<ulong> GetLocalSteamId;              // 0 when unknown or offline
        public static Func<ulong[]> GetRemoteBeamIds;           // never null
        public static Func<ulong, bool> RemoteHasTightBeam;     // false = anything below is a local default
        public static Func<ulong, float[]> GetRemoteBeam;       // [on, intensity, range, spotAngle, r, g, b, a] or null
        public static Func<ulong, float[]> GetRemoteBeamPose;   // [px, py, pz, fwdX, fwdY, fwdZ] or null
        public static Func<ulong, bool> IsRemoteBeamRendered;   // lit but culled reports false here, true in GetRemoteBeam
        public static Action<Action<ulong, bool>> RegisterRemoteToggledListener;
    }
}
