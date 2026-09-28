# publishables

Building in **Release** creates the Thunderstore package here:

- `ShieldShare-<version>.zip` - upload this to Thunderstore
- `ShieldShare\` - the same contents unzipped, for checking

Contents come from `ShieldShare\Package` (manifest.json, icon.png, README.md, CHANGELOG.md)
plus the built DLL in `plugins\`. The version in the zip name is read from manifest.json, and the
build stops if it doesn't match PluginVersion in ShieldShare.cs - bump both together.

Everything in this folder except this README is ignored by git.
