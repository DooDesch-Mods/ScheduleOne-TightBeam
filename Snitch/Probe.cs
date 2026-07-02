#if SNITCH
using Snitch.Api;
using TightBeam.Config;
using TightBeam.Lighting;

namespace TightBeam.Profiling
{
    /// <summary>
    /// DEBUG-only Snitch panel for TightBeam. The Snitch host auto-discovers this type (leaf name SnitchProbe with a
    /// static Register) on bind and calls it; every panel/counter/toggle it registers is also forwarded into the
    /// Hotline overlay by the host, so there is no direct Hotline dependency. Compiled only under the SNITCH symbol
    /// (Debug builds with the Snitch profiler wired in) - the Release DLL contains zero Snitch types.
    /// </summary>
    internal static class SnitchProbe
    {
        public static void Register()
        {
            Panel p = Profiler.RegisterPanel("TightBeam", "TightBeam (Flashlight)");

            p.Text(() =>
            {
                var c = FlashlightController.Instance;
                if (!c.HasLight) return "beam: no rig yet";
                return $"on={c.IsOn}  focus={c.Focus:0.00}\n" +
                       $"intensity={c.CurrentIntensity:0.0}  range={c.CurrentRange:0.0}m  angle={c.CurrentSpotAngle:0}deg";
            });

            // Live gauges (surface in both the Snitch web dashboard and the forwarded Hotline panel).
            p.Counter("Intensity", () => FlashlightController.Instance.CurrentIntensity, "");
            p.Counter("Range", () => FlashlightController.Instance.CurrentRange, "m");
            p.Counter("SpotAngle", () => FlashlightController.Instance.CurrentSpotAngle, "deg");
            p.Counter("Focus", () => FlashlightController.Instance.Focus, "");

            // Master switch (handy for A/B-ing the mod's per-frame cost) + a reset to the configured defaults.
            p.Toggle("Enabled", () => TightBeamPreferences.Enabled, v => TightBeamPreferences.Enabled = v);
            p.Action("Reset to prefs", () => FlashlightController.Instance.InitFromPrefs());
            p.Log();
        }
    }
}
#endif
