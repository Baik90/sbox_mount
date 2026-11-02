param(
    [Parameter(Mandatory = $true)]
    [string] $WorkshopContentRoot,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot '../.sbox/mount-tests/inventory.json' }
$games = @(
    @{ Ident = 'conna.decay'; Id = '3829436870' },
    @{ Ident = 'redfive.rustlegacy'; Id = '3828567418' }
)
$maps = @()
$inventory = foreach ($game in $games) {
    $root = (Resolve-Path -LiteralPath (Join-Path $WorkshopContentRoot $game.Id)).Path
    $map = @{}
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/').ToLowerInvariant()
        $map[$relative] = $file
    }
    $maps += ,$map
    [pscustomobject]@{
        Ident = $game.Ident
        WorkshopId = $game.Id
        Files = $map.Count
        Types = @($map.Values | Group-Object Extension | Sort-Object Count -Descending | Select-Object Name, Count)
    }
}
$collisions = @(
    foreach ($path in ($maps[0].Keys | Sort-Object)) {
        if ($path -eq 'metadata.json' -or -not $maps[1].ContainsKey($path)) { continue }
        $first = $maps[0][$path]
        $second = $maps[1][$path]
        $firstHash = (Get-FileHash -LiteralPath $first.FullName -Algorithm SHA256).Hash
        $secondHash = (Get-FileHash -LiteralPath $second.FullName -Algorithm SHA256).Hash
        [pscustomobject]@{
            Path = $path
            Identical = $firstHash -eq $secondHash
            DecayBytes = $first.Length
            RustLegacyBytes = $second.Length
            DecaySha256 = $firstHash
            RustLegacySha256 = $secondHash
        }
    }
)
$report = [pscustomobject]@{
    Games = @($inventory)
    SharedPaths = $collisions.Count
    Different = @($collisions | Where-Object { -not $_.Identical }).Count
    Collisions = $collisions
}
$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$report | Select-Object SharedPaths, Different
Write-Output "Report: $OutputPath"
