# Helium Pro Packaging Script
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\..\dist"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   HELIUM PRO - BUILD & PACKAGING PIPELINE" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$RepoRoot = Resolve-Path "$PSScriptRoot\.."
$StagingDir = "$OutputDir\HeliumPro"
$ZipPath = "$OutputDir\HeliumPro_v1.0.0_win-x64.zip"

# Step 1: Ensure directories exist
Write-Host "`n[1/5] Preparing staging directories..." -ForegroundColor Yellow
if (-not (Test-Path $StagingDir)) {
    New-Item -ItemType Directory -Force -Path $StagingDir | Out-Null
}
if (-not (Test-Path "$StagingDir\browser")) {
    New-Item -ItemType Directory -Force -Path "$StagingDir\browser" | Out-Null
}
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

# Step 2: Publish Launcher
Write-Host "`n[2/5] Publishing HeliumSecure Launcher ($Configuration)..." -ForegroundColor Yellow
dotnet publish "$RepoRoot\src\HeliumSecure\HeliumSecure.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o "$StagingDir"

# Step 3: Bundle Helium Browser binaries if missing
Write-Host "`n[3/5] Bundling upstream Helium browser binaries..." -ForegroundColor Yellow
$InstalledBrowserDir = "$env:LOCALAPPDATA\imput\Helium\Application"
if (-not (Test-Path "$StagingDir\browser\chrome.exe")) {
    if (Test-Path $InstalledBrowserDir) {
        Write-Host "Copying Helium browser files from $InstalledBrowserDir..."
        Copy-Item -Path "$InstalledBrowserDir\*" -Destination "$StagingDir\browser" -Recurse -Force
    } else {
        Write-Warning "Upstream Helium directory not found at $InstalledBrowserDir."
    }
} else {
    Write-Host "Bundled browser files already present in $StagingDir\browser."
}

# Add Installer Script to package
$InstallerScriptSource = "$RepoRoot\build\Install-HeliumPro.ps1"
if (Test-Path $InstallerScriptSource) {
    Copy-Item -Path $InstallerScriptSource -Destination "$StagingDir\Install-HeliumPro.ps1" -Force
}

# Step 4: Run Automated Verification Tests
Write-Host "`n[4/5] Running automated verification test suite..." -ForegroundColor Yellow
dotnet test "$RepoRoot\HeliumSecure.sln" -c $Configuration --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Verification tests failed! Packaging aborted."
}

# Step 5: Create Distribution ZIP
Write-Host "`n[5/5] Generating distribution archive ($ZipPath)..." -ForegroundColor Yellow
if (Test-Path $ZipPath) {
    Remove-Item -Force $ZipPath
}

Compress-Archive -Path "$StagingDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host "   PACKAGING COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "   Distribution Folder: $StagingDir" -ForegroundColor Green
Write-Host "   Archive Package:     $ZipPath" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
