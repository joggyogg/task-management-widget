using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using TaskManagementWidget.Models;

namespace TaskManagementWidget.Services
{
    /// <summary>Reads/writes the cloud copy of tasks.json under a `TASKly` folder in the user's
    /// Drive, using the drive.file scope (only sees files the app created).</summary>
    public sealed class DriveSyncService
    {
        private const string FolderName = "TASKly";
        private const string FileName   = "tasks.json";
        private const string FolderMime = "application/vnd.google-apps.folder";
        private const string FileMime   = "application/json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters    = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        private readonly DriveService _drive;

        public DriveSyncService(UserCredential credential)
        {
            _drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName       = "TASKly"
            });
        }

        /// <summary>Ensure the TASKly folder + tasks.json exist; cache their ids in settings.</summary>
        public async Task EnsureRemoteAsync(CancellationToken ct = default)
        {
            var s = SettingsService.Current;

            if (string.IsNullOrEmpty(s.RemoteFolderId))
                s.RemoteFolderId = await FindOrCreateFolderAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(s.RemoteFileId))
                s.RemoteFileId = await FindOrCreateFileAsync(s.RemoteFolderId!, ct).ConfigureAwait(false);

            SettingsService.Save();
        }

        /// <summary>Download remote tasks.json, deserialize, return contents (empty list if file new).</summary>
        public async Task<List<TaskItem>> DownloadAsync(CancellationToken ct = default)
        {
            await EnsureRemoteAsync(ct).ConfigureAwait(false);
            var fileId = SettingsService.Current.RemoteFileId!;

            using var ms = new MemoryStream();
            try
            {
                var req = _drive.Files.Get(fileId);
                await req.DownloadAsync(ms, ct).ConfigureAwait(false);
            }
            catch
            {
                // File could have been removed remotely; reset and retry once.
                SettingsService.Current.RemoteFileId = null;
                SettingsService.Save();
                await EnsureRemoteAsync(ct).ConfigureAwait(false);
                fileId = SettingsService.Current.RemoteFileId!;
                ms.SetLength(0);
                await _drive.Files.Get(fileId).DownloadAsync(ms, ct).ConfigureAwait(false);
            }

            if (ms.Length == 0) return new List<TaskItem>();

            try
            {
                ms.Position = 0;
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json)) return new List<TaskItem>();
                return JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOptions) ?? new List<TaskItem>();
            }
            catch
            {
                return new List<TaskItem>();
            }
        }

        /// <summary>Overwrite remote tasks.json with the given list.</summary>
        public async Task UploadAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default)
        {
            await EnsureRemoteAsync(ct).ConfigureAwait(false);
            var fileId = SettingsService.Current.RemoteFileId!;

            var json  = JsonSerializer.Serialize(tasks, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            using var ms = new MemoryStream(bytes);

            var meta = new Google.Apis.Drive.v3.Data.File { Name = FileName };
            var req  = _drive.Files.Update(meta, fileId, ms, FileMime);
            await req.UploadAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Best-effort fetch of the signed-in user's email; cached in settings.</summary>
        public async Task<string?> GetUserEmailAsync(CancellationToken ct = default)
        {
            try
            {
                var about = await _drive.About.Get().Tap(g => g.Fields = "user(emailAddress)")
                                  .ExecuteAsync(ct).ConfigureAwait(false);
                return about?.User?.EmailAddress;
            }
            catch
            {
                return null;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private async Task<string> FindOrCreateFolderAsync(CancellationToken ct)
        {
            // drive.file scope only returns files the app created, so a stale folder created in
            // a previous install won't be visible — that's fine, we'll just make a new one.
            var listReq = _drive.Files.List();
            listReq.Q       = $"mimeType='{FolderMime}' and name='{FolderName}' and trashed=false";
            listReq.Fields  = "files(id,name)";
            listReq.Spaces  = "drive";
            var list        = await listReq.ExecuteAsync(ct).ConfigureAwait(false);

            var existing = list.Files?.FirstOrDefault();
            if (existing != null) return existing.Id;

            var meta = new Google.Apis.Drive.v3.Data.File
            {
                Name     = FolderName,
                MimeType = FolderMime,
            };
            var created = await _drive.Files.Create(meta).Tap(c => c.Fields = "id").ExecuteAsync(ct).ConfigureAwait(false);
            return created.Id;
        }

        private async Task<string> FindOrCreateFileAsync(string folderId, CancellationToken ct)
        {
            var listReq = _drive.Files.List();
            listReq.Q      = $"name='{FileName}' and '{folderId}' in parents and trashed=false";
            listReq.Fields = "files(id,name)";
            listReq.Spaces = "drive";
            var list       = await listReq.ExecuteAsync(ct).ConfigureAwait(false);
            var existing   = list.Files?.FirstOrDefault();
            if (existing != null) return existing.Id;

            var meta = new Google.Apis.Drive.v3.Data.File
            {
                Name    = FileName,
                Parents = new List<string> { folderId },
            };

            using var ms = new MemoryStream(Encoding.UTF8.GetBytes("[]"));
            var createReq = _drive.Files.Create(meta, ms, FileMime);
            createReq.Fields = "id";
            await createReq.UploadAsync(ct).ConfigureAwait(false);
            return createReq.ResponseBody.Id;
        }
    }

    internal static class FluentExtensions
    {
        // Tiny helper for fluent property-tweaks on builder-style request objects.
        public static T Tap<T>(this T value, Action<T> tweak) { tweak(value); return value; }
    }
}
