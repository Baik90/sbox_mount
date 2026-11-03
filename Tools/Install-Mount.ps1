param([string]$EngineRoot = (Resolve-Path "$PSScriptRoot/../../..").Path)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path "$PSScriptRoot/..").Path
$EngineRoot = (Resolve-Path $EngineRoot).Path
if (!(Test-Path -LiteralPath "$EngineRoot/sbox.exe")) { throw 'Not an s&box installation.' }
& "$PSScriptRoot/Build-Mount.ps1" -EngineRoot $EngineRoot
$target = [IO.Path]::GetFullPath((Join-Path $EngineRoot 'mount/sbox_workshop'))
$expected = [IO.Path]::GetFullPath((Join-Path $EngineRoot 'mount')) + [IO.Path]::DirectorySeparatorChar
if (!$target.StartsWith($expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid install target.' }
New-Item -ItemType Directory -Force -Path "$target/Assets" | Out-Null
$dll = Join-Path $target 'sbox_workshop.dll'
if (Test-Path -LiteralPath $dll) { Copy-Item -LiteralPath $dll -Destination "$dll.backup-$(Get-Date -Format yyyyMMdd-HHmmss)" }
Copy-Item -LiteralPath "$projectRoot/.sbox/build/sbox_workshop/sbox_workshop.dll" -Destination $dll -Force
Copy-Item -LiteralPath "$projectRoot/.sbox/build/sbox_workshop/sbox_workshop.pdb" -Destination $target -Force
# Preserve native paths referenced by scenes created with the prototype.
$cache = Join-Path $projectRoot '.sbox/workshop-mount-cache/v3/sbox_workshop'
if (Test-Path -LiteralPath $cache) { Copy-Item -LiteralPath $cache -Destination "$target/Assets" -Recurse -Force }
@{ InstalledAt = (Get-Date).ToString('o'); SHA256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash; Source = $projectRoot } |
    ConvertTo-Json | Set-Content -LiteralPath "$target/install.json" -Encoding UTF8
Write-Output "Installed: $dll. Restart s&box to load the native plugin."
