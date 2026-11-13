# Builds src/QuantumConveyors.Plugin into one file: src/QuantumConveyors.Plugin/bin/Release/net6.0/QuantumConveyors.dll.
# Needs Moon Sweepers with BepInEx and MoonFramework installed (it compiles against them); -MoonSweepersGameRoot if the
# game isn't in Steam's default folder. -Install copies the DLL into the game's BepInEx/plugins/QuantumConveyors.
param(
    [string]$MoonSweepersGameRoot,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectRoot 'src\QuantumConveyors.Plugin\QuantumConveyors.Plugin.csproj'
$dotnet = Join-Path $projectRoot 'work\dotnet-sdk\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }

$propertyArgs = @()
if (-not [string]::IsNullOrWhiteSpace($MoonSweepersGameRoot)) { $propertyArgs += "/p:MoonSweepersGameRoot=$MoonSweepersGameRoot" }

& $dotnet build $project -c Release @propertyArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Install) {
    if ([string]::IsNullOrWhiteSpace($MoonSweepersGameRoot)) { $MoonSweepersGameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers' }
    $destination = Join-Path $MoonSweepersGameRoot 'BepInEx\plugins\QuantumConveyors'
    if (Test-Path -LiteralPath $destination) { Get-ChildItem -LiteralPath $destination -File | Remove-Item -Force }
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\QuantumConveyors.Plugin\bin\Release\net6.0\QuantumConveyors.dll') -Destination $destination -Force
    Write-Host "Installed to $destination"
}
