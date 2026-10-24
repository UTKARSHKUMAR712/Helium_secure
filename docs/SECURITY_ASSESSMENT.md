# HeliumSecure Security Assessment & Threat Model

## 1. Cryptographic Security & Integrity

| Mechanism | Implementation | Security Property |
| :--- | :--- | :--- |
| **Key Derivation** | Argon2id (`Konscious.Security.Cryptography.Argon2`) | Resistant to GPU/ASIC brute force attacks. Unique 32-byte cryptographically secure random salt per profile. |
| **Authenticated Encryption** | AES-256-GCM (`System.Security.Cryptography.AesGcm`) | Confidentiality + Authenticity. Hardware-accelerated with AES-NI. Throws `CryptographicException` on any byte modification. |
| **Cross-Profile Binding** | Associated Authenticated Data (AAD) | Binds profile UUID and format version. Prevents ciphertext swapping across profiles. |
| **Interrupted-Write Safety** | Atomic swap (`.tmp` $\to$ `.bak` $\to$ `.hsv`) | Prevents vault destruction during unexpected termination or system power loss. |
| **Safe Recovery** | Backup rollback (`.bak`) | Automatically restores previous valid snapshot if primary vault ciphertext is damaged. |
| **Memory Hygiene** | `CryptographicOperations.ZeroMemory` | Wipes password bytes and derived 256-bit keys from RAM immediately after cryptographic operations. |

---

## 2. Windows Access Controls

* **NTFS DACL Restrictions:** All profiles, vaults, and manifests are placed in directories protected with explicit Windows Security Identifiers (SIDs):
  * `CurrentUser`: `FileSystemRights.FullControl`
  * `LocalSystemSid`: `FileSystemRights.FullControl`
  * Inherited permissions from parent directories are explicitly stripped.
* **Concurrency Locking:** Unlocked profiles maintain an active Windows file handle with `FileShare.None`. Any secondary process attempting to unlock or modify the active profile is rejected with `ProfileAlreadyLockedException`.

---

## 3. Threat Model & Security Boundaries

### What HeliumSecure Protects Against
1. **At-Rest Inspection:** If the laptop is stolen, powered off, or an attacker inspects user disk files, private profiles are protected with AES-256-GCM and cannot be decrypted without the user's password.
2. **Multi-User Sharing:** Other standard local users on the same Windows machine cannot access or modify the user's vaults or working directory due to NTFS DACLs.
3. **Data Bleed Across Profiles:** Profile A and Profile B have isolated user-data directories (`--user-data-dir`). Cookies, cache, local storage, extensions, history, and bookmarks are completely separate and mutually inaccessible.
4. **Disposable Guest Sessions:** Guest sessions never write to private vaults and are wiped using zero-overwriting and directory shredding upon exit.

### Explicit Limitations & Same-User Boundaries
1. **Unlocked State Plaintext Working Directory:** Chromium requires a normal filesystem for LevelDB and SQLite databases. While the profile is unlocked, working files reside in `%LOCALAPPDATA%\HeliumSecure\Profiles\<ProfileId>`.
2. **Malware / Elevated Attackers:** Malware running under the same Windows user context can read memory or inject into running processes. HeliumSecure does not claim to defend against active rootkits or malware executing with the user's credentials while the browser is running.
3. **Password Recovery:** There is no back-door or recovery key. If a user loses their master password, encrypted data cannot be recovered.
