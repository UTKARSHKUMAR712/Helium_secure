# Helium Pro — Secure Windows Browser Vault

[![Build](https://img.shields.io/badge/build-passing-brightgreen)](#building-from-source)
[![Tests](https://img.shields.io/badge/tests-76%20passing-brightgreen)](#building-from-source)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue)](#prerequisites)
[![License](https://img.shields.io/badge/license-MIT-blue)](#license)

**Helium Pro** is a hardened Windows desktop application that wraps the [Helium browser](https://github.com/imputnet/helium-windows) (Chromium-based) with per-profile AES-256-GCM encrypted vaults, Argon2id key derivation, a two-stage authentication lockout policy, NTFS DACL enforcement, and a disposable guest browsing mode.

> **Zero-Knowledge Design:** Helium Pro never stores or logs plaintext passwords or encryption keys. If a master password is lost, the encrypted data is mathematically unrecoverable.

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
Change the master password for any locked vault from inside the UI. The vault is decrypted in volatile memory with the old Argon2id key and immediately re-encrypted with a new fresh salt — no plaintext data ever touches disk during the re-encryption. Wrong current-password attempts count against the lockout policy.

### 4. Disposable Guest Mode
- Each guest session gets a fresh isolated temporary directory.
- On session end, all temporary files, cookies, history, and cache are shredded from disk.
- Completely independent from private profiles.

### 5. Integrated Update Center
- **Launcher updates:** Checks GitHub Releases on `UTKARSHKUMAR712/Helium_secure`, downloads the installer, and launches it while exiting gracefully.
- **Browser updates:** Checks `imputnet/helium-windows` releases, shows the available version, and opens the official download page — never touches your vaults.

### 6. Premium Windows UI
- Dark-first Electric Indigo / Midnight Charcoal theme.
- Sidebar navigation: Dashboard, Vaults, Security Architecture, Storage & Settings, Update Center.
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

HeliumSecure.Tests          (xUnit test suite — 76+ tests)
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

# Run the full automated test suite (76+ tests)
dotnet test -c Release
```

### Packaging a Standalone Distribution

The packaging script publishes the launcher, bundles the installed Helium browser binaries, runs all tests, and creates a ZIP archive:

```powershell
powershell -ExecutionPolicy Bypass -File build\package.ps1
```

Outputs:
- `dist\HeliumPro\` — standalone folder (copy-anywhere)
- `dist\HeliumPro_v1.0.0_win-x64.zip` — distributable archive (~199 MB with browser)

> **Note:** The packaging script copies your locally installed Helium browser from `%LOCALAPPDATA%\imput\Helium\Application\` into `dist\HeliumPro\browser\`. Helium must be installed first from [helium-windows releases](https://github.com/imputnet/helium-windows/releases).

---

## Installation

### Option A: Installer Script (Recommended)
Run the installer included in the distribution package:

```powershell
# From extracted ZIP or dist\HeliumPro\
powershell -ExecutionPolicy Bypass -File Install-HeliumPro.ps1
```

This will:
1. Copy application files to `%LOCALAPPDATA%\Programs\HeliumPro\`
2. Create a **Desktop shortcut** — "Helium Pro"
3. Create a **Start Menu shortcut**
4. Register in **Windows Settings → Apps** for clean uninstall

### Option B: Portable (No Install)
Extract `HeliumPro_v1.0.0_win-x64.zip` anywhere and run `HeliumSecure.exe` directly.

### Browser Resolution Priority
Helium Pro finds the browser executable in this order:
1. `HELIUM_EXE` environment variable (if set)
2. `browser\chrome.exe` relative to the launcher (bundled portable mode)
3. `%LOCALAPPDATA%\imput\Helium\Application\chrome.exe` (installed system Helium)

---

## Usage

1. **Launch Helium Pro** from Desktop or Start Menu.
2. **Navigate to "Profile Vaults"** in the sidebar.
3. **Create a vault** — give it a name and set a strong master password.
4. **Select your vault**, enter the master password, and click **"Unlock & Launch Browser"**.
5. Browse normally. When you close the Helium browser, the vault is automatically re-encrypted and the working directory is shredded.
6. For temporary browsing, click **"Browse as Guest"** — all data is purged on exit.

### Changing Your Master Password
Select a locked profile in the Vaults page and click **"Change Password"**. Enter your current password (which enforces the lockout policy), then enter and confirm the new password. Re-encryption happens in memory — no plaintext data touches disk.

---

## Update Center

Navigate to **"Update Center"** in the sidebar to check for updates for both Helium Pro and the Helium browser.

### Launcher Updates (Helium Pro)
- Checks GitHub Releases at `github.com/UTKARSHKUMAR712/Helium_secure`
- Downloads the Windows installer asset
- Launches the installer and exits the launcher gracefully
- Never silently installs — always requires explicit confirmation

### Browser Updates (Helium Browser)
- Checks GitHub Releases at `github.com/imputnet/helium-windows`
- Shows the installed version vs. available version
- Opens the official download page for the x64 installer
- Does **not** touch your encrypted vaults or modify browser files while the browser is running

---

## Security Notes and Limitations

### What is Protected
- **At rest:** All private browsing data (cookies, history, credentials, extensions, cache) is encrypted at rest inside `.hsv` vault files using AES-256-GCM with Argon2id-derived keys.
- **In transit:** Profile GUID is bound as AAD — modified ciphertext, swapped profiles, or truncated vaults are rejected on decryption.
- **Against brute force:** Argon2id with 64 MB memory cost and 3 iterations makes offline attacks against strong passwords impractical.

### Known Limitations (Not Covered)

> [!WARNING]
> The following threats are **NOT mitigated** by Helium Pro and are outside its threat model.

| Threat | Status |
|---|---|
| Malware running as your Windows user | ❌ Not mitigated — can inspect process memory while unlocked |
| Administrator / SYSTEM level access | ❌ Not mitigated |
| Keyloggers capturing the master password at entry time | ❌ Not mitigated |
| Cold-boot attacks | ❌ Not mitigated |
| Screen capture / shoulder surfing | ❌ Not mitigated |
| Plaintext working directory while profile is unlocked | ⚠️ Protected by NTFS DACL (current user only) but not encrypted at rest |
| Browser renderer vulnerabilities | ⚠️ Chromium is regularly updated; keep Helium browser up to date |

### Vault File Format
```
[Header]
  Magic bytes (4)     "HSEC"
  Version (1)         Format version
  Salt (32 bytes)     Argon2id salt (unique per vault/re-encryption)
  Iterations (4)      Argon2id iteration count
  MemorySizeKB (4)    Argon2id memory parameter
  Parallelism (4)     Argon2id degree of parallelism
  Nonce (12 bytes)    AES-GCM nonce (unique per encryption)
  Tag (16 bytes)      AES-GCM authentication tag
  CiphertextLength (8)
[Ciphertext]
  ZIP archive (variable) encrypted by AES-256-GCM
  AAD: profile GUID bytes (authenticated but not encrypted)
```

---

## Uninstalling

```powershell
# Run the uninstaller
powershell -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\Programs\HeliumPro\Install-HeliumPro.ps1" -Uninstall
```

This removes:
- The installed `%LOCALAPPDATA%\Programs\HeliumPro\` folder
- Desktop shortcut
- Start Menu shortcut
- Windows Apps & Features registry entry

> [!IMPORTANT]
> Uninstalling Helium Pro does **not** delete your encrypted vaults or profile manifests. They remain in `%LOCALAPPDATA%\HeliumSecure\`. To permanently delete all private data, use the **"Delete Vault"** action within Helium Pro before uninstalling, or manually delete `%LOCALAPPDATA%\HeliumSecure\`.

---

## Release Artifacts

| Artifact | Location |
|---|---|
| Source code | This repository, branch `feature/helium-secure` |
| Distribution folder | `dist\HeliumPro\` (after running `build\package.ps1`) |
| Standalone ZIP | `dist\HeliumPro_v1.0.0_win-x64.zip` (~199 MB) |
| Installer script | `build\Install-HeliumPro.ps1` |

> GitHub Releases with attached installer will be published when code signing is configured. Currently, build from source or use the ZIP archive.

---

## Repository Structure

```
Helium_secure/
├── src/
│   ├── HeliumSecure.Core/          Core library (crypto, vaults, profiles, process, update)
│   │   ├── Crypto/                 Argon2id, AES-256-GCM, secure memory wipe
│   │   ├── Guest/                  Disposable guest session manager
│   │   ├── Hardware/               GPU vendor identification and WMI metrics
│   │   ├── Models/                 ProfileInfo, ProfileManifest, VaultFileHeader
│   │   ├── Process/                BrowserProcessManager + Win32 Job Object
│   │   ├── Profiles/               ProfileManager (create, unlock, change password, delete)
│   │   ├── Security/               NTFS DACL enforcement
│   │   ├── Time/                   IClock / SystemClock (testable abstraction)
│   │   ├── Update/                 UpdateService (GitHub release checks, download, launch)
│   │   └── Vault/                  VaultManager (init, lock, unlock, re-encrypt, recovery)
│   └── HeliumSecure/               WPF desktop launcher
│       ├── Views/                  Dialogs (Create, Rename, Delete, ChangePassword, ClearStorage)
│       ├── Helpers/                WindowThemeHelper
│       ├── App.xaml                Global premium styles
│       └── MainWindow.xaml         Main window with 5-page sidebar navigation
├── tests/
│   └── HeliumSecure.Tests/         xUnit automated test suite (76+ tests)
├── build/
│   ├── package.ps1                 Automated build, test, and packaging pipeline
│   └── Install-HeliumPro.ps1       Per-user installer/uninstaller
├── docs/
│   ├── ARCHITECTURE.md             Technical design specification
│   └── SECURITY_ASSESSMENT.md      Threat model and cryptographic verification
├── HeliumSecure.sln
└── README.md
```

---

## License

MIT License — see `LICENSE` for details.

The bundled Helium browser is copyright The Helium Authors and is distributed under its own terms. See [github.com/imputnet/helium-windows](https://github.com/imputnet/helium-windows) for details.
