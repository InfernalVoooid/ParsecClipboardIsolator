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
    private const int PatchLength = 3;

    // 33 C0 C3 = "xor eax, eax; ret 0" в x86/x64.
    // При вызове OpenClipboard патч заставляет функцию мгновенно возвращать 0 (FALSE),
    // имитируя ошибку открытия буфера обмена и блокируя доступ Parsec к хосту.
    private static readonly byte[] PatchBytes = [0x33, 0xC0, 0xC3];

    // Что сейчас лежит по адресу OpenClipboard в целевом процессе
    private enum PatchState
    {
        Original,
        Patched,

        // Чужой код: либо там хук стороннего оверлея, либо адрес user32 в целевом
        // процессе не совпадает с нашим и мы смотрим не туда
        Foreign,

        Unreadable
    }

    private readonly Dictionary<int, ParsecProcessInfo> _processes = [];
    private readonly HashSet<string> _targetedBlockedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly byte[] _originalBytes = new byte[PatchLength];
    private readonly ParsecMouseFocusIsolator _mouseFocusIsolator = new();
    private readonly object _syncRoot = new();

    private IntPtr _openClipboardAddr;
    private IntPtr _user32Handle;
    private string? _lastFailureReason;
    private bool _disposed;

    private IsolationMode CurrentMode { get; set; } = IsolationMode.Global;
    public bool IsGlobalBlockActive { get; private set; }
    public bool IsMouseFocusBlockActive => _mouseFocusIsolator.IsActive;

    // Причина последнего отказа на границе с ОС (запись в память, установка хука).
    // Сбрасывается в начале каждой операции, чтобы UI не показывал устаревшую ошибку.
    public string? LastFailureReason
    {
        get
        {
            lock (_syncRoot) return _lastFailureReason ??= _mouseFocusIsolator.ConsumeFailureReason();
        }
    }

    public int TrackedInstancesCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _processes.Values
                    .Select(p => p.ExecutablePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            }
        }
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
        Marshal.Copy(_openClipboardAddr, _originalBytes, 0, PatchLength);

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
                if (_disposed) return new RefreshResult(0, 0, 0, 0);
                _lastFailureReason = null;

                if (processes.Length == 0 && _processes.Count == 0)
                    return new RefreshResult(0, 0, 0, 0);

                int newlyAttached = 0;
                int skippedDueToArch = 0;
                int removed = 0;
                int failed = 0;

                var activePids = processes.Select(p => p.Id).ToHashSet();
                var pidsToRemove = _processes.Keys.Where(pid => !activePids.Contains(pid)).ToList();

                foreach (var pid in pidsToRemove)
                {
                    NativeMethods.CloseHandle(_processes[pid].ProcessHandle);
                    _processes.Remove(pid);
                    removed++;
                }

                // Карта окон строится максимум один раз за обновление и только если
                // кому-то из процессов действительно не хватает актуального HWND
                Dictionary<uint, IntPtr>? windowMap = null;

                foreach (var p in processes)
                {
                    if (_processes.TryGetValue(p.Id, out var tracked))
                    {
                        RefreshWindowHandle(tracked, p, ref windowMap);
                        continue;
                    }

                    // PROCESS_VM_READ нужен для сверки байт перед записью, PROCESS_QUERY_LIMITED_INFORMATION
                    // позволяет считывать архитектуру и путь процесса без прав Администратора
                    IntPtr hProc = NativeMethods.OpenProcess(
                        NativeMethods.PROCESS_VM_WRITE | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                        false,
                        p.Id);

                    if (hProc == IntPtr.Zero)
                    {
                        _lastFailureReason = $"PID {p.Id}: нет доступа к процессу (код {Marshal.GetLastPInvokeError()}).";
                        failed++;
                        continue;
                    }

                    if (!IsArchitectureCompatible(hProc))
                    {
                        NativeMethods.CloseHandle(hProc);
                        skippedDueToArch++;
                        continue;
                    }

                    string exePath = GetProcessPath(hProc) ?? $"UnknownPath_{p.Id}";
                    IntPtr mainHwnd = ResolveWindowHandle(p.Id, p.MainWindowHandle, ref windowMap);
                    var info = new ParsecProcessInfo(p.Id, hProc, mainHwnd, exePath);

                    _processes.Add(p.Id, info);
                    newlyAttached++;

                    if (!ApplyStateToProcess(info)) failed++;
                }

                _mouseFocusIsolator.UpdateTrackedProcesses(_processes.Values);

                return new RefreshResult(newlyAttached, skippedDueToArch, removed, failed);
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

    private void SetMouseFocusBlockState(bool active)
    {
        lock (_syncRoot)
        {
            _lastFailureReason = null;
            _mouseFocusIsolator.SetActiveState(active);
        }
    }

    public bool ToggleMouseFocusBlockState()
    {
        lock (_syncRoot)
        {
            _lastFailureReason = null;
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
            _lastFailureReason = null;
            CurrentMode = mode;
            ApplyCurrentModeToAll();
        }
    }

    public void SetGlobalBlockState(bool block)
    {
        lock (_syncRoot)
        {
            _lastFailureReason = null;
            IsGlobalBlockActive = block;
            if (CurrentMode == IsolationMode.Global)
            {
                ApplyCurrentModeToAll();
            }
        }
    }

    public bool ToggleTargetedBlockState(string path)
    {
        lock (_syncRoot)
        {
            _lastFailureReason = null;
            bool newState = !_targetedBlockedPaths.Contains(path);
            SetTargetedBlockStateLocked(path, newState);
            return newState;
        }
    }

    public bool FocusProcessWindow(int pid)
    {
        IntPtr targetHwnd;
        int targetPid;

        lock (_syncRoot)
        {
            if (!_processes.TryGetValue(pid, out var proc))
                return false;

            targetHwnd = proc.MainWindowHandle;
            targetPid = proc.Pid;

            if (targetHwnd == IntPtr.Zero)
            {
                // Если у выбранного PID нет главного окна, ищем другой процесс того же инстанса с окном
                var sibling = _processes.Values.FirstOrDefault(p =>
                    p.ExecutablePath.Equals(proc.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                    p.MainWindowHandle != IntPtr.Zero);

                if (sibling != null)
                {
                    targetHwnd = sibling.MainWindowHandle;
                    targetPid = sibling.Pid;
                }
            }
        }

        // Активация выполняется вне блокировки: SetForegroundWindow зависает
        // на неотвечающем окне и держал бы весь изолятор.
        if (targetHwnd == IntPtr.Zero)
        {
            targetHwnd = WindowLocator.BuildProcessWindowMap().GetValueOrDefault((uint)targetPid, IntPtr.Zero);
        }

        return targetHwnd != IntPtr.Zero && WindowLocator.ActivateWindow(targetHwnd, (uint)targetPid);
    }

    public void LoadTargetedBlockedPaths(IEnumerable<string> paths)
    {
        lock (_syncRoot)
        {
            _lastFailureReason = null;
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
            _lastFailureReason = null;

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

    // При закрытии приложения восстанавливает исходные 3 байта OpenClipboard во всех процессах Parsec
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var info in _processes.Values)
            {
                // Откатываем только свой патч: если поверх лёг хук стороннего оверлея,
                // слепая запись оригинальных байт уничтожила бы его пролог и уронила Parsec.
                if (ReadPatchState(info.ProcessHandle) == PatchState.Patched)
                {
                    WriteMemory(info.ProcessHandle, _originalBytes, info.Pid);
                }
                NativeMethods.CloseHandle(info.ProcessHandle);
            }
            _processes.Clear();
        }

        // Вне блокировки: разблокировка окон обращается к чужим процессам и не должна
        // упираться в изолятор, который в этот момент может обновляться из UI.
        _mouseFocusIsolator.Dispose();

        lock (_syncRoot)
        {
            if (_user32Handle != IntPtr.Zero)
            {
                NativeLibrary.Free(_user32Handle);
                _user32Handle = IntPtr.Zero;
            }
        }

        GC.SuppressFinalize(this);
    }

    private void SetTargetedBlockStateLocked(string path, bool block)
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

    private void ApplyCurrentModeToAll()
    {
        foreach (var info in _processes.Values)
        {
            ApplyStateToProcess(info);
        }
    }

    private bool ApplyStateToProcess(ParsecProcessInfo info)
    {
        bool shouldBlock = CurrentMode switch
        {
            IsolationMode.Global => IsGlobalBlockActive,
            IsolationMode.Targeted => _targetedBlockedPaths.Contains(info.ExecutablePath),
            _ => false
        };

        switch (ReadPatchState(info.ProcessHandle))
        {
            case PatchState.Unreadable:
                _lastFailureReason = $"PID {info.Pid}: не читается память OpenClipboard (код {Marshal.GetLastPInvokeError()}).";
                return false;

            case PatchState.Foreign:
                _lastFailureReason = $"PID {info.Pid}: по адресу OpenClipboard посторонний код — запись отменена.";
                return false;

            case PatchState.Patched when shouldBlock:
            case PatchState.Original when !shouldBlock:
                return true;
        }

        return WriteMemory(info.ProcessHandle, shouldBlock ? PatchBytes : _originalBytes, info.Pid);
    }

    // Сверяет содержимое целевой памяти с известными состояниями. Без этой проверки
    // расхождение базы user32 между процессами (принудительный ASLR в Exploit Protection)
    // приводило бы к записи трёх байт в произвольное место кода Parsec.
    private PatchState ReadPatchState(IntPtr hProc)
    {
        Span<byte> current = stackalloc byte[PatchLength];

        if (!NativeMethods.ReadProcessMemory(hProc, _openClipboardAddr, ref MemoryMarshal.GetReference(current), PatchLength, out int bytesRead)
            || bytesRead != PatchLength)
        {
            return PatchState.Unreadable;
        }

        if (current.SequenceEqual(PatchBytes)) return PatchState.Patched;
        if (current.SequenceEqual(_originalBytes)) return PatchState.Original;
        return PatchState.Foreign;
    }

    // Проверяет совпадение разрядности (32/64 бит) текущего приложения и целевого процесса Parsec
    private static bool IsArchitectureCompatible(IntPtr hProc)
    {
        // Fail-closed: при сбое проверки разрядность неизвестна, а запись 64-битного
        // адреса в 32-битное адресное пространство означает порчу чужой памяти.
        if (!NativeMethods.IsWow64Process(hProc, out bool isTargetWow64)) return false;

        bool isTarget32Bit = Environment.Is64BitOperatingSystem ? isTargetWow64 : true;
        bool isOurApp32Bit = !Environment.Is64BitProcess;

        return isTarget32Bit == isOurApp32Bit;
    }

    // Меняет защиту страницы памяти OpenClipboard на RWX, записывает байты патча/отката и возвращает исходные права
    private bool WriteMemory(IntPtr hProc, ReadOnlySpan<byte> data, int pid)
    {
        if (!NativeMethods.VirtualProtectEx(hProc, _openClipboardAddr, (UIntPtr)data.Length, NativeMethods.PAGE_EXECUTE_READWRITE, out uint oldProtect))
        {
            _lastFailureReason = $"PID {pid}: не удалось снять защиту страницы памяти (код {Marshal.GetLastPInvokeError()}).";
            return false;
        }

        bool written = NativeMethods.WriteProcessMemory(hProc, _openClipboardAddr, in MemoryMarshal.GetReference(data), data.Length, out int bytesWritten)
                       && bytesWritten == data.Length;

        if (!written)
        {
            _lastFailureReason = $"PID {pid}: запись в память не выполнена (код {Marshal.GetLastPInvokeError()}).";
        }

        NativeMethods.VirtualProtectEx(hProc, _openClipboardAddr, (UIntPtr)data.Length, oldProtect, out _);
        return written;
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

    // Parsec пересоздаёт окно при входе/выходе из полноэкранного режима и при переподключении
    // сессии. Раньше уже отслеживаемый процесс никогда не переоценивался, и протухший HWND
    // молча выключал и защиту фокуса, и "Прозвон" до перезапуска самого Parsec.
    private void RefreshWindowHandle(ParsecProcessInfo tracked, Process process, ref Dictionary<uint, IntPtr>? windowMap)
    {
        if (WindowLocator.IsUsableWindow(tracked.MainWindowHandle)) return;

        IntPtr refreshed = ResolveWindowHandle(tracked.Pid, process.MainWindowHandle, ref windowMap);
        if (refreshed != tracked.MainWindowHandle)
        {
            _processes[tracked.Pid] = tracked with { MainWindowHandle = refreshed };
        }
    }

    private static IntPtr ResolveWindowHandle(int pid, IntPtr defaultHwnd, ref Dictionary<uint, IntPtr>? windowMap)
    {
        if (WindowLocator.IsUsableWindow(defaultHwnd) && NativeMethods.GetAncestor(defaultHwnd, NativeMethods.GA_ROOT) == defaultHwnd)
        {
            return defaultHwnd;
        }

        windowMap ??= WindowLocator.BuildProcessWindowMap();
        return windowMap.GetValueOrDefault((uint)pid, IntPtr.Zero);
    }
}
