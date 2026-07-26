using Il2CppScheduleOne.PlayerScripts;
using Il2CppSteamworks;

namespace TightBeam.Net
{
    /// <summary>
    /// How a player is named to other mods and on the board. Player.PlayerCode is a SyncVar carrying the owner's
    /// SteamID64 as a decimal string, replicated to every peer, which makes it the one identifier that is the same
    /// on every machine. It populates a moment after the player object appears, so a lookup that returns 0 means
    /// "not yet", not "no such player" - callers retry rather than caching the miss.
    /// </summary>
    internal static class PlayerIdentity
    {
        internal static ulong SteamIdOf(Player p)
        {
            if (p == null) return 0UL;
            try
            {
                string code = p.PlayerCode;
                if (!string.IsNullOrEmpty(code) && ulong.TryParse(code, out ulong id)) return id;
            }
            catch { }
            return 0UL;
        }

        /// <summary>Our own id. Asks Steam first: PlayerCode arrives a beat after the player spawns, and anything
        /// touching the lobby needs an answer before that - reading the board a frame too early with an id of 0
        /// looks exactly like "nobody posted anything", which is indistinguishable from a real failure.</summary>
        internal static ulong LocalSteamId()
        {
            try
            {
                ulong id = SteamUser.GetSteamID().m_SteamID;
                if (id != 0UL) return id;
            }
            catch { }
            try { return SteamIdOf(Player.Local); }
            catch { return 0UL; }
        }
    }
}
