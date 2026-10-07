# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
`release.ps1` turns the Unreleased heading into a version heading.

## [Unreleased]

### Added
- Runtime texture replacer plugin (BepInEx 5, net35):
  - Replacements are matched by `<name>__<width>x<height>`, loaded lazily from `textures/`, GPU-compressed and mipmapped.
  - Covers material texture properties and terrain splat/detail textures, including materials created after scene load. The property list covers every texture property the game's materials use (Standard, SpeedTree, CW3 blend, AQUAS water, skybox and more), apart from lookup textures.
  - F9 reloads the pack in-game. Optional logging of replacements and a list of the textures the game actually uses.
- Texture pipeline in `tools/textures`:
  - `dump.ps1` exports textures with UnityPy.
  - `upscale.ps1` runs a pinned, hash-checked Real-ESRGAN with wrap padding for seamless tiling.
  - `pack.ps1` crops and resizes, restores alpha, applies hand-made overrides, and deploys.
  - AI output is tone-matched to the original, so upscaled textures keep their brightness and colour (Real-ESRGAN alone darkened some terrain by up to 45%).
  - Normal, height, metallic, gloss, specular, AO, roughness and mask maps get a plain resize instead of the AI.
  - `props.py` lists the shader texture properties the game's materials use, to keep the plugin's property list complete.
- Build, release and project tooling shared with GSO Offline Server.
