using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TaskManagementWidget.Models;

namespace TaskManagementWidget.Services
{
    public static class TaskStorageService
    {
        private static readonly string FilePath = GetRealRoamingFilePath();
        private static readonly string LegacyFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskManagementWidget",
            "tasks.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters    = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public static List<TaskItem> Load()
        {
            try
            {
                MigrateLegacyDataIfNeeded();
                if (!File.Exists(FilePath)) return new List<TaskItem>();
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOptions)
                       ?? new List<TaskItem>();
            }
            catch
            {
                return new List<TaskItem>();
            }
        }

        public static void Save(IEnumerable<TaskItem> tasks)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(tasks, JsonOptions);
                File.WriteAllText(FilePath, json);
            }
            catch { /* swallow — non-critical */ }
        }

        private static string GetRealRoamingFilePath()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TaskManagementWidget",
                    "tasks.json");
            }

            return Path.Combine(userProfile, "AppData", "Roaming", "TaskManagementWidget", "tasks.json");
        }

        private static void MigrateLegacyDataIfNeeded()
        {
            try
            {
                if (File.Exists(FilePath)) return;
                if (string.Equals(FilePath, LegacyFilePath, StringComparison.OrdinalIgnoreCase)) return;
                if (!File.Exists(LegacyFilePath)) return;

                var dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);
                File.Copy(LegacyFilePath, FilePath, overwrite: false);
            }
            catch
            {
                // Ignore migration failures and continue with normal startup.
            }
        }
    }
}
