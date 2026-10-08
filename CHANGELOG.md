# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
`release.ps1` turns the Unreleased heading into a version heading.

## [Unreleased]

### Added
- Interface scaling for high-resolution screens. The classic HUD, chat, windows, login and character screens grow with the screen height (1080p = 100%), with your own preference on top: an **Interface scale** slider in Main menu → Video options, Ctrl + = / - / 0, or `UI.Scale` in the config. Text stays sharp, and the minimap and other canvas UI follow the same setting. Mouse hover, clicks, dragging and nameplates stay aligned.
- Interface icons and images are drawn from the texture pack, so the larger interface stays sharp.
- DevBridge commands `hdui` (screen, UI options, canvases), `setuiscale` and `openwindow`.

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
