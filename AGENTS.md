# AGENTS.md

Dev guide (humans + AI agents). Runs **elevated**, clicks a **security prompt** → safety > features.

## Layout

| Path | Purpose |
|---|---|
| `src/FaceUnlockAutoConfirm.cs` | Entire program, single file, no deps |
| `build.ps1` | In-box `csc.exe` build; UIA3 interop → `obj/`, embedded via `/link` |
| `install.ps1` / `uninstall.ps1` | Locked-down install dir + logon task (`RunLevel Highest`) / removal |
| `.github/workflows/build.yml` | CI build (push/PR) |
| `.github/workflows/release.yml` | Tag `v*` → zip + exe + `SHA256SUMS.txt` → GitHub Release |
| `SECURITY.md` | Security model, private reporting |

## Build / run

```powershell
.\build.ps1          # relaunches under Windows PowerShell 5.1
.\install.ps1        # elevated
.\uninstall.ps1      # elevated
```

- Log: `%ProgramData%\FaceUnlockAutoConfirm\` (elevated) / `%LOCALAPPDATA%\FaceUnlockAutoConfirm\`. Reset per start.
- Ad-hoc run: stop the task (single-instance mutex), run `bin\FaceUnlockAutoConfirm.exe` elevated.
- No automated tests. Manual: trigger Windows Hello from any app → read `state:` lines.

## Flow

```
Main → mutex → PrepareLogDirectory → SetWinEventHook(EVENT_OBJECT_SHOW) → message loop
OnWinEvent: top-level? class == "Credential Dialog Xaml Host"? image == System32\CredentialUIBroker.exe? dedupe HWND
  → ThreadPool: BringToFront (AttachThreadInput + TOPMOST + SetForegroundWindow, retry)
              → WatchAndConfirm (UIA3 FindAllBuildCache every 30 ms, ≤2 min) → Invoke OkButton
              → LogWhenClosed
```

Dialog AutomationIds: `UACTrustBand`, `CloseButton`, `CancelButton`, `BioFeedbackText`, `ChooseAnotherOption`, `OkButton` (post-recognition only).

## Invariants (do not weaken)

Auto-click requires **all**:

1. Class `Credential Dialog Xaml Host` + owner image exactly `%SystemRoot%\System32\CredentialUIBroker.exe`.
2. Dialog observed without `OkButton` first (state transition).
3. `OkButton`: Button, enabled, on-screen.
4. `BioFeedbackText` non-empty; zero Edit controls.
5. Dialog is foreground at click.
6. Max one click per dialog.

Also:

- Decide on AutomationIds/control types only; never on text (text = log only).
- Elevated → never write to user-writable paths (junction/symlink abuse). Log dir: admin/SYSTEM-owned, protected ACL, not a reparse point, else no logging.
- Install dir: non-admin write forbidden.
- No network, no telemetry, no persistence beyond the scheduled task.

Relaxing any item → justify + update `SECURITY.md`.

## Gotchas

- `CredentialUIBroker.exe` integrity > medium → UIPI blocks UIA read/Invoke/`SendInput` from medium. Foreground switch still works. Hence elevation (`uiAccess` alternative needs signing + secure path).
- Managed UIA (`System.Windows.Automation`) sees no content in this XAML dialog → UIA3 COM (`CUIAutomation8`, `e22ad333-b25f-460c-83d0-0581107395c9`).
- Use `CacheRequest` (`FindAllBuildCache`): one cross-process round trip per scan.
- `TypeLibConverter` = .NET Framework only; `AssemblyBuilder.Save` uses the .NET current dir.
- `.ps1`: ASCII only (PS 5.1 reads BOM-less UTF-8 as ANSI). `csc /codepage:65001`.
- `EVENT_OBJECT_SHOW` may repeat per HWND → `_active` dedupe.
- Post-click delay (`dialog closed …`) = Windows verification; not controllable.

## Conventions

- Single file, .NET Framework 4.x, C# 5 (in-box `csc.exe`), no NuGet.
- Terse comments. One log line per state change.
- Release: bump `Assembly*Version` + `CHANGELOG.md` → commit → tag `vX.Y.Z` → workflow publishes.

## License

[AGPL-3.0-only](LICENSE). Forks: stay AGPL-3.0, provide full source (incl. network use), keep source header + `LICENSE`, mark changes. Modified versions = publisher's responsibility.
