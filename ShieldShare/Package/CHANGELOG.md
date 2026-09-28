## Version 1.0.1
- Fixed: on game builds where Inventory.AddItem has a different parameter list (flagged for the Valheim dedicated server build), the mod's patches could fail to load. The inventory patch now finds the right method on any build, and every patch loads on its own, so a future game update can only switch off the one feature it breaks - with a warning in the log.

## Version 1.0.0
First public release.
- Custom shield styles from zip packs: one folder per shield inside the zip, with a shield.json and Pattern1.png, Pattern2.png ... (up to 16 styles). Players pick the style at the workbench, like vanilla shields.
- Works on all nine customisable shields: wood, wood tower, banded, silver, black metal, black metal tower, iron tower, flametal and flametal tower.
- Artwork is painted on the shield's face only - the rim, strap and back keep their vanilla look. On metal shields the artwork only goes where the game's own paint styles go.
- Icons are made automatically from the patterns if a pack doesn't include its own.
- The mod writes a guide (HOW TO MAKE A SHIELD.txt) and a paint-over template for every shield into Documents\Valheim Custom Shields\_Templates.
- Shields from packs you don't have (another player's, on the ground, in chests, on item and armour stands) show as a magenta "missing" shield, with an on-screen notice and details in chat. Nothing is lost - install the pack and they're back.
- Removing a pack turns its shields into "missing" shields instead of deleting them.
- Pack problems (files at the top of the zip, missing shield.json, broken JSON, wrong image type) are explained in the BepInEx log.
