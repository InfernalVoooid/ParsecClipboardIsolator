using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ParsecClipboardIsolator
{
    // Управление сохраненными профилями изолируемых путей (хранятся в каталоге Profiles)
    internal static class ProfileManager
    {
        private static readonly string ProfilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles");

        public static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(ProfilesDir))
            {
                Directory.CreateDirectory(ProfilesDir);
            }
        }

        public static void SaveProfile(string profileName, IEnumerable<string> paths)
        {
            EnsureDirectoryExists();
            
            // Очистка имени файла от недопустимых символов файловой системы
            var safeName = string.Concat(profileName.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Default";

            string filePath = Path.Combine(ProfilesDir, $"{safeName}.txt");
            File.WriteAllLines(filePath, paths.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        public static List<string> LoadProfile(string profileName)
        {
            string filePath = Path.Combine(ProfilesDir, $"{profileName}.txt");
            if (File.Exists(filePath))
            {
                return File.ReadAllLines(filePath)
                           .Where(p => !string.IsNullOrWhiteSpace(p))
                           .Select(p => p.Trim())
                           .ToList();
            }
            return [];
        }

        public static List<string> GetAvailableProfiles()
        {
            EnsureDirectoryExists();
            var files = Directory.GetFiles(ProfilesDir, "*.txt");
            return files.Select(Path.GetFileNameWithoutExtension)
                        .OfType<string>()
                        .ToList();
        }

        public static string? GetDefaultProfile()
        {
            EnsureDirectoryExists();
            string defaultFilePath = Path.Combine(ProfilesDir, ".default");
            if (File.Exists(defaultFilePath))
            {
                string? def = File.ReadAllText(defaultFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(def) && File.Exists(Path.Combine(ProfilesDir, $"{def}.txt")))
                {
                    return def;
                }
            }
            return null;
        }

        public static void SetDefaultProfile(string? profileName)
        {
            EnsureDirectoryExists();
            string defaultFilePath = Path.Combine(ProfilesDir, ".default");
            if (string.IsNullOrWhiteSpace(profileName))
            {
                if (File.Exists(defaultFilePath)) File.Delete(defaultFilePath);
            }
            else
            {
                File.WriteAllText(defaultFilePath, profileName);
            }
        }

        public static void DeleteProfile(string profileName)
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
        }
    }
}
