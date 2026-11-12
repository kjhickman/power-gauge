param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "0.2.1"
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
$dmgStageDir = Join-Path $repoRoot "artifacts/package/macos-dmg"
$releaseDir = Join-Path $repoRoot "artifacts/release"
$releaseZip = Join-Path $releaseDir "PowerGauge-macos-arm64-$Version.zip"
$releaseDmg = Join-Path $releaseDir "PowerGauge-macos-arm64-$Version.dmg"

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
Copy-Item -Path (Join-Path $publishDir "*") -Destination $macOsDir -Recurse -Exclude "*.pdb"

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

    Write-Host "Applying ad-hoc code signature to app bundle..."
    & codesign --force --deep --sign - $appBundlePath
    if ($LASTEXITCODE -ne 0) {
        throw "codesign failed with exit code $LASTEXITCODE"
    }

    & codesign --verify --deep --strict $appBundlePath
    if ($LASTEXITCODE -ne 0) {
        throw "codesign verification failed with exit code $LASTEXITCODE"
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

if (Test-Path $releaseDmg) {
    Remove-Item $releaseDmg -Force
}

if (-not $IsMacOS) {
    throw "Build-PowerGaugeMacOsRelease.ps1 must run on macOS to package the app bundle."
}

Write-Host "Creating release zip $releaseZip..."
& ditto -c -k --norsrc --keepParent $appBundlePath $releaseZip
if ($LASTEXITCODE -ne 0) {
    throw "ditto failed with exit code $LASTEXITCODE"
}

Write-Host "Preparing DMG staging directory..."
if (Test-Path $dmgStageDir) {
    Remove-Item $dmgStageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $dmgStageDir -Force | Out-Null
& ditto $appBundlePath (Join-Path $dmgStageDir "$appName.app")
if ($LASTEXITCODE -ne 0) {
    throw "ditto to DMG staging directory failed with exit code $LASTEXITCODE"
}

& ln -s /Applications (Join-Path $dmgStageDir "Applications")
if ($LASTEXITCODE -ne 0) {
    throw "Failed to create Applications symlink with exit code $LASTEXITCODE"
}

Write-Host "Creating release DMG $releaseDmg..."
$dmgCreated = $false
for ($attempt = 1; $attempt -le 3; $attempt++) {
    & hdiutil create -volname $appName -srcfolder $dmgStageDir -ov -format UDZO $releaseDmg
    if ($LASTEXITCODE -eq 0) {
        $dmgCreated = $true
        break
    }

    Write-Host "hdiutil create attempt $attempt failed with exit code $LASTEXITCODE; retrying..."
    Start-Sleep -Seconds 2
}

if (-not $dmgCreated) {
    throw "hdiutil create failed after 3 attempts."
}

Write-Host "Created macOS release artifacts at $releaseZip and $releaseDmg"
