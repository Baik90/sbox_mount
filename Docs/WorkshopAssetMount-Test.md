# Workshop asset mount: Decay / RustLegacy

Tested on 2026-11-02 with the installed s&box editor 26.10.27c.

This document records the earlier standard-package experiment. The subsequent
working editor prototype and its clean-process isolation test are documented
in [WorkshopMount-Prototype.md](WorkshopMount-Prototype.md).

## Result

The assets can be loaded, but the standard package mount does **not** satisfy
the requested combination of no foreign assemblies and isolated resource paths.
This is a feasibility test, not an implemented mount.

| Installed game | Workshop ID | Files | Models (`.vmdl_c`) | Materials (`.vmat_c`) |
| --- | --- | ---: | ---: | ---: |
| conna.decay | 3829436870 | 39,527 | 9,935 | 3,414 |
| redfive.rustlegacy | 3828567418 | 1,800 | 250 | 161 |

## Static overlap

25 shared relative file paths, excluding the two metadata files; 10 have
identical SHA-256 hashes, 15 differ. This count includes settings, stylesheet
and thumbnail files, not just mountable assets. Ten differing paths are
compiled textures. Different bytes alone do not prove a visible difference.

The shared bandage model, its material and its main textures are identical.
`materials/default/default_skin_diffusewarp_tga_4d3cb601.generated.vtex_c`
has different contents (95,264 bytes in Decay, 195,611 in RustLegacy).

Reproduce the read-only file comparison from the project directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./Tools/Test-WorkshopAssetOverlap.ps1 -WorkshopContentRoot 'D:/Steam/steamapps/workshop/content/590830'
```

Output defaults to `.sbox/mount-tests/inventory.json`. It reads the existing
installations without downloading, copying or changing their assets.

## Live editor observations

1. No Decay/RustLegacy assemblies were present before the test.
2. For each game, fetched its package with `Package.FetchAsync(ident, false)`
   and invoked the instance method `package.MountAsync(withCode: false)`.
3. Both packages reported the expected Workshop ID and
   `InstallsFromWorkshop == true` and returned an `AggregateFileSystem`.
4. Despite the false flag, the editor subsequently contained
   `package.conna.decay`, `package.conna.entities`, `package.conna.inventory`
   and `package.redfive.rustlegacy`. No foreign scene was instantiated.
   The experiment establishes that this editor workflow is unsuitable for
   the no-code requirement; it does not establish behavior in a standalone game
   or the internal reason those assemblies loaded.
5. Native resource loading succeeded (`IsError == false`) for:
   - `models/rust_animals/bear/bear.vmdl` (Decay)
   - `models/legacy/zombie/zombie.vmdl` (RustLegacy)
   - `weapons/rust_bandage/v_bandage.vmdl` (shared)
6. Those models returned material resources. No visual rendering validation,
   complete texture validation or scene compatibility test was performed.
7. After loading Decay followed by RustLegacy, reading the shared diffuse-warp
   texture through `FileSystem.Mounted` returned RustLegacy's SHA-256:
   `FCE51F0BA1066ED1C7600D10EE5DFA57BCFB3E9653252C6F92481196B7655B8E`.
   Decay's file hashes to
   `2F806F65DD50A945EA3374531BEC87E01E6FBBC3F1524963E34F77C34EB51DDB`.
   The global filesystem therefore selects one version at this path, rather
   than providing separate per-game versions. This does not prove how every
   already cached native resource resolves its dependencies.
8. A synthetic `sbox/conna.decay/assets/...` model path returned the error
   model: standard package mounting does not create that namespace.
9. The installed `Sandbox.Workshop` and `MountHost` types are inaccessible
   from project code (CS0122). Their XML documentation alone is insufficient
   evidence for a public addon API.

Raw runtime evidence is in `.sbox/mount-tests/runtime.json`.
The package mounts were temporary editor state, not project references.
The attached editor was left open; restart it before further no-code tests
to clear the assemblies and mounted packages introduced by this experiment.

## Implementation implications

The desired display tree is `S&Box / <game> / Assets / Models|Materials|Scenes`.
The mounting API supplies resource folder trees, but display grouping does not
isolate native model/material/texture dependency paths.

A strict implementation must avoid the tested package-loading workflow,
read only the installed asset files, exclude `.bin`, settings and game code,
and provide per-game dependency resolution and resource cache identities.
Whether this can be done through a separately loaded mount plugin with the
current native loader, or needs engine changes, is still unproven. It must be
validated before calling a universal mount feasible. Scenes referencing custom
components require additional handling without loading their assemblies.
