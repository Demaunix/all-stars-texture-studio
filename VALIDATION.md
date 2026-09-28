# Validation for 0.1.0

This is the first public preview. The editor and its generated archive plans
have been tested locally on Windows; a new in-game visual test of this editor's
exported recolor is still outstanding. Passing the offline checks does not prove
that every texture choice or combination of mods looks correct in a race.

## Completed checks

- 2,288 core checks passed with a local game installation. The catalogue found
  96 character, effect, selection and course resources. All 1,087 DDS textures
  in Sonic, Shibuya Downtown and Rokkaku Hill decoded successfully.
- Synthetic DXT1, DXT3, DXT5 and 32-bit textures exercised odd dimensions,
  mipmaps, transparent pixels, hue changes, selection ranges, malformed headers,
  archive checksums and unchanged-byte preservation. Alpha was retained through
  recoloring. Unchanged compressed blocks remain byte-identical.
- Fifteen DDS samples were compared with an independent Pillow decoder. Alpha
  matched exactly; RGB differences were at most 8/255 from RGB565 rounding.
- 29 export/install checks passed: project round trips; rejecting mismatched
  source resources; readable ZIP members; no full-archive BIN cache; reversible
  install; refusal to remove modified files; preserving other mods' loaders;
  conflicting member, whole-archive and loose-resource checks; unsafe paths and
  output inside the game directory refused.
- The native runtime passed valid-host initialization and rollback checks and
  rejected invalid API versions, missing interfaces and wrong initialization
  state. Both native loader components built from the included source.
- The independent native archive planner accepted the synthetic fixture and
  the real Sonic recolor exported through the desktop editor.
- Desktop interaction verified opening the game, selecting an asset and named
  texture, original/edited previews, hue adjustment, keeping an edit, saving a
  project, and exporting its mod ZIP.

These checks read the installed game's archives. They did not launch the game,
install the test recolor into the real game, or change its saves/settings.
Installer tests used disposable fixtures. Game-derived test data is not included
in this repository or in the downloadable executable.

## Limits

- Visual results still need checking in the intended character/course in game.
- Sky swaps do not adjust lighting, fog, geometry, UV mapping or animation.
- Compression is lossy and different surfaces may require separate edits.
- Installation is limited to the checked original Windows Steam executable and
  recognized loader combinations. Other builds may still be browsed/exported.
- There is no real-time game injection or 3D preview in this release.
- The executable is unsigned; Windows may show an unfamiliar-app warning.

Run the included synthetic tests on a clean Windows build, and optionally run
the real-data tests against your own installation as described in the README.
