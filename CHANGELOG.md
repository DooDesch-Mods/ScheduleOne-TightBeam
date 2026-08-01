# Changelog

All notable changes to TightBeam are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [2.1.0] - 2026-08-01

### Fixed
- Works on Schedule I 0.4.6f11. Stay on 2.0.0 if you are still playing 0.4.5f2.

## [2.0.0] - 2026-07-26

Co-op.

### Added
- **Other players' flashlights are TightBeam cones too.** Aimed where they are actually looking, pitch
  included, instead of the small vanilla point light. Their own focus, brightness and colour come across
  live, so a beam looks the same from every seat.
- **Late joiners see the lights that are already on.** The base game never tells a joining player about a
  flashlight that was switched on before they arrived; TightBeam carries that state itself.
- Players without the mod still get a cone, drawn from your own default settings. Turn that off with
  `RemoteBeamsForUnmoddedPlayers` if you would rather see their vanilla light.
- Blink, flicker and pulse carry across, so a mod that dims or flickers your beam now reads correctly to
  everyone else instead of only to you.
- Performance controls for a full lobby: `MaxRemoteBeams` (default 4), `RemoteBeamMaxDistance`,
  frustum culling, and `RemoteShadowNearest` (default 0 - only your own beam casts shadows). Sized for
  lobbies well past the stock four players, since other mods raise that cap a long way.
- Sharing rides a part of the Steam lobby the game itself never reads or writes, so a player without
  TightBeam has nothing running on their machine because of you. Needs a Steam lobby: on a direct connect
  or a dedicated server you still get correctly aimed cones, just with your own default settings.
- **Modder API v2**: read the other players' beams - who has one, its shape and colour, where it starts
  and which way it points. Deliberately read-only: every player owns their own beam, so you drive the
  local one as before and it replicates by itself.

### Changed
- The transient effects now run from one shared implementation, so a flicker you see on someone else's
  beam has the same strength and rate as the one they see. It runs from each machine's own clock, so the
  noise is offset in phase rather than sample-for-sample identical - which is not something an eye can
  pick up on a flickering light.

### Notes
- Existing mods built against the v1 API keep working unchanged; the API only gained members.
- Single-player behaviour is unchanged - nothing is sent and no extra work runs.

## [1.0.0] - 2026-07-02

Initial release.

### Added
- A proper, limited-range handheld flashlight: one spotlight that follows the camera with a small
  hand-held offset and survives scene loads.
- **Focus control on ALT + mouse wheel** - a single axis from a wide near-flood to a tight far-throw,
  driving both range and cone angle. Velocity-sensitive: slow scrolling makes fine steps, a fast flick
  races the beam to the nearest extreme, and the displayed beam eases in smoothly.
- On/off follows the game's own flashlight (your flashlight key) as the single source of truth, so the beam
  is always in sync and can never drift; TightBeam dims the vanilla point light and shows its own cone.
- Brightness stays within a hard floor and ceiling and is driven by mods via the API - no player brightness keys.
- Soft shadows and a cool-white default tint, both configurable, plus configurable range/angle
  endpoints, colour, start-on and full key rebinding (MelonPreferences under `[TightBeam]`).
- **Cross-mod control API** (`TightBeam.Api` / the `Beam` shim): on/off, intensity, range, colour,
  Blink/Flicker/Pulse, fire-and-forget temporary overrides and a scoped per-field override stack. A safe
  no-op when TightBeam is absent, so consumer mods need no hard dependency.
- Keeps the game's own equipped-flashlight and phone lamp in sync when a mod override holds the beam
  dark, so blackout effects read correctly.
- Hotbar ALT+scroll guard so adjusting focus never cycles your hotbar slot.
