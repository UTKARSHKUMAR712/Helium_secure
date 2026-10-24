# HeliumSecure Windows Installer / Uninstaller
param(
    [switch]$Uninstall,
    [string]$InstallPath = "$env:LOCALAPPDATA\Programs\HeliumSecure"
)

$ErrorActionPreference = "Stop"

$AppName = "HeliumSecure"
$ExeName = "HeliumSecure.exe"
$UninstallRegKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HeliumSecure"

if ($Uninstall) {
    Write-Host "Uninstalling $AppName..." -ForegroundColor Yellow

    # Safely terminate any running HeliumSecure instances
    Get-Process HeliumSecure -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    # Remove Desktop Shortcut
    $DesktopShortcut = "$([Environment]::GetFolderPath('Desktop'))\$AppName.lnk"
    if (Test-Path $DesktopShortcut) { Remove-Item -Force $DesktopShortcut }

    # Remove Start Menu Shortcut
    $StartMenuShortcut = "$([Environment]::GetFolderPath('Programs'))\$AppName.lnk"
    if (Test-Path $StartMenuShortcut) { Remove-Item -Force $StartMenuShortcut }

    # Remove Registry Key
    if (Test-Path $UninstallRegKey) { Remove-Item -Recurse -Force $UninstallRegKey }

    # Remove Installed Application Binaries (NEVER touches %LOCALAPPDATA%\HeliumSecure vaults)
    if (Test-Path $InstallPath) { Remove-Item -Recurse -Force $InstallPath }

    Write-Host "$AppName has been cleanly uninstalled." -ForegroundColor Green
    Write-Host "NOTE: Your encrypted vaults in %LOCALAPPDATA%\HeliumSecure were preserved." -ForegroundColor Cyan
    return
}

Write-Host "Installing $AppName to: $InstallPath" -ForegroundColor Cyan

# 1. Create target directory
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null
}

# 2. Copy application files from script root to target
$SourceDir = $PSScriptRoot
Get-ChildItem -Path $SourceDir -Exclude "*.zip", "*.exe.tmp" | Copy-Item -Destination $InstallPath -Recurse -Force

# 3. Create Desktop and Start Menu Shortcuts
$WshShell = New-Object -ComObject WScript.Shell
$TargetExe = "$InstallPath\$ExeName"

# Desktop
$DesktopPath = [Environment]::GetFolderPath('Desktop')
$Shortcut = $WshShell.CreateShortcut("$DesktopPath\$AppName.lnk")
$Shortcut.TargetPath = $TargetExe
$Shortcut.WorkingDirectory = $InstallPath
$Shortcut.Description = "Launch HeliumSecure with Encrypted Profiles"
$Shortcut.Save()

# Start Menu
$ProgramsPath = [Environment]::GetFolderPath('Programs')
$StartShortcut = $WshShell.CreateShortcut("$ProgramsPath\$AppName.lnk")
$StartShortcut.TargetPath = $TargetExe
$StartShortcut.WorkingDirectory = $InstallPath
$StartShortcut.Description = "Launch HeliumSecure with Encrypted Profiles"
$StartShortcut.Save()

# 4. Register in Windows Add/Remove Programs
if (-not (Test-Path $UninstallRegKey)) {
    New-Item -Path $UninstallRegKey -Force | Out-Null
}

Set-ItemProperty -Path $UninstallRegKey -Name "DisplayName" -Value $AppName
Set-ItemProperty -Path $UninstallRegKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $UninstallRegKey -Name "Publisher" -Value "The HeliumSecure Authors"
Set-ItemProperty -Path $UninstallRegKey -Name "InstallLocation" -Value $InstallPath
Set-ItemProperty -Path $UninstallRegKey -Name "DisplayIcon" -Value "$TargetExe,0"
Set-ItemProperty -Path $UninstallRegKey -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$InstallPath\Install-HeliumSecure.ps1`" -Uninstall"

Write-Host "`n$AppName successfully installed!" -ForegroundColor Green
Write-Host "You can launch $AppName from your Desktop or Start Menu." -ForegroundColor Green
