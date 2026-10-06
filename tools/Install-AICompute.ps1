param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Software Inc'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskGame = (Resolve-Path -LiteralPath $GamePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $taskGame 'Software Inc.exe'))) { throw 'Game executable not found.' }
$taskSource = Join-Path $taskRoot 'mods\AI Compute'
$taskBackup = Join-Path $taskRoot ('build\install-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
foreach ($taskKind in @('Mods', 'DLLMods')) {
    $taskBase = [IO.Path]::GetFullPath((Join-Path $taskGame $taskKind))
    $taskDestination = [IO.Path]::GetFullPath((Join-Path $taskBase 'AI Compute'))
    if (-not $taskDestination.StartsWith($taskBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination escaped the game mod directory.' }
    foreach ($taskCheck in @($taskBase, $taskDestination)) {
        if ((Test-Path -LiteralPath $taskCheck) -and ((Get-Item -LiteralPath $taskCheck).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing redirected mod path: $taskCheck" }
    }
    if (Test-Path -LiteralPath $taskDestination) {
        $taskMeta = Join-Path $taskDestination 'meta.tyd'
        # Native TyD serialization can normalize a quoted author to bare SDSI.
        if (-not (Test-Path -LiteralPath $taskMeta) -or (Get-Content -LiteralPath $taskMeta -Raw) -notmatch '(?m)^\s*Author\s+(?:"SDSI"|SDSI)\s*$') { throw "An unrelated mod already uses $taskDestination" }
    }
}
foreach ($taskKind in @('Mods', 'DLLMods')) {
    $taskBase = Join-Path $taskGame $taskKind
    $taskDestination = Join-Path $taskBase 'AI Compute'
    if (Test-Path -LiteralPath $taskDestination) {
        $taskBackupKind = Join-Path $taskBackup $taskKind
        New-Item -ItemType Directory -Path $taskBackupKind -Force | Out-Null
        Copy-Item -LiteralPath $taskDestination -Destination $taskBackupKind -Recurse
    }
    New-Item -ItemType Directory -Path $taskBase -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskSource "$taskKind\AI Compute") -Destination $taskBase -Recurse -Force
    Write-Output "Installed: $taskDestination"
}
if (Test-Path -LiteralPath $taskBackup) { Write-Output "Backup: $taskBackup" }
Write-Output 'Restart the game, enable both AI Compute mods, and use a fresh test company.'
