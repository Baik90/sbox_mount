param([string]$EngineRoot = (Resolve-Path "$PSScriptRoot/../../..").Path)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$projectRoot = (Resolve-Path "$PSScriptRoot/..").Path
$EngineRoot = (Resolve-Path $EngineRoot).Path
if (!(Test-Path -LiteralPath "$EngineRoot/sbox.exe")) { throw 'Not an s&box installation.' }
& "$PSScriptRoot/Build-Mount.ps1" -EngineRoot $EngineRoot
$target = [IO.Path]::GetFullPath((Join-Path $EngineRoot 'mount/sbox_workshop'))
$expected = [IO.Path]::GetFullPath((Join-Path $EngineRoot 'mount')) + [IO.Path]::DirectorySeparatorChar
if (!$target.StartsWith($expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid install target.' }
New-Item -ItemType Directory -Force -Path "$target/Assets" | Out-Null
$dll = Join-Path $target 'sbox_workshop.dll'
$backup = "$dll.backup-$([Guid]::NewGuid().ToString('N'))"
# Loaded managed DLLs cannot be overwritten, but Windows permits renaming them.
if (Test-Path -LiteralPath $dll) { Move-Item -LiteralPath $dll -Destination $backup }
try {
    Copy-Item -LiteralPath "$projectRoot/.sbox/build/sbox_workshop/sbox_workshop.dll" -Destination $dll
} catch {
    if (!(Test-Path -LiteralPath $dll) -and (Test-Path -LiteralPath $backup)) { Move-Item -LiteralPath $backup -Destination $dll }
    throw
}
$pdb = Join-Path $target 'sbox_workshop.pdb'
if (Test-Path -LiteralPath $pdb) { Move-Item -LiteralPath $pdb -Destination "$pdb.backup-$([Guid]::NewGuid().ToString('N'))" }
Copy-Item -LiteralPath "$projectRoot/.sbox/build/sbox_workshop/sbox_workshop.pdb" -Destination $target
# Preserve native paths referenced by scenes created with the prototype.
$cache = Join-Path $projectRoot '.sbox/workshop-mount-cache/v3/sbox_workshop'
if (Test-Path -LiteralPath $cache) { Copy-Item -LiteralPath $cache -Destination "$target/Assets" -Recurse -Force }
@{ InstalledAt = (Get-Date).ToString('o'); SHA256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash; Source = $projectRoot } |
    ConvertTo-Json | Set-Content -LiteralPath "$target/install.json" -Encoding UTF8
Write-Output "Installed: $dll. Restart s&box to load the native plugin."
