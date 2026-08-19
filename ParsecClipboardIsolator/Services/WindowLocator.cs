using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using ParsecClipboardIsolator.Native;

namespace ParsecClipboardIsolator.Services;

// Поиск главных окон процессов и безопасный вывод окна на передний план.
// Вынесено из ParsecIsolator, потому что тем же поиском пользуется изолятор фокуса мыши.
[SupportedOSPlatform("windows")]
internal static class WindowLocator
{
    // Один обход окон системы заполняет карту "PID -> главное окно" сразу для всех процессов.
    // Раньше EnumWindows запускался отдельно на каждый процесс без окна и на каждое событие
    // фокуса, что давало десятки полных обходов системы в секунду на критическом пути.
    public static Dictionary<uint, IntPtr> BuildProcessWindowMap()
    {
        var best = new Dictionary<uint, (IntPtr Hwnd, int Score)>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            if (NativeMethods.GetAncestor(hWnd, NativeMethods.GA_ROOT) != hWnd) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return true;

            int score = ScoreWindow(hWnd);
            if (!best.TryGetValue(pid, out var current) || score > current.Score)
            {
                best[pid] = (hWnd, score);
            }
            return true;
        }, IntPtr.Zero);

        var map = new Dictionary<uint, IntPtr>(best.Count);
        foreach (var (pid, entry) in best)
        {
            map[pid] = entry.Hwnd;
        }
        return map;
    }

    public static bool IsUsableWindow(IntPtr hWnd)
        => hWnd != IntPtr.Zero && NativeMethods.IsWindow(hWnd) && NativeMethods.IsWindowVisible(hWnd);

    // Отбирает окно сессии Parsec среди прочих окон процесса. Выбор "первое видимое"
    // ненадёжен: у parsecd есть служебные tray- и overlay-окна, а на Windows 11 видимыми
    // числятся и окна с других виртуальных столов. Окно сессии отличают заголовок
    // и отсутствие WS_EX_TOOLWINDOW, поэтому кандидаты ранжируются, а не отсеиваются:
    // при единственном кандидате он будет выбран даже с нулевым счётом.
    private static int ScoreWindow(IntPtr hWnd)
    {
        int score = 0;
        if (NativeMethods.GetWindowTextLength(hWnd) > 0) score += 4;
        if ((NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) == 0) score += 2;
        return score;
    }

    // Выводит окно на передний план. Вызывать только из фоновых потоков: SetForegroundWindow
    // и AttachThreadInput блокируются на неотвечающем приложении.
    public static bool ActivateWindow(IntPtr hWnd, uint targetPid)
    {
        if (hWnd == IntPtr.Zero) return false;

        NativeMethods.AllowSetForegroundWindow(targetPid);

        // SW_RESTORE применяется только к свёрнутому окну: для максимизированного
        // он означает "вернуть обычный размер" и ломает геометрию окна пользователя.
        if (NativeMethods.IsIconic(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
        }

        NativeMethods.BringWindowToTop(hWnd);
        if (NativeMethods.SetForegroundWindow(hWnd)) return true;

        // AttachThreadInput сливает очереди ввода двух потоков: пока они связаны,
        // состояние клавиш общее, и разрыв связи при зажатой клавише оставляет её
        // "залипшей". Поэтому только запасной путь, когда обычная активация отклонена
        // foreground-локом Windows.
        return ActivateThroughAttachedInput(hWnd);
    }

    private static bool ActivateThroughAttachedInput(IntPtr hWnd)
    {
        uint currentThreadId = NativeMethods.GetCurrentThreadId();
        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
        uint fgThreadId = fgHwnd != IntPtr.Zero ? NativeMethods.GetWindowThreadProcessId(fgHwnd, out _) : 0;

        bool activated = false;
        if (fgThreadId != 0 && fgThreadId != currentThreadId && NativeMethods.AttachThreadInput(currentThreadId, fgThreadId, true))
        {
            try
            {
                NativeMethods.BringWindowToTop(hWnd);
                activated = NativeMethods.SetForegroundWindow(hWnd);
            }
            finally
            {
                NativeMethods.AttachThreadInput(currentThreadId, fgThreadId, false);
            }
        }

        if (!activated)
        {
            // Недокументированный, но единственный способ пробить foreground-лок,
            // когда документированный путь отклонён системой.
            NativeMethods.SwitchToThisWindow(hWnd, true);
            activated = true;
        }

        return activated;
    }
}
