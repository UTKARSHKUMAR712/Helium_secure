# Helium Pro Windows Installer / Uninstaller
param(
    [switch]$Uninstall,
    [string]$InstallPath = "$env:LOCALAPPDATA\Programs\HeliumPro"
)

$ErrorActionPreference = "Stop"

$AppName = "Helium Pro"
$ExeName = "HeliumSecure.exe"
$UninstallRegKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HeliumPro"

if ($Uninstall) {
    Write-Host "Uninstalling $AppName..." -ForegroundColor Yellow

    # Remove Desktop Shortcut
    $DesktopShortcut = "$([Environment]::GetFolderPath('Desktop'))\$AppName.lnk"
    if (Test-Path $DesktopShortcut) { Remove-Item -Force $DesktopShortcut }

    # Remove Start Menu Shortcut
    $StartMenuShortcut = "$([Environment]::GetFolderPath('Programs'))\$AppName.lnk"
    if (Test-Path $StartMenuShortcut) { Remove-Item -Force $StartMenuShortcut }

    # Remove Registry Key
    if (Test-Path $UninstallRegKey) { Remove-Item -Recurse -Force $UninstallRegKey }

    # Remove Installed Files
    if (Test-Path $InstallPath) { Remove-Item -Recurse -Force $InstallPath }

    Write-Host "$AppName has been cleanly uninstalled." -ForegroundColor Green
    return
}

Write-Host "Installing $AppName to: $InstallPath" -ForegroundColor Cyan

# 1. Create target directory
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null
}

# 2. Copy application files from script root to target
$SourceDir = $PSScriptRoot
Get-ChildItem -Path $SourceDir -Exclude "*.zip" | Copy-Item -Destination $InstallPath -Recurse -Force

# 3. Create Desktop and Start Menu Shortcuts
$WshShell = New-Object -ComObject WScript.Shell
$TargetExe = "$InstallPath\$ExeName"

# Desktop
$DesktopPath = [Environment]::GetFolderPath('Desktop')
$Shortcut = $WshShell.CreateShortcut("$DesktopPath\$AppName.lnk")
$Shortcut.TargetPath = $TargetExe
$Shortcut.WorkingDirectory = $InstallPath
$Shortcut.Description = "Launch Helium Pro with Encrypted Profiles"
$Shortcut.Save()

# Start Menu
$ProgramsPath = [Environment]::GetFolderPath('Programs')
$StartShortcut = $WshShell.CreateShortcut("$ProgramsPath\$AppName.lnk")
$StartShortcut.TargetPath = $TargetExe
$StartShortcut.WorkingDirectory = $InstallPath
$StartShortcut.Description = "Launch Helium Pro with Encrypted Profiles"
$StartShortcut.Save()

# 4. Register in Windows Add/Remove Programs
if (-not (Test-Path $UninstallRegKey)) {
    New-Item -Path $UninstallRegKey -Force | Out-Null
}

Set-ItemProperty -Path $UninstallRegKey -Name "DisplayName" -Value $AppName
Set-ItemProperty -Path $UninstallRegKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $UninstallRegKey -Name "Publisher" -Value "The Helium Pro Authors"
Set-ItemProperty -Path $UninstallRegKey -Name "InstallLocation" -Value $InstallPath
Set-ItemProperty -Path $UninstallRegKey -Name "DisplayIcon" -Value "$TargetExe,0"
Set-ItemProperty -Path $UninstallRegKey -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$InstallPath\Install-HeliumPro.ps1`" -Uninstall"

Write-Host "`n$AppName successfully installed!" -ForegroundColor Green
Write-Host "You can launch $AppName from your Desktop or Start Menu." -ForegroundColor Green
