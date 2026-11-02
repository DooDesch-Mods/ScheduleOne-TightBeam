#if DEBUG
using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Hash.Api;
using TightBeam.Config;
using TightBeam.Lighting;
using TightBeam.Net;
using UnityEngine;

namespace TightBeam.DevTools
{
    /// <summary>
    /// Debug-only console words for checking the beam without a hotkey. Both Console.SubmitCommand overloads are
    /// patched, because either one can be the one whose prefix fires; Dispatch runs a command once per frame and
    /// signature so a submission seen by both prefixes is not applied twice. Compiled out of Release.
    /// </summary>
    internal static class DebugConsole
    {
        private static readonly string[][] Listing =
        {
            new[] { "tbhelp", "list the TightBeam debug commands", "tbhelp" },
            new[] { "tbstatus", "beam state, focus, co-op counters, hotbar patch count and contained failures", "tbstatus" },
            new[] { "tbon", "switch the game flashlight on", "tbon" },
            new[] { "tboff", "switch the game flashlight off", "tboff" },
            new[] { "tbfocus", "set the focus target, 0 = narrow throw, 1 = wide flood", "tbfocus 0.2" },
            new[] { "tbvanilla", "1 = hide the beam and show the game's own flashlight light, 0 = back to TightBeam", "tbvanilla 1" },
            new[] { "tbflashprobe", "toggle the flashlight N times and count frames where the game's own flashlight light is lit", "tbflashprobe 10" },
            new[] { "tbfx","run an effect: blink [times interval], flicker [strength seconds hz], pulse [amp period seconds], stop", "tbfx flicker 0.6 3 12" },
        };

        private static int _lastFrame = -1;
        private static string _lastSig = "";

        internal static void DeclareForTools()
        {
            foreach (string[] one in Listing) HashCommands.Add(one[0], one[1], one[2]);
        }

        internal static bool TryHandle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Dispatch(raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static bool TryHandle(Il2CppSystem.Collections.Generic.List<string> args)
        {
            if (args == null || args.Count == 0) return false;
            string[] parts = new string[args.Count];
            for (int i = 0; i < args.Count; i++) parts[i] = args[i];
            return Dispatch(parts);
        }

        private static bool IsOurs(string cmd)
        {
            foreach (string[] one in Listing) if (one[0] == cmd) return true;
            return false;
        }

        private static bool Dispatch(string[] parts)
        {
            if (parts.Length == 0) return false;
            string cmd = parts[0].ToLowerInvariant();
            if (!IsOurs(cmd)) return false;

            string sig = string.Join(" ", parts);
            int frame = Time.frameCount;
            if (frame == _lastFrame && sig == _lastSig) return true;
            _lastFrame = frame; _lastSig = sig;

            var c = FlashlightController.Instance;
            switch (cmd)
            {
                case "tbhelp":
                    foreach (string[] one in Listing) Core.Log.Msg($"[Debug] {one[2],-24} {one[1]}");
                    break;
                case "tbstatus":
                    Status();
                    break;
                case "tbon":
                case "tboff":
                    c.SetOn(cmd == "tbon");
                    Core.Log.Msg($"[Debug] {cmd}: requested, game flashlight is now {(c.IsOn ? "on" : "off")} (the beam follows next frame)");
                    break;
                case "tbfocus":
                    if (parts.Length < 2 || !TryFloat(parts[1], out float f)) { Core.Log.Warning("[Debug] tbfocus <0-1>"); break; }
                    c.SetFocus(f);
                    Core.Log.Msg($"[Debug] tbfocus: target {Mathf.Clamp01(f):0.00}");
                    break;
                case "tbvanilla":
                    c.DebugShowVanilla = parts.Length < 2 || parts[1] != "0";
                    Core.Log.Msg($"[Debug] tbvanilla: {(c.DebugShowVanilla ? "showing the vanilla flashlight light" : "showing the TightBeam cone")}, flashlight {(c.IsOn ? "on" : "off")}");
                    break;
                case "tbflashprobe":
                    FlashProbe.Start(parts.Length > 1 && TryFloat(parts[1], out float n) ? (int)n : 10);
                    break;
                case "tbfx":
                    Fx(c, parts);
                    break;
            }
            return true;
        }

        private static void Fx(FlashlightController c, string[] parts)
        {
            string kind = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";
            float A(int i, float d) => parts.Length > i && TryFloat(parts[i], out float v) ? v : d;
            switch (kind)
            {
                case "blink": c.Blink((int)A(2, 3), A(3, 0.25f)); break;
                case "flicker": c.Flicker(A(2, 0.6f), A(3, 3f), A(4, 12f)); break;
                case "pulse": c.Pulse(A(2, 0.5f), A(3, 1.2f), A(4, 5f)); break;
                case "stop": c.StopFlicker(); c.StopPulse(); break;
                default: Core.Log.Warning("[Debug] tbfx blink|flicker|pulse|stop [numbers]"); return;
            }
            Core.Log.Msg($"[Debug] tbfx {kind}: started");
        }

        private static void Status()
        {
            var c = FlashlightController.Instance;
            Core.Log.Msg($"[Debug] tbstatus: enabled={TightBeamPreferences.Enabled} on={c.IsOn} rig={c.HasLight} focus={c.Focus:0.00} " +
                         $"intensity={c.CurrentIntensity:0.0} range={c.CurrentRange:0.0} angle={c.CurrentSpotAngle:0.0}");
            Core.Log.Msg($"[Debug] tbstatus: multiplayer={RemoteBeams.InMultiplayer()} remote tracked={RemoteBeams.TrackedCount} " +
                         $"candidates={RemoteBeams.CandidateCount} drawn={RemoteBeams.ActiveCount} board posts={BeamBoard.KnownCount} reads={BeamBoard.ReadCount}");
            Core.Log.Msg($"[Debug] tbstatus: hotbar guard prefixes={HotbarPrefixCount()} (1 expected)");
            Core.Log.Msg($"[Debug] tbstatus: failures: {Diag.Summary()}");
        }

        // How often this assembly's prefix hangs on the hotbar method. More than one means it runs twice per frame.
        private static int HotbarPrefixCount()
        {
            MethodBase target = AccessTools.Method(typeof(Il2CppScheduleOne.PlayerScripts.PlayerInventory), "UpdateHotbarSelection");
            var info = target == null ? null : HarmonyLib.Harmony.GetPatchInfo(target);
            if (info == null) return 0;
            int n = 0;
            foreach (var p in info.Prefixes) if (p.PatchMethod.DeclaringType?.Assembly == typeof(Core).Assembly) n++;
            return n;
        }

        private static bool TryFloat(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand), new Type[] { typeof(string) })]
    internal static class Tb_Console_SubmitCommand_String_Patch
    {
        private static bool Prefix(string args)
        {
            try { return !DebugConsole.TryHandle(args); }
            catch (Exception e) { Diag.Error("debug.console", e); return true; }
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand), new Type[] { typeof(Il2CppSystem.Collections.Generic.List<string>) })]
    internal static class Tb_Console_SubmitCommand_List_Patch
    {
        private static bool Prefix(Il2CppSystem.Collections.Generic.List<string> args)
        {
            try { return !DebugConsole.TryHandle(args); }
            catch (Exception e) { Diag.Error("debug.console", e); return true; }
        }
    }
}
#endif
