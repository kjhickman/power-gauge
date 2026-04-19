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

Write-Host "Publishing NativeAOT build $Version..."
dotnet publish $projectPath -p:PublishProfile=$publishProfile -p:Version=$Version -p:FileVersion=$Version -p:InformationalVersion=$Version

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
& $isccPath "/DMyAppVersion=$Version" "/DMyPublishDir=$publishDir" $installerScript

Write-Host "Release assets created in $releaseDir"
