# ShieldShare

Add your own shield designs to Valheim, and share them with friends as a single .zip file.

## Installation
Install with a mod manager (r2modman / Thunderstore), or copy `ShieldShare.dll` into `BepInEx\plugins\ShieldShare\`.
Requires BepInEx and Jötunn. Everyone on a server needs the mod, and should have the same shield packs:
a shield from a pack you don't have shows as a magenta "missing" shield (in hand, on the ground, in a
chest, or on an item or armour stand) and a message lists the missing shields in chat. Nothing is lost - install the pack and it's back.

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
If you remove a pack, shields from it turn into magenta "missing" shields instead of disappearing from
inventories; put the zip back to restore them.
The game writes a full guide, `HOW TO MAKE A SHIELD.txt`, and a face-outline template to paint over
(`_Templates\ShieldWood - pattern guide.png`) into the drop folder.

## Known issues
- Shields on item stands show their first style (a Valheim bug that affects vanilla shields too).

## Changelog
- 0.0.2 - Patterns are baked into the shield's own texture layout, so they no longer bleed onto the
  rim, strap and back. Icons generated automatically. Fixed pack layout (a folder per shield inside the zip,
  shield.json required) with clear log messages when a pack is laid out wrong. Author guide and templates
  written to the drop folder. All nine customisable vanilla shields supported (metal shields are painted only
  where the game's own styles paint). Built-in "missing" patterns; removed packs leave stand-ins behind.
  Plugin GUID is now com.hippotech.shieldshare. Other players' shields from packs you don't have show as
  magenta stand-ins with an on-screen notice and chat details (like BannerShare), and are never deleted.
- 0.0.1 - First test build.
