using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using ParsecClipboardIsolator.Models;
using ParsecClipboardIsolator.Native;

namespace ParsecClipboardIsolator.Services;

// Модуль изоляции ввода мыши для неактивных (out-of-focus) окон Parsec.
// Выключает приём ввода для неактивных окон Parsec через EnableWindow, 
// позволяя курсору ОС свободно плавно двигаться по всему экрану без барьеров, 
// но предотвращая трансляцию движений на удаленный хост.
[SupportedOSPlatform("windows")]
internal sealed class ParsecMouseFocusIsolator : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<int, IntPtr> _trackedProcessWindows = [];
    private IReadOnlyDictionary<int, IntPtr> _trackedWindowsSnapshot = new Dictionary<int, IntPtr>();
    private readonly LowLevelMouseProc _hookProc;
    private readonly WinEventProc _winEventProc;
    
    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _hookHandle = IntPtr.Zero;
    private IntPtr _winEventHook = IntPtr.Zero;
    private bool _isActive;
    private bool _disposed;

    public bool IsActive
    {
        get { lock (_syncRoot) return _isActive; }
    }

    public ParsecMouseFocusIsolator()
    {
        // Сохраняем делегаты в полях класса для предотвращения сборки мусора (GC Protection)
        _hookProc = HookCallback;
        _winEventProc = WinEventCallback;
    }

    public void SetActiveState(bool active)
    {
        bool shouldStop = false;
        bool shouldStart = false;

        lock (_syncRoot)
        {
            if (_isActive == active) return;
            _isActive = active;

            if (_isActive)
            {
                shouldStart = true;
                UpdateWindowStatesLocked();
            }
            else
            {
                shouldStop = true;
                RestoreAllWindowsLocked();
            }
        }

        if (shouldStart)
        {
            StartHookThread();
        }
        else if (shouldStop)
        {
            StopHookThread();
        }
    }

    public void UpdateTrackedProcesses(IEnumerable<ParsecProcessInfo> processes)
    {
        lock (_syncRoot)
        {
            _trackedProcessWindows.Clear();
            var newSnapshot = new Dictionary<int, IntPtr>();
            foreach (var proc in processes)
            {
                _trackedProcessWindows[proc.Pid] = proc.MainWindowHandle;
                newSnapshot[proc.Pid] = proc.MainWindowHandle;
            }
            Volatile.Write(ref _trackedWindowsSnapshot, newSnapshot);

            if (_isActive)
            {
                UpdateWindowStatesLocked();
            }
        }
    }

    public void Dispose()
    {
        bool shouldStop = false;
        lock (_syncRoot)
        {
            if (_disposed) return;
            _disposed = true;

            if (_isActive)
            {
                _isActive = false;
                shouldStop = true;
                RestoreAllWindowsLocked();
            }
            _trackedProcessWindows.Clear();
        }

        if (shouldStop)
        {
            StopHookThread();
        }

        GC.SuppressFinalize(this);
    }

    private void StartHookThread()
    {
        lock (_syncRoot)
        {
            if (_hookThread != null) return;
        }

        using var readyEvent = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            _hookThreadId = NativeMethods.GetCurrentThreadId();
            IntPtr hModule = NativeMethods.GetModuleHandle(null);
            
            _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _hookProc, hModule, 0);
            _winEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventProc,
                0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);

            // Гарантируем создание очереди сообщений Win32 до взвода readyEvent
            NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);
            readyEvent.Set();

            if (_hookHandle != IntPtr.Zero)
            {
                // Запуск Win32 Message Pump для обработки хука и WinEvent
                while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
                {
                    if (msg.message == NativeMethods.WM_QUIT) break;
                    NativeMethods.TranslateMessage(in msg);
                    NativeMethods.DispatchMessage(in msg);
                }
            }

            // Выполняем снятие хуков строго в контексте потока, который их создал
            if (_winEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }

            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        })
        {
            IsBackground = true,
            Name = "ParsecMouseHookThread"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        readyEvent.Wait();

        lock (_syncRoot)
        {
            _hookThread = thread;
        }
    }

    private void StopHookThread()
    {
        Thread? threadToJoin;
        uint threadId;

        lock (_syncRoot)
        {
            threadToJoin = _hookThread;
            threadId = _hookThreadId;
            _hookThread = null;
            _hookThreadId = 0;
        }

        if (threadId != 0)
        {
            // Отправляем WM_QUIT в поток хука для грациозного выхода из цикла GetMessage
            NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }

        if (threadToJoin != null && threadToJoin.IsAlive)
        {
            threadToJoin.Join(1000);
        }
    }

    private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        lock (_syncRoot)
        {
            if (!_isActive) return;
            UpdateWindowStatesLocked();
        }
    }

    private void UpdateWindowStatesLocked()
    {
        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);

        foreach (var (pid, hWnd) in _trackedProcessWindows)
        {
            bool isForeground = (fgPid == (uint)pid);

            if (hWnd != IntPtr.Zero)
            {
                NativeMethods.EnableWindow(hWnd, isForeground);
            }
        }
    }

    private void RestoreAllWindowsLocked()
    {
        foreach (var hWnd in _trackedProcessWindows.Values)
        {
            if (hWnd != IntPtr.Zero)
            {
                NativeMethods.EnableWindow(hWnd, true);
            }
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsActive)
        {
            int message = wParam.ToInt32();

            // Перехватываем клик мышью (ЛКМ), чтобы включить ввод и сфокусировать неактивное окно Parsec
            if (message == NativeMethods.WM_LBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                IntPtr hWndUnderCursor = NativeMethods.WindowFromPoint(hookStruct.pt);

                if (hWndUnderCursor != IntPtr.Zero)
                {
                    IntPtr rootHwnd = NativeMethods.GetAncestor(hWndUnderCursor, NativeMethods.GA_ROOT);
                    if (rootHwnd == IntPtr.Zero) rootHwnd = hWndUnderCursor;

                    NativeMethods.GetWindowThreadProcessId(rootHwnd, out uint targetPid);

                    // Lock-free проверка через неизменяемый снимок snapshot без блокировки системного потока мыши
                    var snapshot = Volatile.Read(ref _trackedWindowsSnapshot);
                    bool isTracked = snapshot.ContainsKey((int)targetPid);

                    if (isTracked)
                    {
                        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
                        NativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);

                        if (fgPid != targetPid)
                        {
                            // Включаем окно и переводим его в фокус при клике ЛКМ
                            NativeMethods.EnableWindow(rootHwnd, true);
                            NativeMethods.SetForegroundWindow(rootHwnd);
                        }
                    }
                }
            }
        }

        // Все сообщения движения мыши пропускаются через CallNextHookEx,
        // что позволяет курсору ОС свободно передвигаться по экрану
        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}
