using System;

namespace ParsecClipboardIsolator.UI;

// Изолированный UI-компонент консольного журнала событий (Лог-бокс)
internal sealed class ConsoleLogBox
{
    public int TopPosition { get; set; }

    public void DrawFrame()
    {
        try
        {
            Console.CursorVisible = false;
            int totalWidth = Math.Max(40, Console.WindowWidth - 1);
            int innerWidth = totalWidth - 4; // Отступы слева и справа "│ " и " │"
            int dashCount = Math.Max(0, innerWidth - 15); // 15 - длина " Журнал событий "

            Console.SetCursorPosition(0, TopPosition);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("┌─ ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Журнал событий");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" {new string('─', dashCount)}┐");

            Console.WriteLine($"│{new string(' ', Math.Max(0, totalWidth - 2))}│");
            Console.Write($"└{new string('─', Math.Max(0, totalWidth - 2))}┘");
            Console.ResetColor();

            Console.SetCursorPosition(0, Math.Min(Console.BufferHeight - 1, TopPosition + 2));
            Console.CursorVisible = false;
        }
        catch
        {
            // Безопасное игнорирование при быстрой смене размера окна консоли
        }
    }

    public void ShowFeedback(string message, ConsoleColor color)
    {
        try
        {
            Console.CursorVisible = false;
            int totalWidth = Math.Max(40, Console.WindowWidth - 1);
            int contentWidth = Math.Max(10, totalWidth - 4);

            Console.SetCursorPosition(0, TopPosition + 1);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("│ ");
            
            Console.ForegroundColor = color;
            string text = $"[+] {message}";
            if (text.Length > contentWidth) text = text[..contentWidth];
            else text = text.PadRight(contentWidth);

            Console.Write(text);
            
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │");
            Console.ResetColor();

            Console.SetCursorPosition(0, Math.Min(Console.BufferHeight - 1, TopPosition + 2));
            Console.CursorVisible = false;
        }
        catch
        {
            // Игнорируем исключения при ресайзе
        }
    }

    public void ClearFeedback()
    {
        try
        {
            Console.CursorVisible = false;
            int totalWidth = Math.Max(40, Console.WindowWidth - 1);
            int contentWidth = Math.Max(10, totalWidth - 4);
            Console.SetCursorPosition(2, TopPosition + 1);
            Console.Write(new string(' ', contentWidth));

            Console.SetCursorPosition(0, Math.Min(Console.BufferHeight - 1, TopPosition + 2));
            Console.CursorVisible = false;
        }
        catch
        {
            // Игнорируем исключения при ресайзе
        }
    }
}
