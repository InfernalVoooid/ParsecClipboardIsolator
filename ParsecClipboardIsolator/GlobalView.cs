using System;

namespace ParsecClipboardIsolator
{
    // Отрисовка глобального режима изоляции (блокировка применяется ко всем окнам Parsec сразу)
    internal sealed class GlobalView : IView
    {
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
            
            Console.WriteLine("\nУправление:");
            
            WriteHotkeyLine("ENTER", "- Изолировать / Объединить ВСЕ буферы");
            WriteHotkeyLine("R", "- Обновить список окон");
            WriteHotkeyLine("->", "- Перейти в ТАРГЕТНЫЙ режим (выбор окон)");
            WriteHotkeyLine("Esc", "- Выйти из программы\n");

            _statusCursorTop = Console.CursorTop;
            UpdateDynamic(isolator);
        }

        private static void WriteHotkeyLine(string key, string desc)
        {
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write($"  [{key}] ".PadRight(10)); 
            Console.ResetColor(); Console.WriteLine(desc);
        }

        public void UpdateDynamic(ParsecIsolator isolator)
        {
            Console.SetCursorPosition(0, _trackedCountCursorTop);
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.SetCursorPosition(0, _trackedCountCursorTop);
            Console.WriteLine($"[+] Отслеживается окон Parsec: {isolator.TrackedProcessesCount}");

            Console.SetCursorPosition(0, _statusCursorTop);
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.SetCursorPosition(0, _statusCursorTop);

            if (isolator.IsGlobalBlockActive)
            {
                Console.BackgroundColor = ConsoleColor.DarkGreen;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(" [РАЗДЕЛЕНО] ");
                Console.ResetColor();
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($" Буфер обмена полностью изолирован от хоста для {isolator.TrackedProcessesCount} окон. ");
            }
            else
            {
                Console.BackgroundColor = ConsoleColor.DarkYellow;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.Write(" [ОБЪЕДИНЕНО] ");
                Console.ResetColor();
                
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($" Буфер обмена общий и синхронизируется со всеми окнами. ");
            }
            
            Console.ResetColor();
        }

        public void HandleKey(ConsoleKeyInfo key, ParsecIsolator isolator)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                isolator.SetGlobalBlockState(!isolator.IsGlobalBlockActive);
                UpdateDynamic(isolator);
                ClearFeedback();
            }
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
}
