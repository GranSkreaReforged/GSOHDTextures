# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
`release.ps1` turns the Unreleased heading into a version heading.

## [Unreleased]

## [1.1.0] - 2026-10-09
### Added
- Weather and day/night. Clear skies, clouds, overcast, rain (with its sound) and fog now come and go every few minutes, blending in over a minute, and days run continuously, a full cycle every 15 minutes. It uses the game's own clouds, rain, fog and time-of-day sky, which only the server used to drive. It is mostly fair, fog favours early mornings, and it never snows. The sun dims and shadows soften under heavy cloud. With enhanced lighting the picture follows the time and weather: warm at dawn and dusk, cooler at night, duller in rain. Tune it under `[Weather]`.
- Moonlit nights: a soft blue fill keeps characters and the ground readable at night, where the original left them as silhouettes (`Graphics.NightBrightness`, 0 = original).
- `/weather` chat command: shows the weather and when it changes next. `/weather clear|cloudy|overcast|rain|fog` sets one and keeps it, and `/weather auto` goes back to changing weather. Answered by this plugin, so it works with or without GSO Offline Server.
- DevBridge commands `weather` (clock, clouds, rain, fog and mood), `setweather <clear|partlycloudy|overcast|rain|fog> [now]` or `auto`, and `settime <0-2400>`.
- Ambient lights and particles. Street lamps glow and light up at dusk, unlit standing torches get fires, and fires, torches and lanterns flicker. Windows glow warmly at night in about 60% of buildings. Heavy rain brings lightning with distant bolts, flashes and synthesised thunder. Around you, fireflies drift on dry nights, dust and pollen float on fair days, mist lies over the sea at dawn and in fog, leaves fall from broadleaf trees, and smoke rises from roof tops. Each part can be switched off under `[Ambience]`.
- DevBridge commands `ambience`, `findnames`, `inspect` and `mattex` (what lights, particles, objects and materials a scene has), `ambstatus`, `amblamps`, `ambchimneys`, `ambwindow` and `ambviewchimney` (what the ambience module added, and views for screenshots), and `lightning`.

### Changed
- Branching: work happens on `feature/<area>/<name>` branches merged into `dev`, and each release is one merge of `dev` into `main` (README, "Branches and releasing"). Risky, large or core changes, and every release, go through a reviewed pull request.
- Dev builds and release builds: every `build.ps1` build is a dev build, versioned like `1.0.0-dev+<branch>.<commit>` and logged at startup with "(dev build)". Only `release.ps1` makes release builds, with the plain version.
- `release.ps1` runs on `dev`: its "Release vX" commit carries that version's CHANGELOG section, which becomes the `dev` -> `main` pull request.

## [1.0.0] - 2026-10-08
### Added
- Interface scaling for high-resolution screens. The classic HUD, chat, windows, login and character screens grow with the screen height (1080p = 100%), with your own preference on top: an **Interface scale** slider in Main menu → Video options, Ctrl + = / - / 0, or `UI.Scale` in the config. Text stays sharp, and the minimap and other canvas UI follow the same setting. Mouse hover, clicks, dragging and nameplates stay aligned.
- Interface icons and images are drawn from the texture pack, so the larger interface stays sharp.
- DevBridge commands `hdui` (screen, UI options, canvases), `setuiscale` and `openwindow`.
- Enhanced lighting (on by default, **F10** compares with the original): sky-coloured ambient light instead of flat grey, ambient occlusion, rebalanced exposure/contrast/saturation (caves keep their torch-lit exposure) and anisotropic filtering, all tunable under `[Graphics]`.
- DevBridge commands `frames` (frame-time statistics with hitches, garbage collections and allocation rate), `lighting` (sky, ambient, shadow and post-processing values), `gfx on|off`, `hdtex on|off` (original vs HD textures in the same session) and `hdterrain` (terrain layers).
- Before-and-after screenshots in the README.

### Fixed
- Glossy blue streaks on paths and cobblestones. Unity's terrain takes a layer's shininess from its colour texture's alpha whenever the texture format has one; the originals are DXT1 (no alpha, so the layer's own near-matte value applied), but HD textures are BC7, so the ground turned glossy and mirrored the sky. Terrain layers whose original has no alpha now get a DXT1 copy of the HD texture, made once at scene load.
- No more small stutter every 2 seconds. The periodic check for new materials did all ~2,900 materials in one frame (about 25 ms, frames up to 45 ms); it now runs a slice per frame, and textures that appear during play are read on a background thread into reused buffers and uploaded one per frame. Scene loads still load everything behind the loading screen.
- Grass shows again. Terrain grass textures are no longer replaced: Unity builds grass from them on the CPU, and the compressed HD versions turned it into coloured noise.

### Changed
- Builds no longer touch the game. `build.ps1` puts the plugin in `artifacts\build\<Configuration>\`, laid out like the game folder, with an `INSTALL.txt` saying where it goes; `-Deploy` (replacing the old default and `-NoDeploy`) copies it into the game. BepInEx's DLLs for compiling come from the pinned BepInEx zip, so building doesn't need BepInEx installed in the game. The README explains installing the plugin and a texture pack by hand.
- `docs/INSTALL.md`: installation guides from a release and from a build (with updating, uninstalling and troubleshooting), also shipped in the release zips.

## [0.1.0] - 2026-10-08
### Added
- Runtime texture replacer plugin (BepInEx 5, net35):
  - Replacements are matched by `<name>__<width>x<height>` and loaded lazily from `textures/`: pre-compressed BC7/DXT `.dds` files are uploaded as-is, and hand-made `.png` files are compressed after loading.
  - Covers material texture properties and terrain splat/detail textures, including materials created after scene load. The property list covers every texture property the game's materials use (Standard, SpeedTree, CW3 blend, AQUAS water, skybox and more), apart from lookup textures.
  - F9 reloads the pack in-game. Optional logging of replacements and a list of the textures the game actually uses.
  - `hdplayer` DevBridge command: what each texture property on the local player shows and which key it replaced.
- Texture pipeline in `tools/textures`:
  - `dump.ps1` exports textures with UnityPy. Keys shared by different images or differing only in case, and terrain blend maps, are left alone so nothing gets the wrong art.
  - `upscale.ps1` runs a pinned, hash-checked Real-ESRGAN with wrap padding for seamless tiling.
  - `pack.ps1` crops and resizes, restores alpha, applies hand-made overrides, writes BC7 `.dds` files with mipmaps in parallel, and deploys.
  - AI output is tone-matched to the original, so upscaled textures keep their brightness and colour (Real-ESRGAN alone darkened some terrain by up to 45%).
  - Normal, height, metallic, gloss, specular, AO, roughness and mask maps get a plain resize instead of the AI.
  - `props.py` lists the shader texture properties the game's materials use, to keep the plugin's property list complete.
- Build, release and project tooling shared with GSO Offline Server.
