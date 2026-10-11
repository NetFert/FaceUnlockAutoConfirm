# FaceUnlockAutoConfirm

[![build](https://github.com/NetFert/FaceUnlockAutoConfirm/actions/workflows/build.yml/badge.svg)](https://github.com/NetFert/FaceUnlockAutoConfirm/actions/workflows/build.yml)
[![license](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)

Windows Hello prompt → always in front. Face recognized → **OK** clicked automatically.

[中文](#中文)

## Problem

- Hello prompt opens behind other windows → verification fails / never starts.
- Face recognized → still waits for a manual **OK** ("Select OK to continue").

## How

- `SetWinEventHook` detects the system Hello dialog → forced to foreground.
- UIA3 polls the dialog (30 ms) → invokes **OK** when all conditions hold.

## Auto-confirm conditions

All required. Max one click per dialog.

| # | Condition |
|---|---|
| 1 | Class `Credential Dialog Xaml Host` + owner `%SystemRoot%\System32\CredentialUIBroker.exe` |
| 2 | Dialog first seen **without** OK button |
| 3 | `OkButton` then present, enabled, on-screen |
| 4 | `BioFeedbackText` present, no text input (no PIN/password mode) |
| 5 | Dialog is foreground |

Language-independent: AutomationIds + control types only.

> [!WARNING]
> Removes Windows' intentional confirmation step. Anyone in front of your camera while your session is unlocked passes Hello prompts. Personal, trusted machines only.

## Elevation

Hello dialog runs above medium integrity → reading/clicking it needs admin. Runs via logon scheduled task (`RunLevel Highest`). Not elevated → bring-to-front only.
Install dir: admin-only write. Elevated log: protected `%ProgramData%` dir. See [SECURITY.md](SECURITY.md).

## Requirements

Windows 10/11 with Windows Hello. .NET Framework 4.x (in-box).

## Install

**Release:** download `FaceUnlockAutoConfirm-vX.Y.Z.zip` from [Releases](https://github.com/NetFert/FaceUnlockAutoConfirm/releases) (checksums: `SHA256SUMS.txt`), extract, then in an **elevated** PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Unsigned binary → SmartScreen may warn. Or build from source.

**Source:** no SDK needed.

```powershell
.\build.ps1                                                  # -> bin\FaceUnlockAutoConfirm.exe
.\install.ps1                                                # elevated; -> C:\Program Files\FaceUnlockAutoConfirm
.\install.ps1 -InstallDir 'D:\Tools\FaceUnlockAutoConfirm'   # custom dir
```

## Uninstall

```powershell
.\uninstall.ps1        # elevated; -InstallDir '...' if custom, -KeepLog to keep logs
```

## Log

`%ProgramData%\FaceUnlockAutoConfirm\FaceUnlockAutoConfirm.log` (non-elevated: `%LOCALAPPDATA%\FaceUnlockAutoConfirm\`). Reset on every start.

```
FaceUnlockAutoConfirm 0.0.2 started, elevated=True
detected Windows Hello dialog, pid=1234
brought to front, attempts=1
state: ok=False edit=False bio='Looking for you...'  (t+800ms, scan 5ms)
state: ok=True edit=False bio='Hello, xxx! Select OK to continue.'  (t+1400ms, scan 6ms)
auto-confirmed (clicked OK)  (t+1403ms, invoke 1ms, max scan 30ms)
dialog closed 1500ms after click
```

`dialog closed …` = Windows finishing verification after OK; not adjustable.

## Troubleshooting

| Symptom | Check |
|---|---|
| No log / nothing happens | Task `FaceUnlockAutoConfirm` running; log line 1 `elevated=True` |
| Front OK, no click | `state:` lines. `ok=True` never reached → AutomationIds may have changed; [open an issue](https://github.com/NetFert/FaceUnlockAutoConfirm/issues) with log |

## Limitations

- Depends on internal AutomationIds (`OkButton`, `BioFeedbackText`); Windows updates may change them.
- Signed-in desktop only. Lock screen: built-in setting *Sign-in options → automatically dismiss the lock screen if Windows recognizes your face*.

## AI-generated

Code and docs written with **Claude** (Anthropic). Review the source (single file) before granting admin rights. Dev guide: [AGENTS.md](AGENTS.md).

## Disclaimer

- Provided **as is**, no warranty. Use at your own risk.
- Author not liable for any consequence of use: unlocked accounts, data loss, security incidents, etc.
- Author not responsible for modified versions or redistributions by others.
- Not affiliated with Microsoft or Anthropic. "Windows Hello" is a Microsoft trademark.

## License

[AGPL-3.0-only](LICENSE). Summary (LICENSE text prevails):

- Use, study, modify, share: any purpose.
- Distribute / network-serve a modified version → full source under AGPL-3.0, keep notices, mark changes.
- Credit link back appreciated.
- No warranty, no liability (§15–16).

---

## 中文

Windows Hello 弹窗 → 始终置顶。人脸识别成功 → 自动点**确定**。

### 问题

- 弹窗被其他窗口挡住 → 验证失败或不开始。
- 人脸识别成功 → 仍需手动点**确定**（“请选择‘确定’继续”）。

### 原理

- `SetWinEventHook` 发现系统 Hello 弹窗 → 强制置顶。
- UIA3 每 30 ms 读取弹窗 → 条件全部满足才点**确定**。

### 自动确认条件

全部满足；每个弹窗最多点一次。

| # | 条件 |
|---|---|
| 1 | 类名 `Credential Dialog Xaml Host` + 进程 `%SystemRoot%\System32\CredentialUIBroker.exe` |
| 2 | 先见到**无**确定按钮的状态 |
| 3 | 之后 `OkButton` 出现、可用、可见 |
| 4 | 存在 `BioFeedbackText`，无输入框（非 PIN/密码模式） |
| 5 | 弹窗为前台窗口 |

只看 AutomationId + 控件类型，与系统语言无关。

> [!WARNING]
> 去掉了 Windows 有意设置的确认步骤。会话未锁定时，任何人在摄像头前都能通过 Hello 验证。仅限可信的个人电脑。

### 管理员权限

Hello 弹窗完整性级别高于普通程序 → 读取/点击需管理员权限。由登录计划任务（`RunLevel Highest`）启动。非管理员 → 仅置顶。
安装目录仅管理员可写；管理员日志写在受保护的 `%ProgramData%` 目录。见 [SECURITY.md](SECURITY.md)。

### 要求

Windows 10/11 + Windows Hello；.NET Framework 4.x（系统自带）。

### 安装

**Release：** 从 [Releases](https://github.com/NetFert/FaceUnlockAutoConfirm/releases) 下载 `FaceUnlockAutoConfirm-vX.Y.Z.zip`（校验：`SHA256SUMS.txt`），解压，在**管理员** PowerShell 中：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

未签名 → SmartScreen 可能警告；也可自行从源码构建。

**源码：** 无需 SDK。

```powershell
.\build.ps1                                                  # -> bin\FaceUnlockAutoConfirm.exe
.\install.ps1                                                # 管理员；-> C:\Program Files\FaceUnlockAutoConfirm
.\install.ps1 -InstallDir 'D:\Tools\FaceUnlockAutoConfirm'   # 自定义目录
```

### 卸载

```powershell
.\uninstall.ps1        # 管理员；自定义目录加 -InstallDir '...'，保留日志加 -KeepLog
```

### 日志

`%ProgramData%\FaceUnlockAutoConfirm\FaceUnlockAutoConfirm.log`（非管理员：`%LOCALAPPDATA%\FaceUnlockAutoConfirm\`），每次启动清空。示例见英文部分。
`dialog closed …` = 点击后 Windows 完成验证的耗时，不可调。

### 排查

| 现象 | 检查 |
|---|---|
| 无日志 / 无反应 | 计划任务 `FaceUnlockAutoConfirm` 在运行；日志首行 `elevated=True` |
| 已置顶但不点击 | 看 `state:` 行；始终无 `ok=True` → AutomationId 可能变化，带日志[提 issue](https://github.com/NetFert/FaceUnlockAutoConfirm/issues) |

### 限制

- 依赖内部 AutomationId（`OkButton`、`BioFeedbackText`），Windows 更新可能改变。
- 仅限已登录桌面。锁屏用系统设置：*登录选项 → 识别人脸后自动关闭锁屏*。

### AI 生成

代码与文档由 **Claude**（Anthropic）编写。授予管理员权限前请审阅源码（单文件）。开发指南：[AGENTS.md](AGENTS.md)。

### 免责声明

- 按**原样**提供，无任何担保，风险自负。
- 使用造成的任何后果（账户被解锁、数据丢失、安全事件等），作者概不负责。
- 他人修改或再分发的版本，作者概不负责。
- 与 Microsoft、Anthropic 无关。“Windows Hello”为 Microsoft 商标。

### 许可证

[AGPL-3.0-only](LICENSE)。摘要（以 LICENSE 原文为准）：

- 任何目的均可使用、研究、修改、分享。
- 分发或通过网络提供修改版 → 以 AGPL-3.0 公开完整源码，保留声明，注明修改。
- 欢迎注明来源链接。
- 无担保、无责任（第 15–16 条）。
