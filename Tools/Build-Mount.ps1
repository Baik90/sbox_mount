param([string]$EngineRoot = (Resolve-Path "$PSScriptRoot/../../..").Path)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path "$PSScriptRoot/..").Path
dotnet build "$projectRoot/Mount/sbox_workshop.csproj" -c Release "-p:SboxEngineRoot=$EngineRoot" -o "$projectRoot/.sbox/build/sbox_workshop"
if ($LASTEXITCODE -ne 0) { throw 'Mount build failed.' }
