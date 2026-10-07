# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
`release.ps1` turns the Unreleased heading into a version heading.

## [Unreleased]

### Added
- Runtime texture replacer plugin (BepInEx 5, net35):
  - Replacements are matched by `<name>__<width>x<height>`, loaded lazily from `textures/`, GPU-compressed and mipmapped.
  - Covers material texture properties and terrain splat/detail textures, including materials created after scene load.
  - F9 reloads the pack in-game. Optional logging of replacements and a list of the textures the game actually uses.
- Texture pipeline in `tools/textures`:
  - `dump.ps1` exports textures with UnityPy.
  - `upscale.ps1` runs a pinned, hash-checked Real-ESRGAN with wrap padding for seamless tiling.
  - `pack.ps1` crops and resizes, restores alpha, applies hand-made overrides, and deploys.
- Build, release and project tooling shared with GSO Offline Server.
