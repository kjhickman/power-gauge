param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/ViperLink.App/ViperLink.App.csproj"
$publishProfile = "WindowsNativeAot"
$publishDir = Join-Path $repoRoot "artifacts/publish/windows-nativeaot"
$releaseDir = Join-Path $repoRoot "artifacts/release"
$portableZip = Join-Path $releaseDir "ViperLink-windows-x64-$Version.zip"
$installerScript = Join-Path $repoRoot "installer/ViperLink.iss"
$setupIconFile = Join-Path $repoRoot "src/ViperLink.App/Assets/avalonia-logo.ico"

$numericVersionMatch = [System.Text.RegularExpressions.Regex]::Match($Version, '^\d+(?:\.\d+){0,3}')
if (-not $numericVersionMatch.Success) {
    throw "Version '$Version' must start with a numeric version like 0.1.0"
}

$fileVersion = $numericVersionMatch.Value

Write-Host "Publishing NativeAOT build $Version..."
dotnet publish $projectPath -p:PublishProfile=$publishProfile -p:Version=$Version -p:FileVersion=$fileVersion -p:InformationalVersion=$Version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

if (Test-Path $portableZip) {
    Remove-Item $portableZip -Force
}

Write-Host "Creating portable zip..."
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $portableZip -CompressionLevel Optimal

$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $isccPath) {
    throw "ISCC.exe not found. Install Inno Setup 6 and rerun the script."
}

Write-Host "Building installer..."
& $isccPath "/DMyAppVersion=$Version" "/DMyPublishDir=$publishDir" "/DMySetupIconFile=$setupIconFile" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Release assets created in $releaseDir"
