# xpaste

A lightweight Windows system-tray app for typing predefined text snippets — passwords, boilerplate text, anything you have to type repeatedly — into whatever window has focus, triggered by a global hotkey.

## Features

- **Global hotkeys** — Press `Alt+Shift+1` through `Alt+Shift+9` (and `Alt+Shift+0` for slot 10) to instantly type a snippet into any focused window
- **Real keystroke injection** — Snippets are typed one character at a time as hardware-style scan codes, exactly as a physical keyboard would produce them. This is what makes them work in **RDP sessions, SSH/terminal password prompts and Windows password fields**, and it means your password is never placed on the clipboard.
- **Encrypted storage** — All snippet content is encrypted with AES-256-GCM using a master password you set on first launch. The master password is never stored.
- **Lives in the tray** — Minimize to the system tray via `Alt+Shift+-`. Click or double-click the **XP** tray icon (or press `Alt+Shift++`) to reopen.
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
2. The main window opens immediately — minimize to tray whenever you like with `Alt+Shift+-` or by closing the window.

## Usage

| Action | How |
|---|---|
| Open management window | Click (or double-click) the **XP** tray icon, or press `Alt+Shift++` |
| Minimize to tray | Press `Alt+Shift+-` or close the window |
| Type snippet into focused window | `Alt+Shift+1` through `Alt+Shift+0` |
| Add a snippet | Click the **+** button |
| Edit a snippet | Click the pencil icon on a snippet card |
| Choose how a snippet is delivered | **Delivery method** dropdown in the edit form |
| Delete a snippet | Click the red trash icon on a snippet card, confirm in the inline overlay |
| View keyboard shortcuts | Click the **ⓘ** button in the header |
| Toggle auto-start | Header switch in the management window, or right-click tray icon → **Start with Windows** |
| Change master password | Right-click tray icon → **Change Master Password…** |
| Type into an elevated app | Right-click tray icon → **Restart as Administrator…** |
| View diagnostic log | Right-click tray icon → **View Log** |
| Exit | Right-click tray icon → **Exit** |

## How Pasting Works

xpaste **types** your snippet rather than pasting it. Each character is sent through `SendInput`
as a hardware-style scan-code key event, with modifiers applied per character, so the target
application cannot tell the difference between xpaste and your keyboard.

This matters because the two obvious alternatives both fail in exactly the places a password
manager is most needed:

| Approach | Why it isn't used |
|---|---|
| Clipboard + `Ctrl+V` | RDP credential prompts and remote lock screens never read the local clipboard; terminals like PuTTY ignore `Ctrl+V`; and the secret is readable by every process on the machine while it sits there |
| `PostMessage(WM_CHAR)` | Bypasses the input queue, so password fields, games and RDP ignore it entirely, and Windows blocks it against elevated windows |

Two details are load-bearing:

- **Scan codes must be real.** RDP, Hyper-V, VMware and Citrix forward the *scan code* of a key
  event, not the virtual key. An event with `wScan == 0` arrives at the remote end as "no key at
  all" — which is why virtual-key-only injection silently does nothing over RDP.
- **The hotkey modifiers must be released first.** `Alt+Shift+1` leaves Ctrl and Shift physically
  down and auto-repeating. xpaste waits for you to let go before typing; if you keep holding the
  hotkey it refuses to type rather than sending mangled text into a password field.

Characters that have no key on the active keyboard layout fall back to Unicode injection
(`KEYEVENTF_UNICODE`). That works locally but is generally **not** forwarded into remote sessions,
so for RDP keep the local and remote keyboard layouts the same.

### Choosing a delivery method per snippet

Each snippet has a **Delivery method**:

- **Type as keystrokes** (default) — works in RDP, SSH and password fields; keeps the secret off the clipboard.
- **Clipboard + Ctrl+V** — much faster for long, non-sensitive boilerplate, but not suitable for passwords.

### Elevated applications

Windows UIPI forbids a normal process from sending input to a window owned by an *elevated*
process. If the target runs as administrator, xpaste's keystrokes are discarded — it detects this
and tells you. Use **Restart as Administrator…** in the tray menu, then try again.

### Known limitation: the secure desktop

UAC consent prompts, the lock screen and `Ctrl+Alt+Del` run on a separate *secure desktop*. No
user-mode application can type into those, by design. xpaste detects this and says so instead of
failing silently — you'll have to type those by hand.

## RDP & SSH

Because snippets are delivered as ordinary keystrokes, they work in Remote Desktop and terminal
sessions the same way typing does — no clipboard redirection required:

- **Remote Desktop (`mstsc`, `msrdc`, RDCMan)** — keystrokes are forwarded to the remote session,
  including the remote sign-in screen. xpaste automatically slows its typing for remote clients,
  which drop input that arrives faster than they can forward it.
- **PuTTY / KiTTY / Windows Terminal / OpenSSH** — `sudo` and SSH password prompts read the terminal's
  input stream directly, so typed characters are accepted where `Ctrl+V` would have been ignored.
- **VM consoles (Hyper-V, VMware, VirtualBox) and Citrix** — same mechanism, same slower pacing.

For the best results over RDP, make sure the local and remote keyboard layouts match.

## Data & Security

- Snippets are stored at `%AppData%\xpaste\snippets.json`
- Each snippet is individually encrypted with **AES-256-GCM**
- The encryption key is derived from your master password using **PBKDF2-SHA256** (200,000 iterations) with a random 128-bit salt
- A verification blob is stored alongside the snippets so wrong passwords are detected immediately via the GCM authentication tag — no snippet data is ever decrypted with a wrong key
- The master password exists only in memory while the app is unlocked; it is never written to disk
- Snippet content is **never placed on the clipboard** unless you explicitly set a snippet's delivery method to *Clipboard + Ctrl+V*
- **Change your master password** at any time via the tray icon → **Change Master Password…** — all snippets are automatically re-encrypted under the new key
- **If you forget your master password**, there is no recovery. Delete `%AppData%\xpaste\snippets.json` to reset (all snippets will be lost), then restart xpaste to set a new password.

## Diagnostics

xpaste writes a diagnostic log to `%AppData%\xpaste\xpaste.log`. The log rotates automatically when it reaches 1 MB (previous log saved as `xpaste.log.old`).

**Privacy:** snippet content and passwords are never written to the log — they appear as `[REDACTED]` with only the character count.

When a snippet cannot be typed, xpaste shows a tray notification explaining why (elevated target,
secure desktop, hotkey still held) and records the same detail in the log along with the target
process, the number of keystrokes planned and how many characters needed a Unicode fallback.

Open the log via right-click tray icon → **View Log**.

## Hotkey Slots

| Hotkey | Slot |
|---|---|
| `Alt+Shift+1` | Slot 1 |
| `Alt+Shift+2` | Slot 2 |
| … | … |
| `Alt+Shift+9` | Slot 9 |
| `Alt+Shift+0` | Slot 10 |
| `Alt+Shift++` | Open/close window |
| `Alt+Shift+-` | Minimize to tray |

Slots marked **Unassigned** in the UI do nothing when triggered.

> **If a hotkey does nothing:** `Alt+Shift` is also the legacy Windows shortcut for *switching
> keyboard layout*. If you have two or more input languages installed, Windows may consume the
> combination before xpaste sees it. Clear it under **Settings → Time & language → Typing →
> Advanced keyboard settings → Input language hot keys**, and set "Between input languages" to
> *Not Assigned*. `xpaste.log` records the result of every `RegisterHotKey` call at startup, so a
> `FAILED err=1409` line there means another application already owns that combination.

## Tech Stack

- [.NET 10](https://dotnet.microsoft.com/) / WPF
- [Material Design In XAML Toolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) v5 — teal dark theme
- [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) — system tray support
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM source generators