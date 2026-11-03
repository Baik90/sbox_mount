# S&Box Workshop asset mount

Native plugin: `mount/sbox_workshop/sbox_workshop.dll`, shown as **S&Box** in the mount browser.
Reads locally installed Steam subscriptions for app 590830. It does not download packages or load their game assemblies.

Build from the project directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Build-Mount.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Install-Mount.ps1
```

Both scripts accept `-EngineRoot <sbox installation>`. Restart s&box after installing or updating the DLL, then enable S&Box in the mount settings. The installer preserves prepared prototype assets and backs up an existing DLL before replacing it.

Each game has its own `Game/Assets/Models`, `Materials`, `Textures` and `Sounds` directories. Dependencies are rewritten into separate native namespaces. Scenes and prefabs are not mounted yet. Prepared assets live under the plugin's `Assets/sbox_workshop/v3` directory; subscription source files are unchanged.

The indexed browser and duplicate controls remain editor extensions of this project, accessible through **S&Box Mount** in the editor menu. Identical copies are hidden only in that browser's combined flat/search view. Every game's folder and all resource aliases remain complete. The standard engine Everything browser does not apply this custom filter.

Duplicate scans also compare Workshop paths with compiled files actually installed under the engine's `core` directory. Byte-identical files with matching transitive dependency fingerprints prefer the Core original and hide the Workshop copies in the combined view. Modified versions remain visible. This is a content comparison, not a filename blacklist; `default_*` assets are included. Core references appear as `core://` in the report for provenance only. The filter does not delete files or redirect resource loading. Loose files in `core` are covered; other packages and archives are not classified as Core.

Validation after installation: 41 standalone assertions; live native model/material/texture isolation tests; folder/flat browser counts; duplicate view and hidden alias loading. Automatic registration on a fresh process start still needs a restart when the current editor contains unsaved work.
