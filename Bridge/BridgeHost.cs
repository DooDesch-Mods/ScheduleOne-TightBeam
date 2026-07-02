using System;
using System.Collections.Generic;
using TightBeam.Lighting;
using UnityEngine;

namespace TightBeam.Bridge
{
    /// <summary>Wires every <see cref="FlashlightBridge"/> field to a real <see cref="FlashlightController"/> call.
    /// The single, auditable host-side surface a consumer mod can reach through its vendored Beam shim.</summary>
    internal static class BridgeHost
    {
        private static readonly List<Action<bool>> _toggleListeners = new List<Action<bool>>();

        public static void Install()
        {
            var c = FlashlightController.Instance;

            FlashlightBridge.IsOn = () => c.IsOn;
            FlashlightBridge.GetIntensity = () => c.CurrentIntensity;
            FlashlightBridge.GetRange = () => c.CurrentRange;
            FlashlightBridge.GetSpotAngle = () => c.CurrentSpotAngle;
            FlashlightBridge.GetColor = () => { var k = c.CurrentColor; return new[] { k.r, k.g, k.b, k.a }; };

            FlashlightBridge.SetOn = on => c.SetOn(on);
            FlashlightBridge.Toggle = () => c.Toggle();
            FlashlightBridge.SetIntensity = v => c.SetIntensity(v);
            FlashlightBridge.SetRange = v => c.SetBaseRange(v);
            FlashlightBridge.SetSpotAngle = v => c.SetBaseSpotAngle(v);
            FlashlightBridge.SetColor = (r, g, b, a) => c.SetColor(new Color(r, g, b, a));

            FlashlightBridge.Blink = (t, i) => c.Blink(t, i);
            FlashlightBridge.Flicker = (s, d, f) => c.Flicker(s, d, f);
            FlashlightBridge.StopFlicker = () => c.StopFlicker();
            FlashlightBridge.Pulse = (a, p, d) => c.Pulse(a, p, d);
            FlashlightBridge.StopPulse = () => c.StopPulse();

            FlashlightBridge.TempIntensity = (v, s) => c.TemporaryIntensity(v, s);
            FlashlightBridge.TempColor = (r, g, b, a, s) => c.TemporaryColor(r, g, b, a, s);

            FlashlightBridge.BeginOverride = owner => c.BeginOverride(owner);
            FlashlightBridge.EndOverride = tok => c.EndOverride(tok);
            FlashlightBridge.OvIntensity = (tok, v) => c.OverrideIntensity(tok, v);
            FlashlightBridge.OvRange = (tok, v) => c.OverrideRange(tok, v);
            FlashlightBridge.OvSpotAngle = (tok, v) => c.OverrideSpotAngle(tok, v);
            FlashlightBridge.OvColor = (tok, r, g, b, a) => c.OverrideColor(tok, new Color(r, g, b, a));
            FlashlightBridge.ClrIntensity = tok => c.ClearOverrideIntensity(tok);
            FlashlightBridge.ClrRange = tok => c.ClearOverrideRange(tok);
            FlashlightBridge.ClrSpotAngle = tok => c.ClearOverrideSpotAngle(tok);
            FlashlightBridge.ClrColor = tok => c.ClearOverrideColor(tok);

            FlashlightBridge.RegisterToggledListener = cb => { if (cb != null) _toggleListeners.Add(cb); };
            c.Toggled += on => { for (int i = 0; i < _toggleListeners.Count; i++) { try { _toggleListeners[i](on); } catch { } } };
        }
    }
}
