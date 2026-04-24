using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TaskManagementWidget.Models;

namespace TaskManagementWidget.Services
{
    public static class TaskStorageService
    {
        private static readonly string FilePath = BuildFilePath();
        public static string? LastSaveError { get; private set; }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters    = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public static List<TaskItem> Load()
        {
            try
            {
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
                var json = JsonSerializer.Serialize(tasks.Where(t => !t.IsPreview), JsonOptions);
                File.WriteAllText(FilePath, json);
                LastSaveError = null;
            }
            catch (Exception ex)
            {
                LastSaveError = $"{ex.GetType().Name}: {ex.Message} (path: {FilePath})";
            }
        }

        private static string BuildFilePath()
        {
            // SpecialFolder.ApplicationData resolves correctly on all Windows configurations,
            // including domain-joined machines with redirected folder policies.
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (!string.IsNullOrWhiteSpace(appData))
                return Path.Combine(appData, "TaskManagementWidget", "tasks.json");

            // Fallback: store next to the executable if AppData is unavailable.
            return Path.Combine(AppContext.BaseDirectory, "tasks.json");
        }
    }
}
