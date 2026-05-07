# xpaste

A lightweight Windows system-tray app for typing predefined text snippets — passwords, boilerplate text, anything you have to type repeatedly — into whatever window has focus, triggered by a global hotkey.

## Features

- **Global hotkeys** — Press `Ctrl+Shift+1` through `Ctrl+Shift+9` (and `Ctrl+Shift+0` for slot 10) to instantly type a snippet into any focused window
- **RDP-aware** — Automatically detects when an RDP window is focused and uses clipboard+Ctrl+V instead of keystrokes, so snippets paste reliably into remote sessions
- **Smart input** — Uses `SendInput` Unicode keystrokes for local windows (clipboard untouched); switches to clipboard+Ctrl+V automatically for RDP windows
- **Encrypted storage** — All snippet content is encrypted with AES-256-GCM using a master password you set on first launch. The master password is never stored.
- **Lives in the tray** — Minimize to the system tray via `Ctrl+Shift+-`. Click or double-click the **XP** tray icon (or press `Ctrl+Shift++`) to reopen.
- **Auto-start** — Toggle "Start with Windows" in the tray menu or the header switch to launch xpaste automatically at login (registry run key, no admin rights required)
- **CRUD management** — Add, edit, and delete snippets from the inline management UI
- **Inline confirmations** — Delete confirmation and help overlay appear inside the main window, not as separate popups

## Getting Started

### Requirements

- Windows 10 or later (64-bit)
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build from Source

```
dotnet build
dotnet run
```

Or publish a self-contained single-file release exe (no .NET install required):

```
dotnet publish
```

The output lands in `xpaste\publish\xpaste.exe` — a single portable executable that bundles the .NET 10 runtime and all dependencies. Copy it anywhere and run it.

### First Launch

1. On first run you will be prompted to create a **master password**. This password encrypts all your snippets — there is no recovery if you forget it.
2. The main window opens immediately — minimize to tray whenever you like with `Ctrl+Shift+-` or by closing the window.

## Usage

| Action | How |
|---|---|
| Open management window | Click (or double-click) the **XP** tray icon, or press `Ctrl+Shift++` |
| Minimize to tray | Press `Ctrl+Shift+-` or close the window |
| Type snippet into focused window | `Ctrl+Shift+1` through `Ctrl+Shift+0` |
| Add a snippet | Click the **+** button |
| Edit a snippet | Click the pencil icon on a snippet card |
| Delete a snippet | Click the red trash icon on a snippet card, confirm in the inline overlay |
| View keyboard shortcuts | Click the **ⓘ** button in the header |
| Toggle auto-start | Header switch in the management window, or right-click tray icon → **Start with Windows** |
| Change master password | Right-click tray icon → **Change Master Password…** |
| View diagnostic log | Right-click tray icon → **View Log** |
| Exit | Right-click tray icon → **Exit** |

## RDP Support

xpaste automatically detects when an RDP client window (`mstsc.exe` or `msrdc.exe`) is in focus and switches its paste method accordingly:

| Target window | Method |
|---|---|
| Local app | `SendInput` Unicode keystrokes — types directly, clipboard untouched |
| RDP window | Sets clipboard → sends Ctrl+V → restores clipboard after 500 ms |

RDP clipboard redirection syncs your local clipboard to the remote session, so Ctrl+V pastes correctly on the remote machine.

**Prerequisite:** Clipboard redirection must be enabled in your RDP connection (it is on by default).
To verify: in mstsc → **Show Options** → **Local Resources** tab → ensure **Clipboard** is ticked.

## Data & Security

- Snippets are stored at `%AppData%\xpaste\snippets.json`
- Each snippet is individually encrypted with **AES-256-GCM**
- The encryption key is derived from your master password using **PBKDF2-SHA256** (200,000 iterations) with a random 128-bit salt
- A verification blob is stored alongside the snippets so wrong passwords are detected immediately via the GCM authentication tag — no snippet data is ever decrypted with a wrong key
- The master password exists only in memory while the app is unlocked; it is never written to disk
- **Change your master password** at any time via the tray icon → **Change Master Password…** — all snippets are automatically re-encrypted under the new key
- **If you forget your master password**, there is no recovery. Delete `%AppData%\xpaste\snippets.json` to reset (all snippets will be lost), then restart xpaste to set a new password.

## Diagnostics

xpaste writes a diagnostic log to `%AppData%\xpaste\xpaste.log`. The log rotates automatically when it reaches 1 MB (previous log saved as `xpaste.log.old`).

**Privacy:** snippet content and passwords are never written to the log — they appear as `[REDACTED]` with only the character count.

Open the log via right-click tray icon → **View Log**.

## Hotkey Slots

| Hotkey | Slot |
|---|---|
| `Ctrl+Shift+1` | Slot 1 |
| `Ctrl+Shift+2` | Slot 2 |
| … | … |
| `Ctrl+Shift+9` | Slot 9 |
| `Ctrl+Shift+0` | Slot 10 |
| `Ctrl+Shift++` | Open/close window |
| `Ctrl+Shift+-` | Minimize to tray |

Slots marked **Unassigned** in the UI do nothing when triggered.

## Tech Stack

- [.NET 10](https://dotnet.microsoft.com/) / WPF
- [Material Design In XAML Toolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) v5 — teal dark theme
- [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) — system tray support
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM source generators