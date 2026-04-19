param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appName = "PowerGauge"
$projectPath = Join-Path $repoRoot "src/PowerGauge/PowerGauge.csproj"
$publishProfile = "MacOsArm64"
$publishDir = Join-Path $repoRoot "artifacts/publish/macos-arm64"
$appBundlePath = Join-Path $repoRoot "artifacts/package/macos/PowerGauge.app"
$infoPlistTemplate = Join-Path $repoRoot "src/PowerGauge/Properties/MacOS/Info.plist"
$contentsDir = Join-Path $appBundlePath "Contents"
$macOsDir = Join-Path $contentsDir "MacOS"
$resourcesDir = Join-Path $contentsDir "Resources"
$infoPlistPath = Join-Path $contentsDir "Info.plist"
$releaseDir = Join-Path $repoRoot "artifacts/release"
$releaseZip = Join-Path $releaseDir "PowerGauge-macos-arm64-$Version.zip"

$numericVersionMatch = [System.Text.RegularExpressions.Regex]::Match($Version, '^\d+(?:\.\d+){0,3}')
if (-not $numericVersionMatch.Success) {
    throw "Version '$Version' must start with a numeric version like 0.1.0"
}

$fileVersion = $numericVersionMatch.Value

Write-Host "Publishing macOS arm64 build $Version..."
dotnet publish $projectPath -p:PublishProfile=$publishProfile -p:Version=$Version -p:FileVersion=$fileVersion -p:InformationalVersion=$Version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

if (-not (Test-Path $infoPlistTemplate)) {
    throw "Info.plist template not found: $infoPlistTemplate"
}

Write-Host "Assembling PowerGauge.app..."
if (Test-Path $appBundlePath) {
    Remove-Item $appBundlePath -Recurse -Force
}

New-Item -ItemType Directory -Path $macOsDir -Force | Out-Null
New-Item -ItemType Directory -Path $resourcesDir -Force | Out-Null

Write-Host "Copying app files from $publishDir..."
$publishFiles = Get-ChildItem -Path $publishDir -Recurse -File | Where-Object { $_.Extension -ne '.pdb' }
foreach ($file in $publishFiles) {
    $relativePath = [System.IO.Path]::GetRelativePath($publishDir, $file.FullName)
    $destinationPath = Join-Path $macOsDir $relativePath
    $destinationDirectory = Split-Path -Parent $destinationPath

    if (-not (Test-Path $destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    Copy-Item $file.FullName -Destination $destinationPath
}

$executablePath = Join-Path $macOsDir $appName
if (-not (Test-Path $executablePath)) {
    throw "Executable not found at $executablePath after publish copy."
}

$infoPlist = Get-Content -Path $infoPlistTemplate -Raw
$infoPlist = $infoPlist.Replace("__VERSION__", $Version)
$infoPlist = $infoPlist.Replace("__BUILD_VERSION__", $fileVersion)
[System.IO.File]::WriteAllText($infoPlistPath, $infoPlist, [System.Text.Encoding]::UTF8)

if ($IsMacOS) {
    & chmod +x $executablePath
    if ($LASTEXITCODE -ne 0) {
        throw "chmod failed with exit code $LASTEXITCODE"
    }
}

if ($IsMacOS) {
    Write-Host "Removing extended attributes from app bundle..."
    & xattr -cr $appBundlePath
    if ($LASTEXITCODE -ne 0) {
        throw "xattr failed with exit code $LASTEXITCODE"
    }
}

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
if (Test-Path $releaseZip) {
    Remove-Item $releaseZip -Force
}

Write-Host "Creating release zip $releaseZip..."
if ($IsMacOS) {
    & ditto -c -k --norsrc --keepParent $appBundlePath $releaseZip
    if ($LASTEXITCODE -ne 0) {
        throw "ditto failed with exit code $LASTEXITCODE"
    }
}
else {
    throw "Build-PowerGaugeMacOsRelease.ps1 must run on macOS to package the app bundle."
}

Write-Host "Created macOS release artifact at $releaseZip"
