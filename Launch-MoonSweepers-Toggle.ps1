$ErrorActionPreference = 'Stop'

$gameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers'
$gameExe = Join-Path $gameRoot 'MoonSweepers.exe'
$doorstopConfig = Join-Path $gameRoot 'doorstop_config.ini'

if (-not (Test-Path -LiteralPath $gameExe)) {
    throw "Moon Sweepers was not found at: $gameExe"
}
if (-not (Test-Path -LiteralPath $doorstopConfig)) {
    throw "BepInEx Doorstop configuration was not found at: $doorstopConfig"
}

$running = Get-Process -Name 'MoonSweepers' -ErrorAction SilentlyContinue
if ($running) {
    throw 'Moon Sweepers is already running. Close it before using the toggle launcher.'
}

$originalText = [System.IO.File]::ReadAllText($doorstopConfig)
$enabledPattern = '(?im)^(\s*enabled\s*=\s*)(true|false)(\s*)$'
$enabledMatch = [regex]::Match($originalText, $enabledPattern)
if (-not $enabledMatch.Success) {
    throw "Could not find the Doorstop enabled setting in: $doorstopConfig"
}

$originalState = $enabledMatch.Groups[2].Value
$temporaryState = if ($originalState -ieq 'true') { 'false' } else { 'true' }
$temporaryText = [regex]::Replace($originalText, $enabledPattern, {
    param($match)
    $match.Groups[1].Value + $temporaryState + $match.Groups[3].Value
}, 1)

[System.IO.File]::WriteAllText($doorstopConfig, $temporaryText, [System.Text.UTF8Encoding]::new($false))
$mode = if ($temporaryState -ieq 'true') { 'MODDED' } else { 'VANILLA' }
Write-Host "Launching Moon Sweepers: $mode"
Write-Host 'The original mod-loader setting will be restored when the game closes.'

try {
    $process = Start-Process -FilePath $gameExe -WorkingDirectory $gameRoot -PassThru
    Wait-Process -Id $process.Id
}
finally {
    [System.IO.File]::WriteAllText($doorstopConfig, $originalText, [System.Text.UTF8Encoding]::new($false))
    Write-Host 'Restored the original mod-loader setting.'
}
