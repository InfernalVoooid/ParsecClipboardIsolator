# Parsec Clipboard Isolator

<p align="center">
  <b>English</b> •
  <a href="README.ru.md">Русский</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0%2F9.0-blueviolet?style=flat-square" alt=".NET">
  <img src="https://img.shields.io/badge/Platform-Windows%20x64-0078D6?style=flat-square&logo=windows" alt="Platform">
  <img src="https://img.shields.io/badge/License-MIT-green?style=flat-square" alt="License">
</p>

---

**Parsec Clipboard Isolator** is a lightweight Windows (64-bit) utility designed to isolate and control the clipboard bridge in [Parsec](https://parsec.app/).

---

## 🎯 The Problem & Core Concept

By default, Parsec establishes a shared clipboard "bridge" between your main computer (client) and connected remote devices. This leads to frequent issues:
* **Background Software Activity**: Remote machines often run applications or scripts that constantly read and write to the clipboard, polluting and overwriting your main PC's local buffer.
* **Local Data Overwrites**: Anything copied on a remote computer instantly overwrites what you copied locally on your main PC.
* **Privacy Risks**: Passwords and sensitive data copied locally can accidentally leak into remote sessions.

**The Solution** — temporarily disconnect this "bridge" on the client side, keeping both systems completely independent.

---

## 🔒 How Isolation Works

1. **Runs Only on Your Main PC**: The utility runs on your primary computer and manages only your local Parsec client. Connected remote devices remain completely untouched.
2. **Temporary "Bridge" Shutdown**: While enabled, Parsec stops syncing the clipboard between computers.
3. **In-Window Remote Copy & Paste**:
   * While focused inside a Parsec remote window, pressing `Ctrl+C` and `Ctrl+V` works normally **inside that remote window**.
   * However, text copied there **does not reach** your main PC and **will not overwrite** your personal clipboard.
4. **Independent Clipboards**: The remote system's clipboard functions normally for applications running on that machine. The utility does not interfere with the remote OS or its clipboard.
5. **Granular & Global Control**: Toggle clipboard sync globally for all sessions or selectively for specific remote windows.
6. **Global Mouse Focus Guard (`[F]`)**:
   * When enabled, you can freely move your mouse cursor across Parsec windows without capturing input.
   * If remote automation (bots/scripts) or another user is controlling the mouse inside a Parsec window, hovering over it **will not hijack** or disrupt their control while the window is unfocused.
   * Seamless and zero-configuration: simply **click on the Parsec window** to gain focus and control the mouse. As soon as you click back onto the desktop or another app, the Parsec window instantly loses focus and the cursor becomes invisible/ignored again.

---

## ✨ Features

- **Global & Targeted Isolation Modes**: Switch effortlessly between controlling all Parsec windows at once or targeting individual remote sessions.
- **Global Mouse Focus Protection (`[F]`)**: Prevents unfocused Parsec windows from capturing hover or mouse input until explicitly clicked.
- **Window Selection & Granular Control**: Selectively isolate specific Parsec windows while leaving others connected.
- **Window "Ping" (Bring to Foreground)**: Press `P` on any window in the list to bring it to the foreground for instant visual identification.
- **Profile Management & Auto-load**: Save custom window isolation presets to profiles and set a **Default Profile** to automatically load on startup.
- **Multi-Window Support**: Automatically tracks all active Parsec processes and handles window refresh dynamically.
- **Clean Memory Patching**: Safely patches the Parsec process memory in RAM on your PC without needing third-party drivers or complex setups.
- **Zero Resource Footprint**: Virtually 0% CPU usage and negligible memory consumption.

---

## 🎮 Modes & Controls

The utility provides two flexible modes with an intuitive interactive console UI:

* **[GLOBAL MODE]** — toggle clipboard isolation for **all** running Parsec windows simultaneously (`Enter`).
* **[TARGETED MODE]** — granular control over individual Parsec windows:
  * `[Space]` — toggle isolation for the selected window in the list.
  * `[F]` — toggle Global Mouse Focus Protection (unfocused windows ignore mouse movements until clicked).
  * `[P]` (**Window Ping**) — bring the selected Parsec window to the foreground to instantly identify which remote machine it belongs to.
  * `[1] / [2]` — isolate all windows or unblock all at once.
  * `[S]` — save the current window isolation setup to a profile.
  * `[L]` — open the Profile Manager (load, delete, or set a default profile).
  * `[← / →]` — switch between Global and Targeted modes.
  * `[R]` — refresh the active Parsec processes list.

---

## 📁 Profile System

Easily manage sets of rules for selectively isolated windows:
* **Save & Load Profiles**: Create presets for different workflows and swap them on the fly.
* **Default Profile**: Set a default profile by pressing `[Space]` in the Profile Manager. The utility will automatically load it on startup and switch straight into Targeted Mode.

---

## 🔍 Solved Use Cases & Target Problems

* **Disable Parsec clipboard sharing**: Safely stop automatic clipboard syncing without disconnecting remote sessions.
* **Prevent background mouse & cursor hijacking**: Prevents unfocused Parsec windows from grabbing mouse input while moving your cursor across screens.
* **Protect remote automation & bots**: Keeps remote clickers, automation scripts, and active secondary users running uninterrupted when hovering over Parsec windows.
* **Protect local clipboard from remote overwrites**: Prevent remote applications and scripts from clearing your main PC's copied text.
* **Selectively isolate remote connections**: Keep clipboard sync active for trusted remote machines while blocking untrusted ones.
* **Parsec privacy & security**: Prevent accidental leakage of passwords and personal data into connected remote sessions.

---

## 🚀 System Requirements & Build

### Requirements
* OS: Windows 10 / 11 (64-bit)
* [.NET 9.0 Runtime](https://dotnet.microsoft.com/download) (or higher)

### Build from Source

```bash
git clone https://github.com/InfernalVoooid/ParsecClipboardIsolator.git
cd ParsecClipboardIsolator
dotnet build -c Release
```

---

## 📜 License

Distributed under the standard open-source [MIT License](LICENSE).
