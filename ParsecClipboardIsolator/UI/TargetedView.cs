using System;
using System.IO;
using System.Linq;
using ParsecClipboardIsolator.Services;

namespace ParsecClipboardIsolator.UI;

// Отрисовка таргетного режима изоляции (выбор индивидуальных процессов Parsec и профилей)
internal sealed class TargetedView : IView
{
    private readonly ConsoleLogBox _logBox = new();

    private const int MaxVisibleItems = 8;

    private int _selectedIndex;
    private int _scrollOffset;
    private int _headerBottomTop;
    private int _previousBottomTop;

    public void DrawFull(ParsecIsolator isolator)
    {
        Console.Clear();
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=========================================================================");
        Console.Write("     Parsec Clipboard Isolator                          ");
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine("[TARGETED MODE]  ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=========================================================================");
        Console.ResetColor();
        Console.WriteLine();

        _headerBottomTop = Console.CursorTop;
        _previousBottomTop = 0;
        
        UpdateDynamic(isolator);
    }

    public void UpdateDynamic(ParsecIsolator isolator)
    {
        Console.SetCursorPosition(0, _headerBottomTop);

        var processes = isolator.GetTrackedProcessesSnapshot();
        
        if (processes.Length == 0)
        {
            _selectedIndex = 0;
            _scrollOffset = 0;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Нет запущенных окон Parsec.".PadRight(Console.WindowWidth - 1));
            Console.ResetColor();
        }
        else
        {
            if (_selectedIndex >= processes.Length)
            {
                _selectedIndex = processes.Length - 1;
            }
            if (_selectedIndex < 0)
            {
                _selectedIndex = 0;
            }

            if (_selectedIndex < _scrollOffset)
            {
                _scrollOffset = _selectedIndex;
            }
            else if (_selectedIndex >= _scrollOffset + MaxVisibleItems)
            {
                _scrollOffset = _selectedIndex - MaxVisibleItems + 1;
            }

            int maxOffset = Math.Max(0, processes.Length - MaxVisibleItems);
            if (_scrollOffset > maxOffset)
            {
                _scrollOffset = maxOffset;
            }

            bool showScrollIndicators = processes.Length > MaxVisibleItems;

            if (showScrollIndicators)
            {
                if (_scrollOffset > 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine($"  ▲ ... (еще {_scrollOffset} выше)".PadRight(Console.WindowWidth - 1));
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine(new string(' ', Console.WindowWidth - 1));
                }
            }

            int endIndex = Math.Min(processes.Length, _scrollOffset + MaxVisibleItems);
            for (int i = _scrollOffset; i < endIndex; i++)
            {
                var proc = processes[i];
                bool isBlocked = isolator.IsPathBlocked(proc.ExecutablePath);
                
                if (i == _selectedIndex)
                {
                    Console.BackgroundColor = ConsoleColor.DarkGray;
                    Console.ForegroundColor = ConsoleColor.Black;
                }
                else
                {
                    Console.ResetColor();
                }

                string checkbox = isBlocked ? "[X]" : "[ ]";
                Console.Write($" {checkbox} ");
                
                if (isBlocked && i != _selectedIndex)
                    Console.ForegroundColor = ConsoleColor.Red;
                else if (!isBlocked && i != _selectedIndex)
                    Console.ForegroundColor = ConsoleColor.DarkGreen;

                string path = proc.ExecutablePath;
                
                // Извлекаем имя родительской папки (идентификатор инстанса) и имя файла для форматированного вывода
                string? dirName = Path.GetDirectoryName(path);
                string folderName = !string.IsNullOrEmpty(dirName) ? (Path.GetFileName(dirName) ?? "Unknown") : "Unknown";
                if (string.IsNullOrWhiteSpace(folderName)) folderName = "Unknown";
                
                string fileName = Path.GetFileName(path);
                string tail = string.IsNullOrEmpty(fileName) ? @"\parsecd.exe" : @"\" + fileName;
                string pidStr = $"(PID: {proc.Pid})";

                Console.ForegroundColor = (i == _selectedIndex) ? ConsoleColor.Black : ConsoleColor.Cyan;
                Console.Write(folderName.PadRight(40));

                Console.ForegroundColor = (i == _selectedIndex) ? ConsoleColor.Black : ConsoleColor.DarkGray;
                Console.Write(tail.PadRight(15));
                
                Console.ForegroundColor = (i == _selectedIndex) ? ConsoleColor.Black : ConsoleColor.DarkGray;
                int remainingPad = Math.Max(0, Console.WindowWidth - 61);
                Console.Write(pidStr.PadRight(remainingPad));
                
                Console.ResetColor();
                Console.WriteLine();
            }

            if (showScrollIndicators)
            {
                int itemsBelow = processes.Length - (_scrollOffset + MaxVisibleItems);
                if (itemsBelow > 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine($"  ▼ ... (еще {itemsBelow} ниже)".PadRight(Console.WindowWidth - 1));
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine(new string(' ', Console.WindowWidth - 1));
                }
            }
        }
        
        Console.WriteLine(new string(' ', Console.WindowWidth - 1));

        // Контекстные кнопки управления профилями
        bool hasSelection = isolator.HasTargetedBlockedPaths;
        bool hasProfiles = ProfileManager.GetAvailableProfiles().Count > 0;

        if (hasSelection || hasProfiles)
        {
            if (hasSelection)
            {
                WriteFooterBtn("[S]", "Сохранить профиль", ConsoleColor.Green);
            }
            if (hasProfiles)
            {
                WriteFooterBtn("[L]", "Управление профилями", ConsoleColor.Cyan);
            }
            FinishLine();
        }
        else
        {
            Console.WriteLine(new string(' ', Console.WindowWidth - 1));
        }

        WritePaddedLine("-------------------------------------------------------------------------", ConsoleColor.DarkGray);
        
        // ДВА отступа по требованию пользователя для развязки элементов
        Console.WriteLine(new string(' ', Console.WindowWidth - 1));
        Console.WriteLine(new string(' ', Console.WindowWidth - 1));

        WritePaddedLine("Управление списком буферов:", ConsoleColor.White);
        
        WriteFooterBtn("[Space]", "Изолировать окно");
        WriteFooterBtn("[P]", "Прозвон");
        WriteFooterBtn("[1]/[2]", "Все/Ничего");
        FinishLine();
        
        Console.WriteLine(new string(' ', Console.WindowWidth - 1));
        
        WritePaddedLine("Управление интерфейсом:", ConsoleColor.White);
        
        WriteFooterBtn("[Up/Down]", "Навигация");
        WriteFooterBtn("[<-]", "Глобальный режим");
        WriteFooterBtn("[R]", "Обновить");
        WriteFooterBtn("[Esc]", "Выход");
        FinishLine();

        // Отделение пунктирной линией блока контроля мыши по требованию пользователя
        Console.WriteLine(new string(' ', Console.WindowWidth - 1));
        WritePaddedLine("-------------------------------------------------------------------------", ConsoleColor.DarkGray);

        WritePaddedLine("Глобальный режим контроля мыши (применяется ко всем окнам):", ConsoleColor.White);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write(" [F] ");
        Console.ResetColor();

        if (isolator.IsMouseFocusBlockActive)
        {
            Console.BackgroundColor = ConsoleColor.DarkCyan;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" [ФОКУС МЫШИ: ЗАЩИЩЕН] ");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("  (Неактивные окна не реагируют на мышь)");
            Console.ResetColor();
        }
        else
        {
            Console.BackgroundColor = ConsoleColor.DarkYellow;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write(" [ФОКУС МЫШИ: ОТКЛЮЧЕН] ");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("  (Движение передается во все окна)");
            Console.ResetColor();
        }
        FinishLine();

        Console.WriteLine(new string(' ', Console.WindowWidth - 1));
        
        int currentFooterEnd = Console.CursorTop;
        _logBox.TopPosition = currentFooterEnd;
        _logBox.DrawFrame();

        int endOfBuffer = Console.CursorTop;
        if (_previousBottomTop > endOfBuffer)
        {
            for (int y = endOfBuffer; y < _previousBottomTop; y++)
            {
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));
            }
        }
        _previousBottomTop = Math.Max(endOfBuffer, _previousBottomTop);

        Console.SetCursorPosition(0, Math.Min(Console.BufferHeight - 1, _logBox.TopPosition + 2));
        Console.CursorVisible = false;
    }

    public void HandleKey(ConsoleKeyInfo key, ParsecIsolator isolator)
    {
        var processes = isolator.GetTrackedProcessesSnapshot();
        
        // 1. Сначала обрабатываем навигацию по списку
        if (TryHandleNavigation(key.Key, processes.Length))
        {
            UpdateDynamic(isolator);
            ClearFeedback();
            return;
        }

        // 2. Диспетчеризация команд взаимодействия
        ExecuteKeyCommand(key.Key, isolator, processes);
    }

    public void ShowFeedback(string message, ConsoleColor color) => _logBox.ShowFeedback(message, color);

    public void ClearFeedback() => _logBox.ClearFeedback();

    private bool TryHandleNavigation(ConsoleKey key, int processCount)
    {
        if (key == ConsoleKey.UpArrow && _selectedIndex > 0)
        {
            _selectedIndex--;
            return true;
        }
        if (key == ConsoleKey.DownArrow && _selectedIndex < processCount - 1)
        {
            _selectedIndex++;
            return true;
        }
        return false;
    }

    private void ExecuteKeyCommand(ConsoleKey key, ParsecIsolator isolator, Models.ParsecProcessInfo[] processes)
    {
        switch (key)
        {
            case ConsoleKey.Spacebar:
                ToggleSingleProcessBlock(isolator, processes);
                break;
            case ConsoleKey.P:
                PingSelectedProcessWindow(isolator, processes);
                break;
            case ConsoleKey.F:
                ToggleMouseFocus(isolator);
                break;
            case ConsoleKey.D1 or ConsoleKey.NumPad1:
                SetAllBlockState(isolator, block: true);
                break;
            case ConsoleKey.D2 or ConsoleKey.NumPad2:
                SetAllBlockState(isolator, block: false);
                break;
            case ConsoleKey.S:
                SaveProfileCommand(isolator);
                break;
            case ConsoleKey.L:
                LoadProfileCommand(isolator);
                break;
        }
    }

    private void ToggleSingleProcessBlock(ParsecIsolator isolator, Models.ParsecProcessInfo[] processes)
    {
        if (processes.Length == 0) return;
        
        var proc = processes[_selectedIndex];
        bool isBlocked = isolator.ToggleTargetedBlockState(proc.ExecutablePath);
        UpdateDynamic(isolator);
        
        if (isBlocked)
            ShowFeedback($"Окно {proc.Pid} ИЗОЛИРОВАНО.", ConsoleColor.Red);
        else
            ShowFeedback($"Окно {proc.Pid} ОБЪЕДИНЕНО с хостом.", ConsoleColor.DarkGreen);
    }

    private void PingSelectedProcessWindow(ParsecIsolator isolator, Models.ParsecProcessInfo[] processes)
    {
        if (processes.Length == 0) return;
        
        var proc = processes[_selectedIndex];
        bool success = isolator.FocusProcessWindow(proc.Pid);
        
        if (success)
            ShowFeedback($"Окно {proc.Pid} выведено на передний план.", ConsoleColor.DarkGreen);
        else
            ShowFeedback($"У процесса {proc.Pid} нет главного окна.", ConsoleColor.Red);
    }

    private void ToggleMouseFocus(ParsecIsolator isolator)
    {
        bool mouseFocusState = isolator.ToggleMouseFocusBlockState();
        UpdateDynamic(isolator);
        ShowFeedback(
            mouseFocusState ? "Контроль фокуса мыши ВКЛЮЧЕН (неактивные окна защищены)." : "Контроль фокуса мыши ВЫКЛЮЧЕН.", 
            mouseFocusState ? ConsoleColor.Cyan : ConsoleColor.Yellow
        );
    }

    private void SetAllBlockState(ParsecIsolator isolator, bool block)
    {
        isolator.SetAllTargetedStates(block);
        UpdateDynamic(isolator);
        ShowFeedback(
            block ? "Все окна изолированы." : "Все окна разблокированы (общий буфер).", 
            block ? ConsoleColor.Red : ConsoleColor.DarkGreen
        );
    }

    private void SaveProfileCommand(ParsecIsolator isolator)
    {
        if (!isolator.HasTargetedBlockedPaths) return;

        var result = ProfileManagerView.HandleSaveProfile(isolator);
        DrawFull(isolator);

        if (result.IsSaved)
            ShowFeedback($"Профиль '{result.ProfileName}' успешно сохранен.", ConsoleColor.DarkGreen);
        else if (result.ErrorMessage != null)
            ShowFeedback(result.ErrorMessage, ConsoleColor.Red);
        else
            ShowFeedback("Сохранение отменено (пустое имя).", ConsoleColor.Yellow);
    }

    private void LoadProfileCommand(ParsecIsolator isolator)
    {
        if (ProfileManager.GetAvailableProfiles().Count == 0) return;

        var result = ProfileManagerView.RunProfileManager(isolator);
        DrawFull(isolator);

        if (result.IsLoaded)
            ShowFeedback($"Профиль '{result.ProfileName}' загружен.", ConsoleColor.DarkGreen);
        else if (result.ErrorMessage != null)
            ShowFeedback(result.ErrorMessage, ConsoleColor.Red);
    }

    private static void WritePaddedLine(string text, ConsoleColor color = ConsoleColor.DarkGray)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text.PadRight(Console.WindowWidth - 1));
        Console.ResetColor();
    }

    private static void FinishLine()
    {
        int currentLeft = Console.CursorLeft;
        int remaining = Console.WindowWidth - 1 - currentLeft;
        if (remaining > 0)
        {
            Console.Write(new string(' ', remaining));
        }
        Console.WriteLine();
    }

    private static void WriteFooterBtn(string hotkey, string desc, ConsoleColor hotkeyColor = ConsoleColor.Yellow)
    {
        Console.ForegroundColor = hotkeyColor;
        Console.Write($" {hotkey} ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{desc}  ");
        Console.ResetColor();
    }
}
