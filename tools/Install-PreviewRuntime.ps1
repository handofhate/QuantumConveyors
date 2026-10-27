param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers',
    [string]$BepInExZip = '',
    [string]$PatcherZip = '',
    [string]$MoonFrameworkZip = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$backupRoot = Join-Path $projectRoot 'work\backups\runtime-install'

if (-not (Test-Path -LiteralPath $GamePath)) {
    throw "Game folder was not found: $GamePath"
}

if (Get-Process -Name 'MoonSweepers' -ErrorAction SilentlyContinue) {
    throw 'Moon Sweepers is still running. Close it before installing runtime files.'
}

if ([string]::IsNullOrWhiteSpace($BepInExZip)) {
    $BepInExZip = Join-Path $projectRoot 'work\downloads\BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.791+b782da4.zip'
}

foreach ($required in @($BepInExZip, $PatcherZip, $MoonFrameworkZip)) {
    if ([string]::IsNullOrWhiteSpace($required) -or -not (Test-Path -LiteralPath $required)) {
        throw "Required archive is missing: $required"
    }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $backupRoot $stamp
New-Item -ItemType Directory -Force -Path $backup | Out-Null

$existingPaths = @('BepInEx', 'dotnet', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt') |
    ForEach-Object { Join-Path $GamePath $_ } |
    Where-Object { Test-Path -LiteralPath $_ }

foreach ($path in $existingPaths) {
    $relative = $path.Substring($GamePath.Length).TrimStart('\')
    $destination = Join-Path $backup $relative
    $parent = Split-Path -Parent $destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Move-Item -LiteralPath $path -Destination $destination
}

$temp = Join-Path $projectRoot 'work\staging\install-temp'
if (Test-Path -LiteralPath $temp) {
    Remove-Item -LiteralPath $temp -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $temp | Out-Null

Expand-Archive -LiteralPath $BepInExZip -DestinationPath $temp -Force
Expand-Archive -LiteralPath $PatcherZip -DestinationPath $temp -Force
Expand-Archive -LiteralPath $MoonFrameworkZip -DestinationPath $temp -Force

$plugin = Join-Path $projectRoot 'src\QuantumConveyors.Plugin\bin\Release\net6.0\QuantumConveyors.dll'
if (-not (Test-Path -LiteralPath $plugin)) {
    throw "Build the Quantum Conveyors plug-in before installing: $plugin"
}
$pluginDestination = Join-Path $temp 'BepInEx\plugins\QuantumConveyors'
New-Item -ItemType Directory -Force -Path $pluginDestination | Out-Null
Copy-Item -LiteralPath $plugin -Destination $pluginDestination -Force

Get-ChildItem -LiteralPath $temp -Force | Copy-Item -Destination $GamePath -Recurse -Force

@{
    InstalledAt = (Get-Date).ToString('o')
    GamePath = $GamePath
    BackupPath = $backup
    BepInExZip = $BepInExZip
    PatcherZip = $PatcherZip
    MoonFrameworkZip = $MoonFrameworkZip
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'install-manifest.json') -Encoding UTF8

Write-Output "Installed the staged runtime. Rollback material is in: $backup"
