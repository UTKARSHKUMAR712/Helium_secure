# HeliumSecure Windows Installer / Uninstaller (Compatibility Alias)
param(
    [switch]$Uninstall,
    [string]$InstallPath = "$env:LOCALAPPDATA\Programs\HeliumSecure"
)

& "$PSScriptRoot\Install-HeliumSecure.ps1" -Uninstall:$Uninstall -InstallPath:$InstallPath

