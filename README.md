# ShieldShare

A Valheim mod (BepInEx + [Jötunn](https://github.com/Valheim-Modding/Jotunn)) for easily adding and sharing custom shield styles.
Player/author-facing documentation is in [`ShieldShare/README.md`](ShieldShare/README.md) and in the
`HOW TO MAKE A SHIELD.txt` guide the mod writes into `Documents\Valheim Custom Shields\` (source: `ShieldShare/AuthorGuide.cs`).

## How it works

| File | Job |
|---|---|
| `ShieldShare.cs` | Plugin entry point. On `PrefabManager.OnVanillaPrefabsAvailable` it syncs packs, clones the base shield for each pack, applies optional texture layers, builds the styles and registers the item with Jötunn. Also writes the author templates. |
| `ShieldPackSync.cs` | Copies packs from the drop folder (zips in any layout, or plain folders) into `BepInEx\config\ShieldShare\Shields`. Folders it creates carry a `.shieldshare-source` marker; only marked folders are ever removed. |
| `ShieldPack.cs` | Reads one pack folder: finds files case-insensitively, reads the optional `shield.json`, fills in defaults. |
| `StyleBaker.cs` | Pure math, no Unity textures: works out the shield's face triangles and bakes flat pattern images into the mesh's own UV layout. |
| `TextureIO.cs` | PNG/JPG loading (linear for data maps such as normal maps), saving, sprites. |

### Why patterns are baked, not UV-remapped
Valheim's `Custom/Creature` shader reads the style atlas (`_StyleTex`, a 4x4 grid, style 0 bottom-left) through
the mesh's **original UVs**, and the face, rim, strap and back all share that UV space. Moving only the face UVs
(the approach in 0.0.1) left the other parts pointing into the same cell, so they showed stretched pieces of the
artwork. Instead, each front-facing triangle is drawn into the atlas cell at its vanilla UV position, sampling
the author's flat image through a straight-on (planar) projection of the face. The rest of the cell stays
transparent, so everything but the face shows the plain base texture - the same way the vanilla atlas is made.

"Front" is decided per triangle: the average vertex normal must point down local -Z (`normal.z < -0.5`).
That axis was established in game for `ShieldWood`; other base prefabs may need checking.

## Building
Standard Jötunn mod stub: build in Visual Studio / `dotnet build`. The Debug post-build step (`scripts/publish.ps1`)
copies the plugin into `<Valheim>\BepInEx\plugins` or `MOD_DEPLOYPATH`; Release builds a Thunderstore zip.
See the [Jötunn docs](https://valheim-modding.github.io/Jotunn/guides/overview.html) for environment setup.

`JotunnModStubUnity/Assets/Assemblies/*.dll` are copied in by the build and are **gitignored** - they include
Valheim's own copyrighted assemblies and must not be committed.
