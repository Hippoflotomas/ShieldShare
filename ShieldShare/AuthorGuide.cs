namespace ShieldShare
{
    /// <summary>Plain-text guide written into the drop folder on every launch, so pack authors always have it.</summary>
    internal static class AuthorGuide
    {
        public const string FileName = "HOW TO MAKE A SHIELD.txt";

        public const string Text =
@"SHIELDSHARE - HOW TO MAKE A SHIELD PACK
=======================================

(This file is rewritten every time the game starts - don't keep notes in it.)

INSTALLING SHIELDS
------------------
Put shield pack .zip files in this 'Valheim Custom Shields' folder and start the game.
Don't unzip them. Remove a zip to remove its shields. Everyone on a server needs the same zips.


PACK LAYOUT
-----------
Each shield is a FOLDER INSIDE THE ZIP, and that shield's files go inside that folder:

  MyShields.zip
    BlackDog\                 <- the shield's ID. Must be unique. Don't rename it later.
      shield.json             <- required
      Pattern1.png            <- style 1 artwork
      Pattern2.png            <- style 2 artwork (as many as you like, up to 16)
      Icon1.png               <- optional icon for style 1
      Icon2.png               <- optional icon for style 2
    GrandAthera\
      shield.json
      Pattern1.png

Rules:
- Files loose at the top of the zip won't work.
- Folders inside folders won't work - the shield folder must be directly inside the zip.
- A folder without shield.json is skipped.
- File names are fixed (capitals don't matter). Images must be .png.
- The folder name (the ID) may only use letters, digits, - and _ ; anything else becomes _.
- Folders placed directly in 'Valheim Custom Shields' are ignored. Zip them.


THE ARTWORK (PatternN.png)
--------------------------
- Each Pattern image is a straight-on picture of the shield's FACE. The whole image is
  stretched over the face; anything outside the face outline is not shown.
- Only the face is painted. The rim, strap and back always stay plain wood.
- Open  _Templates\ShieldWood - pattern guide.png  and paint over it: the grey area is the
  face, at the correct proportions. Use it as a bottom layer in your paint program.
- Transparent parts of your image show the plain wood underneath. Solid parts cover it.
  If you want wood around your design, make that area FULLY transparent (alpha 0).
- Size: 512 x 512 is ideal; 256 to 1024 is fine.
- Number the patterns from 1: Pattern1.png, Pattern2.png ... Pattern16.png.
  Players pick the style at the workbench, like the vanilla shields.


ICONS (IconN.png, optional)
---------------------------
If a style has no icon, one is made from its pattern (shield-shaped, on wood).
To use your own, add Icon1.png, Icon2.png ... matching the Pattern numbers.
Any size; 128 x 128 is plenty.


shield.json (required)
----------------------
The file must exist, but every line in it is optional. Leave something out and you get:
name = the folder name, base = the wood shield, crafted at the workbench for 10 Wood.
The smallest valid shield.json is just:  {}

{
  ""displayName"": ""Black Dog Shield"",
  ""description"": ""Carried by the Black Dogs."",
  ""basePrefab"": ""ShieldWood"",
  ""craftingStation"": ""workbench"",
  ""minStationLevel"": 1,
  ""hidden"": false,
  ""requirements"": [
    { ""item"": ""Wood"", ""amount"": 10 },
    { ""item"": ""Resin"", ""amount"": 4 },
    { ""item"": ""LeatherScraps"", ""amount"": 4, ""amountPerLevel"": 2 }
  ]
}

- craftingStation: workbench, forge, stonecutter, artisan, blackforge, galdr, cauldron, none
  (or the game's own name, e.g. piece_workbench)
- requirements: use the game's item names (Wood, Resin, LeatherScraps, Bronze, Iron ...).
  amountPerLevel is the extra cost for each upgrade level.
- hidden: true means the shield can't be crafted.
- basePrefab: which vanilla shield model to use. ShieldWood is the tested one. Others may work;
  their pattern guide appears in _Templates once a pack uses them.
- styleCount: only use the first N patterns (normally they are just counted).
If shield.json can't be read (a missing comma, say), that shield is skipped and the BepInEx
log says exactly what's wrong.


ADVANCED (optional)
-------------------
- MainTex.png / BumpMap.png / MetallicGlossMap.png / EmissionMap.png replace the base wood
  textures for the WHOLE shield. These follow the model's own UV layout - see
  _Templates\ShieldWood - UV layout.png (orange = face, grey = rim, strap and back).
- StyleTex.png replaces the automatically built style sheet: a 4 x 4 grid, style 1 in the
  bottom-left cell, going left to right then upwards, each cell following the UV layout.
  You still need the PatternN files so the mod knows how many styles there are.


WHERE THINGS GO
---------------
- Players and authors use:  Documents\Valheim Custom Shields\   (.zip files only)
- The mod keeps a working copy in BepInEx\config\ShieldShare\Shields\ - don't edit there;
  it's refreshed from the zips every launch.
- Folders starting with _ (like _Templates) are the mod's own helpers.
";
    }
}
