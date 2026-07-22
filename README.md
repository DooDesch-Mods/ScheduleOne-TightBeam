# TightBeam - A Proper Flashlight

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/tightbeam](https://support.doodesch.de/tightbeam).

> A proper handheld flashlight for Schedule I. It upgrades the game's own flashlight - toggle it with your
> flashlight key, then hold **ALT + mouse wheel** to dial the beam from a wide near-flood to a tight,
> far-reaching throw. No map-wide floodlight, no blinding glare - just a light that feels like a flashlight.

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![Status](https://img.shields.io/badge/status-working-brightgreen)

## Features

- **A beam you can aim and shape.** Hold **ALT + mouse wheel** to slide a single Focus axis: the wide
  end is a short, broad flood right in front of you; the narrow end is a tight cone that throws far.
  Range and cone angle both follow the focus, so you get real control without ever washing out the map.
- **Velocity-sensitive focus.** Scroll slowly for fine steps; flick fast and the beam races to the
  nearest extreme. The displayed beam eases in smoothly instead of popping.
- **Fixed, sane brightness.** The beam holds a sensible brightness within a hard floor and ceiling - no
  accidental whiteout. Brightness is not a player control; mods can drive it through the API (e.g. dim it in a
  dark room).
- **Shadows and a cool-white tint by default**, so the beam is blocked by walls and reads against the
  game's warm lighting. Both are configurable (turn shadows off on low-end machines).
- **In perfect sync with the game.** TightBeam drives off the game's own flashlight state, so its on/off
  never falls out of step - your flashlight key toggles it, and it swaps the vanilla point light for its
  own clean cone.
- **Light-touch and update-resilient.** A spotlight that follows your camera - no gameplay rewired, no
  networking touched. It reads the game's flashlight state locally, and a tiny guard keeps ALT+scroll from
  cycling your hotbar.
- **Cross-mod API.** Other mods can drive the beam - dim it in a dark room, flicker it near power,
  blink it as an alert - through a drop-in `Beam` shim. See [For modders](#for-modders).

## Controls

| Input | Action |
|---|---|
| Flashlight key (your game bind) | Toggle the flashlight on/off |
| `ALT` + mouse wheel | Focus: wide near-flood to tight far-throw |

On/off uses your game's own flashlight key; the focus modifier is rebindable in TightBeam's config.

## Requirements

| Component | Version / Source |
|---|---|
| Schedule I | IL2CPP (current Steam public build) |
| MelonLoader | `0.7.3+` |

No other dependencies - just MelonLoader. TightBeam reads the game's own flashlight state and renders its own light; no S1API, no networking.

## Installation

### Recommended: a Thunderstore mod manager

Install with r2modman / Gale from the Schedule I community; MelonLoader is pulled in automatically.

### Manual

1. Install **MelonLoader 0.7.3** for Schedule I.
2. Drop **`TightBeam.dll`** into your Schedule I `Mods/` folder.

## Configuration

Settings live in `UserData/MelonPreferences.cfg` under `[TightBeam]`. Highlights:

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Master on/off for the whole mod. |
| `FocusModifierKey` | `LeftAlt` | Hold this and scroll to change focus. |
| `DefaultFocus` | `0.5` | Starting focus. `1` = wide flood, `0` = tight throw. |
| `DefaultIntensity` | `7` | Base brightness (clamped to Min/Max). |
| `MinIntensity` / `MaxIntensity` | `1` / `20` | Hard floor / ceiling for brightness. |
| `RangeWide` / `RangeNarrow` | `8` / `34` | Beam range (m) at each focus extreme. |
| `AngleWide` / `AngleNarrow` | `66` / `16` | Cone angle (deg) at each focus extreme. |
| `ColorHex` | `#E6F2FF` | Beam colour (cool white). |
| `CastShadows` | `true` | Soft shadows (turn off on low-end machines). |

There are more fine-tuning knobs for the focus feel (sensitivity, easing, flick threshold) in the same
section if you want to dial it in. Editing the file takes effect on the next launch.

## For modders

TightBeam exposes a small cross-mod control API so your mod can drive the player's flashlight - on/off,
brightness, range, colour, plus Blink/Flicker/Pulse and scoped per-field overrides. Every call is a safe
no-op when TightBeam is not installed, so you can ship it with no hard dependency.

**Full reference: the [TightBeam Wiki](https://github.com/DooDesch-Mods/ScheduleOne-TightBeam/wiki)** - see the
[Modder API](https://github.com/DooDesch-Mods/ScheduleOne-TightBeam/wiki/Modder-API) page.

Copy the [Beam shim](https://github.com/DooDesch-Mods/ScheduleOne-TightBeam/wiki/The-Beam-Shim) (`TightBeam.cs`)
into your project (or reference `TightBeam.Api.dll`) and call it:

```csharp
using TightBeam.Api;

Beam.Blink(3);                                 // blink as an alert
using (var ov = Beam.BeginOverride("MyMod"))   // scoped, per-field override
{
    ov.SetIntensity(1f).SetSpotAngle(28f);     // dim + narrow while in a zone
}                                              // released -> back to the player's own settings
```

## Credits

- **DooDesch** - mod author.

## License

Provided as-is under the [MIT License](LICENSE.md).
