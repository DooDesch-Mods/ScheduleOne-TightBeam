using System;
using System.Globalization;
using UnityEngine;

namespace TightBeam.Net
{
    internal enum FxKind { None = 0, Flicker = 1, StopFlicker = 2, Pulse = 3, StopPulse = 4, Blink = 5 }

    /// <summary>
    /// One player's whole beam, as it sits on the board: the shape plus whichever effect is currently running.
    /// This is STATE, not a message - it is written over, not appended to, so there is no ordering to get wrong and
    /// nothing to replay for a late joiner.
    /// </summary>
    internal struct BeamPost
    {
        public bool On;
        public int I100;    // intensity * 100
        public int R10;     // range * 10, metres
        public int A10;     // spot angle * 10, degrees
        public int Rgb24;   // 0xRRGGBB

        public int FxSeq;   // bumped on every new effect; a reader fires when it changes
        public FxKind Fx;
        public int FxA, FxB, FxC, FxSeed;   // all * 1000

        public float Intensity => I100 / 100f;
        public float Range => R10 / 10f;
        public float Angle => A10 / 10f;
        public float R => ((Rgb24 >> 16) & 0xFF) / 255f;
        public float G => ((Rgb24 >> 8) & 0xFF) / 255f;
        public float B => (Rgb24 & 0xFF) / 255f;

        public bool SameAs(BeamPost o)
            => On == o.On && I100 == o.I100 && R10 == o.R10 && A10 == o.A10 && Rgb24 == o.Rgb24
               && FxSeq == o.FxSeq;
    }

    /// <summary>
    /// The value TightBeam writes into its own Steam lobby member-data key, and reads back from everyone else's.
    ///
    ///   TB1|&lt;on&gt;|&lt;i100&gt;|&lt;r10&gt;|&lt;a10&gt;|&lt;rrggbb&gt;|&lt;fxSeq&gt;|&lt;fxKind&gt;|&lt;a&gt;|&lt;b&gt;|&lt;c&gt;|&lt;seed&gt;
    ///
    /// ASCII and integers only: the wire must not depend on the writer's locale, and the quantisation step doubles
    /// as the change deadband. ADDITIVE-ONLY - readers require at least the fields they need and ignore extra ones,
    /// so a later version can append without breaking an older reader.
    ///
    /// Steam batches repeated writes and sends the last one, so a player sweeping their focus produces one final
    /// value rather than a stream. That is the behaviour we want: the beam shape is a target the reader eases
    /// toward. It does mean an effect that starts and is replaced within one batch window can be missed - losing an
    /// effect START is cosmetic, and an effect STOP can never be lost because a stop IS the resulting state.
    /// </summary>
    internal static class BeamWire
    {
        internal const string Prefix = "TB1|";

        /// <summary>The member-data key. Vanilla uses none of these (`SetLobbyMemberData` does not appear anywhere
        /// in the game), so a collision is impossible; the name is still namespaced out of politeness to other mods.</summary>
        internal const string Key = "doodesch_tightbeam";

        /// <summary>Guards the parser against a value some other tool wrote into our key.</summary>
        internal const int MaxPayloadChars = 128;
        private const int MinFields = 12;
        private const int MaxFields = 24;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal static string Encode(BeamPost p)
            => Prefix + (p.On ? "1" : "0") + "|" +
               p.I100.ToString(Inv) + "|" + p.R10.ToString(Inv) + "|" + p.A10.ToString(Inv) + "|" +
               (p.Rgb24 & 0xFFFFFF).ToString("X6", Inv) + "|" +
               p.FxSeq.ToString(Inv) + "|" + ((int)p.Fx).ToString(Inv) + "|" +
               p.FxA.ToString(Inv) + "|" + p.FxB.ToString(Inv) + "|" + p.FxC.ToString(Inv) + "|" +
               p.FxSeed.ToString(Inv);

        internal static bool TryDecode(string raw, out BeamPost p)
        {
            p = default;
            if (raw == null || raw.Length > MaxPayloadChars) return false;
            if (!raw.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            string[] f = raw.Split('|');
            if (f.Length < MinFields || f.Length > MaxFields) return false;

            if (!TryInt(f[1], out int on) || !TryInt(f[2], out p.I100) ||
                !TryInt(f[3], out p.R10) || !TryInt(f[4], out p.A10)) return false;
            if (!TryHex(f[5], out p.Rgb24)) return false;
            if (!TryInt(f[6], out p.FxSeq) || !TryInt(f[7], out int fx) ||
                !TryInt(f[8], out p.FxA) || !TryInt(f[9], out p.FxB) ||
                !TryInt(f[10], out p.FxC) || !TryInt(f[11], out p.FxSeed)) return false;
            if (fx < 0 || fx > (int)FxKind.Blink) return false;   // a newer effect we do not know about

            p.On = on != 0;
            p.Fx = (FxKind)fx;

            // Clamp everything that came off the board. Steam proves WHO wrote a key, not that what they wrote is
            // sane, and these values land on a Unity Light and in a loop bound.
            p.I100 = Mathf.Clamp(p.I100, 0, 100 * 100);
            p.R10 = Mathf.Clamp(p.R10, 2 * 10, 60 * 10);
            p.A10 = Mathf.Clamp(p.A10, 8 * 10, 90 * 10);
            p.Rgb24 &= 0xFFFFFF;
            p.FxA = ClampFx(p.FxA);
            p.FxB = ClampFx(p.FxB);
            p.FxC = ClampFx(p.FxC);
            p.FxSeed = Mathf.Clamp(p.FxSeed, 0, 1000 * 1000);
            return true;
        }

        /// <summary>An endless effect duration is a supported input (Beam.Pulse takes float.PositiveInfinity), so it
        /// gets a reserved value rather than being clamped into a finite one - a peer must not quietly stop a pulse
        /// the owner is still running.</summary>
        internal const int Endless = int.MaxValue;

        private const int MaxFinite = 60 * 1000;   // 60s is longer than any sensible finite effect

        internal static int Q(float v)
        {
            if (float.IsNaN(v)) return 0;
            if (float.IsPositiveInfinity(v)) return Endless;
            double scaled = (double)v * 1000.0;
            if (scaled >= Endless) return Endless;
            if (scaled < 0) return 0;
            return (int)scaled;
        }

        /// <summary>Back to seconds, mapping the reserved value to a genuinely endless effect.</summary>
        internal static float Dq(int q) => q == Endless ? float.PositiveInfinity : q / 1000f;

        private static int ClampFx(int q) => q == Endless ? Endless : Mathf.Clamp(q, 0, MaxFinite);

        private static bool TryInt(string s, out int v)
            => int.TryParse(s, NumberStyles.Integer, Inv, out v);

        private static bool TryHex(string s, out int v)
        {
            v = 0;
            if (string.IsNullOrEmpty(s) || s.Length > 6) return false;
            for (int i = 0; i < s.Length; i++)
            {
                int d = HexDigit(s[i]);
                if (d < 0) return false;
                v = (v << 4) | d;
            }
            return true;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }
    }
}
