using System;
using System.IO;
using System.Linq;

namespace ParsecClipboardIsolator
{
    // Отрисовка таргетного режима изоляции (выбор индивидуальных процессов Parsec и профилей)
    internal sealed class TargetedView : IView
    {
        private int _selectedIndex;
        private int _headerBottomTop;
        private int _feedbackTop;

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
            
            UpdateDynamic(isolator);
        }

        public void UpdateDynamic(ParsecIsolator isolator)
        {
            Console.SetCursorPosition(0, _headerBottomTop);

            var processes = isolator.GetTrackedProcessesSnapshot();
            
            if (_selectedIndex >= processes.Length && processes.Length > 0)
            {
                _selectedIndex = processes.Length - 1;
            }
            else if (processes.Length == 0)
            {
                _selectedIndex = 0;
            }
            
            if (processes.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  Нет запущенных окон Parsec.".PadRight(Console.WindowWidth - 1));
                Console.ResetColor();
            }
            else
            {
                for (int i = 0; i < processes.Length; i++)
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
                    string folderName = Path.GetFileName(Path.GetDirectoryName(path)) ?? "Unknown";
                    string fileName = Path.GetFileName(path);
                    string tail = string.IsNullOrEmpty(fileName) ? @"\parsecd.exe" : @"\" + fileName;
                    string pidStr = $"(PID: {proc.Pid})";

                    Console.ForegroundColor = (i == _selectedIndex) ? ConsoleColor.Black : ConsoleColor.Cyan;
                    Console.Write(folderName.PadRight(40)); // Фиксированная ширина колонки папки

                    Console.ForegroundColor = (i == _selectedIndex) ? ConsoleColor.Black : ConsoleColor.DarkGray;
                    Console.Write(tail.PadRight(15));
                    
                    Console.ForegroundColor = (i == _selectedIndex) ? ConsoleColor.Black : ConsoleColor.DarkGray;
                    int remainingPad = Math.Max(0, Console.WindowWidth - 62);
                    Console.Write(pidStr.PadRight(remainingPad)); // Заполняем остаток ширины строки
                    
                    Console.ResetColor();
                    Console.WriteLine();
                }
            }
            
            Console.WriteLine(new string(' ', Console.WindowWidth - 1));

            // Контекстные кнопки управления профилями (отображаются над разделительной линией)
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
                
                int currentLeft = Console.CursorLeft;
                if (currentLeft < Console.WindowWidth - 1)
                {
                    Console.Write(new string(' ', Console.WindowWidth - 1 - currentLeft));
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("-------------------------------------------------------------------------");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Управление списком:");
            Console.ResetColor();
            
            WriteFooterBtn("[Space]", "Изолировать окно");
            WriteFooterBtn("[P]", "Прозвон");
            WriteFooterBtn("[1]/[2]", "Все/Ничего");
            Console.WriteLine();
            
            Console.WriteLine();
            
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Управление интерфейсом:");
            Console.ResetColor();
            
            WriteFooterBtn("[Up/Down]", "Навигация");
            WriteFooterBtn("[<-]", "Глобальный режим");
            WriteFooterBtn("[R]", "Обновить");
            WriteFooterBtn("[Esc]", "Выход");
            Console.WriteLine();

            // Очищаем старый след строк в консоли на случай сокращения размера списка
            for (int i = 0; i < 2; i++)
            {
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));
            }

            _feedbackTop = Console.CursorTop - 2;
        }

        private static void WriteFooterBtn(string hotkey, string desc, ConsoleColor hotkeyColor = ConsoleColor.Yellow)
        {
            Console.ForegroundColor = hotkeyColor;
            Console.Write($" {hotkey} ");
            Console.ResetColor();
            Console.Write($"{desc}  ");
        }

        public void HandleKey(ConsoleKeyInfo key, ParsecIsolator isolator)
        {
            var processes = isolator.GetTrackedProcessesSnapshot();
            bool hasSelection = isolator.HasTargetedBlockedPaths;
            bool hasProfiles = ProfileManager.GetAvailableProfiles().Count > 0;

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (_selectedIndex > 0)
                    {
                        _selectedIndex--;
                        UpdateDynamic(isolator);
                        ClearFeedback();
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (_selectedIndex < processes.Length - 1)
                    {
                        _selectedIndex++;
                        UpdateDynamic(isolator);
                        ClearFeedback();
                    }
                    break;
                case ConsoleKey.Spacebar:
                    if (processes.Length > 0)
                    {
                        var proc = processes[_selectedIndex];
                        bool newBlockedState = isolator.ToggleTargetedBlockState(proc.ExecutablePath);
                        
                        UpdateDynamic(isolator);
                        
                        if (newBlockedState)
                            ShowFeedback($"Окно {proc.Pid} ИЗОЛИРОВАНО.", ConsoleColor.Red);
                        else
                            ShowFeedback($"Окно {proc.Pid} ОБЪЕДИНЕНО с хостом.", ConsoleColor.DarkGreen);
                    }
                    break;
                case ConsoleKey.P:
                    if (processes.Length > 0)
                    {
                        var proc = processes[_selectedIndex];
                        if (proc.MainWindowHandle != IntPtr.Zero)
                        {
                            Native.ShowWindow(proc.MainWindowHandle, Native.SW_RESTORE);
                            Native.SetForegroundWindow(proc.MainWindowHandle);
                            ShowFeedback($"Окно {proc.Pid} выведено на передний план.", ConsoleColor.DarkGreen);
                        }
                        else
                        {
                            ShowFeedback($"У процесса {proc.Pid} нет главного окна.", ConsoleColor.Red);
                        }
                    }
                    break;
                case ConsoleKey.D1:
                case ConsoleKey.NumPad1:
                    isolator.SetAllTargetedStates(true);
                    UpdateDynamic(isolator);
                    ShowFeedback("Все окна изолированы.", ConsoleColor.Red);
                    break;
                case ConsoleKey.D2:
                case ConsoleKey.NumPad2:
                    isolator.SetAllTargetedStates(false);
                    UpdateDynamic(isolator);
                    ShowFeedback("Все окна разблокированы (общий буфер).", ConsoleColor.DarkGreen);
                    break;
                case ConsoleKey.S:
                    if (hasSelection) 
                        ProfileManagerView.HandleSaveProfile(isolator, () => DrawFull(isolator), ShowFeedback);
                    break;
                case ConsoleKey.L:
                    if (hasProfiles) 
                        ProfileManagerView.RunProfileManager(isolator, () => DrawFull(isolator), ShowFeedback);
                    break;
            }
        }

        public void ShowFeedback(string message, ConsoleColor color)
        {
            Console.SetCursorPosition(0, _feedbackTop);
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.SetCursorPosition(0, _feedbackTop);
            
            Console.ForegroundColor = color;
            Console.Write(message);
            Console.ResetColor();
        }

        public void ClearFeedback()
        {
            Console.SetCursorPosition(0, _feedbackTop);
            Console.Write(new string(' ', Console.WindowWidth - 1));
        }
    }
}
