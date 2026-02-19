param(
    [string]$SourceDir = "",
    [string]$OutputDir = "",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = Join-Path $repoRoot "mod\ZomboidGuideMultiBaseMod\Contents\mods\ZomboidGuideMultiBase"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\mod"
}

if (-not (Test-Path $SourceDir)) {
    throw "Mod source folder not found: $SourceDir"
}

$modInfoPath = Join-Path $SourceDir "mod.info"
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.1.0"
    if (Test-Path $modInfoPath) {
        $versionLine = Get-Content -Path $modInfoPath | Where-Object { $_ -match '^version=' } | Select-Object -First 1
        if ($versionLine) {
            $Version = ($versionLine -split '=', 2)[1].Trim()
            if ([string]::IsNullOrWhiteSpace($Version)) {
                $Version = "0.1.0"
            }
        }
    }
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$stagingDir = Join-Path $OutputDir "_staging_multibase_mod"
if (Test-Path $stagingDir) {
    Remove-Item -Path $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

$modsDir = Join-Path $stagingDir "mods"
New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
Copy-Item -Path $SourceDir -Destination $modsDir -Recurse -Force

$modReadmePath = Join-Path $repoRoot "mod\ZomboidGuideMultiBaseMod\README.md"
if (Test-Path $modReadmePath) {
    Copy-Item -Path $modReadmePath -Destination (Join-Path $stagingDir "README.md") -Force
}

$zipName = "ZomboidGuideCompanionMod-$Version.zip"
$zipPath = Join-Path $OutputDir $zipName
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Remove-Item -Path $stagingDir -Recurse -Force

Write-Host "Created mod release ZIP:"
Write-Host "  $zipPath"
