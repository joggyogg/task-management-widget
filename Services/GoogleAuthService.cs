using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;

namespace TaskManagementWidget.Services
{
    /// <summary>Google OAuth flow for desktop installed-app. Uses the `drive.file` scope so the
    /// app only sees files it created — no Google verification required, safe for public release.
    ///
    /// Credentials live in the partial-class file <c>GoogleAuthService.Credentials.cs</c>,
    /// which is gitignored. Copy <c>GoogleAuthService.Credentials.cs.template</c> to
    /// <c>GoogleAuthService.Credentials.cs</c> and paste your real OAuth Desktop client ID +
    /// secret to enable sign-in. For installed apps, the secret is not actually confidential
    /// per Google's documentation — it ships in the binary.</summary>
    public static partial class GoogleAuthService
    {
        private static readonly string[] Scopes = { DriveService.Scope.DriveFile };
        private const string AppName = "TASKly";

        public static bool IsConfigured => !ClientId.StartsWith("PASTE_");

        /// <summary>Active credential cached after sign-in or restore. Null when signed out.</summary>
        public static UserCredential? Current { get; private set; }

        /// <summary>Launches the system browser for OAuth consent on first sign-in.</summary>
        public static async Task<UserCredential> SignInAsync(CancellationToken ct = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException(
                    "Google OAuth client credentials are not configured. " +
                    "Paste your Client ID + Secret into GoogleAuthService.cs.");

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = ClientId, ClientSecret = ClientSecret },
                Scopes        = Scopes,
                DataStore     = new DpapiSettingsDataStore(),
            });

            var codeReceiver = new Google.Apis.Auth.OAuth2.LocalServerCodeReceiver();
            var auth = new AuthorizationCodeInstalledApp(flow, codeReceiver);
            var cred = await auth.AuthorizeAsync("user", ct).ConfigureAwait(false);

            Current = cred;
            return cred;
        }

        /// <summary>Try to silently restore a saved refresh token. Returns null if no token,
        /// or the token has been revoked.</summary>
        public static async Task<UserCredential?> TryRestoreAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return null;

            var refresh = SettingsService.Unprotect(SettingsService.Current.ProtectedRefreshToken);
            if (string.IsNullOrEmpty(refresh)) return null;

            try
            {
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets { ClientId = ClientId, ClientSecret = ClientSecret },
                    Scopes        = Scopes,
                    DataStore     = new DpapiSettingsDataStore(),
                });

                // Reading from the data store will populate Token from settings.json.
                var token = await flow.LoadTokenAsync("user", ct).ConfigureAwait(false);
                if (token == null) return null;

                var cred = new UserCredential(flow, "user", token);
                // Refresh access token if needed.
                if (token.IsStale)
                    await cred.RefreshTokenAsync(ct).ConfigureAwait(false);

                Current = cred;
                return cred;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Revoke + clear stored credentials.</summary>
        public static async Task SignOutAsync(CancellationToken ct = default)
        {
            try
            {
                if (Current != null)
                    await Current.RevokeTokenAsync(ct).ConfigureAwait(false);
            }
            catch { /* best effort */ }

            Current = null;
            var s = SettingsService.Current;
            s.ProtectedRefreshToken = null;
            s.UserEmail             = null;
            s.SyncEnabled           = false;
            s.RemoteFileId          = null;
            s.RemoteFolderId        = null;
            SettingsService.Save();
        }

        /// <summary>Custom IDataStore that round-trips OAuth tokens through SettingsService,
        /// keeping the refresh token DPAPI-encrypted on disk.</summary>
        private sealed class DpapiSettingsDataStore : IDataStore
        {
            public Task ClearAsync()
            {
                var s = SettingsService.Current;
                s.ProtectedRefreshToken = null;
                SettingsService.Save();
                return Task.CompletedTask;
            }

            public Task DeleteAsync<T>(string key)
            {
                var s = SettingsService.Current;
                s.ProtectedRefreshToken = null;
                SettingsService.Save();
                return Task.CompletedTask;
            }

            public Task<T> GetAsync<T>(string key)
            {
                var s = SettingsService.Current;
                var json = SettingsService.Unprotect(s.ProtectedRefreshToken);
                if (string.IsNullOrEmpty(json))
                    return Task.FromResult<T>(default!);

                try
                {
                    var value = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
                    return Task.FromResult<T>(value!);
                }
                catch
                {
                    return Task.FromResult<T>(default!);
                }
            }

            public Task StoreAsync<T>(string key, T value)
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(value);
                var s    = SettingsService.Current;
                s.ProtectedRefreshToken = SettingsService.Protect(json);
                SettingsService.Save();
                return Task.CompletedTask;
            }
        }
    }
}
