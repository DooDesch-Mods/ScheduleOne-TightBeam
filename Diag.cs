using System;
using System.Collections.Generic;
using System.Text;

namespace TightBeam
{
    /// <summary>
    /// Where a caught exception goes. Most catch sites in TightBeam sit on per-frame paths that read game objects which
    /// can be torn down between frames, so a failure there is contained and the frame carries on. Containing it must not
    /// hide it: every site logs its first failure in full, then again at 10, 100, 1000... so a site that fails every
    /// frame is visible without flooding the log. <c>tbstatus</c> lists the counters in Debug builds.
    /// </summary>
    internal static class Diag
    {
        private static readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        /// <summary>A failure the code recovers from by skipping one step.</summary>
        internal static void Warn(string site, Exception e) => Record(site, e, false);

        /// <summary>A failure that made an action the player or a mod asked for not happen.</summary>
        internal static void Error(string site, Exception e) => Record(site, e, true);

        private static void Record(string site, Exception e, bool error)
        {
            int n;
            lock (_counts)
            {
                _counts.TryGetValue(site, out n);
                _counts[site] = ++n;
            }
            if (!ShouldLog(n)) return;

            string line = n == 1
                ? $"[{site}] {e}"
                : $"[{site}] failed {n} times so far, latest: {e?.GetType().Name}: {e?.Message}";
            if (error) Core.Log?.Error(line);
            else Core.Log?.Warning(line);
        }

        private static bool ShouldLog(int n)
        {
            if (n == 1) return true;
            while (n % 10 == 0) n /= 10;
            return n == 1;
        }

        internal static string Summary()
        {
            lock (_counts)
            {
                if (_counts.Count == 0) return "no contained failures";
                var sb = new StringBuilder();
                foreach (var kv in _counts) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
                return sb.ToString().TrimEnd();
            }
        }
    }
}
