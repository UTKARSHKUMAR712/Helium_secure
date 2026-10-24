# HeliumSecure — Secure Windows Browser Vault

[![Build](https://img.shields.io/badge/build-passing-brightgreen)](#building-from-source)
[![Tests](https://img.shields.io/badge/tests-84%20passing-brightgreen)](#building-from-source)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue)](#prerequisites)
[![License](https://img.shields.io/badge/license-MIT-blue)](#license)

**HeliumSecure** is a hardened Windows desktop application that wraps the [Helium browser](https://github.com/imputnet/helium-windows) (Chromium-based) with per-profile AES-256-GCM encrypted vaults, Argon2id key derivation, a two-stage authentication lockout policy, NTFS DACL enforcement, and a disposable guest browsing mode.

> **Zero-Knowledge Design:** HeliumSecure never stores or logs plaintext passwords or encryption keys. If a master password is lost, the encrypted data is mathematically unrecoverable.

---

## Table of Contents

- [Key Features](#key-features)
- [Architecture Overview](#architecture-overview)
- [Prerequisites](#prerequisites)
- [Building From Source](#building-from-source)
- [Installation](#installation)
- [Usage](#usage)
- [Update Center](#update-center)
- [Security Notes and Limitations](#security-notes-and-limitations)
- [Uninstalling](#uninstalling)
- [Release Artifacts](#release-artifacts)
- [Repository Structure](#repository-structure)

---

## Key Features

### 1. Hardware-Isolated Profile Vaults
- Every private profile is stored at rest inside an **AES-256-GCM encrypted vault** (`.hsv` container format).
- Key derivation uses **Argon2id** (memory-hard KDF) with a 32-byte cryptographically secure random salt per profile — resisting GPU/ASIC brute-force.
- Profile GUID is bound as **Authenticated Additional Data (AAD)**, preventing cross-profile cipher transplant attacks.
- Vault re-encryption via atomic swap (`.tmp` → `.bak` → `.hsv`) ensures a power cut or crash never corrupts a profile.

### 2. Two-Stage Authentication Policy
| Stage | Trigger | Consequence |
|---|---|---|
| Stage 1 | 3 consecutive wrong passwords | 30-minute lockout. Clock-rollback resistant. |
| Stage 2 | 5 wrong passwords after lockout expires | **Irreversible terminal vault destruction** — vault, backups, working files permanently shredded. |

A correct password during Stage 2 resets all counters to normal.

### 3. Master Password Change
Change the master password for any locked vault directly from the UI. The vault is decrypted in volatile memory with the old Argon2id key and immediately re-encrypted with a fresh salt and nonce — zero plaintext data touches disk during the re-encryption. Incorrect current-password attempts count against the lockout policy.

### 4. Disposable Guest Mode
- Each guest session gets a fresh, isolated temporary directory.
- On session exit, all temporary files, cookies, history, and cache are purged and shredded from disk.
- Completely independent from private profiles.

### 5. Integrated Update Center
- **Launcher updates:** Checks GitHub Releases on `UTKARSHKUMAR712/Helium_secure`, downloads `HeliumSecure-Setup.exe` with integrity verification (length and PE header checks), launches the installer, and exits gracefully.
- **Browser updates:** Checks `imputnet/helium-windows` releases, shows the available version, and opens the official download page — never touches your vaults.

### 6. Premium Windows UI
- Dark-first Electric Indigo / Midnight Charcoal theme (`#0B0E17`).
- Sidebar navigation: Dashboard, Profile Vaults, Security Architecture, Storage & Settings, Update Center.
- Real-time lockout countdown, stage banners, and attempt remaining indicators.
- Ultra-thin custom scrollbars, rounded cards, smooth hover states.

---

## Architecture Overview

```
HeliumSecure.Core           (class library — no UI dependency)
├── Crypto/                 Argon2id KDF, AES-256-GCM encrypt/decrypt, secure memory wipe
├── Guest/                  Disposable guest session directory lifecycle
├── Hardware/               GPU vendor identification, WMI metrics provider
├── Models/                 ProfileInfo, ProfileManifest, VaultFileHeader
├── Process/                BrowserProcessManager, Win32 Job Object tracking
├── Profiles/               ProfileManager — create, unlock, change password, delete
├── Security/               NTFS DACL enforcement (SecureFile / SecureDirectory)
├── Update/                 UpdateService — GitHub release checks, download, launch
├── Vault/                  VaultManager — init, lock, unlock, re-encrypt, crash recovery
└── Time/                   IClock / SystemClock (testable time abstraction)

HeliumSecure                (WPF launcher — Windows only)
├── App.xaml                Global styles: colors, scrollbars, TextBox, PasswordBox, buttons
├── MainWindow.xaml         Main shell — sidebar + 5 page views
├── Views/                  Modal dialogs (Create/Rename/Delete Profile, Change Password,
│                           Clear Storage)
└── Helpers/                WindowThemeHelper (dark title bar)

HeliumSecure.Tests          (xUnit test suite — 84+ tests)
├── AuthenticationPolicyTests.cs
├── CryptoTests.cs
├── GuestSessionTests.cs
├── GpuVendorIdentificationTests.cs
├── HeliumIntegrationTests.cs
├── PackagingAuditTests.cs
├── ProfileManagerTests.cs
├── ProcessManagerTests.cs
├── StorageManagementTests.cs
├── UpdateServiceTests.cs
└── VaultManagerTests.cs
```

---

## Prerequisites

| Requirement | Version |
|---|---|
| OS | Windows 10 22H2 or Windows 11 (x64) |
| .NET SDK | 8.0 (with `Microsoft.WindowsDesktop.App` runtime) |
| Inno Setup | 6.x+ (optional, for compiling `HeliumSecure-Setup.exe`) |
| PowerShell | 5.1+ or PowerShell 7+ |
| Helium Browser | [0.20.1.1+](https://github.com/imputnet/helium-windows/releases) (installed or bundled) |

---

## Building From Source

```powershell
# Clone the repository
git clone https://github.com/UTKARSHKUMAR712/Helium_secure.git
cd Helium_secure
git checkout feature/helium-secure

# Restore NuGet packages and build
dotnet build -c Release

# Run the full automated test suite (84+ tests)
dotnet test -c Release
```

### Packaging a Standalone Distribution & Installer

The packaging script publishes the launcher, bundles the installed Helium browser binaries, runs all tests, creates the ZIP archive, and compiles the Inno Setup Windows installer:

```powershell
powershell -ExecutionPolicy Bypass -File build\package.ps1
```

Generated outputs in `dist\`:
- `dist\HeliumSecure-Setup.exe` — Windows Setup Wizard installer
- `dist\HeliumSecure_v1.0.0_win-x64.zip` — portable standalone archive (~199 MB with browser)
- `dist\HeliumSecure\` — standalone unpacked directory

---

## Installation

### Option A: Windows Setup Wizard (`HeliumSecure-Setup.exe`)
Run `dist\HeliumSecure-Setup.exe`:
- Standard Windows per-user setup wizard.
- Default path: `%LOCALAPPDATA%\Programs\HeliumSecure` (no admin rights needed, handles spaces).
- Automatically registers in **Windows Settings → Installed apps**.
- Creates Start Menu shortcut and optional Desktop shortcut.
- Clean upgrades: installing a new version overwrites binaries cleanly without duplicating registry entries.

### Option B: PowerShell Installer Script
```powershell
# From extracted ZIP or dist\HeliumSecure\
powershell -ExecutionPolicy Bypass -File Install-HeliumSecure.ps1
```

### Option C: Portable Mode
Extract `HeliumSecure_v1.0.0_win-x64.zip` anywhere and run `HeliumSecure.exe` directly.

### Browser Resolution Priority
HeliumSecure finds the browser executable in this order:
1. `HELIUM_EXE` environment variable (if set)
2. `browser\chrome.exe` relative to the launcher (bundled portable mode)
3. `%LOCALAPPDATA%\imput\Helium\Application\chrome.exe` (installed system Helium)

---

## Usage

1. **Launch HeliumSecure** from Desktop or Start Menu.
2. **Navigate to "Profile Vaults"** in the sidebar.
3. **Create a vault** — enter a profile name and choose a strong master password.
4. **Select your vault**, enter the master password, and click **"Unlock & Launch Browser"**.
5. Browse normally. When you close the Helium browser window, HeliumSecure detects shutdown via Windows Job Objects, re-encrypts updated browsing data into the vault, shreds the temporary working directory, and locks the profile.
6. For temporary browsing, click **"Browse as Guest"** — all data is purged on exit.

### Changing Your Master Password
Select a locked profile in the Vaults page and click **"Change Password"**. Enter your current password (which enforces the lockout policy), then enter and confirm your new password. Re-encryption happens in memory — no plaintext data touches disk.

---

## Update Center

Navigate to **"Update Center"** in the sidebar to check for updates for both HeliumSecure and the Helium browser.

### Launcher Updates (HeliumSecure)
- Checks GitHub Releases at `github.com/UTKARSHKUMAR712/Helium_secure`.
- Downloads `HeliumSecure-Setup.exe` with integrity verification.
- Launches the installer and exits the running instance gracefully.
- Never silently installs without explicit user action.

### Browser Updates (Helium Browser)
- Checks GitHub Releases at `github.com/imputnet/helium-windows`.
- Displays the installed browser version vs. latest available version.
- Opens the official download page for the x64 installer.
- Does **not** touch your encrypted vaults or modify browser files while the browser is running.

---

## Security Notes and Limitations

### What is Protected
- **At rest:** All private browsing data (cookies, history, credentials, extensions, cache) is encrypted at rest inside `.hsv` vault files using AES-256-GCM with Argon2id-derived keys.
- **In transit:** Profile GUID is bound as AAD — modified ciphertext, swapped profiles, or truncated vaults are rejected on decryption.
- **Against brute force:** Argon2id with 64 MB memory cost and 3 iterations makes offline attacks against strong passwords computationally impractical.

### Known Limitations (Not Covered)

> [!WARNING]
> The following threats are **NOT mitigated** by HeliumSecure and are outside its threat model.

| Threat | Status |
|---|---|
| Malware running as your Windows user | ❌ Not mitigated — can inspect process memory while unlocked |
| Administrator / SYSTEM level access | ❌ Not mitigated |
| Keyloggers capturing the master password at entry time | ❌ Not mitigated |
| Cold-boot attacks | ❌ Not mitigated |
| Screen capture / shoulder surfing | ❌ Not mitigated |
| Plaintext working directory while profile is unlocked | ⚠️ Protected by NTFS DACL (current user only) but not encrypted at rest |
| Browser renderer vulnerabilities | ⚠️ Chromium is regularly updated; keep Helium browser up to date |

---

## Uninstalling

### Via Windows Settings / Control Panel
Go to **Settings → Apps → Installed apps**, locate **HeliumSecure**, and click **Uninstall**.

### Via PowerShell
```powershell
powershell -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\Programs\HeliumSecure\Install-HeliumSecure.ps1" -Uninstall
```

> [!IMPORTANT]
> **Vault Preservation on Uninstall:**
> Uninstalling HeliumSecure does **not** delete your encrypted vaults or profile manifests. They remain safely preserved in `%LOCALAPPDATA%\HeliumSecure\`. To permanently delete all private data, use the **"Delete Vault"** action within HeliumSecure before uninstalling, or manually delete `%LOCALAPPDATA%\HeliumSecure\`.

---

## Release Artifacts

| Artifact | Description | Location |
|---|---|---|
| `HeliumSecure-Setup.exe` | Windows Setup Wizard Installer | `dist\HeliumSecure-Setup.exe` |
| `HeliumSecure_v1.0.0_win-x64.zip` | Standalone Portable Archive (~199 MB) | `dist\HeliumSecure_v1.0.0_win-x64.zip` |
| `dist\HeliumSecure\` | Unpacked Distribution Directory | `dist\HeliumSecure\` |
| `Install-HeliumSecure.ps1` | PowerShell Per-User Installer | `build\Install-HeliumSecure.ps1` |
| Source code | Branch `feature/helium-secure` | This repository |

---

## License

MIT License — see `LICENSE` for details.

The bundled Helium browser is copyright The Helium Authors and is distributed under its own terms. See [github.com/imputnet/helium-windows](https://github.com/imputnet/helium-windows) for details.
