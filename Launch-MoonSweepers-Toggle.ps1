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
$nextState = if ($originalState -ieq 'true') { 'false' } else { 'true' }
$nextText = [regex]::Replace($originalText, $enabledPattern, {
    param($match)
    $match.Groups[1].Value + $nextState + $match.Groups[3].Value
}, 1)

[System.IO.File]::WriteAllText($doorstopConfig, $nextText, [System.Text.UTF8Encoding]::new($false))
$mode = if ($nextState -ieq 'true') { 'MODDED' } else { 'VANILLA' }
Write-Host "Launching Moon Sweepers: $mode"
Write-Host "The next toggle will switch back to the other mode."

try {
    $process = Start-Process -FilePath $gameExe -WorkingDirectory $gameRoot -PassThru
    Wait-Process -Id $process.Id
}
catch {
    # If the game could not be launched, do not leave the loader in the new state.
    [System.IO.File]::WriteAllText($doorstopConfig, $originalText, [System.Text.UTF8Encoding]::new($false))
    throw
}
