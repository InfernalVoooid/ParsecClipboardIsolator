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
5. **1-Click Bridge Control**: When you need to transfer text between your PC and a remote machine, you toggle the sync with a single key, copy your data, and close the "bridge" again.

---

## ✨ Features

- **Multi-Window Support**: Automatically tracks all active Parsec windows.
- **Clean Memory Patching**: Safely patches the Parsec process memory in RAM on your PC without needing third-party drivers or complex setups.
- **Zero Resource Footprint**: Virtually 0% CPU usage and negligible memory consumption.

---

## 🔍 Solved Use Cases & Target Problems

* **Disable Parsec clipboard sharing**: Safely stop automatic clipboard syncing without disconnecting remote sessions.
* **Protect local clipboard from remote overwrites**: Prevent remote applications and scripts from clearing your main PC's copied text.
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
