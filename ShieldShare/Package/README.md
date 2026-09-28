# ShieldShare
Custom shield designs for Valheim, shared as a single zip file.

Paint your own artwork onto any of the nine customisable shields, give it to your friends as a zip, and pick
your style at the workbench just like the vanilla shields.

## Features
- Up to 16 styles per shield, chosen at the workbench.
- Works on all nine customisable shields - wood, wood tower, banded, silver, black metal, black metal tower,
  iron tower, flametal and flametal tower.
- Your artwork goes on the shield's face only. The rim, strap and back keep their vanilla look, and on the
  metal shields the art only goes where the game's own paint styles go.
- Icons are made for you from your artwork (or bring your own).
- Paint-over templates for every shield are written to your documents folder.
- Missing packs never cost anyone a shield (see below).

## Installing shields
After running the game once there is a folder called **Valheim Custom Shields** in your Documents folder.
Put shield pack **.zip** files in there - don't unzip them - and start the game.
The new shields are crafted at the workbench. Remove a zip to remove its shields.

## Playing with friends
Everyone on a server needs ShieldShare and should have the **same shield packs**.

If someone has a shield from a pack you don't have, you'll see it as a magenta and black "missing" shield -
in their hand, on the ground, in a chest or on an item or armour stand - and a message tells you to check
your chat, where every missing shield is listed. Ask around for the pack, drop the zip in your
Valheim Custom Shields folder and restart. Nothing is ever deleted: the real shield comes back as soon as you
have the pack. The same happens to your own shields if you remove a pack.

## Making a shield pack
Each shield is a **folder inside the zip**, and that shield's files go inside that folder:

```
MyShields.zip
  BlackDog/            <- the shield's ID - must be unique, don't rename it later
    shield.json        <- required
    Pattern1.png       <- style 1 artwork
    Pattern2.png       <- style 2 ... up to Pattern16.png
    Icon1.png          <- optional; made from the pattern if missing
```

- Files loose at the top of the zip, and folders inside folders, won't work.
- Images must be PNG. File names are fixed but capitals don't matter.
- Each Pattern image is a straight-on picture of the shield's face. Paint over the template for your shield
  in `Valheim Custom Shields\_Templates` - on metal shields the dark striped areas are metal and never painted.
- Transparent parts of your artwork show the plain shield underneath.
- 512 pixels wide is plenty.

### shield.json
The file must exist, but every line is optional. The smallest valid file is just `{}` - that gives you a
wood shield named after the folder, made at the workbench for 10 wood.

```json
{
  "displayName": "Black Dog Shield",
  "description": "Carried by the Black Dogs.",
  "basePrefab": "ShieldWood",
  "craftingStation": "workbench",
  "minStationLevel": 1,
  "hidden": false,
  "requirements": [
    { "item": "Wood", "amount": 10 },
    { "item": "Resin", "amount": 4 },
    { "item": "LeatherScraps", "amount": 4, "amountPerLevel": 2 }
  ]
}
```

| basePrefab | Shield | Pattern shape (width x height) |
|---|---|---|
| ShieldWood | Wood shield | 512 x 512 |
| ShieldWoodTower | Wood tower shield | 512 x 1039 |
| ShieldBanded | Banded shield | 512 x 512 |
| ShieldSilver | Silver shield | 512 x 554 |
| ShieldBlackmetal | Black metal shield | 512 x 488 |
| ShieldBlackmetalTower | Black metal tower shield | 512 x 973 |
| ShieldIronTower | Iron tower shield | 512 x 988 |
| ShieldFlametal | Flametal shield | 512 x 512 |
| ShieldFlametalTower | Flametal tower shield | 512 x 969 |

`craftingStation` can be workbench, forge, stonecutter, artisan, blackforge, galdr, cauldron or none.
`requirements` use the game's item names (Wood, Resin, LeatherScraps, Bronze, Iron ...).

The full guide, including the advanced options, is written to `Valheim Custom Shields\HOW TO MAKE A SHIELD.txt`.
If a pack isn't laid out right, the BepInEx log says exactly what's wrong.

## Config
`Testing.ShowMissingShields` (default off) - makes the built-in magenta "missing" shields craftable, for testing.

## Known issues
- Shields on item stands sit at a slightly odd angle.

## See also
[BannerShare](https://valheim.hexium.gg/mods/HippoTech/BannerShare) - the same idea for custom banners.
