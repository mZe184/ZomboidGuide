param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$UpdateFeedOutput = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$projectPath = Join-Path $repoRoot "ZomboidGuide\ZomboidGuide.csproj"

if (-not (Test-Path $projectPath)) {
    throw "Projektdatei nicht gefunden: $projectPath"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$csprojXml = Get-Content -Path $projectPath
    $Version = $csprojXml.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "1.0.0"
    }
}

$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishDir = Join-Path $artifactsRoot "publish\app"
$installerDir = Join-Path $artifactsRoot "installer"
$defaultFeedDir = Join-Path $artifactsRoot "update-feed"

if ([string]::IsNullOrWhiteSpace($UpdateFeedOutput)) {
    $UpdateFeedOutput = $defaultFeedDir
}

if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null

Write-Host "Publishing App..."
dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=false `
    /p:PublishTrimmed=false `
    /p:Version=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish fehlgeschlagen."
}

Write-Host "Erzeuge Update-Feed..."
powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot "Create-UpdateFeed.ps1") `
    -PublishDir $publishDir `
    -FeedDir $UpdateFeedOutput `
    -Version $Version `
    -ExeName "ZomboidGuide.exe"

if ($LASTEXITCODE -ne 0) {
    throw "Create-UpdateFeed.ps1 fehlgeschlagen."
}

Write-Host ""
Write-Host "Release-Artefakte erstellt:"
Write-Host "  Publish: $publishDir"
Write-Host "  Update : $UpdateFeedOutput"
Write-Host ""
Write-Host "Installer bauen (Inno Setup erforderlich):"
Write-Host "  iscc /DMyAppVersion=$Version /DSourceDir=`"$publishDir`" /DOutputDir=`"$installerDir`" `"$scriptRoot\Installer.iss`""
