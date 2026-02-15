param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,
    [Parameter(Mandatory = $true)]
    [string]$FeedDir,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$ExeName = "ZomboidGuide.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PublishDir)) {
    throw "PublishDir nicht gefunden: $PublishDir"
}

$publishDirFull = (Resolve-Path $PublishDir).Path
$feedDirFull = [System.IO.Path]::GetFullPath($FeedDir)
$packageDir = Join-Path $feedDirFull "package"

if (Test-Path $packageDir) {
    Remove-Item -Path $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDirFull "*") -Destination $packageDir -Recurse -Force

$manifest = @{
    version = $Version
    packagePath = "package"
    exeName = $ExeName
    notes = "Automatisch erzeugtes Updatepaket."
}

New-Item -ItemType Directory -Path $feedDirFull -Force | Out-Null
$manifestPath = Join-Path $feedDirFull "manifest.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "Update-Feed erstellt:"
Write-Host "  Manifest: $manifestPath"
Write-Host "  Package : $packageDir"
