param(
    [switch]$CopyToStagedRuntime
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectRoot 'src\QuantumConveyors.Plugin\QuantumConveyors.Plugin.csproj'
$dotnet = Join-Path $projectRoot 'work\dotnet-sdk\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}

& $dotnet restore $project --force-evaluate --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($CopyToStagedRuntime) {
    $destination = Join-Path $projectRoot 'work\staging\BepInEx\BepInEx\plugins\QuantumConveyors'
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\QuantumConveyors.Plugin\bin\Release\net6.0\QuantumConveyors.dll') -Destination $destination -Force
}
