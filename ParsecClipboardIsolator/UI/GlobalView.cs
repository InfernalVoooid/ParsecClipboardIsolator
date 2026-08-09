using System;
using ParsecClipboardIsolator.Services;

namespace ParsecClipboardIsolator.UI;

// Отрисовка глобального режима изоляции (блокировка применяется ко всем окнам Parsec сразу)
internal sealed class GlobalView : IView
{
    private readonly ConsoleLogBox _logBox = new();

    // Координаты строк консоли для точечного обновления информации без полного мерцания экрана
    private int _statusCursorTop;
    private int _trackedCountCursorTop;

    public void DrawFull(ParsecIsolator isolator)
    {
        Console.Clear();
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=========================================================================");
        Console.Write("     Parsec Clipboard Isolator                          ");
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine("[GLOBAL MODE]    ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=========================================================================");
        Console.ResetColor();

        _trackedCountCursorTop = Console.CursorTop;
        Console.WriteLine($"[+] Отслеживается окон Parsec: {isolator.TrackedProcessesCount}");
        
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\nУправление:");
        Console.ResetColor();

        WriteHotkeyLine("R", "- Обновить список окон");
        WriteHotkeyLine("->", "- Перейти в ТАРГЕТНЫЙ режим (выбор окон)");
        WriteHotkeyLine("Esc", "- Выйти из программы");

        // ДВА отступа перед секцией текущего состояния систем по требованию пользователя
        Console.WriteLine();
        Console.WriteLine();

        _statusCursorTop = Console.CursorTop;
        
        // Заранее рисуем визуальный блок журнала событий (мини-консоль)
        _logBox.TopPosition = _statusCursorTop + 5;
        _logBox.DrawFrame();

        UpdateDynamic(isolator);
    }

    public void UpdateDynamic(ParsecIsolator isolator)
    {
        Console.SetCursorPosition(0, _trackedCountCursorTop);
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.SetCursorPosition(0, _trackedCountCursorTop);
        Console.WriteLine($"[+] Отслеживается окон Parsec: {isolator.TrackedProcessesCount}");

        Console.SetCursorPosition(0, _statusCursorTop);
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("Текущее состояние систем:".PadRight(Console.WindowWidth - 1));
        Console.ResetColor();

        // Секция 1: Изоляция буфера обмена (строка 1)
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.SetCursorPosition(0, _statusCursorTop + 1);
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  [ENTER] ");
        Console.ResetColor();

        if (isolator.IsGlobalBlockActive)
        {
            Console.BackgroundColor = ConsoleColor.DarkGreen;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" [БУФЕР: РАЗДЕЛЕН] ");
            Console.ResetColor();
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"  Буфер обмена полностью изолирован от хоста ({isolator.TrackedProcessesCount} окон).");
        }
        else
        {
            Console.BackgroundColor = ConsoleColor.DarkYellow;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write(" [БУФЕР: ОБЩИЙ] ");
            Console.ResetColor();
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  Буфер обмена общий и синхронизируется со всеми окнами.");
        }
        Console.ResetColor();
        Console.WriteLine();

        // Секция 2: Защита фокуса мыши (строка 2, напрямую под первой строкой без пустого отступа!)
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.SetCursorPosition(0, _statusCursorTop + 2);
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  [F]     ");
        Console.ResetColor();

        if (isolator.IsMouseFocusBlockActive)
        {
            Console.BackgroundColor = ConsoleColor.DarkCyan;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" [ФОКУС МЫШИ: ЗАЩИЩЕН] ");
            Console.ResetColor();
            
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("  Неактивные окна Parsec игнорируют движение мыши (активация по ЛКМ).");
        }
        else
        {
            Console.BackgroundColor = ConsoleColor.DarkYellow;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write(" [ФОКУС МЫШИ: ОТКЛЮЧЕН] ");
            Console.ResetColor();
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  Движения мыши передаются штатно во все окна Parsec.");
        }
        Console.ResetColor();

        Console.SetCursorPosition(0, Math.Min(Console.BufferHeight - 1, _logBox.TopPosition + 2));
        Console.CursorVisible = false;
    }

    public void HandleKey(ConsoleKeyInfo key, ParsecIsolator isolator)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                ToggleGlobalBlock(isolator);
                break;
            case ConsoleKey.F:
                ToggleMouseFocus(isolator);
                break;
        }
    }

    public void ShowFeedback(string message, ConsoleColor color) => _logBox.ShowFeedback(message, color);

    public void ClearFeedback() => _logBox.ClearFeedback();

    private void ToggleGlobalBlock(ParsecIsolator isolator)
    {
        isolator.SetGlobalBlockState(!isolator.IsGlobalBlockActive);
        UpdateDynamic(isolator);
        ShowFeedback(
            isolator.IsGlobalBlockActive ? "Изоляция буфера обмена ВКЛЮЧЕНА для всех окон." : "Буфер обмена ОБЪЕДИНЕН со всеми окнами.", 
            isolator.IsGlobalBlockActive ? ConsoleColor.Green : ConsoleColor.Yellow
        );
    }

    private void ToggleMouseFocus(ParsecIsolator isolator)
    {
        bool newState = isolator.ToggleMouseFocusBlockState();
        UpdateDynamic(isolator);
        ShowFeedback(
            newState ? "Контроль фокуса мыши ВКЛЮЧЕН (неактивные окна защищены)." : "Контроль фокуса мыши ВЫКЛЮЧЕН.", 
            newState ? ConsoleColor.Cyan : ConsoleColor.Yellow
        );
    }

    private static void WriteHotkeyLine(string key, string desc)
    {
        Console.ForegroundColor = ConsoleColor.Yellow; 
        Console.Write($"  [{key}] ".PadRight(10)); 
        Console.ForegroundColor = ConsoleColor.DarkGray; 
        Console.WriteLine(desc);
        Console.ResetColor();
    }
}
