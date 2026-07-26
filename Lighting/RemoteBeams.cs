using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.AvatarFramework.Equipping;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using TightBeam.Config;
using TightBeam.Net;
using UnityEngine;

namespace TightBeam.Lighting
{
    /// <summary>
    /// Draws every OTHER player's flashlight as a TightBeam cone instead of the game's small point light, so a co-op
    /// session looks the same from any seat.
    ///
    /// Two halves of this need no networking of our own, because the game already replicates them:
    /// - AIM: Player.MimicCamera is a Transform the game writes every LateUpdate for every player, remote proxies
    ///   included, from the replicated CameraPosition/CameraRotation SyncVars. That is their real look direction,
    ///   pitch and all. It arrives about ten times a second, unreliable and snapped, so it is smoothed here or the
    ///   cone visibly steps.
    /// - ON/OFF: the vanilla flashlight ObserversRpc does nothing but
    ///   ThirdPersonFlashlight.gameObject.SetActive(on), so that active flag IS the replicated on/off. We suppress
    ///   the vanilla light through OptimizedLight.Enabled and never through SetActive, which keeps it readable.
    ///
    /// Only the beam's SHAPE comes off the board (BeamBoard). A player without TightBeam simply has no entry there
    /// and gets a cone built from local defaults.
    ///
    /// Sized for a raised lobby cap: other mods take this game well past its stock four players, so the expensive
    /// pass runs on a timer rather than per frame, and lookups are by key rather than by scanning.
    /// </summary>
    internal static class RemoteBeams
    {
        private sealed class Entry
        {
            public Player Owner;
            public ulong SteamId;
            public Transform Anchor;
            public bool Lit;
            public bool ToggleOwed;    // a transition seen before SteamId replicated, still to be announced

            public Vector3 Pos;
            public Quaternion Rot;
            public bool PoseInit;

            public float Intensity, Range, Angle;
            public Color Color;
            public bool ParamsInit;

            public bool HasPost;
            public BeamPost Post;
            public BeamFx Fx;
            public float FxMul = 1f;
            public int LastFxSeq;

            // The vanilla on/off arrives instantly but is not buffered, so a player who joined late sees an
            // already-lit flashlight as off until its owner toggles it. Until a real vanilla transition has been
            // observed the posted flag fills that hole; from then on vanilla wins, being instant.
            public bool LastVanillaActive;
            public bool SawVanillaTransition;

            // What we darkened. The bool records whether WE switched it off - a light the game or another mod
            // already had off must be left off, not handed back on.
            public OptimizedLight SupThird, SupEquip;
            public bool SupThirdTaken, SupEquipTaken;

            public int Rig = -1;
            public bool Selected;
            public float Dist2;
        }

        private const float RosterInterval = 0.2f;
        private const float SelectInterval = 0.1f;

        // The same hand-held offset the local beam uses, so both read as the same flashlight.
        private static readonly Vector3 HandOffset = new Vector3(0.15f, -0.2f, 0.25f);

        private static readonly Dictionary<ulong, Entry> _byId = new Dictionary<ulong, Entry>();
        private static readonly List<Entry> _entries = new List<Entry>();
        private static readonly List<Entry> _pending = new List<Entry>();   // player known, SteamId not yet

        // MUST be an Il2CppStructArray: a managed Plane[] is converted (copied) at the interop boundary, so the
        // engine would fill a throwaway copy and this array would stay all-zero - which reads as "everything is
        // visible" and silently turns the frustum cull off.
        private static readonly Il2CppStructArray<Plane> _frustum = new Il2CppStructArray<Plane>(6);

        private static GameObject[] _rigGo;
        private static Light[] _rigLight;
        private static Entry[] _winners;
        private static int _winnerCount;
        private static float _nextRoster, _nextSelect;
        private static bool _broken;

        internal static int ActiveCount { get; private set; }
        internal static int CandidateCount { get; private set; }
        internal static int TrackedCount => _entries.Count;

        // ----- frame -------------------------------------------------------------------------------------------

        public static void Tick(float dt)
        {
            // Everything below reaches into game objects that can be torn down between frames. A throw here would
            // flood the log once per frame and leave the rigs wherever the pass got to, so it is contained.
            if (_broken) return;
            try { TickInner(dt); }
            catch (Exception ex)
            {
                _broken = true;
                Core.Log?.Error("RemoteBeams disabled for this session after an error: " + ex);
                try { DisableAll(); } catch { }
            }
        }

        private static void TickInner(float dt)
        {
            int cap = TightBeamPreferences.MaxRemoteBeams;

            // Whether we DRAW other players' beams is a rendering preference. Whether we KNOW about them is not:
            // the mod API promises a view of the session that survives that preference, and a consumer that turns
            // our rendering off to draw its own still needs the roster, the posted state and the toggle events.
            bool render = TightBeamPreferences.RemoteBeams && cap > 0;

            if (Time.time >= _nextRoster) { _nextRoster = Time.time + RosterInterval; RefreshRoster(); }

            // Reading posts and working out who is lit touches a few interop properties per player, so it runs on a
            // timer rather than per frame - with the lobby cap raised far past vanilla that adds up.
            bool evaluate = Time.time >= _nextSelect;
            if (evaluate) _nextSelect = Time.time + SelectInterval;

            if (!render)
            {
                if (evaluate) Evaluate();
                ReleaseAll();
                CandidateCount = 0;
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) { if (evaluate) Evaluate(); ReleaseAll(); return; }
            if (_entries.Count == 0) { ReleaseAll(); CandidateCount = 0; return; }

            EnsurePool(cap);
            if (evaluate) { Evaluate(); Select(cam, cap); }

            // Per frame, only the handful that won: their pose has to keep up with the camera.
            int shadowBudget = TightBeamPreferences.RemoteShadowNearest;
            for (int k = 0; k < _winnerCount; k++)
            {
                Entry e = _winners[k];
                if (e == null || !e.Selected) continue;
                e.FxMul = e.Fx.Multiplier();
                if (!ResolveAnchor(e)) { Release(e); continue; }
                Render(e, k, k < shadowBudget, dt);
            }
            ActiveCount = _winnerCount;
        }

        /// <summary>Take everyone's posted beam and work out who is lit. This is what the mod API reports, so it runs
        /// whether or not we are drawing anything.</summary>
        private static void Evaluate()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                PullPost(e);
                bool lit = IsLit(e);
                if (lit != e.Lit) { e.Lit = lit; RaiseToggled(e); }
            }
        }

        /// <summary>Decide who gets a rig: lit, in range, on screen, nearest first, capped.</summary>
        private static void Select(Camera cam, int cap)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, _frustum);
            Vector3 eye = cam.transform.position;
            float maxDist = TightBeamPreferences.RemoteBeamMaxDistance;
            float maxDist2 = maxDist * maxDist;

            int candidates = 0;
            int kept = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                e.Selected = false;

                if (!e.Lit) { Release(e); continue; }
                if (!e.HasPost && !TightBeamPreferences.RemoteBeamsForUnmoddedPlayers) { Release(e); continue; }
                if (!ResolveAnchor(e)) { Release(e); continue; }

                Vector3 at = e.Anchor.position;
                float d2 = (at - eye).sqrMagnitude;
                if (d2 > maxDist2) { Release(e); continue; }
                // Cull against the range this beam is HEADED for, not the local default. A player posting a 60 m
                // throw would otherwise be tested with our shorter default sphere, get rejected before it ever
                // rendered, and so never set the value that would have let it pass - rejected for good.
                float cullRange = e.Range;
                if (!e.ParamsInit) TargetParams(e, out _, out cullRange, out _, out _);
                if (!ConeVisible(at, e.Anchor.forward, cullRange)) { Release(e); continue; }

                candidates++;
                e.Dist2 = d2;

                // Fixed-size insertion, so no sort allocation and no comparison delegate on a hot path.
                if (kept < cap) { _winners[kept++] = e; continue; }
                int worst = 0;
                for (int k = 1; k < cap; k++) if (_winners[k].Dist2 > _winners[worst].Dist2) worst = k;
                if (_winners[worst].Dist2 <= d2) { Release(e); continue; }
                Release(_winners[worst]);
                _winners[worst] = e;
            }
            CandidateCount = candidates;

            SortWinners(kept);
            for (int k = 0; k < kept; k++) _winners[k].Selected = true;
            for (int s = kept; s < _rigGo.Length; s++) SetRigActive(s, false);
            _winnerCount = kept;
        }

        // ----- roster ------------------------------------------------------------------------------------------

        private static void RefreshRoster()
        {
            try
            {
                var list = Player.PlayerList;
                if (list == null) return;

                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    Entry e = _entries[i];
                    if (e.Owner != null) continue;
                    Forget(e, i);
                }

                for (int j = 0; j < list.Count; j++)
                {
                    Player p = list[j];
                    if (p == null) continue;
                    bool local;
                    try { local = p.IsLocalPlayer; } catch { continue; }
                    if (local) continue;

                    ulong id = PlayerIdentity.SteamIdOf(p);
                    Entry e = id != 0UL && _byId.TryGetValue(id, out Entry known) ? known : FindPending(p);
                    if (e == null)
                    {
                        e = new Entry { Owner = p, SteamId = id };
                        _entries.Add(e);
                        if (id != 0UL) _byId[id] = e; else _pending.Add(e);
                    }
                    else
                    {
                        e.Owner = p;
                        // PlayerCode replicates a moment after the player object, so keep asking until it lands.
                        if (e.SteamId == 0UL && id != 0UL)
                        {
                            e.SteamId = id;
                            _byId[id] = e;
                            _pending.Remove(e);
                            if (e.ToggleOwed) RaiseToggled(e);
                        }
                    }
                }

                // A player who left is gone from PlayerList; drop them and hand their lights back.
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    Entry e = _entries[i];
                    bool present = false;
                    for (int j = 0; j < list.Count; j++) if (ReferenceEquals(list[j], e.Owner)) { present = true; break; }
                    if (!present) Forget(e, i);
                }
            }
            catch (Exception ex) { Core.Log?.Warning("RemoteBeams roster failed: " + ex.Message); }
        }

        private static Entry FindPending(Player p)
        {
            for (int i = 0; i < _pending.Count; i++) if (ReferenceEquals(_pending[i].Owner, p)) return _pending[i];
            return null;
        }

        private static void Forget(Entry e, int index)
        {
            Release(e);
            _entries.RemoveAt(index);
            _pending.Remove(e);
            if (e.SteamId != 0UL) _byId.Remove(e.SteamId);
        }

        /// <summary>Take this player's posted beam, if they have one. Losing the post (they turned sharing off, or
        /// quit TightBeam) drops them back to local defaults and stops any effect they left running - only its owner
        /// can end an effect, and an endless pulse has no end time of its own.</summary>
        private static void PullPost(Entry e)
        {
            if (e.SteamId == 0UL) return;
            bool had = e.HasPost;
            e.HasPost = BeamBoard.TryGet(e.SteamId, out BeamPost post);
            if (!e.HasPost) { if (had) e.Fx.Clear(); return; }

            e.Post = post;
            if (!had)
            {
                // First sight of this player's notice. Adopt whatever effect counter it already carries WITHOUT
                // firing it: joining a session should not replay an effect that started before we arrived, and for
                // a blink that already finished there is nothing sensible to replay.
                e.LastFxSeq = post.FxSeq;
                return;
            }
            if (post.FxSeq != e.LastFxSeq)
            {
                e.LastFxSeq = post.FxSeq;
                ApplyFx(e, post);
            }
        }

        private static void ApplyFx(Entry e, BeamPost p)
        {
            // Dq, not a plain divide: an endless pulse travels as a reserved value and has to come back as one.
            float a = BeamWire.Dq(p.FxA), b = BeamWire.Dq(p.FxB), c = BeamWire.Dq(p.FxC), seed = p.FxSeed / 1000f;
            switch (p.Fx)
            {
                case FxKind.Flicker: e.Fx.Flicker(a, b, c, seed); break;
                case FxKind.StopFlicker: e.Fx.StopFlicker(); break;
                case FxKind.Pulse: e.Fx.Pulse(a, b, c, seed); break;
                case FxKind.StopPulse: e.Fx.StopPulse(); break;
                case FxKind.Blink: e.Fx.Blink(Mathf.RoundToInt(a), b); break;
            }
        }

        private static bool IsLit(Entry e)
        {
            try
            {
                if (e.Owner == null) return false;
                OptimizedLight tpf = e.Owner.ThirdPersonFlashlight;
                bool vanilla = tpf != null && tpf.gameObject.activeSelf;
                if (vanilla != e.LastVanillaActive) { e.LastVanillaActive = vanilla; e.SawVanillaTransition = true; }

                bool on = e.SawVanillaTransition || !e.HasPost ? vanilla : e.Post.On;
                return on && !e.Owner.IsInVehicle;
            }
            catch { return false; }
        }

        private static bool ResolveAnchor(Entry e)
        {
            try
            {
                if (e.Owner == null) return false;
                Transform t = e.Owner.MimicCamera;
                if (t == null)
                {
                    var av = e.Owner.Avatar;
                    if (av != null && av.Eyes != null) t = av.Eyes.transform;
                    if (t == null && e.Owner.ThirdPersonFlashlight != null) t = e.Owner.ThirdPersonFlashlight.transform;
                    if (t == null) t = e.Owner.transform;
                }
                e.Anchor = t;
                return t != null;
            }
            catch { return false; }
        }

        // ----- mod API (read-only) -----------------------------------------------------------------------------

        private static readonly List<Action<ulong, bool>> _toggledListeners = new List<Action<ulong, bool>>();
        private static readonly ulong[] _noIds = new ulong[0];

        internal static void AddToggledListener(Action<ulong, bool> cb) { if (cb != null) _toggledListeners.Add(cb); }

        /// <summary>Tell listeners a player's beam went on or off. A transition seen before their id replicated is
        /// held, not dropped: otherwise a player whose flashlight was already on when they appeared would only ever
        /// produce an "off", and a consumer tracking state would have it backwards.</summary>
        private static void RaiseToggled(Entry e)
        {
            if (e.SteamId == 0UL) { e.ToggleOwed = true; return; }
            e.ToggleOwed = false;
            for (int i = 0; i < _toggledListeners.Count; i++)
            {
                try { _toggledListeners[i](e.SteamId, e.Lit); } catch { }
            }
        }

        /// <summary>Whether there is at least one other player in the session. Answered from the game's player list,
        /// NOT from what we happen to be drawing: a consumer asking whether this is multiplayer must get the same
        /// answer whether or not the player switched remote beams off.</summary>
        internal static bool InMultiplayer()
        {
            try { var l = Player.PlayerList; return l != null && l.Count > 1; }
            catch { return false; }
        }

        /// <summary>Every other player we know a beam for. Drawn or not, and whether or not rendering is switched
        /// off - the reported state has to survive a rendering preference.</summary>
        internal static ulong[] RemoteIds()
        {
            var ids = BeamBoard.PostedIds();
            if (_byId.Count == 0) return ids;
            var set = new List<ulong>(ids);
            foreach (ulong id in _byId.Keys) if (!set.Contains(id)) set.Add(id);
            return set.Count == 0 ? _noIds : set.ToArray();
        }

        /// <summary>Whether this player is running TightBeam and sharing a beam. Read straight from the board, so it
        /// keeps answering while rendering is off.</summary>
        internal static bool HasSyncedBeam(ulong steamId) => BeamBoard.TryGet(steamId, out _);

        /// <summary>[on, intensity, range, spotAngle, r, g, b, a], or null if unknown. Intensity includes any effect
        /// that is running, so a blinking beam reports the brightness it is actually showing. Reading never disturbs
        /// the beam: before it has been drawn once this reports the values it will settle on. When nothing is being
        /// drawn for that player, their posted values are reported instead of nothing.</summary>
        internal static float[] BeamOf(ulong steamId)
        {
            if (_byId.TryGetValue(steamId, out Entry e))
            {
                float i, r, a; Color c;
                if (e.ParamsInit) { i = e.Intensity; r = e.Range; a = e.Angle; c = e.Color; }
                else TargetParams(e, out i, out r, out a, out c);
                return new[] { e.Lit ? 1f : 0f, Mathf.Max(0f, i * e.FxMul), r, a, c.r, c.g, c.b, c.a };
            }
            if (!BeamBoard.TryGet(steamId, out BeamPost p)) return null;
            return new[]
            {
                p.On ? 1f : 0f,
                Mathf.Min(p.Intensity, TightBeamPreferences.MaxIntensity),
                p.Range, p.Angle, p.R, p.G, p.B, 1f,
            };
        }

        /// <summary>[posX, posY, posZ, fwdX, fwdY, fwdZ], or null when that beam is not being drawn.</summary>
        internal static float[] PoseOf(ulong steamId)
        {
            if (!_byId.TryGetValue(steamId, out Entry e) || !e.PoseInit) return null;
            Vector3 p = e.Pos + e.Rot * HandOffset;
            Vector3 f = e.Rot * Vector3.forward;
            return new[] { p.x, p.y, p.z, f.x, f.y, f.z };
        }

        internal static bool IsRendered(ulong steamId)
            => _byId.TryGetValue(steamId, out Entry e) && e.Rig >= 0;

        // ----- rendering ---------------------------------------------------------------------------------------

        private static void Render(Entry e, int slot, bool shadows, float dt)
        {
            e.Rig = slot;

            Vector3 targetPos = e.Anchor.position;
            Quaternion targetRot = e.Anchor.rotation;
            if (!e.PoseInit) { e.Pos = targetPos; e.Rot = targetRot; e.PoseInit = true; }
            else
            {
                // Frame-rate-independent low-pass, same shape as the local beam's focus ease. Without it the ~10 Hz
                // replicated camera pose reads as a stepping cone.
                float k = 1f - Mathf.Exp(-dt / TightBeamPreferences.RemoteSmoothingTau);
                e.Pos = Vector3.Lerp(e.Pos, targetPos, k);
                e.Rot = Quaternion.Slerp(e.Rot, targetRot, k);
            }

            ResolveParams(e, dt);

            GameObject go = _rigGo[slot];
            Light li = _rigLight[slot];
            go.transform.position = e.Pos + e.Rot * HandOffset;
            go.transform.rotation = e.Rot;

            li.intensity = Mathf.Max(0f, e.Intensity * e.FxMul);
            li.range = Mathf.Clamp(e.Range, 2f, 60f);
            li.spotAngle = Mathf.Clamp(e.Angle, 8f, 90f);
            li.innerSpotAngle = li.spotAngle * 0.6f;
            li.color = e.Color;
            li.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (!go.activeSelf) go.SetActive(true);
            li.enabled = true;

            Suppress(e);
        }

        /// <summary>What this beam should settle on. Pure: reading it changes nothing.</summary>
        private static void TargetParams(Entry e, out float intensity, out float range, out float angle, out Color color)
        {
            if (e.HasPost)
            {
                // Bounded by YOUR brightness CEILING, so nothing posted can blind the screen. Not by the floor:
                // MinIntensity keeps the local player from dimming themselves into uselessness, but a mod blacking
                // out its own beam is a deliberate effect that has to reach the other players intact.
                intensity = Mathf.Min(e.Post.Intensity, TightBeamPreferences.MaxIntensity);
                range = e.Post.Range;
                angle = e.Post.Angle;
                color = new Color(e.Post.R, e.Post.G, e.Post.B, 1f);
                return;
            }
            float focus = TightBeamPreferences.DefaultFocus;
            intensity = TightBeamPreferences.DefaultIntensity;
            range = Mathf.Lerp(TightBeamPreferences.RangeNarrow, TightBeamPreferences.RangeWide, focus);
            angle = Mathf.Lerp(TightBeamPreferences.AngleNarrow, TightBeamPreferences.AngleWide, focus);
            color = TightBeamPreferences.Color;
        }

        private static void ResolveParams(Entry e, float dt)
        {
            TargetParams(e, out float wantIntensity, out float wantRange, out float wantAngle, out Color wantColor);

            if (!e.ParamsInit)
            {
                e.Intensity = wantIntensity; e.Range = wantRange; e.Angle = wantAngle; e.Color = wantColor;
                e.ParamsInit = true;
                return;
            }
            float k = 1f - Mathf.Exp(-dt / TightBeamPreferences.FocusEaseTau);
            e.Intensity = Mathf.Lerp(e.Intensity, wantIntensity, k);
            e.Range = Mathf.Lerp(e.Range, wantRange, k);
            e.Angle = Mathf.Lerp(e.Angle, wantAngle, k);
            e.Color = Color.Lerp(e.Color, wantColor, k);
        }

        /// <summary>Test the cone's bounding sphere, not its origin. Testing the origin pops a beam out of existence
        /// while it is still lighting something you can see.</summary>
        private static bool ConeVisible(Vector3 origin, Vector3 forward, float range)
        {
            Vector3 centre = origin + forward * (range * 0.5f);
            float radius = range * 0.55f;
            for (int i = 0; i < 6; i++)
                if (_frustum[i].GetDistanceToPoint(centre) < -radius) return false;
            return true;
        }

        private static void SortWinners(int count)
        {
            for (int i = 1; i < count; i++)
            {
                Entry x = _winners[i];
                int j = i - 1;
                while (j >= 0 && _winners[j].Dist2 > x.Dist2) { _winners[j + 1] = _winners[j]; j--; }
                _winners[j + 1] = x;
            }
        }

        // ----- rig pool ----------------------------------------------------------------------------------------

        private static void EnsurePool(int cap)
        {
            if (_rigGo != null && _rigGo.Length >= cap)
            {
                if (_winners == null || _winners.Length < cap) _winners = new Entry[cap];
                return;
            }

            // The pool only ever grows, to its high-water mark. Lowering the cap leaves the extra rigs allocated but
            // switched off by the sweep in Select, which is cheaper than rebuilding them whenever the setting moves.
            var go = new GameObject[cap];
            var li = new Light[cap];
            int carry = _rigGo != null ? _rigGo.Length : 0;
            for (int i = 0; i < carry; i++) { go[i] = _rigGo[i]; li[i] = _rigLight[i]; }

            for (int i = carry; i < cap; i++)
            {
                go[i] = new GameObject("TightBeamRemote" + i);
                UnityEngine.Object.DontDestroyOnLoad(go[i]);
                li[i] = go[i].AddComponent<Light>();
                li[i].type = LightType.Spot;
                li[i].renderMode = LightRenderMode.Auto;
                li[i].shadows = LightShadows.None;
                li[i].shadowBias = 0.05f;
                li[i].shadowNormalBias = 0.4f;
                li[i].enabled = false;
                go[i].SetActive(false);
            }
            _rigGo = go; _rigLight = li; _winners = new Entry[cap];
        }

        private static void SetRigActive(int slot, bool on)
        {
            if (_rigGo == null || slot < 0 || slot >= _rigGo.Length) return;
            if (_rigGo[slot] == null) return;
            if (_rigLight[slot] != null) _rigLight[slot].enabled = on;
            if (_rigGo[slot].activeSelf != on) _rigGo[slot].SetActive(on);
        }

        // ----- vanilla light suppression -----------------------------------------------------------------------

        /// <summary>Darken this player's own flashlight lights while our cone stands in for them. Writing
        /// OptimizedLight.Enabled (not Light.enabled) is what sticks - the game rewrites Light.enabled from
        /// UpdateLightState on the next camera-movement event.</summary>
        private static void Suppress(Entry e)
        {
            if (!TightBeamPreferences.SuppressRemoteVanillaLights) { Restore(e); return; }
            try
            {
                OptimizedLight third = e.Owner != null ? e.Owner.ThirdPersonFlashlight : null;
                if (!ReferenceEquals(third, e.SupThird)) { Give(e.SupThird, e.SupThirdTaken); e.SupThird = third; e.SupThirdTaken = false; }
                if (Take(e.SupThird)) e.SupThirdTaken = true;

                OptimizedLight equip = null;
                var av = e.Owner != null ? e.Owner.Avatar : null;
                if (av != null)
                {
                    var cur = av.CurrentEquippable;
                    if (cur != null)
                    {
                        var fl = cur.TryCast<FlashlightAvatarEquippable>();
                        if (fl != null) equip = fl.Light;
                    }
                }
                if (!ReferenceEquals(equip, e.SupEquip)) { Give(e.SupEquip, e.SupEquipTaken); e.SupEquip = equip; e.SupEquipTaken = false; }
                if (Take(e.SupEquip)) e.SupEquipTaken = true;
            }
            catch (Exception ex) { Core.Log?.Warning("RemoteBeams suppress failed: " + ex.Message); }
        }

        private static void Restore(Entry e)
        {
            Give(e.SupThird, e.SupThirdTaken); e.SupThird = null; e.SupThirdTaken = false;
            Give(e.SupEquip, e.SupEquipTaken); e.SupEquip = null; e.SupEquipTaken = false;
        }

        /// <summary>Switch a light off. True only when this call is what turned it off, which is the flag that later
        /// decides whether we may switch it back on. Re-asserting every pass therefore never claims a light we did
        /// not own.</summary>
        private static bool Take(OptimizedLight ol)
        {
            try { if (ol != null && ol.Enabled) { ol.Enabled = false; ol.UpdateLightState(); return true; } } catch { }
            return false;
        }

        /// <summary>Known limitation: this restores what WE changed, but `Enabled` is a single shared flag with no
        /// owner. If a second mod also switches the same light off while ours is held, whichever of us releases
        /// first turns it back on under the other. Vanilla is safe - it never writes `Enabled` on either of these
        /// two carriers, and `UpdateLightState` only reads it - so this needs another mod suppressing the same
        /// lights to bite. Fixing it properly needs a shared ownership convention that does not exist today.</summary>
        private static void Give(OptimizedLight ol, bool weTurnedItOff)
        {
            if (!weTurnedItOff) return;
            try { if (ol != null && !ol.Enabled) { ol.Enabled = true; ol.UpdateLightState(); } } catch { }
        }

        // ----- teardown ----------------------------------------------------------------------------------------

        /// <summary>Start of a new session: forget everything, including a breaker tripped by an earlier one. An
        /// exception during a teardown says nothing about the next lobby.</summary>
        public static void NewSession()
        {
            _broken = false;
            try { DisableAll(); } catch { }
            _nextRoster = 0f;
            _nextSelect = 0f;
        }

        /// <summary>Release every rig, hand every darkened vanilla light back, and forget everyone.</summary>
        public static void DisableAll()
        {
            ReleaseAll();
            _entries.Clear();
            _byId.Clear();
            _pending.Clear();
            ActiveCount = 0;
            CandidateCount = 0;
        }

        private static void Release(Entry e)
        {
            e.Rig = -1;
            e.Selected = false;
            Restore(e);
            e.PoseInit = false;
            e.ParamsInit = false;
        }

        private static void ReleaseAll()
        {
            for (int i = 0; i < _entries.Count; i++) Release(_entries[i]);
            if (_rigGo != null) for (int s = 0; s < _rigGo.Length; s++) SetRigActive(s, false);
            _winnerCount = 0;
            ActiveCount = 0;
        }
    }
}
