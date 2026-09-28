# ShieldShare

Add your own shield designs to Valheim, and share them with friends as a single .zip file.

## Installation
Install with a mod manager (r2modman / Thunderstore), or copy `ShieldShare.dll` into `BepInEx\plugins\ShieldShare\`.
Requires BepInEx and Jötunn. Everyone on a server needs the mod and the same shield packs.

## Adding shields
Drop shield pack `.zip` files (or folders) into `Documents\Valheim Custom Shields\` and start the game.
The new shields are crafted at the workbench. Remove the zip to remove the shield.

## Making shields
1. Create a folder in `Documents\Valheim Custom Shields\`. Its name is the shield's name.
2. Put your artwork in it as `Pattern1.png` (plus `Pattern2.png`, `Pattern3.png`... for extra styles, up to 16).
3. Start the game.

Your artwork is painted onto the shield's face only; the rim, strap and back stay plain wood.
Icons are generated for you, and `shield.json` (name, recipe, crafting station...) is optional.
The game writes a full guide, `HOW TO MAKE A SHIELD.txt`, and a face-outline template to paint over
(`_Templates\ShieldWood - pattern guide.png`) into that folder the first time it runs.

## Changelog
- 0.0.2 - Patterns are baked into the shield's own texture layout, so they no longer bleed onto the
  rim, strap and back. Icons generated automatically. shield.json optional. Zips can be laid out any way;
  plain folders work too. Author guide and templates written to the drop folder.
- 0.0.1 - First test build.
