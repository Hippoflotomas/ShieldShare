namespace ShieldShare
{
    /// <summary>Plain-text guide written into the drop folder on every launch, so pack authors always have it.</summary>
    internal static class AuthorGuide
    {
        public const string FileName = "HOW TO MAKE A SHIELD.txt";

        public const string Text =
@"SHIELDSHARE - HOW TO MAKE A SHIELD
==================================

(This file is rewritten every time the game starts - don't keep notes in it.)

THE SHORT VERSION
-----------------
1. Make a folder in this 'Valheim Custom Shields' folder. Its name becomes the shield's name,
   e.g.  Valheim Custom Shields\Black Dog\
2. Put your artwork in it as  Pattern1.png  (and Pattern2.png, Pattern3.png ... for more styles).
3. Start the game. Your shield is at the workbench.

That's it. Everything else is optional.

To share it, zip the folder and give people the .zip. They drop the zip in THEIR
'Valheim Custom Shields' folder. No unzipping needed.


THE ARTWORK (PatternN.png)
--------------------------
- Each Pattern image is a straight-on picture of the shield's FACE. The whole image is
  stretched over the face; anything outside the face outline is simply not shown.
- The mod paints ONLY the face. The rim, strap and back always stay plain wood.
- Open  _Templates\ShieldWood - pattern guide.png  and paint over it: the grey area is the
  face, at the correct proportions. Use it as a bottom layer in your paint program.
- Transparent parts of your image show the plain wood underneath. Solid parts cover it.
  If you want wood around your design, leave that area FULLY transparent (alpha 0).
- Size: 512 x 512 is ideal. Anything from 256 to 1024 is fine. PNG or JPG.
- Up to 16 styles per shield: Pattern1 ... Pattern16. Players pick the style at the workbench,
  just like the vanilla shields.
- Names are forgiving: Pattern1.png, pattern_1.PNG, Style1.jpg all work. Pattern.png = Pattern1.png.


ICONS (optional)
----------------
If you don't make icons, they are made for you from each Pattern (shield-shaped, on wood).
To use your own, add  Icon1.png, Icon2.png ...  matching the Pattern numbers.
Any size; 128 x 128 is plenty.


shield.json (optional)
----------------------
Leave it out and you get: the folder name as the shield name, the wood shield as the base,
crafted at the workbench for 10 Wood. To change any of that, add a file called shield.json.
Every line is optional - include only what you want to change:

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
- hidden: true means the shield can't be crafted (spawn-only).
- basePrefab: which vanilla shield model to use. ShieldWood is the tested one. Other shields
  work too, but check their pattern guide in _Templates (it appears once a pack uses them).
- styleCount: only use the first N patterns (normally they're just counted).
If shield.json has a mistake, the shield still loads with defaults and its description says so;
the BepInEx log says exactly what's wrong.


ADVANCED (optional)
-------------------
- MainTex.png / BumpMap.png / MetallicGlossMap.png / EmissionMap.png replace the base wood
  textures for the WHOLE shield. These follow the model's own UV layout - see
  _Templates\ShieldWood - UV layout.png (orange = face, grey = rim, strap and back).
- StyleTex.png replaces the automatically built style sheet: a 4 x 4 grid, style 1 in the
  bottom-left cell, going left to right then upwards, each cell following the UV layout.
  You still need PatternN files so the mod knows how many styles there are.


WHERE THINGS GO
---------------
- You edit:   Documents\Valheim Custom Shields\   (folders and .zip files)
- The mod keeps a working copy in BepInEx\config\ShieldShare\Shields\ - don't edit there;
  it's refreshed from this folder every launch, and shields you remove here are removed there.
- Folders starting with _ (like _Templates) are ignored.
";
    }
}
