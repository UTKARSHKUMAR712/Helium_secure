# Helium Pro — Secure Windows Browser with Encrypted Profiles

**Helium Pro** is a hardened desktop distribution of the Helium Browser (Chromium 156.0.8078.47 / Helium 0.20.1.1) equipped with a native Windows launcher, Argon2id key derivation, authenticated AES-256-GCM vault encryption, strict NTFS ACL permissions, and a disposable guest browsing mode.

---

## 🌟 Key Features

1. **Hardware-Isolated Profile Vaults**
   * Every profile is stored at rest inside an AES-256-GCM encrypted vault container (`.hsv`).
   * Key derivation uses Argon2id with 32-byte cryptographically secure random salts and memory cost parameters.
   * Profile IDs and header versions are authenticated via Associated Authenticated Data (AAD), preventing cross-profile cipher manipulation.

2. **Native Windows Launcher (`HeliumSecure`)**
   * Built with C# and WPF on .NET 8.0 for Windows 10/11 x64.
   * Multi-profile management: Create, rename, delete, and unlock private profiles.
   * Interactive password reveal toggle (show/hide).
   * Process monitoring with Windows Job Objects: tracks Chromium root and child processes (GPU, renderers, utilities).
   * Automatic re-encryption on browser exit: flushes files, re-encrypts vault with atomic swap (`.tmp` $\to$ `.bak` $\to$ `.hsv`), securely shreds working files.

3. **Safe Interrupted-Write & Crash Recovery**
   * Double-buffered atomic commits ensure power cuts or unexpected exits never leave a profile corrupted.
   * Automatic recovery from `.bak` snapshot if a primary vault is damaged.

4. **Disposable Guest Mode**
   * Generates a fresh, isolated temporary user data directory for each guest session.
   * Completely independent of private profiles.
   * Securely shreds all temporary files, cookies, and cache upon exit.

5. **Windows Access Controls (NTFS DACL)**
   * Vault storage and working directories are protected with strict NTFS security descriptors, restricting access exclusively to the current Windows user and `NT AUTHORITY\SYSTEM`.

---

## 📁 Repository Structure

```
Helium/
├── src/
│   ├── HeliumSecure.Core/          # Cryptography, Vaults, NTFS ACLs, Process Manager
│   │   ├── Crypto/                 # Argon2id, AES-256-GCM, Secure Memory Wiping
│   │   ├── Guest/                  # Disposable Guest Session Manager
│   │   ├── Models/                 # Profile Metadata & Manifest
│   │   ├── Process/                # Win32 Job Object & Browser Process Lifecycle
│   │   ├── Profiles/               # Profile Manager & Concurrency Locks
│   │   ├── Security/               # NTFS Security Descriptors & DACLs
│   │   └── Vault/                  # Vault File Format, Header, Atomic Swap & Recovery
│   └── HeliumSecure/               # Native Windows WPF Desktop Launcher
│       ├── Views/                  # Dialogs (Create, Rename, Delete Profile)
│       ├── MainWindow.xaml         # Main Profile Dashboard & Password Controls
│       └── App.xaml
├── tests/
│   └── HeliumSecure.Tests/         # 26 automated unit and integration tests
├── build/
│   ├── package.ps1                 # Automated verification, build & packaging pipeline
│   └── Install-HeliumPro.ps1       # Per-user installer with Start Menu & Desktop shortcuts
├── docs/
│   ├── ARCHITECTURE.md             # Technical design & lifecycle specification
│   └── SECURITY_ASSESSMENT.md      # Threat model & cryptographic verification
└── dist/                           # Built standalone packages and installer
```

---

## 🛠️ Building From Source

### Prerequisites
* Windows 10 or Windows 11 (x64)
* .NET 8.0 SDK (with `Microsoft.WindowsDesktop.App` runtime)
* PowerShell 7+ or Windows PowerShell 5.1

### Build & Run Tests
```powershell
# Restore and build the solution
dotnet build -c Release

# Run the complete automated test suite (26 tests)
dotnet test -c Release
```

### Packaging Distribution
To run the automated verification, publish the launcher, bundle the browser, and create the ZIP archive:
```powershell
powershell -ExecutionPolicy Bypass -File build/package.ps1
```

Generated outputs will be located in:
* Distribution folder: `dist\HeliumPro\`
* Standalone ZIP archive: `dist\HeliumPro_v1.0.0_win-x64.zip`

---

## 🚀 Installation & Usage

### Installing Helium Pro
Run the installer script included in the distribution:
```powershell
powershell -ExecutionPolicy Bypass -File dist\HeliumPro\Install-HeliumPro.ps1
```
This installs Helium Pro to `%LOCALAPPDATA%\Programs\HeliumPro`, generates Desktop and Start Menu shortcuts, and registers in Windows Settings / Programs.

To uninstall:
```powershell
powershell -ExecutionPolicy Bypass -File dist\HeliumPro\Install-HeliumPro.ps1 -Uninstall
```

### Running Helium Pro
1. Launch **Helium Pro** from your Desktop or Start Menu.
2. Select your private profile, enter your password, and click **Unlock & Launch Browser**.
3. Browse normally. When you close the browser window, Helium Pro automatically waits for all background processes to terminate, re-encrypts your updated browsing data back into the vault, shreds the temporary working directory, and locks the profile.
4. For temporary browsing without saving any data, click **Browse as Guest**.

---

## 🔒 Security Notes & Limitations

* **Plaintext Working Directory while Unlocked:** As Chromium requires a standard filesystem to read and write LevelDB and SQLite databases, decrypted data temporarily resides in `%LOCALAPPDATA%\HeliumSecure\Profiles\<ProfileId>`. The directory is protected with NTFS DACLs restricting access exclusively to your Windows user account.
* **Malware / Same-User Threat Model:** Like all user-space desktop applications, malware or root processes running with your active Windows user identity can inspect process memory while the browser is running. Profiles are cryptographically secure when locked at rest.
* **Master Password Security:** Master passwords are never written to disk or logs. If a master password is forgotten, the encrypted vault cannot be decrypted.
