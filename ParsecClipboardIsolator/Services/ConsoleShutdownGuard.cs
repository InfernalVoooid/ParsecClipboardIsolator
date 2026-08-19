using System;
using System.Runtime.Versioning;
using System.Threading;
using ParsecClipboardIsolator.Native;

namespace ParsecClipboardIsolator.Services;

// .NET транслирует в CancelKeyPress только Ctrl+C и Ctrl+Break. Закрытие консоли
// по крестику — это CTRL_CLOSE_EVENT: среда его не обрабатывает, процесс снимается
// самой ОС, поэтому ни AppDomain.ProcessExit, ни финализаторы не выполняются и
// откат патча памяти с разблокировкой окон Parsec не происходит.
// Единственный способ получить это событие — SetConsoleCtrlHandler.
[SupportedOSPlatform("windows")]
internal static class ConsoleShutdownGuard
{
    // ОС хранит только неуправляемый указатель на thunk делегата, поэтому сам делегат
    // обязан жить в статическом поле — иначе GC соберёт его вместе с thunk-ом.
    private static ConsoleCtrlHandler? _handler;
    private static Action? _cleanup;
    private static int _cleanupStarted;

    public static bool Install(Action cleanup)
    {
        _cleanup = cleanup;
        _handler = OnConsoleCtrlEvent;
        return NativeMethods.SetConsoleCtrlHandler(_handler, true);
    }

    // Очистка вызывается из потока, созданного ОС для обработчика, и параллельно
    // может прийти из ProcessExit, поэтому выполняется строго один раз.
    public static void RunCleanupOnce()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0) return;
        _cleanup?.Invoke();
    }

    private static bool OnConsoleCtrlEvent(uint ctrlType)
    {
        RunCleanupOnce();

        // TRUE обрывает цепочку обработчиков — для CLOSE/LOGOFF/SHUTDOWN процесс всё
        // равно снимет система, нам нужно лишь успеть завершить откат. Для Ctrl+C и
        // Ctrl+Break отдаём событие дальше, сохраняя штатное поведение консоли.
        return ctrlType is NativeMethods.CTRL_CLOSE_EVENT
            or NativeMethods.CTRL_LOGOFF_EVENT
            or NativeMethods.CTRL_SHUTDOWN_EVENT;
    }
}
