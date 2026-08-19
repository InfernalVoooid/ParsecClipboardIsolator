using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
//
// Модель потоков (разделение обязательно, а не стилистический выбор):
//   hook-поток   — только WH_MOUSE_LL и WinEvent. Коллбэки обязаны укладываться в
//                  LowLevelHooksTimeout (по умолчанию 300 мс), иначе Windows пропускает
//                  событие мыши и может снять хук. Поэтому в них нет ни одного lock и ни
//                  одного вызова, способного заблокироваться на чужом процессе.
//   worker-поток — вся работа с окнами: EnableWindow шлёт синхронный WM_ENABLE в чужой
//                  процесс без таймаута и зависает вместе с неотвечающим Parsec.
//                  Здесь такая пауза безвредна, на hook-потоке она вешала ввод всей системы.
[SupportedOSPlatform("windows")]
internal sealed class ParsecMouseFocusIsolator : IDisposable
{
    // Полный обход окон системы для поиска потерянных HWND — не чаще этого интервала
    private const int WindowRescanIntervalMs = 2000;

    // Окно становится передним асинхронно: пока идёт активация, ближайший проход
    // состояний увидит прежний foreground и отключит только что открытое окно
    private const int ActivationGraceMs = 500;

    private const int ThreadJoinTimeoutMs = 2000;
    private const int HookStartTimeoutMs = 5000;

    private readonly object _syncRoot = new();
    private readonly LowLevelMouseProc _hookProc;
    private readonly WinEventProc _winEventProc;

    // Событие с автосбросом склеивает пачку сигналов в один проход: при шквале
    // переключений фокуса состояние всё равно пересчитывается по текущему снимку ОС.
    private readonly AutoResetEvent _workSignal = new(false);
    private readonly ConcurrentQueue<(IntPtr Hwnd, uint Pid)> _activationRequests = new();

    // Публикуется целиком и после публикации не мутируется, поэтому читается
    // из hook-потока через Volatile.Read без блокировок.
    private IReadOnlyDictionary<int, IntPtr> _trackedWindows = new Dictionary<int, IntPtr>();

    private Thread? _hookThread;
    private Thread? _workerThread;
    private uint _hookThreadId;
    private IntPtr _hookHandle;
    private IntPtr _winEventHook;

    private volatile bool _isActive;
    private volatile bool _workerShutdown;
    private string? _lastFailureReason;
    private bool _disposed;

    private long _lastWindowScanTicks;
    private IntPtr _recentlyActivatedHwnd;
    private long _recentActivationTicks;

    public bool IsActive => _isActive;

    // Читается один раз и сбрасывается: иначе разовый отказ установки хука навсегда
    // подменял бы собой обычные сообщения журнала.
    public string? ConsumeFailureReason() => Interlocked.Exchange(ref _lastFailureReason, null);

    private void ReportFailure(string reason) => Interlocked.Exchange(ref _lastFailureReason, reason);

    public ParsecMouseFocusIsolator()
    {
        // Сохраняем делегаты в полях класса для предотвращения сборки мусора (GC Protection)
        _hookProc = HookCallback;
        _winEventProc = WinEventCallback;
    }

    public void SetActiveState(bool active)
    {
        lock (_syncRoot)
        {
            if (_disposed || _isActive == active) return;
            _isActive = active;
        }

        if (active)
        {
            StartThreads();
            _workSignal.Set();
        }
        else
        {
            StopThreads();
            RestoreAllWindows();
        }
    }

    public void UpdateTrackedProcesses(IEnumerable<ParsecProcessInfo> processes)
    {
        var snapshot = new Dictionary<int, IntPtr>();
        foreach (var proc in processes)
        {
            snapshot[proc.Pid] = proc.MainWindowHandle;
        }

        lock (_syncRoot)
        {
            Volatile.Write(ref _trackedWindows, snapshot);
        }

        if (_isActive)
        {
            _workSignal.Set();
        }
        else
        {
            // Окна могли остаться заблокированными после аварийного завершения прошлого
            // запуска (диспетчер задач, падение) — привязавшись к процессу, снимаем блок.
            RestoreAllWindows();
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed) return;
            _disposed = true;
            _isActive = false;
        }

        // Потоки останавливаются до восстановления, чтобы никто не выставил окна
        // обратно в disabled уже после отката.
        StopThreads();
        RestoreAllWindows();

        lock (_syncRoot)
        {
            Volatile.Write(ref _trackedWindows, new Dictionary<int, IntPtr>());
        }

        // _workSignal намеренно не освобождается: коллбэк хука, уже вошедший в работу,
        // не прерывается UnhookWindowsHookEx и может дёрнуть Set() после Dispose.
        // Освобождение дескриптора здесь дало бы падение на самом выходе из программы.
        GC.SuppressFinalize(this);
    }

    // ---- hook-поток: только неблокирующие вызовы -------------------------------------

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isActive)
        {
            int message = (int)wParam;
            if (message is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN)
            {
                QueueActivationIfTracked(lParam);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    // Перехватываем физический клик мышью, чтобы включить ввод и сфокусировать неактивное
    // окно Parsec. Игнорируем инжектированный (синтетический) ввод от удаленных сессий
    // Parsec/RDP (LLMHF_INJECTED). Сама активация выполняется рабочим потоком.
    private unsafe void QueueActivationIfTracked(IntPtr lParam)
    {
        ref readonly MSLLHOOKSTRUCT hookStruct = ref Unsafe.AsRef<MSLLHOOKSTRUCT>((void*)lParam);

        if ((hookStruct.flags & (NativeMethods.LLMHF_INJECTED | NativeMethods.LLMHF_LOWER_IL_INJECTED)) != 0) return;

        IntPtr hWndUnderCursor = NativeMethods.WindowFromPoint(hookStruct.pt);
        if (hWndUnderCursor == IntPtr.Zero) return;

        IntPtr rootHwnd = NativeMethods.GetAncestor(hWndUnderCursor, NativeMethods.GA_ROOT);
        if (rootHwnd == IntPtr.Zero) rootHwnd = hWndUnderCursor;

        NativeMethods.GetWindowThreadProcessId(rootHwnd, out uint targetPid);
        if (!Volatile.Read(ref _trackedWindows).ContainsKey((int)targetPid)) return;

        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);
        if (fgPid == targetPid) return;

        // Единственный кросс-процессный вызов, оставленный в хуке: коллбэк выполняется
        // ДО того, как система положит клик в очередь окна, поэтому включение здесь
        // сохраняет привычное поведение "одним кликом и разбудил, и нажал". Отложи мы
        // его в рабочий поток — первый клик уходил бы в ещё выключенное окно вхолостую.
        // Риск блокировки снят проверкой отзывчивости: неотвечающее окно всё равно
        // не обработало бы клик, и его пробуждение целиком уходит в рабочий поток.
        if (!NativeMethods.IsWindowEnabled(rootHwnd) && !NativeMethods.IsHungAppWindow(rootHwnd))
        {
            NativeMethods.EnableWindow(rootHwnd, true);
        }

        _activationRequests.Enqueue((rootHwnd, targetPid));
        _workSignal.Set();
    }

    private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // WinEvent доставляется в очередь того же потока, что обслуживает WH_MOUSE_LL,
        // поэтому здесь недопустима никакая работа: пока коллбэк не вернулся,
        // события мыши всей системы не обрабатываются.
        if (_isActive) _workSignal.Set();
    }

    // ---- worker-поток: вся работа с окнами -------------------------------------------

    private void WorkerLoop()
    {
        while (true)
        {
            _workSignal.WaitOne();
            if (_workerShutdown) return;

            try
            {
                ProcessActivationRequests();
                if (_isActive) ApplyWindowStates();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ReportFailure($"Сбой изоляции фокуса мыши: {ex.Message}");
            }
        }
    }

    private void ProcessActivationRequests()
    {
        while (_activationRequests.TryDequeue(out var request))
        {
            if (!NativeMethods.IsWindow(request.Hwnd)) continue;

            SetWindowEnabled(request.Hwnd, true);
            WindowLocator.ActivateWindow(request.Hwnd, request.Pid);

            Volatile.Write(ref _recentlyActivatedHwnd, request.Hwnd);
            Volatile.Write(ref _recentActivationTicks, Environment.TickCount64);
        }
    }

    private void ApplyWindowStates()
    {
        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();

        // Переднего окна нет: экран блокировки, запрос UAC на защищённом рабочем столе,
        // переключение виртуального стола. Определить активный процесс нельзя, а прежняя
        // логика в этот момент считала неактивными сразу все окна и глушила их до
        // следующего события фокуса — вплоть до полной потери ввода в Parsec.
        if (fgHwnd == IntPtr.Zero) return;

        NativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);

        var tracked = Volatile.Read(ref _trackedWindows);
        bool needsRescan = false;

        foreach (var (pid, hWnd) in tracked)
        {
            if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
            {
                needsRescan = true;
                continue;
            }

            bool shouldBeEnabled = fgPid == (uint)pid;
            if (!shouldBeEnabled && IsWithinActivationGrace(hWnd)) continue;

            SetWindowEnabled(hWnd, shouldBeEnabled);
        }

        if (needsRescan) RescanWindowsThrottled();
    }

    private bool IsWithinActivationGrace(IntPtr hWnd)
        => Volatile.Read(ref _recentlyActivatedHwnd) == hWnd
           && Environment.TickCount64 - Volatile.Read(ref _recentActivationTicks) < ActivationGraceMs;

    private void SetWindowEnabled(IntPtr hWnd, bool enable)
    {
        // EnableWindow шлёт окну синхронный WM_ENABLE без таймаута, поэтому вызывается
        // только при реальном расхождении состояния: на переключение фокуса это оставляет
        // один-два вызова вместо одного на каждое отслеживаемое окно.
        if (NativeMethods.IsWindowEnabled(hWnd) == enable) return;

        // Неотвечающее окно (типично для свёрнутого Parsec) пропускаем только при
        // отключении: оставить окно заблокированным навсегда хуже, чем подождать.
        if (!enable && NativeMethods.IsHungAppWindow(hWnd)) return;

        NativeMethods.EnableWindow(hWnd, enable);
    }

    // Parsec пересоздаёт окно при смене режима отображения и переподключении сессии,
    // из-за чего кэшированный HWND протухает и защита молча перестаёт работать.
    private void RescanWindowsThrottled()
    {
        long now = Environment.TickCount64;
        if (now - _lastWindowScanTicks < WindowRescanIntervalMs) return;
        _lastWindowScanTicks = now;

        var windowMap = WindowLocator.BuildProcessWindowMap();
        bool changed = false;

        lock (_syncRoot)
        {
            var current = Volatile.Read(ref _trackedWindows);
            var updated = new Dictionary<int, IntPtr>(current.Count);

            foreach (var (pid, cached) in current)
            {
                IntPtr resolved = WindowLocator.IsUsableWindow(cached)
                    ? cached
                    : windowMap.GetValueOrDefault((uint)pid, IntPtr.Zero);

                if (resolved != cached) changed = true;
                updated[pid] = resolved;
            }

            if (changed) Volatile.Write(ref _trackedWindows, updated);
        }

        // Новые дескрипторы применяем следующим проходом, а не рекурсией:
        // без изменений сигнал не взводится, поэтому цикл сходится.
        if (changed) _workSignal.Set();
    }

    private void RestoreAllWindows()
    {
        foreach (var (_, hWnd) in Volatile.Read(ref _trackedWindows))
        {
            if (hWnd != IntPtr.Zero && NativeMethods.IsWindow(hWnd))
            {
                SetWindowEnabled(hWnd, true);
            }
        }
    }

    // ---- жизненный цикл потоков ------------------------------------------------------

    private void StartThreads()
    {
        lock (_syncRoot)
        {
            if (_hookThread != null) return;
        }

        _workerShutdown = false;
        var worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "ParsecFocusWorker"
        };
        worker.Start();

        // Событие не оборачивается в using: при истечении таймаута поток хука ещё жив
        // и обратился бы к освобождённому объекту.
        var readyEvent = new ManualResetEventSlim(false);
        var hookThread = new Thread(() => HookThreadBody(readyEvent))
        {
            IsBackground = true,
            Name = "ParsecMouseHookThread"
        };
        hookThread.Start();

        if (!readyEvent.Wait(HookStartTimeoutMs))
        {
            ReportFailure("Поток хука мыши не запустился за отведённое время.");
        }

        lock (_syncRoot)
        {
            _hookThread = hookThread;
            _workerThread = worker;
        }
    }

    private void HookThreadBody(ManualResetEventSlim readyEvent)
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        IntPtr hModule = NativeMethods.GetModuleHandle(null);

        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _hookProc, hModule, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            ReportFailure($"Не удалось установить хук мыши (код {Marshal.GetLastPInvokeError()}). Активация окон по клику недоступна.");
        }

        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);

        if (_winEventHook == IntPtr.Zero)
        {
            ReportFailure($"Не удалось подписаться на события фокуса (код {Marshal.GetLastPInvokeError()}).");
        }

        // Гарантируем создание очереди сообщений Win32 до взвода readyEvent
        NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);
        readyEvent.Set();

        if (_hookHandle != IntPtr.Zero || _winEventHook != IntPtr.Zero)
        {
            // Запуск Win32 Message Pump: система вызывает коллбэки хука и WinEvent
            // внутри GetMessage, поэтому поток обязан непрерывно качать очередь.
            while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
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
    }

    private void StopThreads()
    {
        Thread? hookThread;
        Thread? workerThread;
        uint hookThreadId;

        lock (_syncRoot)
        {
            hookThread = _hookThread;
            workerThread = _workerThread;
            hookThreadId = _hookThreadId;
            _hookThread = null;
            _workerThread = null;
            _hookThreadId = 0;
        }

        if (hookThreadId != 0)
        {
            // Отправляем WM_QUIT в поток хука для грациозного выхода из цикла GetMessage
            NativeMethods.PostThreadMessage(hookThreadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }
        hookThread?.Join(ThreadJoinTimeoutMs);

        _workerShutdown = true;
        _workSignal.Set();
        workerThread?.Join(ThreadJoinTimeoutMs);

        _activationRequests.Clear();
    }
}
