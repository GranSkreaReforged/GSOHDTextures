# GSO HD Textures

A BepInEx plugin that swaps **Gran Skrea Online**'s textures for higher-resolution versions at runtime, plus a pipeline that builds those versions from your own copy of the game with AI upscaling (Real-ESRGAN). No game files are modified. Remove the plugin and the game is back to stock.

It works with or without GSO Offline Server.

## About this project

This is a non-commercial passion project for a game that is no longer sold. It is not affiliated with or endorsed by the original developers or publisher. "Gran Skrea Online" and all game assets belong to their respective owners.

**This repository and its release zips contain no game textures.** The texture pack is derived from proprietary art, so you build it locally from your own install (below). `work/` is git-ignored for that reason.

## How it works

```
game assets ──dump.ps1──▶ work/dump/<key>.png ──upscale.ps1──▶ work/upscaled/ ──pack.ps1──▶ work/pack/ ──-Deploy──▶ <game>/BepInEx/plugins/GSOHDTextures/textures/
                                                               work/overrides/ (hand-made, wins) ┘
```

- **Key:** every texture is identified as `<name>__<width>x<height>` (the original size), e.g. `Roof_A_A__512x512.png`. Of the game's 4,083 textures, all but 31 have unique keys, and those 31 are duplicates.
- **Plugin:** at scene load and every few seconds, it scans all materials (and terrain splat and detail textures). When a texture has a matching file, it loads that file once (as an RGBA PNG, GPU-compressed to DXT, with mipmaps) and points the material at it.
- **Upscaling:** Real-ESRGAN x4 runs on a wrap-padded copy of each texture so tiling edges stay seamless. The result is cropped and Lanczos-resized to `-Scale` (default 2×, max 4096). Its brightness and colour are corrected back to the original's (the AI only adds detail), and alpha is restored from the original. Normal, height, metallic, gloss, AO and mask maps get a plain resize, because AI detail breaks lighting.

## Building a texture pack

Requirements: Windows, Python 3.10+, a Vulkan-capable GPU, and the plugin installed (next section). The Python venv and the pinned, hash-checked Real-ESRGAN build are set up automatically on first use.

```powershell
.\tools\textures\dump.ps1                 # 1. export ~3,900 textures to work\dump (a few minutes)
.\tools\textures\upscale.ps1              # 2. AI upscale into work\upscaled (hours for everything; resumable)
.\tools\textures\pack.ps1 -Deploy         # 3. build work\pack and copy it into the game
```

Working on a subset first is much faster:

```powershell
.\tools\textures\dump.ps1 -Only Roof_A                           # just textures whose name contains Roof_A
# or: play with RecordSeenTextures = true, then use the list of textures you actually saw
.\tools\textures\upscale.ps1 -OnlyList '<game>\BepInEx\GSOHDTextures-seen.txt'
.\tools\textures\pack.ps1   -OnlyList '<game>\BepInEx\GSOHDTextures-seen.txt' -Deploy
```

To hand-fix a texture, put your version in `work\overrides\<key>.png` (any size, same aspect ratio) and re-run `pack.ps1 -Deploy`. Press **F9** in-game to reload the pack without restarting.

Useful options: `upscale.ps1 -Model realesrgan-x4plus-anime` (for flat, stylised art), `-Tile 256` (low VRAM), `-Gpu 1`; `pack.ps1 -Scale 4`, `-MaxSize 2048`.

## Installing the plugin

```powershell
.\build.ps1                    # Debug build, deployed to <game>\BepInEx\plugins\GSOHDTextures
.\build.ps1 -InstallBepInEx    # first time on a machine without BepInEx
```

Settings are in `BepInEx/config/gso.hdtextures.cfg`:

| Setting | Default | |
|---|---|---|
| `General.Enabled` | true | |
| `General.TextureFolder` | `textures` | relative to the plugin folder, or absolute |
| `General.CompressTextures` | true | DXT on the GPU: about ¼ of the VRAM, slower first load |
| `General.ScanInterval` | 2 | seconds between scans for new materials; 0 = scan on scene load only |
| `Advanced.ExtraTextureProperties` | | extra shader properties to check; suffix `!` for linear data |
| `Debug.ReloadKey` | F9 | |
| `Debug.LogReplacements` | false | |
| `Debug.RecordSeenTextures` | false | writes `BepInEx/GSOHDTextures-seen.txt` |

The game folder is found automatically in any Steam library. Override it with `-GameDir`, `GSO_GAME_DIR` or `-p:GameDir=`.

## Releasing

`.\release.ps1 -Version x.y.z` (PowerShell 7) packages the plugin only, commits, and tags. It never pushes. Work happens on `dev`; `main` is for releases.

## Known limitations / roadmap

- UI sprites (UGUI `Image`) are not replaced yet. A sprite's rect is in pixels, so it needs rebuilding at the new scale.
- Only the shader properties in `TextureReplacer.BuiltInProperties` are checked (Unity 2017.4 can't list them at runtime). The list was mined from the game's materials with `tools/textures/props.py`; add others with `ExtraTextureProperties`.
- PNG decoding is synchronous, so a large pack can hitch on scene load. Pre-compressed DDS (texconv BC1/BC3/BC7) with `LoadRawTextureData` would fix that.
- Lightmaps, reflection probes and font atlases are deliberately left alone.

## License

GPL-3.0-or-later for the code in this repository (see `LICENSE`).
