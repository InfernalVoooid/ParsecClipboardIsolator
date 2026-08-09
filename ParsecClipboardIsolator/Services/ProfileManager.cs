using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ParsecClipboardIsolator.Services;

// Управление сохраненными профилями изолируемых путей (хранятся в каталоге Profiles)
internal static class ProfileManager
{
    private static readonly string ProfilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles");

    public static bool EnsureDirectoryExists(out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            if (!Directory.Exists(ProfilesDir))
            {
                Directory.CreateDirectory(ProfilesDir);
            }
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Ошибка создания каталога профилей: {ex.Message}";
            return false;
        }
    }

    public static (bool Success, string? ErrorMessage) SaveProfile(string profileName, IEnumerable<string> paths)
    {
        if (!EnsureDirectoryExists(out string? dirErr))
            return (false, dirErr);

        try
        {
            // Очистка имени файла от недопустимых символов файловой системы
            var safeName = string.Concat(profileName.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
            if (string.IsNullOrWhiteSpace(safeName)) 
            {
                return (false, "Имя профиля содержит только недопустимые символы (используйте буквы, цифры, '-' или '_').");
            }

            string filePath = Path.Combine(ProfilesDir, $"{safeName}.txt");
            File.WriteAllLines(filePath, paths.Where(p => !string.IsNullOrWhiteSpace(p)));
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка сохранения профиля: {ex.Message}");
        }
    }

    public static List<string> LoadProfile(string profileName)
    {
        try
        {
            string filePath = Path.Combine(ProfilesDir, $"{profileName}.txt");
            if (File.Exists(filePath))
            {
                return File.ReadAllLines(filePath)
                           .Where(p => !string.IsNullOrWhiteSpace(p))
                           .Select(p => p.Trim())
                           .ToList();
            }
        }
        catch
        {
            // Возвращаем пустой список при ошибках чтения
        }
        return [];
    }

    public static List<string> GetAvailableProfiles()
    {
        if (!EnsureDirectoryExists(out _)) return [];

        try
        {
            var files = Directory.GetFiles(ProfilesDir, "*.txt");
            return files.Select(Path.GetFileNameWithoutExtension)
                        .OfType<string>()
                        .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static string? GetDefaultProfile()
    {
        if (!EnsureDirectoryExists(out _)) return null;

        try
        {
            string defaultFilePath = Path.Combine(ProfilesDir, ".default");
            if (File.Exists(defaultFilePath))
            {
                string? def = File.ReadAllText(defaultFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(def) && File.Exists(Path.Combine(ProfilesDir, $"{def}.txt")))
                {
                    return def;
                }
            }
        }
        catch
        {
            // Игнорируем ошибки чтения файла по умолчанию
        }
        return null;
    }

    public static (bool Success, string? ErrorMessage) SetDefaultProfile(string? profileName)
    {
        if (!EnsureDirectoryExists(out string? dirErr))
            return (false, dirErr);

        try
        {
            string defaultFilePath = Path.Combine(ProfilesDir, ".default");
            if (string.IsNullOrWhiteSpace(profileName))
            {
                if (File.Exists(defaultFilePath)) File.Delete(defaultFilePath);
            }
            else
            {
                File.WriteAllText(defaultFilePath, profileName);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка назначения дефолтного профиля: {ex.Message}");
        }
    }

    public static (bool Success, string? ErrorMessage) DeleteProfile(string profileName)
    {
        try
        {
            string filePath = Path.Combine(ProfilesDir, $"{profileName}.txt");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            if (string.Equals(GetDefaultProfile(), profileName, StringComparison.OrdinalIgnoreCase))
            {
                SetDefaultProfile(null);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка удаления профиля: {ex.Message}");
        }
    }
}
