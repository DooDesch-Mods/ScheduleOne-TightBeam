using TightBeam.Lighting;
using UnityEngine;

namespace TightBeam.Net
{
    /// <summary>
    /// Turns the local beam into the value that goes on the board. Kept apart from the board itself so the lighting
    /// layer never learns that Steam exists, and the board never learns what a beam is.
    /// </summary>
    internal static class BeamPostBuilder
    {
        private static int _fxSeq;
        private static FxKind _fxKind;
        private static int _fxA, _fxB, _fxC, _fxSeed;
        private static bool _hooked;

        internal static void Start()
        {
            if (_hooked) return;
            _hooked = true;
            FlashlightController.Instance.FxRaised += OnFxRaised;
        }

        /// <summary>The local beam as everyone else should see it: the composed shape WITHOUT the effect multiplier,
        /// plus whichever effect is running. Effects are replayed from their parameters on each machine, so sending
        /// their per-frame value would fight the copy the reader is already drawing.</summary>
        internal static BeamPost Current()
        {
            var c = FlashlightController.Instance;
            Color col = c.WireColor;
            return new BeamPost
            {
                On = c.IsOn,
                I100 = Mathf.RoundToInt(Mathf.Max(0f, c.WireIntensity) * 100f),
                R10 = Mathf.RoundToInt(Mathf.Clamp(c.WireRange, 2f, 60f) * 10f),
                A10 = Mathf.RoundToInt(Mathf.Clamp(c.WireSpotAngle, 8f, 90f) * 10f),
                Rgb24 = (Byte(col.r) << 16) | (Byte(col.g) << 8) | Byte(col.b),
                FxSeq = _fxSeq,
                Fx = _fxKind,
                FxA = _fxA,
                FxB = _fxB,
                FxC = _fxC,
                FxSeed = _fxSeed,
            };
        }

        private static void OnFxRaised(char code, float a, float b, float c)
        {
            FxKind kind;
            switch (code)
            {
                case 'F': kind = FxKind.Flicker; break;
                case 'f': kind = FxKind.StopFlicker; break;
                case 'P': kind = FxKind.Pulse; break;
                case 'p': kind = FxKind.StopPulse; break;
                case 'B': kind = FxKind.Blink; break;
                default: return;
            }
            // The sequence number is what tells a reader this is a NEW effect rather than the one already running.
            // It only ever goes up, so a reader that missed one still moves on rather than replaying it.
            unchecked { _fxSeq++; }
            _fxKind = kind;
            _fxA = BeamWire.Q(a);
            _fxB = BeamWire.Q(b);
            _fxC = BeamWire.Q(c);
            _fxSeed = BeamWire.Q(FlashlightController.Instance.CurrentFxSeed);
        }

        private static int Byte(float v) => Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
