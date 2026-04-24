using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaskManagementWidget.Services
{
    /// <summary>Persistent app-level settings: sync state, OAuth refresh token (DPAPI-encrypted),
    /// and bookkeeping such as the remote Drive file id.</summary>
    public class AppSettings
    {
        public bool      SyncEnabled              { get; set; }
        public bool      FirstRunCompleted        { get; set; }
        public DateTime? LastSyncUtc              { get; set; }
        public string?   RemoteFileId             { get; set; }
        public string?   RemoteFolderId           { get; set; }
        public string?   UserEmail                { get; set; }
        /// <summary>Base64 of DPAPI-protected refresh token bytes (CurrentUser scope).</summary>
        public string?   ProtectedRefreshToken    { get; set; }
    }

    public static class SettingsService
    {
        private static readonly string FilePath = BuildFilePath();
        private static readonly object  _lock   = new();
        private static AppSettings?     _cached;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static AppSettings Current
        {
            get
            {
                lock (_lock)
                {
                    return _cached ??= Load();
                }
            }
        }

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void Save()
        {
            lock (_lock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(FilePath)!;
                    Directory.CreateDirectory(dir);
                    var json = JsonSerializer.Serialize(Current, JsonOptions);
                    File.WriteAllText(FilePath, json);
                }
                catch
                {
                    // Settings persistence is best-effort.
                }
            }
        }

        // ── DPAPI helpers (Windows-only) ─────────────────────────────────────────
        public static string? Protect(string? plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            try
            {
                var bytes     = Encoding.UTF8.GetBytes(plain);
                var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(protectedBytes);
            }
            catch
            {
                return null;
            }
        }

        public static string? Unprotect(string? protectedB64)
        {
            if (string.IsNullOrEmpty(protectedB64)) return null;
            try
            {
                var bytes  = Convert.FromBase64String(protectedB64);
                var plain  = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
                return Path.Combine(appData, "TaskManagementWidget", "settings.json");
            return Path.Combine(AppContext.BaseDirectory, "settings.json");
        }
    }
}
