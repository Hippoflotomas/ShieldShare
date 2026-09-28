# ShieldShare

A Valheim mod (BepInEx + [Jötunn](https://github.com/Valheim-Modding/Jotunn)) for easily adding and sharing custom shield styles.
Player/author-facing documentation is in [`ShieldShare/README.md`](ShieldShare/README.md) and in the
`HOW TO MAKE A SHIELD.txt` guide the mod writes into `Documents\Valheim Custom Shields\` (source: `ShieldShare/AuthorGuide.cs`).

## How it works

| File | Job |
|---|---|
| `ShieldShare.cs` | Plugin entry point. On `PrefabManager.OnVanillaPrefabsAvailable` it syncs packs, clones the base shield for each pack, applies optional texture layers, builds the styles and registers the item with Jötunn. Also writes the author templates. |
| `ShieldPackSync.cs` | Copies packs from the drop folder into `BepInEx\config\ShieldShare\Shields`. Fixed layout, like BannerShare: zips only, one folder per shield directly inside the zip, `shield.json` required; anything else is ignored with a warning. Folders it creates carry a `.shieldshare-source` marker; only marked folders are ever removed. |
| `ShieldPack.cs` | Reads one shield folder: fixed names (`shield.json`, `PatternN.png`, `IconN.png`, case-insensitive), fills in defaults for fields missing from `shield.json`. |
| `StyleBaker.cs` | Pure math, no Unity textures: works out the shield's face triangles and bakes flat pattern images into the mesh's own UV layout. |
| `MeshReader.cs` | Reads vertices/normals/UVs/triangles from a mesh. Meshes the game ships without a CPU copy (`isReadable == false`: blackmetal, flametal, iron tower...) are copied back from the GPU vertex/index buffers and decoded. |
| `FallbackPatterns.cs` | The magenta/black "missing" patterns from `Assets/*.png`, embedded in the DLL (`EmbeddedResource` in the .csproj; name before ` - ` = base prefab). Used for the built-in `ShieldShare_Missing_<Base>` shields (hidden unless `ShowMissingShields` is on), for pattern images that fail to load, and for stand-ins. |
| `MissingShields.cs` | Shields other players have that you don't. The game only passes item hashes around, so ShieldShare tags its shields: item custom data (`ShieldShare.Id` / `ShieldShare.Base`), the player's ZDO (`ShieldShare.Left` / `ShieldShare.LeftBack` = `ID|Base`) and dropped items' ZDO (`ShieldShare.Item`). Item and armour stands are tagged per slot from `ItemDrop.SaveToZDO` (`ShieldShare.Item` for index -1 - item stands and dropped items - or `<slot>_ShieldShare.Item` for armour stand slots). Harmony patches on `Humanoid.SetupVisEquipment`, `VisEquipment.UpdateEquipmentVisuals`, `ItemDrop.Start`, `ItemDrop.SaveToZDO`, `ZNetScene.CreateObject`, `ItemStand.SetVisualItem`, `ArmorStand.SetVisualItem` and `Inventory.AddItem(int prefabHash, …)` read the tags and register a stand-in on the spot - a copy of the built-in magenta shield for that base under the shield's real name - so nothing is converted or deleted. Notices work like BannerShare's: after 2 s of quiet, one centre message and one chat line per shield. |
| `KnownShields.cs` | `known-shields.json`: every pack shield ever registered (base, name, highest style count). Shields whose pack is gone are registered as magenta stand-ins so the game doesn't delete players' copies. |
| `TextureIO.cs` | Image loading via Jötunn (linear for data maps such as normal maps), saving, sprites. |

### Why patterns are baked, not UV-remapped
Valheim's `Custom/Creature` shader reads the style atlas (`_StyleTex`, a 4x4 grid, style 0 bottom-left) through
the mesh's **original UVs**, and the face, rim, strap and back all share that UV space. Moving only the face UVs
(the approach in 0.0.1) left the other parts pointing into the same cell, so they showed stretched pieces of the
artwork. Instead, each front-facing triangle is drawn into the atlas cell at its vanilla UV position, sampling
the author's flat image through a straight-on (planar) projection of the face. The rest of the cell stays
transparent, so everything but the face shows the plain base texture - the same way the vanilla atlas is made.

"Front" is decided per triangle: the average vertex normal must point along the face direction (> 0.5).
Models differ in which way their outside faces and which way up they are. The `KnownBases` table in
`ShieldShare.cs` records what in-game testing established for each vanilla shield (face -Z/+Z, flip vertical,
flip horizontal, mask). Test with an asymmetric pattern - a symmetric one hides mirroring. For shields not in the table, the face is worked out by checking which side's UVs land on painted pixels
in the vanilla style atlas - the vanilla artists only painted the outside. For +Z faces the pattern's U axis is
mirrored so artwork isn't back to front.

### Paint-area mask
Metal shields share one texture between the paintable panel and the metal frame, so a face-shaped bake also
lands on metal. For those bases the baked pattern is multiplied by the vanilla "paintable area": the highest
alpha at each position across all 16 cells of the vanilla style atlas (read back through the GPU), with a soft
edge. Patterns then appear exactly where the game's own styles do.
The log line `Base '...': face = ...` shows the face, the paint-check numbers and whether the mask is used;
`'<pack>': masked to the vanilla paint area - N% of the face is paintable` shows how much of the face survived.

## Building
Standard Jötunn mod stub: build in Visual Studio / `dotnet build`. The Debug post-build step (`scripts/publish.ps1`)
copies the plugin into `<Valheim>\BepInEx\plugins` or `MOD_DEPLOYPATH`; Release builds a Thunderstore zip.
See the [Jötunn docs](https://valheim-modding.github.io/Jotunn/guides/overview.html) for environment setup.

`JotunnModStubUnity/Assets/Assemblies/*.dll` are copied in by the build and are **gitignored** - they include
Valheim's own copyrighted assemblies and must not be committed.

## Known issues

- The orientation of shields on item stands looks a little odd. Not investigated yet.
- Shields crafted before shield tagging existed can only be identified by players without the pack once a
  player *with* the pack has loaded them (inventory, chest or stand), which adds the tag.
