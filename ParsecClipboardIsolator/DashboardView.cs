using System;

namespace ParsecClipboardIsolator;

internal sealed class DashboardView
{
    private int _statusCursorTop;
    private int _trackedCountCursorTop;

    public void DrawInitialDashboard(int trackedCount)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("           Parsec Clipboard Isolator             ");
        Console.WriteLine("=================================================");
        Console.ResetColor();

        _trackedCountCursorTop = Console.CursorTop;
        Console.WriteLine($"[+] Отслеживается окон Parsec: {trackedCount}");
        
        Console.WriteLine("\nУправление:");
        Console.WriteLine("  [ENTER] - Разделить / Объединить буфер обмена");
        Console.WriteLine("  [R]     - Обновить список окон (найти новые / очистить закрытые)");
        Console.WriteLine("  [Esc]   - Выйти из программы\n");

        _statusCursorTop = Console.CursorTop;
    }

    public void UpdateTrackedCount(int trackedCount)
    {
        Console.SetCursorPosition(0, _trackedCountCursorTop);
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.SetCursorPosition(0, _trackedCountCursorTop);
        Console.Write($"[+] Отслеживается окон Parsec: {trackedCount}");
    }

    public void UpdateStatus(bool isActive, int trackedCount)
    {
        Console.SetCursorPosition(0, _statusCursorTop);
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.SetCursorPosition(0, _statusCursorTop);

        if (isActive)
        {
            Console.BackgroundColor = ConsoleColor.DarkGreen;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" [РАЗДЕЛЕНО] ");
            Console.ResetColor();
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($" Буфер обмена независим. Parsec ({trackedCount} окон) изолирован от локального ПК. ");
        }
        else
        {
            Console.BackgroundColor = ConsoleColor.DarkYellow;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write(" [ОБЪЕДИНЕНО] ");
            Console.ResetColor();
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($" Буфер обмена общий. Parsec ({trackedCount} окон) синхронизирует данные. ");
        }
        
        Console.ResetColor();
    }

    public void ShowFeedback(string message, ConsoleColor color)
    {
        Console.SetCursorPosition(0, _statusCursorTop + 2);
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.SetCursorPosition(0, _statusCursorTop + 2);
        
        Console.ForegroundColor = color;
        Console.Write(message);
        Console.ResetColor();
    }

    public void ClearFeedback()
    {
        Console.SetCursorPosition(0, _statusCursorTop + 2);
        Console.Write(new string(' ', Console.WindowWidth - 1));
    }
}
