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

    private sealed record ProcessGroup(string ExecutablePath, int PrimaryPid, int[] AllPids, IntPtr MainWindowHandle);

    private static ProcessGroup[] GetGroups(Models.ParsecProcessInfo[] processes)
    {
        if (processes.Length == 0) return [];

        return processes
            .GroupBy(p => p.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var guiProc = g.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero) ?? g.First();
                return new ProcessGroup(
                    g.Key,
                    guiProc.Pid,
                    g.Select(p => p.Pid).ToArray(),
                    guiProc.MainWindowHandle
                );
            })
            .ToArray();
    }

    public void UpdateDynamic(ParsecIsolator isolator)
    {
        Console.SetCursorPosition(0, _headerBottomTop);

        var groups = GetGroups(isolator.GetTrackedProcessesSnapshot());
        
        if (groups.Length == 0)
        {
            _selectedIndex = 0;
            _scrollOffset = 0;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Нет запущенных окон Parsec.".PadRight(Console.WindowWidth - 1));
            Console.ResetColor();
        }
        else
        {
            if (_selectedIndex >= groups.Length)
            {
                _selectedIndex = groups.Length - 1;
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

            int maxOffset = Math.Max(0, groups.Length - MaxVisibleItems);
            if (_scrollOffset > maxOffset)
            {
                _scrollOffset = maxOffset;
            }

            bool showScrollIndicators = groups.Length > MaxVisibleItems;

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

            int endIndex = Math.Min(groups.Length, _scrollOffset + MaxVisibleItems);
            for (int i = _scrollOffset; i < endIndex; i++)
            {
                var group = groups[i];
                bool isBlocked = isolator.IsPathBlocked(group.ExecutablePath);
                
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

                string path = group.ExecutablePath;
                
                // Извлекаем имя родительской папки (идентификатор инстанса) и имя файла для форматированного вывода
                string? dirName = Path.GetDirectoryName(path);
                string folderName = !string.IsNullOrEmpty(dirName) ? (Path.GetFileName(dirName) ?? "Unknown") : "Unknown";
                if (string.IsNullOrWhiteSpace(folderName)) folderName = "Unknown";
                
                string fileName = Path.GetFileName(path);
                string tail = string.IsNullOrEmpty(fileName) ? @"\parsecd.exe" : @"\" + fileName;
                string pidStr = group.AllPids.Length > 1 
                    ? $"(PIDs: {string.Join(", ", group.AllPids)})" 
                    : $"(PID: {group.PrimaryPid})";

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
                int itemsBelow = groups.Length - (_scrollOffset + MaxVisibleItems);
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

        // Контекстные кнопки управления профилями (находятся прямо под списком окон, как и раньше)
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
        
        // Симметричный верхний отступ в 1 строку для баланса с нижним отступом
        Console.WriteLine(new string(' ', Console.WindowWidth - 1));

        // Категории горячих клавиш в 2 параллельных столбца
        var leftItems = new (string Key, string Desc, ConsoleColor Color)[]
        {
            ("[Space]", "Изолировать окно", ConsoleColor.Yellow),
            ("[P]", "Прозвон", ConsoleColor.Yellow),
            ("[1]/[2]", "Все/Ничего", ConsoleColor.Yellow)
        };

        var rightItems = new (string Key, string Desc, ConsoleColor Color)[]
        {
            ("[Up/Down]", "Навигация", ConsoleColor.Yellow),
            ("[<-]", "Глобальный режим", ConsoleColor.Yellow),
            ("[R]", "Обновить", ConsoleColor.Yellow),
            ("[Esc]", "Выход", ConsoleColor.Yellow)
        };

        int colWidth = 42;

        // Отрисовка заголовков левого и правого столбца в одной строке
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("Управление списком буферов:".PadRight(colWidth));
        int remHeaderPad = Math.Max(0, Console.WindowWidth - 1 - colWidth);
        Console.Write("Управление интерфейсом:".PadRight(remHeaderPad));
        Console.WriteLine();
        Console.ResetColor();

        // Построчная параллельная отрисовка двух столбцов
        int maxRows = Math.Max(leftItems.Length, rightItems.Length);
        for (int row = 0; row < maxRows; row++)
        {
            // Элемент левого столбца (Управление списком буферов)
            if (row < leftItems.Length)
            {
                var item = leftItems[row];
                Console.ForegroundColor = item.Color;
                Console.Write($" {item.Key} ".PadRight(10));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(item.Desc.PadRight(colWidth - 10));
                Console.ResetColor();
            }
            else
            {
                Console.Write(new string(' ', colWidth));
            }

            // Элемент правого столбца (Управление интерфейсом)
            if (row < rightItems.Length)
            {
                var item = rightItems[row];
                Console.ForegroundColor = item.Color;
                Console.Write($" {item.Key} ".PadRight(12));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(item.Desc);
                Console.ResetColor();
            }

            FinishLine();
        }

        // Симметричный нижний отступ в 1 строку перед пунктирной линией блока мыши
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
        var groups = GetGroups(isolator.GetTrackedProcessesSnapshot());
        
        // 1. Сначала обрабатываем навигацию по списку
        if (TryHandleNavigation(key.Key, groups.Length))
        {
            UpdateDynamic(isolator);
            ClearFeedback();
            return;
        }

        // 2. Диспетчеризация команд взаимодействия
        ExecuteKeyCommand(key.Key, isolator, groups);
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

    private void ExecuteKeyCommand(ConsoleKey key, ParsecIsolator isolator, ProcessGroup[] groups)
    {
        switch (key)
        {
            case ConsoleKey.Spacebar:
                ToggleSingleProcessBlock(isolator, groups);
                break;
            case ConsoleKey.P:
                PingSelectedProcessWindow(isolator, groups);
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

    private void ToggleSingleProcessBlock(ParsecIsolator isolator, ProcessGroup[] groups)
    {
        if (groups.Length == 0) return;
        
        var group = groups[_selectedIndex];
        bool isBlocked = isolator.ToggleTargetedBlockState(group.ExecutablePath);
        UpdateDynamic(isolator);
        
        if (isBlocked)
            ShowFeedback($"Инстанс {group.PrimaryPid} ИЗОЛИРОВАН.", ConsoleColor.Red);
        else
            ShowFeedback($"Инстанс {group.PrimaryPid} ОБЪЕДИНЕН с хостом.", ConsoleColor.DarkGreen);
    }

    private void PingSelectedProcessWindow(ParsecIsolator isolator, ProcessGroup[] groups)
    {
        if (groups.Length == 0) return;
        
        var group = groups[_selectedIndex];
        bool success = isolator.FocusProcessWindow(group.PrimaryPid);
        
        if (success)
            ShowFeedback($"Инстанс {group.PrimaryPid} выведен на передний план.", ConsoleColor.DarkGreen);
        else
            ShowFeedback($"У инстанса {group.PrimaryPid} нет главного окна.", ConsoleColor.Red);
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
