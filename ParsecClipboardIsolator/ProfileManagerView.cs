using System;

namespace ParsecClipboardIsolator
{
    // Модальное консольное представление для сохранения, загрузки и удаления профилей
    internal static class ProfileManagerView
    {
        // Диалог сохранения текущего набора заблокированных окон в файл профиля
        public static void HandleSaveProfile(ParsecIsolator isolator, Action drawTargetedFull, Action<string, ConsoleColor> showFeedback)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine("=== Сохранение профиля ===");
            Console.ResetColor();
            Console.Write("Введите имя профиля (на английском): ");
            
            Console.CursorVisible = true;
            string? nameToSave = Console.ReadLine();
            Console.CursorVisible = false;
            
            if (!string.IsNullOrWhiteSpace(nameToSave))
            {
                ProfileManager.SaveProfile(nameToSave, isolator.GetTargetedBlockedPathsSnapshot());
                drawTargetedFull();
                showFeedback($"Профиль '{nameToSave}' успешно сохранен.", ConsoleColor.DarkGreen);
            }
            else
            {
                drawTargetedFull();
                showFeedback("Сохранение отменено (пустое имя).", ConsoleColor.Yellow);
            }
        }

        public static void RunProfileManager(ParsecIsolator isolator, Action drawTargetedFull, Action<string, ConsoleColor> showFeedback)
        {
            int selectedProfile = 0;
            
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=========================================================================");
            Console.WriteLine("     Управление Профилями                                                ");
            Console.WriteLine("=========================================================================");
            Console.ResetColor();
            Console.WriteLine();
            
            int listStartTop = Console.CursorTop;
            
            while (true)
            {
                var profiles = ProfileManager.GetAvailableProfiles();
                if (profiles.Count == 0)
                {
                    drawTargetedFull();
                    return;
                }

                if (selectedProfile >= profiles.Count) selectedProfile = profiles.Count - 1;

                Console.SetCursorPosition(0, listStartTop);

                string? defaultProfile = ProfileManager.GetDefaultProfile();

                for (int i = 0; i < profiles.Count; i++)
                {
                    if (i == selectedProfile)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkGray;
                        Console.ForegroundColor = ConsoleColor.Black;
                    }
                    else
                    {
                        Console.ResetColor();
                    }

                    string defMark = (profiles[i] == defaultProfile) ? "[* DEFAULT] " : "            ";
                    Console.WriteLine($" {defMark}{profiles[i]} ".PadRight(Console.WindowWidth - 1));
                    Console.ResetColor();
                }

                // Затираем старые строки списка (если удалили профиль)
                for (int i = 0; i < 2; i++)
                {
                    Console.WriteLine(new string(' ', Console.WindowWidth - 1));
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("-------------------------------------------------------------------------");
                Console.ResetColor();
                
                Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  [Up/Down] "); Console.ResetColor(); Console.WriteLine("- Выбор профиля");
                Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  [Enter]   "); Console.ResetColor(); Console.WriteLine("- Загрузить профиль");
                Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  [Space]   "); Console.ResetColor(); Console.WriteLine("- Назначить / Снять по умолчанию");
                Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  [Del]     "); Console.ResetColor(); Console.WriteLine("- Удалить профиль");
                Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  [Esc]     "); Console.ResetColor(); Console.WriteLine("- Назад");
                
                var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                        drawTargetedFull();
                        return;
                    case ConsoleKey.UpArrow:
                        if (selectedProfile > 0) selectedProfile--;
                        break;
                    case ConsoleKey.DownArrow:
                        if (selectedProfile < profiles.Count - 1) selectedProfile++;
                        break;
                    case ConsoleKey.Spacebar:
                        if (profiles[selectedProfile] == defaultProfile)
                            ProfileManager.SetDefaultProfile(null);
                        else
                            ProfileManager.SetDefaultProfile(profiles[selectedProfile]);
                        break;
                    case ConsoleKey.Delete:
                        Console.SetCursorPosition(0, Console.CursorTop);
                        Console.Write($"Удалить профиль '{profiles[selectedProfile]}'? (Y/N): ");
                        var confirm = Console.ReadKey(true);
                        if (confirm.Key == ConsoleKey.Y)
                        {
                            ProfileManager.DeleteProfile(profiles[selectedProfile]);
                        }
                        break;
                    case ConsoleKey.Enter:
                        var loadedPaths = ProfileManager.LoadProfile(profiles[selectedProfile]);
                        isolator.LoadTargetedBlockedPaths(loadedPaths);
                        drawTargetedFull();
                        showFeedback($"Профиль '{profiles[selectedProfile]}' загружен.", ConsoleColor.DarkGreen);
                        return;
                }
            }
        }
    }
}
