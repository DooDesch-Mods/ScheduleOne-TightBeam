using UnityEngine;

namespace TightBeam.Lighting
{
    /// <summary>
    /// The transient effects layer of a beam: a multiplicative modulation on intensity that never touches
    /// Light.enabled. One implementation shared by the local beam and every remote one, so a replicated effect
    /// cannot drift from the original.
    ///
    /// Everything is derived from the clock and the seed, so co-op shares an effect once and each machine runs it
    /// locally at its own framerate instead of streaming a value per frame.
    ///
    /// Note what that does and does not promise. The clock is this machine's own uptime, so a replicated flicker is
    /// NOT sample-for-sample the original: it is the same noise at the same strength and rate, offset in phase.
    /// Perlin noise is stationary and a sine phase shift is invisible, so the two read as the same effect - but
    /// anything that needs peers to agree on an exact value at an exact moment would need a shared clock, which
    /// this does not have.
    /// </summary>
    internal struct BeamFx
    {
        private enum Mode { None, Flicker, Pulse }

        private Mode _mode;
        private float _strength, _freq, _amp, _period, _end, _seed;
        private int _blinkLeft;
        private float _blinkInterval, _blinkNext;
        private bool _blinkDark;

        public float Seed => _seed;

        public void Flicker(float strength01, float durationSeconds, float freqHz, float seed)
        {
            _mode = Mode.Flicker;
            _strength = Mathf.Clamp01(strength01);
            _freq = Mathf.Max(0.1f, freqHz);
            _end = Time.time + Mathf.Max(0.05f, durationSeconds);
            _seed = seed;
        }

        public void Pulse(float amp01, float periodSeconds, float durationSeconds, float seed)
        {
            _mode = Mode.Pulse;
            _amp = Mathf.Clamp01(amp01);
            _period = Mathf.Max(0.1f, periodSeconds);
            _end = float.IsInfinity(durationSeconds) ? float.MaxValue : Time.time + Mathf.Max(0.05f, durationSeconds);
            _seed = seed;
        }

        /// <summary>Returns true only when an effect was actually running, so a caller can avoid announcing a stop
        /// that changed nothing.</summary>
        public bool StopFlicker() { if (_mode != Mode.Flicker) return false; _mode = Mode.None; return true; }
        public bool StopPulse() { if (_mode != Mode.Pulse) return false; _mode = Mode.None; return true; }

        /// <summary>Blink a bounded number of times. The cap matters because nothing can cancel a blink once it has
        /// started, and this runs from the wire as well as from the local API - an unbounded count would let one
        /// message blink another player's beam for hours on every machine that can see it.</summary>
        public const int MaxBlinks = 20;

        public void Blink(int times, float intervalSeconds)
        {
            if (times <= 0) return;
            _blinkLeft = Mathf.Min(times, MaxBlinks) * 2;
            _blinkInterval = Mathf.Max(0.02f, intervalSeconds);
            _blinkNext = Time.time;
            _blinkDark = false;
        }

        public void Clear()
        {
            _mode = Mode.None;
            _blinkLeft = 0;
            _blinkDark = false;
        }

        public float Multiplier()
        {
            float mul = 1f;
            if (_mode == Mode.Flicker)
            {
                if (Time.time >= _end) _mode = Mode.None;
                else
                {
                    float n = Mathf.PerlinNoise(Time.time * _freq + _seed, _seed * 0.37f); // 0..1, smooth
                    mul *= Mathf.Lerp(1f - _strength, 1f, n);
                }
            }
            else if (_mode == Mode.Pulse)
            {
                if (Time.time >= _end) _mode = Mode.None;
                else mul *= 1f + _amp * Mathf.Sin(Time.time * (2f * Mathf.PI / Mathf.Max(0.05f, _period)) + _seed);
            }

            if (_blinkLeft > 0)
            {
                if (Time.time >= _blinkNext) { _blinkDark = !_blinkDark; _blinkLeft--; _blinkNext = Time.time + _blinkInterval; }
                if (_blinkDark) mul *= 0.04f;
            }
            return Mathf.Max(0f, mul);
        }
    }
}
