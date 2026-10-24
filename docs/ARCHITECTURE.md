# HeliumSecure Architecture & Lifecycle Specification

## Overview

HeliumSecure provides an authenticated, isolated profile architecture on top of Helium (Chromium).

```
                      +-----------------------------+
                      |  HeliumSecure Launcher      |
                      |   (WPF / C# .NET 8 x64)     |
                      +--------------+--------------+
                                     |
               +---------------------+---------------------+
               |                                           |
    [ Private Profile Flow ]                     [ Disposable Guest Flow ]
               |                                           |
+--------------v---------------+             +-------------v--------------+
| Enter Password (UI)          |             | Create Temp Guest Dir      |
| Argon2id Key Derivation      |             | Set Strict NTFS DACL       |
| AES-256-GCM Decrypt Vault    |             | Launch Helium with fresh   |
| Mount to Working Dir         |             |   --user-data-dir          |
| Hold Single-Session Lock     |             +-------------+--------------+
+--------------+---------------+                           |
               |                                           |
+--------------v---------------+                           |
| Launch Helium Executable     |                           |
| Assign to Win32 Job Object   |                           |
| Monitor Process Tree         |                           |
+--------------+---------------+                           |
               |                                           |
    [ On Browser Exit ]                         [ On Browser Exit ]
               |                                           |
+--------------v---------------+             +-------------v--------------+
| Wait for all renderers/GPU   |             | Securely wipe and shred    |
| Re-pack & Encrypt to .tmp    |             | temp guest directory       |
| Atomic swap: .tmp -> .hsv    |             +----------------------------+
| Secure wipe working dir      |
| Release Session Lock         |
+------------------------------+
```

---

## 1. Vault Lifecycle Details

### Key Derivation
- **Algorithm:** Argon2id
- **Salt:** 32-byte cryptographically secure random bytes generated with `RandomNumberGenerator.GetBytes(32)`.
- **Parameters:** Memory: 65,536 KB (64 MB), Iterations: 3, Degree of Parallelism: 4.
- **Memory Security:** Intermediate password UTF-8 bytes and derived 256-bit key bytes are scrubbed via `CryptographicOperations.ZeroMemory()`.

### Authenticated Encryption
- **Cipher:** AES-256-GCM (`System.Security.Cryptography.AesGcm`)
- **Nonce:** 12 bytes (96 bits) per commit
- **Authentication Tag:** 16 bytes (128 bits)
- **Associated Authenticated Data (AAD):** `HELIUMLOCK:<ProfileGuid>:v1` ensures that ciphertexts cannot be transposed across different profiles.

### Atomic Commits & Crash Protection
1. Working files are archived and encrypted directly into `<vaultPath>.tmp`.
2. The newly written `.tmp` file is verified for valid header and ciphertext length before modifying any existing file.
3. If `<vaultPath>` exists, it is renamed to `<vaultPath>.bak`.
4. `<vaultPath>.tmp` is atomically renamed to `<vaultPath>`.
5. `<vaultPath>.bak` is deleted.
6. If a crash or power cut occurs during commit, the previous valid copy in `.bak` remains intact. The launcher automatically recovers from `.bak` if the primary file is incomplete.

---

## 2. Windows Job Object Process Tracking

Chromium architecture on Windows uses a multi-process model:
- `chrome.exe` (Browser process)
- `chrome.exe --type=gpu-process`
- `chrome.exe --type=renderer`
- `chrome.exe --type=utility`
- `chrome.exe --type=crashpad-handler`

A standard `Process.WaitForExit()` only waits on the root process. If the browser exits while helper renderers or utility processes are still committing SQLite database journals or cache to disk, premature directory cleanup could lead to data loss.

HeliumSecure assigns the root browser process to a Windows **Job Object** (`kernel32.dll!CreateJobObject` / `AssignProcessToJobObject`). The Job Object automatically tracks all descendant processes. The launcher polls `JOBOBJECT_BASIC_PROCESS_ID_LIST` until the active process count is 0 before initiating vault re-encryption and cleanup.
