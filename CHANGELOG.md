# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
`release.ps1` turns the Unreleased heading into a version heading.

## [Unreleased]

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
