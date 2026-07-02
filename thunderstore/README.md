# TightBeam - A Proper Flashlight for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de](https://support.doodesch.de).

> **A proper flashlight.** Toggle it with a key, then hold **ALT + mouse wheel** to dial the beam
> from a wide near-flood to a tight, far-reaching throw. No map-wide floodlight, no blinding glare - just
> a light that feels like a flashlight.

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)

## Features

- **A beam you can aim and shape.** Hold **ALT + mouse wheel** to slide a single Focus axis: wide end =
  a short, broad flood; narrow end = a tight cone that throws far. Range and cone angle both follow it.
- **Velocity-sensitive focus.** Scroll slowly for fine steps; flick fast and the beam races to the
  nearest extreme, easing in smoothly instead of popping.
- **Sensible brightness.** `[` and `]` nudge brightness within a hard floor and ceiling, so you never
  turn night into noon.
- **Shadows and a cool-white tint by default**, both configurable, so the beam is blocked by walls and
  reads against the game's warm lighting.
- **Light-touch and update-resilient.** Just a spotlight that follows your camera - no game systems
  rewired. The only game hook keeps ALT+scroll from cycling your hotbar.
- **Cross-mod API.** Other mods can drive the beam (dim, flicker, blink, override) through a drop-in
  shim that is a safe no-op when TightBeam is absent.

## Controls

- `F` - toggle on/off
- `ALT` + mouse wheel - focus (wide near-flood to tight far-throw)
- `]` / `[` - brighter / dimmer

All keys are rebindable in the config.

## Requirements

- **Schedule I** (IL2CPP) with **MelonLoader 0.7.3+**. No other dependencies.

## Settings

`Enabled`, `ToggleKey` (F), `FocusModifierKey` (LeftAlt), `DefaultFocus` (0.5), `DefaultIntensity` (7),
`MinIntensity`/`MaxIntensity` (1/20), `RangeWide`/`RangeNarrow` (8/34), `AngleWide`/`AngleNarrow` (66/16),
`ColorHex` (#E6F2FF), `CastShadows`, `StartOn`, plus fine focus-feel tuning. In `UserData/MelonPreferences.cfg`
under `[TightBeam]`.

## License

MIT. See the included LICENSE.md.
