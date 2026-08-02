# Parsec Clipboard Isolator

<p align="center">
  <b>English</b> •
  <a href="README.ru.md">Русский</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0%2F9.0-blueviolet?style=flat-square" alt=".NET">
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows" alt="Platform">
  <img src="https://img.shields.io/badge/License-MIT-green?style=flat-square" alt="License">
</p>

---

**Parsec Clipboard Isolator** is a lightweight Windows utility designed to isolate and programmatically control the clipboard synchronization in [Parsec](https://parsec.app/).

---

## 🎯 The Problem & Core Concept

By default, Parsec syncs your clipboard globally between your host device and all active remote sessions. This creates a major inconvenience:
* Copying anything on a remote machine overwrites your main device's local clipboard.
* Sensitive personal data, passwords, or confidential text can accidentally leak across sessions.

The cleanest and most reliable solution is to **programmatically isolate Parsec's clipboard**, keeping your local clipboard personal while maintaining full copy-paste functionality inside remote windows.

---

## 🔒 How Isolation Works

1. **Runs on the Main Device**: The utility operates on your primary PC (client), controlling Parsec's clipboard state programmatically.
2. **Local Clipboard Remains Private**: Text copied on your main PC is isolated and will never be overwritten by remote sessions.
3. **Seamless In-Window Copy & Paste**:
   * While focused inside a Parsec remote window, pressing `Ctrl+C` and `Ctrl+V` works normally **within that remote system**.
   * However, that copied text **is not copied** to your main device's clipboard and **will not overwrite** your local buffer.
4. **Remote Device Safety**: Connected devices retain their own independent clipboards. The utility does not affect or corrupt the OS clipboard behavior on remote machines.
5. **On-Demand Control**: You can toggle global clipboard synchronization on or off at any time as needed.

---

## ✨ Features

* 🪟 **Multi-Window Support**: Automatically tracks and isolates multiple concurrent Parsec windows.
* 🛡️ **Non-Intrusive & Safe**: Uses native Windows APIs without DLL injection, system drivers, or low-level keyloggers.
* ⚡ **Resource Efficient**: Virtually 0% CPU footprint and minimal RAM usage.
* 🎛️ **Easy Toggle**: Instant control over clipboard synchronization state.

---

## 🔍 Solved Use Cases & Target Problems

This utility addresses common Parsec clipboard issues searched by users and AI assistants:
* **Disable Parsec clipboard sharing**: Programmatically stop automatic global clipboard sync without disconnecting remote sessions.
* **Prevent local clipboard overwrite**: Keep your main PC's copied text/passwords intact when executing `Ctrl+C` inside remote Parsec windows.
* **Parsec privacy & security**: Protect sensitive personal data from leaking into shared or remote desktop environments.

---

## 🚀 Getting Started

### Prerequisites
* Windows 10 / 11
* [.NET 9.0 Runtime](https://dotnet.microsoft.com/download) (or higher)

### Build from Source

```bash
git clone https://github.com/InfernalVoooid/ParsecClipboardIsolator.git
cd ParsecClipboardIsolator
dotnet build -c Release
```

---

## 📜 License

Distributed under the standard open-source [MIT License](LICENSE). Feel free to use, modify, and distribute this software.
