param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/PowerGauge/PowerGauge.csproj"
$publishProfile = "WindowsNativeAot"
$publishDir = Join-Path $repoRoot "artifacts/publish/windows-nativeaot"
$packageDir = Join-Path $repoRoot "artifacts/package/windows-nativeaot"
$releaseDir = Join-Path $repoRoot "artifacts/release"
$portableZip = Join-Path $releaseDir "PowerGauge-windows-x64-$Version.zip"
$installerScript = Join-Path $repoRoot "installer/PowerGauge.iss"
$setupIconFile = Join-Path $repoRoot "src/PowerGauge/Assets/avalonia-logo.ico"

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

if (Test-Path $packageDir) {
    Remove-Item $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

Write-Host "Preparing package contents..."
$publishFiles = Get-ChildItem -Path $publishDir -Recurse -File | Where-Object { $_.Extension -ne '.pdb' }
foreach ($file in $publishFiles) {
    $relativePath = [System.IO.Path]::GetRelativePath($publishDir, $file.FullName)
    $destinationPath = Join-Path $packageDir $relativePath
    $destinationDirectory = Split-Path -Parent $destinationPath

    if (-not (Test-Path $destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    Copy-Item $file.FullName -Destination $destinationPath
}

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

if (Test-Path $portableZip) {
    Remove-Item $portableZip -Force
}

Write-Host "Creating portable zip..."
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $portableZip -CompressionLevel Optimal

$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $isccPath) {
    throw "ISCC.exe not found. Install Inno Setup 6 and rerun the script."
}

Write-Host "Building installer..."
& $isccPath "/DMyAppVersion=$Version" "/DMyPublishDir=$packageDir" "/DMySetupIconFile=$setupIconFile" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Release assets created in $releaseDir"
