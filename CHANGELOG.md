# Changelog

All notable changes to TightBeam are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-07-02

Initial release.

### Added
- A believable, limited-range handheld flashlight: one spotlight that follows the camera with a small
  hand-held offset and survives scene loads.
- **Focus control on ALT + mouse wheel** - a single axis from a wide near-flood to a tight far-throw,
  driving both range and cone angle. Velocity-sensitive: slow scrolling makes fine steps, a fast flick
  races the beam to the nearest extreme, and the displayed beam eases in smoothly.
- Brightness nudge on `[` / `]` within a hard floor and ceiling; toggle on `F`.
- Soft shadows and a cool-white default tint, both configurable, plus configurable range/angle
  endpoints, colour, start-on and full key rebinding (MelonPreferences under `[TightBeam]`).
- **Cross-mod control API** (`TightBeam.Api` / the `Beam` shim): on/off, intensity, range, colour,
  Blink/Flicker/Pulse, fire-and-forget temporary overrides and a scoped per-field override stack. A safe
  no-op when TightBeam is absent, so consumer mods need no hard dependency.
- Keeps the game's own equipped-flashlight and phone lamp in sync when a mod override holds the beam
  dark, so blackout effects read correctly.
- Hotbar ALT+scroll guard so adjusting focus never cycles your hotbar slot.
