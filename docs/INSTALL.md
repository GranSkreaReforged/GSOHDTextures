# Installing GSO HD Textures

There are two ways to install, and both end with the same files in your game folder:

| | [From a release](#from-a-release) | [From a build](#from-a-build) |
|---|---|---|
| For | players | developers, or anyone who wants to build it themselves |
| You need | the game | the game, the .NET SDK, PowerShell |
| You get | the plugin: HD texture replacer and interface scaling | the same, built from source and reviewable before installing |
| HD textures | build a pack yourself ([texture pack](#texture-pack)) | same |

Either way you need **your own copy of Gran Skrea Online** (Steam). Nothing here contains or redistributes game files. The HD textures are made from *your* install, on your machine.

**Finding the game folder:** in Steam, right-click Gran Skrea Online → Manage → Browse local files. It's the folder that contains `GSO.exe`, called `<game>` below.

---

## From a release

### 1. Download

From the releases page, pick **one** zip:

| Zip | Use it when |
|---|---|
| `GSOHDTextures-<version>-with-BepInEx.zip` | BepInEx isn't installed yet (no `BepInEx` folder in `<game>`) |
| `GSOHDTextures-<version>.zip` | BepInEx 5 (x64) is already installed, e.g. by GSO Offline Server |

Optional: check the download against `SHA256SUMS.txt` from the same release. In PowerShell:

```powershell
Get-FileHash .\GSOHDTextures-<version>.zip -Algorithm SHA256   # must match the line in SHA256SUMS.txt
```

### 2. Extract into the game folder

Close the game, then extract the zip **into `<game>`**, merging folders and replacing files if asked. Both zips are laid out like the game folder, so the plugin lands in `<game>\BepInEx\plugins\GSOHDTextures\`.

```
<game>\
  GSO.exe
  winhttp.dll, doorstop_config.ini   (BepInEx loader; only in the -with-BepInEx zip)
  BepInEx\
    core\...                         (BepInEx itself; only in the -with-BepInEx zip)
    plugins\GSOHDTextures\
      GSOHDTextures.dll
      README.md, CHANGELOG.md, LICENSE, INSTALL.md (this guide)
      textures\                      (put a texture pack here)
```

### 3. Start the game and check it loaded

Start the game as usual. The first start with BepInEx takes a little longer. Then:

- The interface (hotbar, chat, windows) is bigger on screens taller than 1080 pixels. Adjust it with **Ctrl + =** / **Ctrl + -** (**Ctrl + 0** resets), or under **Main menu → Video options → Interface scale**.
- `<game>\BepInEx\LogOutput.log` contains `GSO HD Textures <version> loaded`.
- Settings are now in `<game>\BepInEx\config\gso.hdtextures.cfg` (see the README for each one).

The plugin does nothing to textures until you add a pack (next section).

---

## From a build

### 1. Prerequisites

- Windows with your own Gran Skrea Online install.
- The [.NET SDK](https://dotnet.microsoft.com/download) 9.0.200 or newer.
- PowerShell: Windows PowerShell 5.1 is enough to build. Releases need PowerShell 7.
- Git, to get the source.

You do **not** need BepInEx in the game to build. The build downloads the pinned BepInEx 5.4.23.5 (checksum-verified) into the repo's `.cache\` and compiles against that.

### 2. Build

```powershell
git clone <repo-url> GSOHDTextures
cd GSOHDTextures
.\build.ps1                         # Debug build
.\build.ps1 -Configuration Release  # or a Release build
```

The game is found automatically in your Steam libraries (override with `-GameDir 'X:\...\Gran Skrea Online'`). The build only **reads** the game's DLLs to compile against. It never copies them into the output and never writes to the game.

### 3. Review the output

Everything the build produces is in `artifacts\build\<Configuration>\`, laid out exactly like the game folder:

```
artifacts\build\Debug\
  INSTALL.txt                                  (these steps, short version)
  BepInEx\plugins\GSOHDTextures\GSOHDTextures.dll
```

That DLL is the only file that goes into the game.

### 4. Install

Close the game first. Pick one of these:

**By hand**
1. If `<game>` has no `BepInEx` folder, install BepInEx 5.4.23.5 x64: download `BepInEx_win_x64_5.4.23.5.zip` from [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) and extract it into `<game>`.
2. Copy the `BepInEx` folder from `artifacts\build\<Configuration>\` into `<game>`, merging.

**With the script**
```powershell
.\build.ps1 -InstallBepInEx -Deploy   # first time: installs BepInEx into the game, then copies the plugin
.\build.ps1 -Deploy                   # afterwards: build and copy
```

`-Deploy` copies exactly the files in `artifacts\build\<Configuration>\BepInEx\` and deletes nothing. `-InstallBepInEx` also writes `steam_appid.txt` (so `GSO.exe` can be started without Steam).

### 5. Check it loaded

As for a release: start the game and look for `GSO HD Textures <version> loaded` in `<game>\BepInEx\LogOutput.log`.

---

## Texture pack

The HD textures aren't in any download, because they are made from the game's own art. You build the pack from your install with the repo's tools, so you need a clone of the repo even if you installed the plugin from a release.

Extra requirements: Python 3.10+, a Vulkan-capable GPU, and about 36 GB of free disk space for the working files (`work\`), plus 10 GB for the finished pack in the game folder. Packing uses a lot of RAM: `-Jobs 3` (below) is safe on a 32 GB machine, so use fewer jobs with less. On an RX 7900 XTX the whole pack took about 75 minutes. The Real-ESRGAN upscaler is downloaded and checksum-verified on first use.

```powershell
.\tools\textures\dump.ps1           # 1. export the game's textures to work\dump
.\tools\textures\upscale.ps1        # 2. AI-upscale them into work\upscaled (resumable)
.\tools\textures\pack.ps1 -Jobs 3   # 3. build the finished pack in work\pack
```

Then install it:

- **By hand:** copy the `*.dds` files from `work\pack\` into `<game>\BepInEx\plugins\GSOHDTextures\textures\`.
- **With the script:** `.\tools\textures\pack.ps1 -Jobs 3 -Deploy` does the copy and removes files that are no longer in the pack.

Run the steps one at a time; the upscale and the pack each need a lot of GPU or RAM. The log then shows `<N> replacement textures indexed.` at start-up and `Loaded <N> textures in <ms> ms` as you play. The textures need DirectX 11, which is the game's default.

Keep the pack to yourself: it is derived from the game's art.

---

## Updating

- **Release:** extract the new plugin-only zip over the old one. Your settings and texture pack stay.
- **Build:** `git pull`, then build and install again (`.\build.ps1 -Deploy`). Rebuild the texture pack only if the changelog says the pipeline changed; `pack.ps1` only redoes what changed.

## Uninstalling

- **Just this mod:** delete `<game>\BepInEx\plugins\GSOHDTextures\` (this includes the texture pack) and, if you like, `<game>\BepInEx\config\gso.hdtextures.cfg`.
- **BepInEx as well** (only if no other mod needs it, e.g. GSO Offline Server): also delete `winhttp.dll`, `doorstop_config.ini` and the `BepInEx` folder from `<game>`.

The game itself is never modified, so after this it is back to stock.

## Troubleshooting

| Symptom | Check |
|---|---|
| No `LogOutput.log`, nothing changes | BepInEx isn't installed or isn't the x64 build. `winhttp.dll` must sit next to `GSO.exe`. |
| Log says `.dds textures are ignored` | The game is running under DirectX 9. Use DirectX 11 (the default; remove any `-force-d3d9` launch option). |
| Interface too big or too small | Ctrl + 0 resets it. `UI.Enabled = false` in the config turns scaling off. |
| `Couldn't write ... Is the game running?` from `-Deploy` | Close the game; it locks the plugin DLL. |
| Build: `Could not find Gran Skrea Online` | Pass `-GameDir 'X:\...\Gran Skrea Online'`, or set the `GSO_GAME_DIR` environment variable. |
