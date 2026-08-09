using System;
using ParsecClipboardIsolator.Services;

namespace ParsecClipboardIsolator.UI;

internal readonly record struct ProfileSaveResult(bool IsSaved, string? ProfileName, string? ErrorMessage = null);
internal readonly record struct ProfileLoadResult(bool IsLoaded, string? ProfileName, string? ErrorMessage = null);

// Модальное консольное представление для сохранения, загрузки и удаления профилей
internal static class ProfileManagerView
{
    // Диалог сохранения текущего набора заблокированных окон в файл профиля
    public static ProfileSaveResult HandleSaveProfile(ParsecIsolator isolator)
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
            var (success, errorMsg) = ProfileManager.SaveProfile(nameToSave, isolator.GetTargetedBlockedPathsSnapshot());
            if (success)
            {
                return new ProfileSaveResult(true, nameToSave);
            }
            return new ProfileSaveResult(false, nameToSave, errorMsg);
        }
        
        return new ProfileSaveResult(false, null);
    }

    public static ProfileLoadResult RunProfileManager(ParsecIsolator isolator)
    {
        int selectedProfile = 0;
        string? statusMessage = null;
        ConsoleColor statusColor = ConsoleColor.Yellow;

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
                return new ProfileLoadResult(false, null, statusMessage);
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

            if (!string.IsNullOrEmpty(statusMessage))
            {
                Console.ForegroundColor = statusColor;
                Console.WriteLine($" [!] {statusMessage}".PadRight(Console.WindowWidth - 1));
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));
            }
            
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  [Up/Down] "); Console.ResetColor(); Console.WriteLine("- Выбор профиля");
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  [Enter]   "); Console.ResetColor(); Console.WriteLine("- Загрузить профиль");
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  [Space]   "); Console.ResetColor(); Console.WriteLine("- Назначить / Снять по умолчанию");
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  [Del]     "); Console.ResetColor(); Console.WriteLine("- Удалить профиль");
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  [Esc]     "); Console.ResetColor(); Console.WriteLine("- Назад");
            
            var key = Console.ReadKey(true);
            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return new ProfileLoadResult(false, null, statusMessage);
                case ConsoleKey.UpArrow:
                    if (selectedProfile > 0) selectedProfile--;
                    break;
                case ConsoleKey.DownArrow:
                    if (selectedProfile < profiles.Count - 1) selectedProfile++;
                    break;
                case ConsoleKey.Spacebar:
                    bool isDef = profiles[selectedProfile] == defaultProfile;
                    var setRes = isDef 
                        ? ProfileManager.SetDefaultProfile(null) 
                        : ProfileManager.SetDefaultProfile(profiles[selectedProfile]);
                    
                    if (!setRes.Success)
                    {
                        statusMessage = setRes.ErrorMessage;
                        statusColor = ConsoleColor.Red;
                    }
                    else
                    {
                        statusMessage = null;
                    }
                    break;
                case ConsoleKey.Delete:
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.Write($"Удалить профиль '{profiles[selectedProfile]}'? (Y/N): ");
                    var confirm = Console.ReadKey(true);
                    if (confirm.Key == ConsoleKey.Y)
                    {
                        var delRes = ProfileManager.DeleteProfile(profiles[selectedProfile]);
                        if (!delRes.Success)
                        {
                            statusMessage = delRes.ErrorMessage;
                            statusColor = ConsoleColor.Red;
                        }
                        else
                        {
                            statusMessage = $"Профиль '{profiles[selectedProfile]}' удален.";
                            statusColor = ConsoleColor.DarkGreen;
                        }
                    }
                    break;
                case ConsoleKey.Enter:
                    string selectedName = profiles[selectedProfile];
                    var loadedPaths = ProfileManager.LoadProfile(selectedName);
                    isolator.LoadTargetedBlockedPaths(loadedPaths);
                    return new ProfileLoadResult(true, selectedName);
            }
        }
    }
}
