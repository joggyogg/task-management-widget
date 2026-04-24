using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TaskManagementWidget.Models;
using TaskManagementWidget.ViewModels;

namespace TaskManagementWidget.Services
{
    public enum SyncState { Disabled, Idle, Syncing, Error }

    /// <summary>Single global coordinator that serializes all Drive sync activity. Triggered by:
    ///   - debounced changes (5s)
    ///   - hourly timer
    ///   - explicit SyncNowAsync() (startup, exit, tray "Sync now")
    /// </summary>
    public sealed class SyncCoordinator
    {
        public static SyncCoordinator? Instance { get; private set; }

        public static void Initialize(MainViewModel vm)
        {
            Instance = new SyncCoordinator(vm);
        }

        // ── State ──
        private readonly MainViewModel  _vm;
        private readonly SemaphoreSlim  _gate         = new(1, 1);
        private readonly DispatcherTimer _debounceTimer;
        private readonly DispatcherTimer _hourlyTimer;
        private DriveSyncService?       _drive;

        public SyncState State     { get; private set; }
        public string?   LastError { get; private set; }
        public DateTime? LastSyncUtc => SettingsService.Current.LastSyncUtc;

        public event EventHandler? StateChanged;

        private SyncCoordinator(MainViewModel vm)
        {
            _vm = vm;

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _debounceTimer.Tick += async (_, _) => { _debounceTimer.Stop(); await SyncNowAsync(); };

            _hourlyTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _hourlyTimer.Tick += async (_, _) => await SyncNowAsync();

            UpdateStateFromSettings();
        }

        /// <summary>Called after sign-in or restored credential — wires up Drive client and timers.</summary>
        public void Enable(Google.Apis.Auth.OAuth2.UserCredential credential)
        {
            _drive = new DriveSyncService(credential);
            SettingsService.Current.SyncEnabled = true;
            SettingsService.Save();
            _hourlyTimer.Start();
            SetState(SyncState.Idle, null);
        }

        public void Disable()
        {
            _drive = null;
            _debounceTimer.Stop();
            _hourlyTimer.Stop();
            SettingsService.Current.SyncEnabled = false;
            SettingsService.Save();
            SetState(SyncState.Disabled, null);
        }

        /// <summary>Schedule a debounced sync. Cheap to call from many save sites.</summary>
        public void RequestSync()
        {
            if (_drive == null) return;
            // Restart the debounce window.
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        /// <summary>Run a sync immediately (waits if one is in flight).</summary>
        public async Task SyncNowAsync(CancellationToken ct = default)
        {
            if (_drive == null) return;

            // Don't sync while the user has an add/edit form open — ReplaceAll would otherwise
            // wipe the in-flight preview ghost. The next change/timer/manual trigger picks it up.
            if (_vm.AllTasks.Any(t => t.IsPreview))
            {
                // Re-arm the debounce so we try again shortly after the preview commits.
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                }));
                return;
            }

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                SetState(SyncState.Syncing, null);

                // Pull remote, merge with local-on-disk (which includes tombstones), push back.
                var remote = await _drive.DownloadAsync(ct).ConfigureAwait(false);
                var local  = TaskStorageService.LoadAll();
                var merged = TaskMerger.Merge(local, remote);

                await _drive.UploadAsync(merged, ct).ConfigureAwait(false);

                // Apply merged set to the live UI on the dispatcher thread.
                await Application.Current.Dispatcher.InvokeAsync(() => _vm.ReplaceAll(merged));

                // Cache the user's email if we don't have it yet.
                if (string.IsNullOrEmpty(SettingsService.Current.UserEmail))
                {
                    SettingsService.Current.UserEmail = await _drive.GetUserEmailAsync(ct).ConfigureAwait(false);
                }

                SettingsService.Current.LastSyncUtc = DateTime.UtcNow;
                SettingsService.Save();
                SetState(SyncState.Idle, null);
            }
            catch (Exception ex)
            {
                SetState(SyncState.Error, $"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        private void UpdateStateFromSettings()
        {
            State = SettingsService.Current.SyncEnabled ? SyncState.Idle : SyncState.Disabled;
        }

        private void SetState(SyncState s, string? err)
        {
            State     = s;
            LastError = err;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
