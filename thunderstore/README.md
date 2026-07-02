# TightBeam - A Proper Flashlight for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de](https://support.doodesch.de).

> **A proper flashlight.** It upgrades the game's own flashlight - toggle it with your flashlight key, then
> hold **ALT + mouse wheel** to dial the beam from a wide near-flood to a tight, far-reaching throw. No
> map-wide floodlight, no blinding glare - just a light that feels like a flashlight.

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)

## Features

- **A beam you can aim and shape.** Hold **ALT + mouse wheel** to slide a single Focus axis: wide end =
  a short, broad flood; narrow end = a tight cone that throws far. Range and cone angle both follow it.
- **Velocity-sensitive focus.** Scroll slowly for fine steps; flick fast and the beam races to the
  nearest extreme, easing in smoothly instead of popping.
- **Fixed, sane brightness.** Held within a hard floor and ceiling, so nothing blinds the screen. Brightness
  is not a player control; mods can drive it via the API for effects.
- **Shadows and a cool-white tint by default**, both configurable, so the beam is blocked by walls and
  reads against the game's warm lighting.
- **In perfect sync with the game.** It drives off the game's own flashlight state, so on/off never falls
  out of step - your flashlight key toggles it, and it swaps the vanilla point light for its own clean cone.
- **Light-touch and update-resilient.** A spotlight that follows your camera - no gameplay rewired, no
  networking touched; a tiny guard keeps ALT+scroll from cycling your hotbar.
- **Cross-mod API.** Other mods can drive the beam (dim, flicker, blink, override) through a drop-in
  shim that is a safe no-op when TightBeam is absent.

## Controls

- Flashlight key (your game bind) - toggle on/off
- `ALT` + mouse wheel - focus (wide near-flood to tight far-throw)

On/off uses your game's own flashlight key; the focus modifier is rebindable in the config.

## Requirements

- **Schedule I** (IL2CPP) with **MelonLoader 0.7.3+**. No other dependencies.

## Settings

`Enabled`, `FocusModifierKey` (LeftAlt), `DefaultFocus` (0.5), `DefaultIntensity` (7),
`MinIntensity`/`MaxIntensity` (1/20), `RangeWide`/`RangeNarrow` (8/34), `AngleWide`/`AngleNarrow` (66/16),
`ColorHex` (#E6F2FF), `CastShadows`, plus fine focus-feel tuning. In `UserData/MelonPreferences.cfg`
under `[TightBeam]`.

## License

MIT. See the included LICENSE.md.
