# ShieldShare

Add your own shield designs to Valheim, and share them with friends as a single .zip file.

## Installation
Install with a mod manager (r2modman / Thunderstore), or copy `ShieldShare.dll` into `BepInEx\plugins\ShieldShare\`.
Requires BepInEx and Jötunn. Everyone on a server needs the mod and the same shield packs.

## Adding shields
Drop shield pack `.zip` files into `Documents\Valheim Custom Shields\` (don't unzip them) and start the game.
The new shields are crafted at the workbench. Remove the zip to remove its shields.

## Making shields
Each shield is a folder inside the zip, with its files inside that folder:

```
MyShields.zip
  BlackDog/           <- shield ID, must be unique
    shield.json       <- required (can be just {} - every field has a default)
    Pattern1.png      <- artwork for style 1
    Pattern2.png      <- style 2 ... up to Pattern16.png
    Icon1.png         <- optional; generated from the pattern if missing
```

Files at the top of the zip, folders nested inside other folders, and folders without `shield.json` are ignored.
Your artwork is painted onto the shield's face only; the rim, strap and back stay plain wood.
The game writes a full guide, `HOW TO MAKE A SHIELD.txt`, and a face-outline template to paint over
(`_Templates\ShieldWood - pattern guide.png`) into the drop folder.

## Changelog
- 0.0.2 - Patterns are baked into the shield's own texture layout, so they no longer bleed onto the
  rim, strap and back. Icons generated automatically. Fixed pack layout (a folder per shield inside the zip,
  shield.json required) with clear log messages when a pack is laid out wrong. Author guide and templates
  written to the drop folder.
- 0.0.1 - First test build.
