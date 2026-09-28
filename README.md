# All-Stars Texture Studio

A portable Windows texture editor for **Sonic & SEGA All-Stars Racing (2010)**.
Browse the original game archives, recolor characters, replace images and swap
sky textures without manually unpacking the game.

## Download

Get **AllStarsTextureStudio.exe** from [Releases](https://github.com/Demaunix/all-stars-texture-studio/releases).
Run it anywhere outside the game folder. Windows 10/11 and .NET Framework 4.8
are recommended. No Python, unpacker or compiler is needed to use the release.

## Make your first texture mod

1. Choose **Open game** and select the game's executable. Steam's **Manage →
   Browse local files** shows its location.
2. Pick a character or course, then a texture. Search works in both lists.
3. Adjust hue, saturation or brightness. **Choose color** limits changes to a
   color range. The right preview shows the actual encoded result.
4. Click **Keep this edit**. Repeat for any other textures.
5. **Save project** keeps an editable `.textureproject` file. Save it outside
   the game folder.
6. Close the game and choose **Install project**. Launch normally through Steam.
   **Restore original look** removes the editor's installed texture mod.

**Export mod** creates a ZIP for sharing or manual installation. The editor
contains no game artwork; projects and exported mods contain the textures and
resource members you choose to edit from your own installed copy.

## Sky swaps and character colors

Use **Sky and cloud textures** to find likely sky images. For example, open
Rokkaku Hill, copy a sky texture, open Shibuya Downtown, select the corresponding
sky surface and use **Paste texture**. Check the preview and keep the edit.
Images with different dimensions are fitted to the destination automatically.

A sky texture swap changes that surface. It does **not** automatically change
the course's lighting, fog, shadows, sky geometry or animation. Tracks may use
several sky textures with different UV layouts; match the appropriate surfaces.

Character bodies, vehicles, effects and character-select previews are separate
resources. Edit the relevant textures in each if you want a consistent skin.
This release provides a 2D texture preview, not a live 3D scene preview.

## Import and export

- Import PNG, JPEG, BMP or DDS. PNG is convenient for external painting.
- Export PNG for editing in your preferred image editor.
- Copy and paste textures between characters and courses.
- Original DDS dimensions, compression format and mip counts are preserved.
- Recoloring preserves alpha. Imported images supply their own alpha, subject
  to the target format (DXT1 supports only a transparency threshold).
- Supported texture formats: DXT1, DXT3, DXT5 and common 32-bit RGB/RGBA layouts.
- Recompression is lossy. Review the encoded preview before keeping an edit.

## Installation and other mods

Original `.xpac` files, game saves and settings are never rewritten. A small
mod package supplies changed archive members through the included ChaoGarage
loader. No full-archive `.bin` cache is generated.

Installation supports the checked original Windows Steam executable. Browsing
and ZIP export do not require executable patching. This is not a tool for
Transformed or the console editions.

The installer preserves recognized existing loaders and refuses unknown ones.
Independent archive members can coexist with other compatible mods. Two mods
editing the same character/course resource require a combined plan; installation
stops with an explanation. Loose extracted copies of edited resources must be
moved to a backup first, because they can override archive edits.

To update a project already installed by this editor, use **Restore original
look**, then **Install project** again. Restore only removes recorded files
whose contents still match; it preserves added files and loaders needed by
other mods. Manually installed ZIPs have no editor receipt: remove their specific
mod folder manually after closing the game.

## Build and test

Build requirements: Windows, .NET Framework 4.x build tools and Visual Studio
C++ Build Tools (x86 desktop workload).

```powershell
./native/build-loaders.cmd
./native/build.cmd
./build.ps1
./build.ps1 -Tests
./bin/CoreTests.exe ./test-output/core
```

The release embeds its three runtime DLLs inside the EXE. Native loader source
is included under `native/`; the loaders are only installed when requested.

Optional real-data checks read three representative resources directly from a
local game installation. They never launch the game or modify its installation:

```powershell
./bin/CoreTests.exe ./test-output/real 'C:/Games/All-Stars'
```

The synthetic tests cover DDS/mipmap/alpha handling, archive checksums, project
round trips, mod ZIPs, conflict refusal, installation and removal. Native
`compose.exe` independently verifies a generated mod's archive layout and hash.
See [VALIDATION.md](VALIDATION.md) for the release's verified scope.

## License

MIT. This is an unofficial community tool, not affiliated with SEGA, Sumo
Digital or Microsoft. Game names and artwork belong to their respective owners.
No original game assets are distributed with the tool.
