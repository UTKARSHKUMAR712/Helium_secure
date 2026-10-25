# HeliumSecure Packaging & Installer Pipeline
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\..\dist"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   HELIUMSECURE - BUILD & PACKAGING PIPELINE" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$RepoRoot = Resolve-Path "$PSScriptRoot\.."
$StagingDir = "$OutputDir\HeliumSecure"
$ZipPath = "$OutputDir\HeliumSecure_v1.0.3_win-x64.zip"
$SetupExePath = "$OutputDir\HeliumSecure-Setup.exe"

# Step 1: Clean legacy artifacts and ensure staging directories exist
Write-Host "`n[1/6] Cleaning legacy artifacts and preparing staging directories..." -ForegroundColor Yellow
$LegacyProDir = "$OutputDir\HeliumPro"
if (Test-Path $LegacyProDir) {
    Write-Host "Removing legacy directory: $LegacyProDir"
    Remove-Item -Path $LegacyProDir -Recurse -Force -ErrorAction SilentlyContinue
}
Get-ChildItem -Path $OutputDir -Filter "HeliumPro*.zip" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Removing legacy zip: $($_.FullName)"
    Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}
if (-not (Test-Path $StagingDir)) {
    New-Item -ItemType Directory -Force -Path $StagingDir | Out-Null
}
if (-not (Test-Path "$StagingDir\browser")) {
    New-Item -ItemType Directory -Force -Path "$StagingDir\browser" | Out-Null
}

# Step 2: Publish Launcher
Write-Host "`n[2/6] Publishing HeliumSecure Launcher ($Configuration)..." -ForegroundColor Yellow
dotnet publish "$RepoRoot\src\HeliumSecure\HeliumSecure.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o "$StagingDir"

# Step 3: Bundle Helium Browser binaries
Write-Host "`n[3/6] Bundling upstream Helium browser binaries..." -ForegroundColor Yellow
$InstalledBrowserDir = "$env:LOCALAPPDATA\imput\Helium\Application"
if (-not (Test-Path "$StagingDir\browser\chrome.exe")) {
    if (Test-Path $InstalledBrowserDir) {
        Write-Host "Copying Helium browser files from $InstalledBrowserDir..."
        Copy-Item -Path "$InstalledBrowserDir\*" -Destination "$StagingDir\browser" -Recurse -Force
    } else {
        # Check if legacy staging has it
        $LegacyDir = "$OutputDir\HeliumPro\browser"
        if (Test-Path "$LegacyDir\chrome.exe") {
            Write-Host "Copying Helium browser files from existing package..."
            Copy-Item -Path "$LegacyDir\*" -Destination "$StagingDir\browser" -Recurse -Force
        } else {
            Write-Warning "Upstream Helium directory not found at $InstalledBrowserDir."
        }
    }
} else {
    Write-Host "Bundled browser files already present in $StagingDir\browser."
}

# Storage Optimization: Purge redundant files and debug symbols to reduce installation footprint
Write-Host "Optimizing payload storage size (purging symbols and redundant setup artifacts)..."
Get-ChildItem -Path $StagingDir -Include *.pdb, *.xml -Recurse -File | ForEach-Object {
    Remove-Item -Force $_.FullName -ErrorAction SilentlyContinue
}
Get-ChildItem -Path "$StagingDir\browser" -Directory -Filter "Installer" -Recurse | ForEach-Object {
    Write-Host "Removing redundant upstream installer folder: $($_.FullName)"
    Remove-Item -Recurse -Force $_.FullName -ErrorAction SilentlyContinue
}

$UncompressedSizeMB = [math]::Round(((Get-ChildItem -Path $StagingDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 2)
Write-Host "Optimized uncompressed payload footprint: $UncompressedSizeMB MB" -ForegroundColor Green

# Copy Installer Script to package
$InstallerScriptSource = "$RepoRoot\build\Install-HeliumSecure.ps1"
if (Test-Path $InstallerScriptSource) {
    Copy-Item -Path $InstallerScriptSource -Destination "$StagingDir\Install-HeliumSecure.ps1" -Force
}

# Step 4: Create Distribution ZIP
Write-Host "`n[4/6] Generating distribution archive ($ZipPath)..." -ForegroundColor Yellow
if (Test-Path $ZipPath) {
    Remove-Item -Force $ZipPath
}
Compress-Archive -Path "$StagingDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal

# Step 5: Run Automated Verification Tests
Write-Host "`n[5/6] Running automated verification test suite..." -ForegroundColor Yellow
dotnet test "$RepoRoot\HeliumSecure.sln" -c $Configuration --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Verification tests failed! Packaging aborted."
}

# Step 6: Build Windows Setup.exe via Inno Setup
Write-Host "`n[6/6] Compiling Windows Installer (HeliumSecure-Setup.exe)..." -ForegroundColor Yellow
$IsccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
) | Where-Object { $_ -and (Test-Path $_) }

$IsccExe = $IsccCandidates | Select-Object -First 1

if ($IsccExe) {
    Write-Host "Using Inno Setup compiler: $IsccExe"
    $IssScript = "$RepoRoot\build\installer.iss"
    & "$IsccExe" /Q "$IssScript"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Inno Setup compilation failed!"
    }
    if (Test-Path $SetupExePath) {
        $Hash = (Get-FileHash -Path $SetupExePath -Algorithm SHA256).Hash
        $SizeMB = [math]::Round(((Get-Item $SetupExePath).Length / 1MB), 2)
        Write-Host "Created: $SetupExePath ($SizeMB MB, SHA256: $Hash)" -ForegroundColor Green
    }
} else {
    Write-Warning "Inno Setup compiler (ISCC.exe) was not found in standard paths. Skipping Setup.exe compilation."
}

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host "   PACKAGING COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "   Distribution Folder: $StagingDir" -ForegroundColor Green
Write-Host "   Archive Package:     $ZipPath" -ForegroundColor Green
if (Test-Path $SetupExePath) {
    Write-Host "   Windows Setup:       $SetupExePath" -ForegroundColor Green
}
Write-Host "==========================================" -ForegroundColor Green
