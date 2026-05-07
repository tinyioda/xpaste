# xpaste — Implementation Plan

## Overview
A WPF (.NET 10) system-tray app that lets the user paste predefined text snippets (especially passwords) into any focused window via global hotkeys. Snippets are encrypted at rest (AES-256-GCM) with a master password. The management UI is hidden by default and toggled with Ctrl+Shift+=.

---

## Tech Stack
| Concern | Choice |
|---|---|
| Framework | .NET 10 WPF |
| UI Theming | MaterialDesignThemes (teal primary + accent) |
| MVVM | CommunityToolkit.Mvvm |
| System Tray | H.NotifyIcon.Wpf |
| Encryption | AES-256-GCM via `System.Security.Cryptography` |
| Key Derivation | PBKDF2 / `Rfc2898DeriveBytes` (SHA-256, 200k iterations) |
| Input Simulation | Win32 `SendInput` (P/Invoke) |
| Global Hotkeys | Win32 `RegisterHotKey` (P/Invoke) |
| Storage | JSON file in `%AppData%\xpaste\` |
| Auto-start | Windows Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |

---

## Hotkeys
| Hotkey | Action |
|---|---|
| Ctrl+Shift+1 … Ctrl+Shift+0 | Type snippet in slot 1–10 into focused window |
| Ctrl+Shift+= | Toggle management window (show/hide) |

---

## Encryption Design
- On first launch: prompt user to set a master password.
- Key = PBKDF2(masterPassword, randomSalt, 200 000 iter, SHA-256) → 32 bytes.
- Each snippet's `Content` is encrypted with AES-256-GCM (unique IV per snippet).
- Stored on disk: `{ Salt, Snippets: [{ Id, Name, Slot, CipherText, IV, Tag }] }`
- The derived key lives in memory only for the session duration; it is never written to disk.
- Wrong master password → GCM authentication tag fails → clear error, retry.

---

## Project Root
`C:\Users\justi\OneDrive\Desktop\xpaste\`

---

## Project Structure
```
C:\Users\justi\OneDrive\Desktop\xpaste\
├── xpaste.csproj
├── App.xaml / App.xaml.cs          ← entry point, tray icon, hotkey service bootstrap
├── Models/
│   └── Snippet.cs                  ← Id, Name, Slot, CipherText, IV, Tag
├── Services/
│   ├── EncryptionService.cs        ← AES-256-GCM encrypt/decrypt, PBKDF2 key derivation
│   ├── SnippetStore.cs             ← load/save JSON (encrypted), in-memory decrypted list
│   ├── HotkeyService.cs            ← RegisterHotKey / WndProc hook, fires events
│   ├── InputSimulator.cs           ← SendInput P/Invoke to type snippet text
│   └── StartupService.cs           ← read/write registry auto-start key
├── ViewModels/
│   ├── MainViewModel.cs            ← ObservableCollection<SnippetViewModel>, CRUD commands
│   └── SnippetViewModel.cs         ← wraps Snippet, editing state
├── Views/
│   ├── MainWindow.xaml             ← management UI: snippet list + add/edit/delete
│   ├── SnippetEditDialog.xaml      ← dialog: name, content, slot picker
│   └── MasterPasswordDialog.xaml   ← first-launch / unlock dialog
├── Assets/
│   └── tray-icon.ico
└── Converters/
    └── SlotDisplayConverter.cs     ← int slot → "Ctrl+Shift+1" label
```

---

## UI Design (Material Design, Teal)
- **Primary**: Teal 500 — `#009688`
- **Accent**: Teal A200 — `#64FFDA`
- **Theme**: Dark base (looks great for a utility app)
- Main window: DataGrid/ListView of snippets — columns: Slot, Name, (masked) Content preview, Edit, Delete
- FAB (+) button to add new snippet
- Tray right-click menu: "Open xpaste", "Lock", "Exit"
- App does **not** appear in taskbar (`ShowInTaskbar="False"`)

---

## Todos (in order)

1. **scaffold** — `dotnet new wpf` project targeting net10.0-windows, add NuGet packages
2. **models** — `Snippet.cs`, `SnippetStoreFile.cs` DTOs
3. **encryption-service** — `EncryptionService.cs` (PBKDF2 + AES-GCM)
4. **snippet-store** — `SnippetStore.cs` (load/save JSON, decrypt on load, encrypt on save)
5. **hotkey-service** — `HotkeyService.cs` (RegisterHotKey Win32, WM_HOTKEY dispatcher)
6. **input-simulator** — `InputSimulator.cs` (SendInput for typing text)
7. **startup-service** — `StartupService.cs` (registry run key)
8. **tray-icon** — App.xaml.cs tray icon setup, hide from taskbar, context menu
9. **master-password-dialog** — `MasterPasswordDialog.xaml` + VM
10. **snippet-edit-dialog** — `SnippetEditDialog.xaml` + VM
11. **main-window** — `MainWindow.xaml` + `MainViewModel.cs` full CRUD
12. **hotkey-wiring** — connect HotkeyService events → SnippetStore → InputSimulator
13. **auto-start** — wire StartupService to a toggle in settings area of MainWindow
14. **polish** — tray icon, teal Material theme, icons, masked content preview, error handling

---

## Notes / Decisions
- Slots are 1-indexed (1–10, where 10 maps to Ctrl+Shift+0).
- A snippet with no assigned slot can exist but won't be triggered by hotkey.
- Typing is done via `SendInput` (not clipboard) to avoid clobbering clipboard contents.
- Lock option in tray clears the in-memory key so snippets can't be accessed without re-entering master password.
- `ShowInTaskbar="False"` + `WindowStyle="None"` or `ToolWindow` to hide from Alt+Tab is optional / user preference — keep as `SingleBorderWindow` for now.
