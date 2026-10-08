# GSO HD Textures

A BepInEx plugin that swaps **Gran Skrea Online**'s textures for higher-resolution versions at runtime, plus a pipeline that builds those versions from your own copy of the game with AI upscaling (Real-ESRGAN). No game files are modified. Remove the plugin and the game is back to stock.

It works with or without GSO Offline Server.

**Installing:** see [docs/INSTALL.md](docs/INSTALL.md), with step-by-step guides for installing from a release zip and from your own build, plus building the texture pack.

## About this project

This is a non-commercial passion project for a game that is no longer sold. It is not affiliated with or endorsed by the original developers or publisher. "Gran Skrea Online" and all game assets belong to their respective owners.

**This repository and its release zips contain no game textures.** The texture pack is derived from proprietary art, so you build it locally from your own install (below). `work/` is git-ignored for that reason.

## How it works

```
game assets ──dump.ps1──▶ work/dump/<key>.png ──upscale.ps1──▶ work/upscaled/ ──pack.ps1──▶ work/pack/ ──-Deploy──▶ <game>/BepInEx/plugins/GSOHDTextures/textures/
                                                               work/overrides/ (hand-made, wins) ┘
```

- **Key:** every texture is identified as `<name>__<width>x<height>` (the original size), e.g. `Roof_A_A__512x512.png`. A few keys are shared by different images (e.g. four different `Material.001_Base_Color`) or differ only in case. The plugin can't tell those apart, so `dump.ps1` marks them and they keep their original look.
- **Plugin:** at scene load and every few seconds, it scans all materials (and terrain splat and detail textures). When a texture has a matching file, it loads that file once and points the material at it. Packs are BC7 `.dds` files with mipmaps, uploaded to the GPU as-is. A hand-made `.png` also works; it is decoded and compressed in-game, which is slower.
- **Upscaling:** Real-ESRGAN x4 runs on a wrap-padded copy of each texture so tiling edges stay seamless. The result is cropped and Lanczos-resized to `-Scale` (default 2×, max 4096). Its brightness and colour are corrected back to the original's (the AI only adds detail), and alpha is restored from the original. Normal, height, metallic, gloss, AO and mask maps get a plain resize, because AI detail breaks lighting.

## Interface scaling

The classic interface (HUD, hotbar, chat, every window, the login and character screens) is drawn at 1:1 pixels, so it gets tiny on 1440p, ultrawide and 4K screens. The plugin scales it:

- **Automatic:** the interface looks as it was designed on a 1080p screen: 133% at 1440p, 200% at 4K.
- **Your preference on top:** in-game under **Main menu → Video options → Interface scale** (50% to 200%, applied when you let go of the slider), or with **Ctrl + =** and **Ctrl + -** in 5% steps and **Ctrl + 0** to reset. The new size shows briefly on screen and is saved to `UI.Scale`.
- The newer canvas parts (minimap, zone name) follow the same preference.
- Icons and other interface images are drawn from the texture pack when it has them, so they stay sharp. Text is re-rendered at the new size, not magnified.

Turn it off with `UI.Enabled = false`, or set `UI.AutoScale = false` to use native pixels times `UI.Scale`. The game's own `/scalegui` console mode (a stretch to a fixed size) takes precedence when it's on.

## Building a texture pack

Requirements: Windows, Python 3.10+, a Vulkan-capable GPU, and the plugin installed (next section). The Python venv and the pinned, hash-checked Real-ESRGAN build are set up automatically on first use.

```powershell
.\tools\textures\dump.ps1                 # 1. export ~3,800 textures to work\dump (a few minutes)
.\tools\textures\upscale.ps1              # 2. AI upscale into work\upscaled (hours for everything; resumable)
.\tools\textures\pack.ps1                 # 3. build the finished pack in work\pack (add -Deploy to copy it into the game)
```

`work\pack\` holds the finished pack: one `<name>__<width>x<height>.dds` per texture. To install it by hand, copy those files into `<game>\BepInEx\plugins\GSOHDTextures\textures\` (create the folder if needed). `-Deploy` does the same and also removes files there that are no longer in the pack. The pack is derived from the game's art, so build it from your own copy and keep it to yourself.

Working on a subset first is much faster:

```powershell
.\tools\textures\dump.ps1 -Only Roof_A                           # just textures whose name contains Roof_A
# or: play with RecordSeenTextures = true, then use the list of textures you actually saw
.\tools\textures\upscale.ps1 -OnlyList '<game>\BepInEx\GSOHDTextures-seen.txt'
.\tools\textures\pack.ps1   -OnlyList '<game>\BepInEx\GSOHDTextures-seen.txt' -Deploy
```

To hand-fix a texture, put your version in `work\overrides\<key>.png` (any size, same aspect ratio) and re-run `pack.ps1 -Deploy`. To try one quickly, drop `<key>.png` into the game's `textures` folder (a PNG beats a DDS with the same key) and press **F9** in-game to reload the pack without restarting.

Useful options: `upscale.ps1 -Model realesrgan-x4plus-anime` (for flat, stylised art), `-Tile 256` (low VRAM), `-Gpu 1`; `pack.ps1 -Scale 4`, `-MaxSize 2048` (less VRAM and disk), `-Jobs 6` (parallel BC7 encoders; each can need over 1 GB of RAM).

## Building and installing the plugin

You need your own copy of Gran Skrea Online. The build reads the game's DLLs to compile against; it never copies them into the output and never writes to the game.

```powershell
.\build.ps1                          # Debug build into artifacts\build\Debug; the game is untouched
.\build.ps1 -Deploy                  # ...and copy it into the game (close the game first)
.\build.ps1 -InstallBepInEx -Deploy  # first time, as a convenience: also put BepInEx into the game
.\build.ps1 -Configuration Release
```

Every build lands in `artifacts\build\<Configuration>\`, laid out exactly like the game folder, with an `INSTALL.txt`:

```
artifacts\build\Debug\
  INSTALL.txt
  BepInEx\plugins\GSOHDTextures\GSOHDTextures.dll
```

Review it there, then install it either way:
- **By hand:** install [BepInEx 5.4.23.5 (x64)](https://github.com/BepInEx/BepInEx/releases) into the game folder (the one with `GSO.exe`), then copy this `BepInEx` folder into it, merging. Put a texture pack in `<game>\BepInEx\plugins\GSOHDTextures\textures\` (see above). Interface scaling works without a pack.
- **With the scripts:** `build.ps1 -Deploy` copies the plugin, `-InstallBepInEx` installs BepInEx, and `pack.ps1 -Deploy` copies the pack.

BepInEx's DLLs for compiling come from the same pinned, hash-checked BepInEx zip, unpacked into `.cache\`, so building doesn't need BepInEx in the game. Release zips (`release.ps1`) use the same layout.

Settings are in `BepInEx/config/gso.hdtextures.cfg`:

| Setting | Default | |
|---|---|---|
| `General.Enabled` | true | |
| `General.TextureFolder` | `textures` | relative to the plugin folder, or absolute |
| `General.CompressTextures` | true | `.png` replacements only: compress to DXT after loading, about ¼ of the VRAM but slower |
| `General.ScanInterval` | 2 | seconds between scans for new materials; 0 = scan on scene load only |
| `Advanced.ExtraTextureProperties` | | extra shader properties to check; suffix `!` for linear data |
| `Debug.ReloadKey` | F9 | |
| `Debug.LogReplacements` | false | |
| `Debug.RecordSeenTextures` | false | writes `BepInEx/GSOHDTextures-seen.txt` |
| `UI.Enabled` | true | scale the interface (restart to switch completely) |
| `UI.AutoScale` | true | grow with the screen height (1080 px = 100%) |
| `UI.Scale` | 1 | your preference on top, 0.5 to 3; also Video options → Interface scale, or Ctrl + = / - / 0 |
| `UI.HdTextures` | true | draw interface images from the texture pack |
| `UI.ScaleUpKey` / `ScaleDownKey` / `ScaleResetKey` | Ctrl + = / - / 0 | |

The game folder is found automatically in any Steam library. Override it with `-GameDir`, `GSO_GAME_DIR` or `-p:GameDir=`.

## Releasing

`.\release.ps1 -Version x.y.z` (PowerShell 7) packages the plugin only, commits, and tags. It never pushes. Work happens on `dev`; `main` is for releases.

## Known limitations / roadmap

- Canvas sprites (UGUI `Image`, e.g. the minimap frame) are not replaced with HD versions yet. A sprite's rect is in pixels, so it needs rebuilding at the new scale. Window frames drawn from the GUI skin are scaled but not replaced either; they hold up well up to about 2x.
- At large scales on ultrawide screens the minimap (which also grows with screen width) can touch the HUD buttons next to it.
- Only the shader properties in `TextureReplacer.BuiltInProperties` are checked (Unity 2017.4 can't list them at runtime). The list was mined from the game's materials with `tools/textures/props.py`; add others with `ExtraTextureProperties`.
- Textures load on the main thread. DDS loading is fast, but entering a new area with hundreds of new textures still takes a moment longer than stock.
- BC7 needs DirectX 11. Under DirectX 9 the plugin ignores `.dds` files and says so in the log.
- Lightmaps, reflection probes and font atlases are deliberately left alone.

## License

GPL-3.0-or-later for the code in this repository (see `LICENSE`).
