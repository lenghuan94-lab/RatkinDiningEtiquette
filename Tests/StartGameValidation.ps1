# Run explicitly to open the isolated validation game in a visible window.
# The profile is inside this repository; normal player Config/Saves are not used.
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskProfile = [IO.Path]::GetFullPath((Join-Path $taskRoot '.tools/game-validation-1.0.1'))
$taskGame = 'D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64.exe'
if (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue) {
    throw 'Close the current RimWorld instance before starting isolated validation.'
}
if (-not (Test-Path -LiteralPath (Join-Path $taskProfile 'Config/ModsConfig.xml'))) {
    throw 'The isolated validation profile is missing. Do not launch with the normal profile.'
}
$taskLog = Join-Path $taskProfile 'Player-visible.log'
if (Test-Path -LiteralPath $taskLog) {
    $taskLogArchive = Join-Path $taskProfile ('Player-visible-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
    Copy-Item -LiteralPath $taskLog -Destination $taskLogArchive -ErrorAction Stop
}
$taskArgs = @('-screen-width', '1280', '-screen-height', '800', '-screen-fullscreen', '0',
    ('-savedatafolder="{0}"' -f $taskProfile), '-logFile',
    ('"{0}"' -f (Join-Path $taskProfile 'Player-visible.log')))
Start-Process -FilePath $taskGame -WorkingDirectory (Split-Path -Parent $taskGame) `
    -ArgumentList $taskArgs -WindowStyle Normal
Write-Output "Opened isolated validation game. Profile: $taskProfile"
