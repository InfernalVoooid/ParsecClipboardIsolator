using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ParsecClipboardIsolator.Models;
using ParsecClipboardIsolator.Native;

namespace ParsecClipboardIsolator.Services;

// Отвечает за отслеживание процессов Parsec и модификацию байт OpenClipboard в памяти
[SupportedOSPlatform("windows")]
internal sealed class ParsecIsolator : IDisposable
{
    private const string TargetProcessName = "parsecd";
    
    // 33 C0 C3 = "xor eax, eax; ret 0" в x86/x64. 
    // При вызове OpenClipboard патч заставляет функцию мгновенно возвращать 0 (FALSE), 
    // имитируя ошибку открытия буфера обмена и блокируя доступ Parsec к хосту.
    private static readonly byte[] PatchBytes = [0x33, 0xC0, 0xC3];
    
    private readonly Dictionary<int, ParsecProcessInfo> _processes = [];
    private readonly HashSet<string> _targetedBlockedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly byte[] _originalBytes = new byte[3];
    private readonly ParsecMouseFocusIsolator _mouseFocusIsolator = new();
    private readonly object _syncRoot = new();

    private IntPtr _openClipboardAddr;
    private IntPtr _user32Handle;
    private bool _disposed;

    public IsolationMode CurrentMode { get; private set; } = IsolationMode.Global;
    public bool IsGlobalBlockActive { get; private set; }
    public bool IsMouseFocusBlockActive => _mouseFocusIsolator.IsActive;

    public int TrackedProcessesCount 
    { 
        get { lock (_syncRoot) return _processes.Count; } 
    }

    public bool HasTargetedBlockedPaths
    {
        get { lock (_syncRoot) return _targetedBlockedPaths.Count > 0; }
    }

    // Получает экспортный адрес OpenClipboard из user32.dll и сохраняет оригинальные 3 байта для отката
    public RefreshResult Initialize()
    {
        if (!NativeLibrary.TryLoad("user32.dll", out _user32Handle))
            throw new InvalidOperationException("Не удалось загрузить user32.dll.");

        if (!NativeLibrary.TryGetExport(_user32Handle, "OpenClipboard", out _openClipboardAddr))
            throw new InvalidOperationException("Не удалось найти адрес функции OpenClipboard.");

        // Сохраняем оригинальные байты OpenClipboard для восстановления при выходе
        Marshal.Copy(_openClipboardAddr, _originalBytes, 0, 3);

        SetGlobalBlockState(true);
        SetMouseFocusBlockState(true);

        return Refresh();
    }

    public RefreshResult Refresh()
    {
        var processes = Process.GetProcessesByName(TargetProcessName);
        
        try
        {
            lock (_syncRoot)
            {
                if (processes.Length == 0 && _processes.Count == 0) 
                    return new RefreshResult(0, 0, 0);

                int newlyAttached = 0;
                int skippedDueToArch = 0;
                int removed = 0;

                var activePids = processes.Select(p => p.Id).ToHashSet();
                var pidsToRemove = _processes.Keys.Where(pid => !activePids.Contains(pid)).ToList();

                foreach (var pid in pidsToRemove)
                {
                    NativeMethods.CloseHandle(_processes[pid].ProcessHandle);
                    _processes.Remove(pid);
                    removed++;
                }

                foreach (var p in processes)
                {
                    if (_processes.ContainsKey(p.Id)) continue; 

                    // PROCESS_QUERY_LIMITED_INFORMATION позволяет считывать архитектуру и путь процесса без прав Администратора
                    IntPtr hProc = NativeMethods.OpenProcess(
                        NativeMethods.PROCESS_VM_WRITE | NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, 
                        false, 
                        p.Id);
                    
                    if (hProc == IntPtr.Zero) continue;

                    if (!IsArchitectureCompatible(hProc))
                    {
                        NativeMethods.CloseHandle(hProc);
                        skippedDueToArch++;
                        continue;
                    }

                    string exePath = GetProcessPath(hProc) ?? $"UnknownPath_{p.Id}";
                    var info = new ParsecProcessInfo(p.Id, hProc, p.MainWindowHandle, exePath);
                    
                    _processes.Add(p.Id, info);
                    newlyAttached++;
                    
                    ApplyStateToProcess(info);
                }

                _mouseFocusIsolator.UpdateTrackedProcesses(_processes.Values);

                return new RefreshResult(newlyAttached, skippedDueToArch, removed);
            }
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    public void SetMouseFocusBlockState(bool active)
    {
        lock (_syncRoot)
        {
            _mouseFocusIsolator.SetActiveState(active);
        }
    }

    public bool ToggleMouseFocusBlockState()
    {
        lock (_syncRoot)
        {
            bool newState = !_mouseFocusIsolator.IsActive;
            _mouseFocusIsolator.SetActiveState(newState);
            return newState;
        }
    }

    public ParsecProcessInfo[] GetTrackedProcessesSnapshot() 
    { 
        lock (_syncRoot) return _processes.Values.ToArray(); 
    }

    public HashSet<string> GetTargetedBlockedPathsSnapshot()
    {
        lock (_syncRoot) return new HashSet<string>(_targetedBlockedPaths, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsPathBlocked(string path)
    {
        lock (_syncRoot) return _targetedBlockedPaths.Contains(path);
    }

    public void SetMode(IsolationMode mode)
    {
        lock (_syncRoot)
        {
            if (CurrentMode == mode) return;
            CurrentMode = mode;
            ApplyCurrentModeToAll();
        }
    }

    public void SetGlobalBlockState(bool block)
    {
        lock (_syncRoot)
        {
            IsGlobalBlockActive = block;
            if (CurrentMode == IsolationMode.Global)
            {
                ApplyCurrentModeToAll();
            }
        }
    }

    public void SetTargetedBlockState(string path, bool block)
    {
        lock (_syncRoot)
        {
            if (block)
            {
                _targetedBlockedPaths.Add(path);
            }
            else
            {
                _targetedBlockedPaths.Remove(path);
            }

            if (CurrentMode == IsolationMode.Targeted)
            {
                foreach (var info in _processes.Values.Where(p => p.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                {
                    ApplyStateToProcess(info);
                }
            }
        }
    }

    public bool ToggleTargetedBlockState(string path)
    {
        lock (_syncRoot)
        {
            bool newState = !_targetedBlockedPaths.Contains(path);
            SetTargetedBlockState(path, newState);
            return newState;
        }
    }

    public bool FocusProcessWindow(int pid)
    {
        lock (_syncRoot)
        {
            if (_processes.TryGetValue(pid, out var proc) && proc.MainWindowHandle != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(proc.MainWindowHandle, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(proc.MainWindowHandle);
                return true;
            }
            return false;
        }
    }

    public void LoadTargetedBlockedPaths(IEnumerable<string> paths)
    {
        lock (_syncRoot)
        {
            _targetedBlockedPaths.Clear();
            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _targetedBlockedPaths.Add(path);
                }
            }

            if (CurrentMode == IsolationMode.Targeted)
            {
                ApplyCurrentModeToAll();
            }
        }
    }
    
    public void SetAllTargetedStates(bool block)
    {
        lock (_syncRoot)
        {
            if (block)
            {
                foreach (var proc in _processes.Values)
                {
                    _targetedBlockedPaths.Add(proc.ExecutablePath);
                }
            }
            else
            {
                _targetedBlockedPaths.Clear();
            }
            
            if (CurrentMode == IsolationMode.Targeted)
            {
                ApplyCurrentModeToAll();
            }
        }
    }

    public void ApplyCurrentModeToAll()
    {
        lock (_syncRoot)
        {
            foreach (var info in _processes.Values)
            {
                ApplyStateToProcess(info);
            }
        }
    }

    // При закрытии приложения восстанавливает исходные 3 байта OpenClipboard во всех процессах Parsec
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed) return;
            _disposed = true;

            ReadOnlySpan<byte> dataToWrite = _originalBytes;
            foreach (var info in _processes.Values)
            {
                WriteMemory(info.ProcessHandle, dataToWrite);
                NativeMethods.CloseHandle(info.ProcessHandle);
            }
            _processes.Clear();

            _mouseFocusIsolator.Dispose();

            if (_user32Handle != IntPtr.Zero)
            {
                NativeLibrary.Free(_user32Handle);
                _user32Handle = IntPtr.Zero;
            }
        }

        GC.SuppressFinalize(this);
    }

    private void ApplyStateToProcess(ParsecProcessInfo info)
    {
        bool shouldBlock = CurrentMode switch
        {
            IsolationMode.Global => IsGlobalBlockActive,
            IsolationMode.Targeted => _targetedBlockedPaths.Contains(info.ExecutablePath),
            _ => false
        };

        ReadOnlySpan<byte> dataToWrite = shouldBlock ? PatchBytes : _originalBytes;
        WriteMemory(info.ProcessHandle, dataToWrite);
    }

    // Проверяет совпадение разрядности (32/64 бит) текущего приложения и целевого процесса Parsec
    private static bool IsArchitectureCompatible(IntPtr hProc)
    {
        if (!NativeMethods.IsWow64Process(hProc, out bool isTargetWow64)) return true;

        bool isTarget32Bit = Environment.Is64BitOperatingSystem ? isTargetWow64 : true;
        bool isOurApp32Bit = !Environment.Is64BitProcess;

        return isTarget32Bit == isOurApp32Bit;
    }

    // Меняет защиту страницы памяти OpenClipboard на RWX, записывает байты патча/отката и возвращает исходные права
    private void WriteMemory(IntPtr hProc, ReadOnlySpan<byte> data)
    {
        if (NativeMethods.VirtualProtectEx(hProc, _openClipboardAddr, (UIntPtr)data.Length, NativeMethods.PAGE_EXECUTE_READWRITE, out uint oldProtect))
        {
            NativeMethods.WriteProcessMemory(hProc, _openClipboardAddr, in MemoryMarshal.GetReference(data), data.Length, out _);
            NativeMethods.VirtualProtectEx(hProc, _openClipboardAddr, (UIntPtr)data.Length, oldProtect, out _);
        }
    }

    // Получает полный путь к исполняемому файлу через Win32 API без аллокаций строки на каждую попытку
    private static string? GetProcessPath(IntPtr hProc)
    {
        uint bufferSize = 1024;
        Span<char> buffer = stackalloc char[(int)bufferSize];
        if (NativeMethods.QueryFullProcessImageName(hProc, 0, buffer, ref bufferSize))
        {
            return buffer.Slice(0, (int)bufferSize).ToString();
        }
        return null;
    }
}
