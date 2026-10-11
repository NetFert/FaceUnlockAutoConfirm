# Security Policy

Runs elevated; clicks a Windows security prompt.

## Report

**No public issues.** Use **Security → Report a vulnerability** (private reporting).
Include: Windows build, version (log line 1), repro steps.

## Model

- Target: class `Credential Dialog Xaml Host` owned by `%SystemRoot%\System32\CredentialUIBroker.exe` only.
- Click only on transition no-OK → `OkButton`, biometric mode, no text input, dialog foreground; max once per dialog.
- Install dir: Administrators/SYSTEM write only.
- Elevated log: `%ProgramData%\FaceUnlockAutoConfirm`, protected ACL; no logging if reparse point or not admin/SYSTEM-owned.

## Out of scope

Removing the manual OK step is the intended behavior: anyone in front of the camera while the session is unlocked passes a Hello prompt. Documented trade-off, not a vulnerability.
