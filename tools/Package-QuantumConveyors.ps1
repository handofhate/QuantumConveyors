[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutputDirectory,
    [string]$BepInExStage,
    [string]$MoonSweepersGameRoot
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\QuantumConveyors.Plugin\QuantumConveyors.Plugin.csproj'
$buildScript = Join-Path $projectRoot 'build.ps1'
$buildOutput = Join-Path $projectRoot 'src\QuantumConveyors.Plugin\bin\Release\net6.0'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'dist'
}
if ([string]::IsNullOrWhiteSpace($BepInExStage)) {
    $BepInExStage = Join-Path $projectRoot 'work\staging\BepInEx'
}
if ([string]::IsNullOrWhiteSpace($MoonSweepersGameRoot)) {
    $MoonSweepersGameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers'
}

if ($Version -notmatch '^\d+(\.\d+){1,2}$') {
    throw "Version '$Version' is not a simple dotted version such as 1.1.2."
}
if (-not (Test-Path -LiteralPath $project)) {
    throw "Plugin project was not found: $project"
}
if (-not (Test-Path -LiteralPath $BepInExStage)) {
    throw "BepInEx build staging folder was not found: $BepInExStage. Set MOONSWEEPERS_BEPINEX_STAGE for the self-hosted runner."
}
if (-not (Test-Path -LiteralPath $MoonSweepersGameRoot)) {
    throw "Moon Sweepers was not found: $MoonSweepersGameRoot. Set MOONSWEEPERS_GAME_ROOT for the self-hosted runner."
}

$pluginSource = Get-Content -LiteralPath (Join-Path $projectRoot 'src\QuantumConveyors.Plugin\Plugin.cs') -Raw
if ($pluginSource -notmatch ('PluginVersion\s*=\s*"' + [regex]::Escape($Version) + '"')) {
    throw "Plugin.cs does not declare version $Version. Update PluginVersion before publishing this release."
}

& $buildScript -BepInExStage $BepInExStage -MoonSweepersGameRoot $MoonSweepersGameRoot
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
if (-not (Test-Path -LiteralPath (Join-Path $buildOutput 'QuantumConveyors.dll'))) {
    throw "Build completed without producing QuantumConveyors.dll."
}

$packageRoot = Join-Path $OutputDirectory "QuantumConveyors-$Version"
$pluginDestination = Join-Path $packageRoot 'BepInEx\plugins\QuantumConveyors'
$archivePath = Join-Path $OutputDirectory "QuantumConveyors-$Version.zip"

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
New-Item -ItemType Directory -Force -Path $pluginDestination | Out-Null

Get-ChildItem -LiteralPath $buildOutput -File |
    Where-Object { $_.Extension -in @('.dll', '.obj', '.mtl', '.png', '.jpg', '.jpeg') } |
    Copy-Item -Destination $pluginDestination -Force

$readmeSource = Join-Path $projectRoot 'RELEASE_README_1.1.txt'
if (-not (Test-Path -LiteralPath $readmeSource)) {
    $readmeSource = Join-Path $projectRoot 'README.md'
}
Copy-Item -LiteralPath $readmeSource -Destination (Join-Path $packageRoot 'README.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination (Join-Path $packageRoot 'CHANGELOG.txt') -Force

Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal
Write-Host "Created $archivePath"

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "package=$archivePath"
}
