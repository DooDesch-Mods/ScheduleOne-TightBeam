using System;
using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Networking;
using Il2CppSteamworks;
using TightBeam.Config;
using UnityEngine;

namespace TightBeam.Net
{
    /// <summary>
    /// The noticeboard. Every player writes their own beam into their own Steam lobby member-data key; everyone
    /// running TightBeam reads the others'. Nobody can write anyone else's key, so authorship is Steam's problem
    /// rather than ours, and there is no host, no relay and no ordering.
    ///
    /// Why this and not the game's own networking: FishNet's code generator does not run for mods on IL2CPP, so a
    /// mod cannot declare its own RPC. Riding a vanilla RPC works between modded clients but fans out to EVERY
    /// observer, and an unmodded client then runs the vanilla handler for our payload. Lobby chat is worse still -
    /// the game logs every chat message and acts on three magic strings without checking who sent them. Member data
    /// is the one channel the game touches nowhere: `SetLobbyMemberData` and `GetLobbyMemberData` do not appear in
    /// the game at all, on either branch, so a player without TightBeam runs no code because of us.
    ///
    /// Late joining needs no code: Steam hands a new member the existing data, and the value lives as long as the
    /// lobby. There is nothing to re-announce and no heartbeat to miss.
    /// </summary>
    internal static class BeamBoard
    {
        private const float PublishDebounce = 0.2f;   // Steam batches writes anyway; this just avoids pointless calls

        // How often the whole board is re-read. The change callback is meant to carry this, but it may not exist at
        // all on this build: the game itself never uses LobbyDataUpdate_t, and an IL2CPP runtime only has code for
        // the generic instantiations something in the build actually referenced. So the sweep starts fast enough to
        // carry the feature ALONE, and only relaxes once a callback has actually been observed.
        private const float SweepWhenCallbackUnproven = 1f;
        private const float SweepWhenCallbackWorks = 5f;

        private static readonly Dictionary<ulong, BeamPost> _posts = new Dictionary<ulong, BeamPost>();
        private static Callback<LobbyDataUpdate_t> _dataCallback;
        private static Action _onLobbyChange;

        private static BeamPost _lastPublished;
        private static bool _hasPublished;
        private static bool _withdrawPending;
        private static float _nextPublishAt;
        private static float _nextRescanAt;
        private static float _writeBackoff;   // earliest time the next write may be attempted
        private static float _writeDelay;     // the current backoff length, doubled per failure
        private static bool _warnedWrite;
        private static ulong _boundLobby;
        private static bool _callbackProven;   // a change callback has actually fired at least once
        private static bool _verifiedWrite;    // our own key was read back at least once

        // A write that throws is retried, but never every frame: an interop exception per frame costs far more than
        // the update is worth, and the first failure is usually the start of a run of them.
        private const float BackoffMin = 0.5f;
        private const float BackoffMax = 15f;

        internal static int KnownCount => _posts.Count;
        internal static int ReadCount { get; private set; }
        internal static int WriteCount { get; private set; }

        /// <summary>The lobby we can actually use, or 0. Deliberately not `Lobby.IsHost`, which returns true when
        /// the player is in no lobby at all. Single-player, the debug local-multiplayer tool and a Steam that never
        /// initialised all land here as 0 while FishNet still reports a live session.</summary>
        private static ulong LobbyId
        {
            get
            {
                try
                {
                    var l = PersistentSingleton<Lobby>.Instance;
                    if (l == null || !l.IsInLobby) return 0UL;
                    return l.LobbyID;
                }
                catch { return 0UL; }
            }
        }

        internal static bool Available => LobbyId != 0UL;

        // ----- lifecycle ---------------------------------------------------------------------------------------

        /// <summary>Called every frame while in gameplay, INCLUDING when the mod is switched off - a disabled mod
        /// still has to take its notice down, or the others keep drawing a beam that is no longer being maintained.</summary>
        internal static void Tick(bool modEnabled)
        {
            ulong lobby = LobbyId;
            if (lobby == 0UL)
            {
                if (_boundLobby != 0UL) Reset();
                return;
            }
            // A different lobby means a different session: drop what we knew about the old one, then read the board
            // once straight away rather than waiting out a sweep interval for the first look.
            if (lobby != _boundLobby) { Reset(); _boundLobby = lobby; Bind(); ReadAll(lobby); }

            // Writing and reading are independent. Someone who shares nothing still wants to SEE the others, and a
            // taken-down notice has to keep retrying so a Steam hiccup cannot leave a stale beam advertised.
            bool share = modEnabled && TightBeamPreferences.SyncMyBeam;
            if (share) { _withdrawPending = false; Publish(lobby); }
            else if (_hasPublished || _withdrawPending) Withdraw(lobby);

            // Switched off entirely: no point reading a board nothing will draw from.
            if (!modEnabled) return;

            if (Time.time >= _nextRescanAt)
            {
                _nextRescanAt = Time.time + (_callbackProven ? SweepWhenCallbackWorks : SweepWhenCallbackUnproven);
                ReadAll(lobby);
            }
        }

        private static void Bind()
        {
            // The game already pumps SteamAPI.RunCallbacks every frame, so registering is all that is needed.
            // Held in a static field: a garbage-collected Callback silently stops firing.
            if (_dataCallback == null)
            {
                // This can legitimately fail on IL2CPP: nothing in the game references LobbyDataUpdate_t, so the
                // runtime may have no code for this generic instantiation. The sweep covers us either way; the log
                // line just says which path is carrying it.
                try
                {
                    _dataCallback = Callback<LobbyDataUpdate_t>.Create((Callback<LobbyDataUpdate_t>.DispatchDelegate)OnLobbyData);
                    Core.Log?.Msg("[board] lobby-data callback registered.");
                }
                catch (Exception e)
                {
                    Core.Log?.Warning("[board] no lobby-data callback on this build (" + e.GetType().Name +
                                      "); beam updates come from the periodic sweep instead.");
                }
            }
            // Vanilla already listens for membership changes and raises this, so joins and leaves come for free.
            if (_onLobbyChange == null)
            {
                try
                {
                    var l = PersistentSingleton<Lobby>.Instance;
                    if (l != null) { _onLobbyChange = new Action(OnLobbyChange); l.OnLobbyChange += _onLobbyChange; }
                }
                catch (Exception e) { Core.Log?.Warning("[board] lobby-change hook failed: " + e.Message); }
            }
        }

        /// <summary>Leaving gameplay. The notice must come DOWN rather than just be forgotten: the lobby can outlive
        /// the scene, and a forgotten notice is one nobody will ever take down. Marking it pending and letting Tick
        /// drain it means a Steam failure at exactly this moment still gets retried.</summary>
        internal static void EndSession()
        {
            _posts.Clear();
            if (_hasPublished) _withdrawPending = true;
        }

        internal static void Reset()
        {
            _posts.Clear();
            _hasPublished = false;
            _withdrawPending = false;
            _verifiedWrite = false;
            _nextPublishAt = 0f;
            _nextRescanAt = 0f;
            _writeBackoff = 0f;
            _writeDelay = 0f;
            _boundLobby = 0UL;
            _warnedWrite = false;
        }

        // ----- reading -----------------------------------------------------------------------------------------

        /// <summary>That player's beam, if they are running TightBeam and have posted one.</summary>
        internal static bool TryGet(ulong steamId, out BeamPost post) => _posts.TryGetValue(steamId, out post);

        private static readonly ulong[] _noIds = new ulong[0];

        /// <summary>Everyone who has a beam on the board. Independent of what is being drawn.</summary>
        internal static ulong[] PostedIds()
        {
            if (_posts.Count == 0) return _noIds;
            var ids = new ulong[_posts.Count];
            _posts.Keys.CopyTo(ids, 0);
            return ids;
        }

        private static void OnLobbyChange() { ulong l = LobbyId; if (l != 0UL) ReadAll(l); }

        private static void OnLobbyData(LobbyDataUpdate_t e)
        {
            try
            {
                if (!_callbackProven)
                {
                    _callbackProven = true;
                    Core.Log?.Msg("[board] lobby-data callback is live; sweeping less often now.");
                }
                // m_ulSteamIDMember == m_ulSteamIDLobby means the lobby's own data changed, not a member's.
                if (e.m_ulSteamIDLobby != _boundLobby) return;
                if (e.m_ulSteamIDMember == e.m_ulSteamIDLobby) return;
                ReadOne(e.m_ulSteamIDLobby, e.m_ulSteamIDMember);
            }
            catch { }
        }

        /// <summary>Re-read every member. Enumerated through Steam rather than the game's player list because the
        /// two are not the same set, and because the lobby cap is raised well past the vanilla four by other mods.</summary>
        private static readonly HashSet<ulong> _seen = new HashSet<ulong>();
        private static readonly List<ulong> _gone = new List<ulong>();

        private static void ReadAll(ulong lobby)
        {
            try
            {
                var id = new CSteamID(lobby);
                _seen.Clear();
                int n = SteamMatchmaking.GetNumLobbyMembers(id);
                for (int i = 0; i < n; i++)
                {
                    CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(id, i);
                    _seen.Add(member.m_SteamID);
                    ReadOne(lobby, member.m_SteamID);
                }

                // Someone who left the lobby stops being enumerated but their cached notice would otherwise sit
                // here for the rest of the session, and the mod API would keep reporting a player who is gone.
                _gone.Clear();
                foreach (ulong known in _posts.Keys) if (!_seen.Contains(known)) _gone.Add(known);
                for (int i = 0; i < _gone.Count; i++) _posts.Remove(_gone[i]);
            }
            catch (Exception e) { Core.Log?.Warning("[board] read failed: " + e.Message); }
        }

        private static void ReadOne(ulong lobby, ulong member)
        {
            try
            {
                // Our own notice is never read back: everything downstream is about the OTHER players, and keeping
                // ourselves here would put the local player into the mod API's remote list.
                if (member == PlayerIdentity.LocalSteamId()) { _posts.Remove(member); return; }
                string raw = SteamMatchmaking.GetLobbyMemberData(new CSteamID(lobby), new CSteamID(member), BeamWire.Key);
                ReadCount++;
                if (string.IsNullOrEmpty(raw)) { _posts.Remove(member); return; }   // not running TightBeam
                if (BeamWire.TryDecode(raw, out BeamPost p)) _posts[member] = p;
                else _posts.Remove(member);
            }
            catch { }
        }

        // ----- writing -----------------------------------------------------------------------------------------

        private static void Publish(ulong lobby)
        {
            if (Time.time < _nextPublishAt) return;

            BeamPost now = BeamPostBuilder.Current();
            if (_hasPublished && now.SameAs(_lastPublished)) return;

            string encoded = BeamWire.Encode(now);
            if (!Write(lobby, encoded)) return;   // keep the change dirty and retry after the backoff
            _lastPublished = now;
            _hasPublished = true;
            _nextPublishAt = Time.time + PublishDebounce;

            // SetLobbyMemberData returns void, so "did not throw" is not "was accepted" - Steam caps how many keys
            // one member may hold, and another mod may have used them up. Read our own key back once so a silently
            // rejected write is visible instead of latching us into believing we are published.
            if (!_verifiedWrite)
            {
                _verifiedWrite = true;
                try
                {
                    string back = SteamMatchmaking.GetLobbyMemberData(new CSteamID(lobby), new CSteamID(PlayerIdentity.LocalSteamId()), BeamWire.Key);
                    if (string.IsNullOrEmpty(back))
                        Core.Log?.Warning("[board] our beam did not stick in the lobby - another mod may have used up the member-data slots. Others will see default cones.");
                }
                catch { }
            }
        }

        /// <summary>Take the notice down, so the others fall back to their own defaults instead of holding the last
        /// thing we said. Only counts as done once the write actually went through.</summary>
        private static void Withdraw(ulong lobby)
        {
            _withdrawPending = true;
            if (!Write(lobby, "")) return;
            _hasPublished = false;
            _withdrawPending = false;
        }

        /// <summary>One member-data write, with backoff. Returns false when it did not go through.</summary>
        private static bool Write(ulong lobby, string value)
        {
            if (Time.time < _writeBackoff) return false;
            try
            {
                SteamMatchmaking.SetLobbyMemberData(new CSteamID(lobby), BeamWire.Key, value);
                if (_warnedWrite) { _warnedWrite = false; Core.Log?.Msg("[board] beam sharing recovered."); }
                _writeBackoff = 0f;
                _writeDelay = 0f;
                WriteCount++;
                return true;
            }
            catch (Exception e)
            {
                // The delay has to be kept as its own value. Deriving the next one from the deadline cannot work:
                // we only get here once the deadline has passed, so the remaining time is always zero or less and
                // every retry would come out as the minimum, forever.
                _writeDelay = _writeDelay <= 0f ? BackoffMin : Mathf.Min(BackoffMax, _writeDelay * 2f);
                _writeBackoff = Time.time + _writeDelay;
                if (!_warnedWrite) { _warnedWrite = true; Core.Log?.Warning("[board] beam write failed, backing off: " + e.Message); }
                return false;
            }
        }
    }
}
