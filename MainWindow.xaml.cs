using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TaskManagementWidget.Controls;
using TaskManagementWidget.Models;
using TaskManagementWidget.ViewModels;
using TaskManagementWidget.Views;
using TaskStatus = TaskManagementWidget.Models.TaskStatus;

namespace TaskManagementWidget
{
    public partial class MainWindow : Window
    {
        // Win32 constants
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOMOVE      = 0x0002;
        private const uint SWP_NOSIZE      = 0x0001;
        private const uint SWP_NOACTIVATE  = 0x0010;
        private const uint SWP_NOZORDER    = 0x0004;
        private const int  GWL_EXSTYLE     = -20;
        private const int  WS_EX_TOOLWINDOW = 0x00000080;
        private const int  WS_EX_NOACTIVATE = 0x08000000;
        private const int  WM_WINDOWPOSCHANGING          = 0x0046;
        private const int  WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const int  WIDGET_WIDTH_PX = 336;
        private const int  PADDING_PX      = 32;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] private static extern bool  SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern int   GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int   SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern uint  GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll")] private static extern bool  GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] private static extern bool  SetForegroundWindow(IntPtr hWnd);

        // ── Acrylic blur ─────────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY { public int AccentState, AccentFlags, GradientColor, AnimationId; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WINCOMPATTRDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] private static extern int   SetWindowCompositionAttribute(IntPtr hwnd, ref WINCOMPATTRDATA data);
        [DllImport("gdi32.dll")]  private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] private static extern int   SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        private MainViewModel  _vm = null!;
        private const double   DragThreshold = 8;
        private int            _anchorX, _anchorY;
        private double         _scale = 1.0;
        private MONITORINFO    _mi;
        private System.Windows.Forms.NotifyIcon _trayIcon = null!;

        // ── Drag-and-drop state ──────────────────────────────────────────────────
        private TaskCard? _indicatorCard;

        public MainWindow() { InitializeComponent(); }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _vm = new MainViewModel();
            DoingList.ItemsSource   = _vm.DoingView;
            OverdueList.ItemsSource = _vm.OverdueView;
            TodoList.ItemsSource    = _vm.TodoView;
            DoneList.ItemsSource    = _vm.DoneView;

            _vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(MainViewModel.HasDoingTasks)
                                      or nameof(MainViewModel.HasOverdueTasks)
                                      or nameof(MainViewModel.HasTodoTasks)
                                      or nameof(MainViewModel.HasDoneTasks))
                    UpdateSectionVisibility();
            };

            UpdateSectionVisibility();
            UpdateMaxHeight();
            InitTrayIcon();

            // ── Sync wiring ────────────────────────────────────────────────────
            TaskManagementWidget.Services.SyncCoordinator.Initialize(_vm);
            TaskManagementWidget.Services.SyncCoordinator.Instance!.StateChanged += (_, _) =>
                Dispatcher.BeginInvoke(new Action(UpdateTrayTooltip));

            // Show welcome panel on first run, otherwise try to restore credentials silently.
            _ = InitGoogleSyncAsync();

            // ── Ghost popup driven by GiveFeedback on the drag source ──────────────
            TaskManagementWidget.Controls.TaskCard.DragGhostStarted += task =>
            {
                GhostNameText.Text       = task.Name;
                GhostImportanceText.Text = task.Importance.ToString();
                GhostBadge.Background    = GetBadgeBrush(task.BadgeT);
            };
            TaskManagementWidget.Controls.TaskCard.DragGhostMoved += pt =>
            {
                GhostPopup.IsOpen              = true;
                GhostPopup.Placement           = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
                GhostPopup.PlacementTarget     = null;
                GhostPopup.HorizontalOffset    = pt.X + 14;
                GhostPopup.VerticalOffset      = pt.Y + 14;
            };
            TaskManagementWidget.Controls.TaskCard.DragEnded += () =>
            {
                GhostPopup.IsOpen = false;
                ClearDropIndicator();
            };
        }

        private void InitTrayIcon()
        {
            // Draw a small coloured square as the tray icon
            var bmp = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x89, 0xB4, 0xFA)), 1, 1, 14, 14);
            }

            var menu = new System.Windows.Forms.ContextMenuStrip();

            _trayAccountItem = new System.Windows.Forms.ToolStripMenuItem("Sign in to Google Drive…");
            _trayAccountItem.Click += async (_, _) => await OnTrayAccountClicked();
            menu.Items.Add(_trayAccountItem);

            _traySyncNowItem = new System.Windows.Forms.ToolStripMenuItem("Sync now");
            _traySyncNowItem.Click += async (_, _) =>
            {
                if (TaskManagementWidget.Services.SyncCoordinator.Instance is { } sc)
                    await sc.SyncNowAsync();
            };
            menu.Items.Add(_traySyncNowItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) =>
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                Application.Current.Shutdown();
            });

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon             = System.Drawing.Icon.FromHandle(bmp.GetHicon()),
                Visible          = true,
                Text             = "TASKly",
                ContextMenuStrip = menu
            };

            UpdateTrayTooltip();
        }

        // ── Tray sync menu state ────────────────────────────────────────────────
        private System.Windows.Forms.ToolStripMenuItem _trayAccountItem  = null!;
        private System.Windows.Forms.ToolStripMenuItem _traySyncNowItem  = null!;

        private void UpdateTrayTooltip()
        {
            var s  = TaskManagementWidget.Services.SettingsService.Current;
            var sc = TaskManagementWidget.Services.SyncCoordinator.Instance;

            if (sc == null || sc.State == TaskManagementWidget.Services.SyncState.Disabled)
            {
                if (_trayIcon != null) _trayIcon.Text = "TASKly — Offline";
                if (_trayAccountItem != null) _trayAccountItem.Text = "Sign in to Google Drive…";
                if (_traySyncNowItem != null) _traySyncNowItem.Enabled = false;
                return;
            }

            string status = sc.State switch
            {
                TaskManagementWidget.Services.SyncState.Syncing => "Syncing…",
                TaskManagementWidget.Services.SyncState.Error   => "Sync error",
                _ when s.LastSyncUtc.HasValue                   => $"Synced {Humanize(DateTime.UtcNow - s.LastSyncUtc.Value)}",
                _                                               => "Synced"
            };
            // NotifyIcon.Text is limited to 63 chars.
            if (_trayIcon != null) _trayIcon.Text = Truncate($"TASKly — {status}", 60);
            if (_trayAccountItem != null) _trayAccountItem.Text = $"Google Drive: {s.UserEmail ?? "signed in"}  •  Sign out";
            if (_traySyncNowItem != null) _traySyncNowItem.Enabled = sc.State != TaskManagementWidget.Services.SyncState.Syncing;
        }

        private static string Humanize(TimeSpan ts)
        {
            if (ts.TotalSeconds < 60) return "just now";
            if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes} min ago";
            if (ts.TotalHours   < 24) return $"{(int)ts.TotalHours} h ago";
            return $"{(int)ts.TotalDays} d ago";
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        private async System.Threading.Tasks.Task OnTrayAccountClicked()
        {
            var s  = TaskManagementWidget.Services.SettingsService.Current;
            var sc = TaskManagementWidget.Services.SyncCoordinator.Instance;

            if (sc != null && sc.State != TaskManagementWidget.Services.SyncState.Disabled)
            {
                // Signed in → sign out.
                var confirm = MessageBox.Show("Sign out of Google Drive?\n\nLocal tasks will remain. You can sign back in any time.",
                    "Sign out", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                await TaskManagementWidget.Services.GoogleAuthService.SignOutAsync();
                sc.Disable();
                UpdateTrayTooltip();
            }
            else
            {
                // Not signed in → start sign-in.
                await StartSignInAsync();
            }
        }

        // ── First-run welcome / sign-in ──────────────────────────────────────────
        private async System.Threading.Tasks.Task InitGoogleSyncAsync()
        {
            try
            {
                var cred = await TaskManagementWidget.Services.GoogleAuthService.TryRestoreAsync();
                if (cred != null)
                {
                    TaskManagementWidget.Services.SyncCoordinator.Instance!.Enable(cred);
                    _ = TaskManagementWidget.Services.SyncCoordinator.Instance.SyncNowAsync();
                    UpdateTrayTooltip();
                    return;
                }
            }
            catch { /* ignore restore failures */ }

            // No restored credential. Show welcome panel only on first run.
            if (!TaskManagementWidget.Services.SettingsService.Current.FirstRunCompleted
                && TaskManagementWidget.Services.GoogleAuthService.IsConfigured)
            {
                WelcomeOverlay.Visibility = Visibility.Visible;
            }

            UpdateTrayTooltip();
        }

        private async System.Threading.Tasks.Task StartSignInAsync()
        {
            if (!TaskManagementWidget.Services.GoogleAuthService.IsConfigured)
            {
                MessageBox.Show("Google Drive sync is not configured in this build.\n\n" +
                    "Paste an OAuth Client ID + Secret into GoogleAuthService.cs and rebuild.",
                    "Sync unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (WelcomeStatusText != null)
                {
                    WelcomeStatusText.Text = "Opening browser for sign-in…";
                    WelcomeStatusText.Visibility = Visibility.Visible;
                    WelcomeSignInBtn.IsEnabled = false;
                    WelcomeSkipBtn.IsEnabled   = false;
                }

                var cred = await TaskManagementWidget.Services.GoogleAuthService.SignInAsync();
                TaskManagementWidget.Services.SyncCoordinator.Instance!.Enable(cred);
                TaskManagementWidget.Services.SettingsService.Current.FirstRunCompleted = true;
                TaskManagementWidget.Services.SettingsService.Save();

                WelcomeOverlay.Visibility = Visibility.Collapsed;
                _ = TaskManagementWidget.Services.SyncCoordinator.Instance.SyncNowAsync();
                UpdateTrayTooltip();
            }
            catch (Exception ex)
            {
                if (WelcomeStatusText != null)
                {
                    WelcomeStatusText.Text = $"Sign-in failed: {ex.Message}";
                    WelcomeStatusText.Visibility = Visibility.Visible;
                    WelcomeSignInBtn.IsEnabled = true;
                    WelcomeSkipBtn.IsEnabled   = true;
                }
                else
                {
                    MessageBox.Show($"Sign-in failed:\n{ex.Message}", "Sign in", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private async void WelcomeSignInBtn_Click(object sender, RoutedEventArgs e)
            => await StartSignInAsync();

        private void WelcomeSkipBtn_Click(object sender, RoutedEventArgs e)
        {
            TaskManagementWidget.Services.SettingsService.Current.FirstRunCompleted = true;
            TaskManagementWidget.Services.SettingsService.Save();
            WelcomeOverlay.Visibility = Visibility.Collapsed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

            uint dpi = GetDpiForWindow(hwnd);
            _scale   = dpi / 96.0;

            _mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            GetMonitorInfo(hMon, ref _mi);

            int paddingPx = (int)(PADDING_PX      * _scale);
            int widthPx   = (int)(WIDGET_WIDTH_PX * _scale);

            _anchorX = _mi.rcWork.Right - widthPx - paddingPx;
            _anchorY = _mi.rcWork.Top   + paddingPx;

            SetWindowPos(hwnd, HWND_BOTTOM, _anchorX, _anchorY, widthPx, 0, SWP_NOSIZE | SWP_NOACTIVATE);
            HwndSource.FromHwnd(hwnd)!.AddHook(WndProc);
            SizeChanged += MainWindow_SizeChanged;
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Keep the RIGHT edge pinned as window grows/shrinks leftward
            int paddingPx      = (int)(PADDING_PX * _scale);
            int currentWidthPx = (int)(e.NewSize.Width * _scale);
            int dynamicAnchorX = _mi.rcWork.Right - currentWidthPx - paddingPx;
            SetWindowPos(hwnd, HWND_BOTTOM, dynamicAnchorX, _anchorY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
            ApplyWindowRgn(hwnd);
        }

        private void EnableAcrylic(IntPtr hwnd)
        {
            try
            {
                var accent = new ACCENT_POLICY
                {
                    AccentState   = 4,
                    AccentFlags   = 0,
                    GradientColor = unchecked((int)0x40353E4C),
                    AnimationId   = 0
                };
                int    size = Marshal.SizeOf(accent);
                IntPtr ptr  = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WINCOMPATTRDATA { Attribute = 19, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(hwnd, ref data);
                Marshal.FreeHGlobal(ptr);
            }
            catch { }
        }

        private void ApplyWindowRgn(IntPtr hwnd)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            int wPx = (int)(ActualWidth  * _scale) + 1;
            int hPx = (int)(ActualHeight * _scale) + 1;
            int rPx = (int)(24 * _scale * 2);
            var rgn = CreateRoundRectRgn(0, 0, wPx, hPx, rPx, rPx);
            SetWindowRgn(hwnd, rgn, true);
        }

        private void UpdateMaxHeight()
        {
            double workHDip = SystemParameters.WorkArea.Height;
            double maxH     = workHDip - (PADDING_PX * 2);
            if (maxH > 0) this.MaxHeight = maxH;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING)
            {
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                pos.flags &= ~SWP_NOZORDER;
                pos.hwndInsertAfter = HWND_BOTTOM;
                Marshal.StructureToPtr(pos, lParam, false);
            }
            if (msg == WM_DWMCOLORIZATIONCOLORCHANGED)
                App.UpdateAccentTheme();
            return IntPtr.Zero;
        }

        private void UpdateSectionVisibility()
        {
            DoingSection.Visibility   = _vm.HasDoingTasks   ? Visibility.Visible : Visibility.Collapsed;
            OverdueSection.Visibility = _vm.HasOverdueTasks ? Visibility.Visible : Visibility.Collapsed;
            TodoSection.Visibility    = _vm.HasTodoTasks    ? Visibility.Visible : Visibility.Collapsed;
            DoneSection.Visibility    = _vm.HasDoneTasks    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddTaskBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenTaskForm(null);
        }

        private void Card_StatusSetRequested(object sender, (TaskItem task, TaskManagementWidget.Models.TaskStatus status) e)
            => _vm.SetStatus(e.task, e.status);

        private void Card_EditRequested(object sender, TaskItem task)
        {
            OpenTaskForm(task);
        }

        // ── Inline task form ─────────────────────────────────────────────────────
        private TaskItem? _editingTask;
        private TaskItem? _previewTask;   // ghost task for add mode
        private bool      _populatingForm; // true while OpenTaskForm is setting fields — suppresses UpdatePreview

        // Snapshot of original values captured when edit mode begins (for Cancel)
        private record TaskSnapshot(
            string Name, int Importance, string? Description, string? Url,
            int? TickerPoints, double? TickerHours, DateTime? TickerLastApplied,
            DateTime? Deadline, int ManualOrder);
        private TaskSnapshot? _editSnapshot;

        private void OpenTaskForm(TaskItem? existing)
        {
            _populatingForm = true;
            try
            {
            _editingTask = existing;

            // Populate hour dropdown 00:00 – 23:00
            if (FormDeadlineHourBox.Items.Count == 0)
            {
                for (int h = 0; h < 24; h++)
                    FormDeadlineHourBox.Items.Add($"{h:D2}:00");
            }

            if (existing == null)
            {
                // ── Add mode: create a ghost preview task ────────────────────────
                FormTitleText.Text       = "New Task";
                FormSaveBtn.Content      = "Add Task";
                FormDeleteBtn.Visibility = Visibility.Collapsed;
                FormNameBox.Text         = string.Empty;
                FormImportanceSlider.Value = 50;
                FormDescriptionBox.Text  = string.Empty;
                FormUrlBox.Text          = string.Empty;
                FormTickerEnabledBox.IsChecked   = false;
                FormDeadlineEnabledBox.IsChecked = false;
                FormTickerPointsBox.Text = "1";
                FormTickerHoursBox.Text  = "1";
                FormDeadlineCal.SelectedDate = null;
                FormDateInlineLabel.Text = "Date";
                FormDeadlineHourBox.SelectedIndex = 0;
                FormSaveBtn.IsEnabled    = false;

                _previewTask = new TaskItem { Importance = 50 };
                _vm.AddPreviewTask(_previewTask);
            }
            else
            {
                // ── Edit mode: snapshot and mark task as preview ─────────────────
                _editSnapshot = new TaskSnapshot(
                    existing.Name, existing.Importance, existing.Description, existing.Url,
                    existing.TickerPoints, existing.TickerHours, existing.TickerLastApplied,
                    existing.Deadline, existing.ManualOrder);

                FormTitleText.Text       = "Edit Task";
                FormSaveBtn.Content      = "Save Changes";
                FormDeleteBtn.Visibility = Visibility.Visible;
                FormNameBox.Text         = existing.Name;
                FormImportanceSlider.Value = existing.Importance;
                FormDescriptionBox.Text  = existing.Description ?? string.Empty;
                FormUrlBox.Text          = existing.Url ?? string.Empty;

                if (existing.TickerPoints.HasValue)
                {
                    FormTickerEnabledBox.IsChecked = true;
                    FormTickerPointsBox.Text = existing.TickerPoints.Value.ToString();
                    FormTickerHoursBox.Text  = (existing.TickerHours ?? 1).ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    FormTickerEnabledBox.IsChecked = false;
                }

                if (existing.Deadline.HasValue)
                {
                    FormDeadlineEnabledBox.IsChecked = true;
                    var local = existing.Deadline.Value.ToLocalTime();
                    FormDeadlineCal.SelectedDate = local.Date;
                    FormDateInlineLabel.Text = "Date | " + local.Date.ToString("dd/MM/yy");
                    FormDeadlineHourBox.SelectedIndex = local.Hour;
                }
                else
                {
                    FormDeadlineEnabledBox.IsChecked = false;
                }

                FormSaveBtn.IsEnabled = true;
                _vm.BeginEditPreview(existing);
            }

            TaskFormPanel.Visibility = Visibility.Visible;
            AddTaskTabBtn.Visibility  = Visibility.Collapsed;

            } // end _populatingForm block
            finally { _populatingForm = false; }

            // Sync preview to the now-stable form state
            UpdatePreview();

            // Allow keyboard input while form is open
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_NOACTIVATE);
            SetForegroundWindow(hwnd);
            FormNameBox.Focus();
        }

        private void CloseTaskForm(bool save = false)
        {
            if (_editingTask == null && _previewTask != null)
            {
                // ── Add mode ─────────────────────────────────────────────────────
                if (save)
                    _vm.CommitPreviewTask(_previewTask);
                else
                    _vm.RemovePreviewTask(_previewTask);
                _previewTask = null;
            }
            else if (_editingTask != null)
            {
                // ── Edit mode ────────────────────────────────────────────────────
                if (!save && _editSnapshot != null)
                {
                    // Restore all original values
                    _editingTask.Name              = _editSnapshot.Name;
                    _editingTask.Importance        = _editSnapshot.Importance;
                    _editingTask.Description       = _editSnapshot.Description;
                    _editingTask.Url               = _editSnapshot.Url;
                    _editingTask.TickerPoints      = _editSnapshot.TickerPoints;
                    _editingTask.TickerHours       = _editSnapshot.TickerHours;
                    _editingTask.TickerLastApplied = _editSnapshot.TickerLastApplied;
                    _editingTask.Deadline          = _editSnapshot.Deadline;
                    _editingTask.ManualOrder       = _editSnapshot.ManualOrder;
                }
                _vm.EndEditPreview(_editingTask, save);
            }

            _editingTask  = null;
            _editSnapshot = null;

            TaskFormPanel.Visibility = Visibility.Collapsed;
            AddTaskTabBtn.Visibility = Visibility.Visible;

            // Restore no-activate so widget doesn't steal focus
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
        }

        private void TaskFormPopup_Opened(object sender, EventArgs e)
        {
            FormNameBox.Focus();
        }

        private void FormCancelBtn_Click(object sender, RoutedEventArgs e)
            => CloseTaskForm(save: false);

        // ── UpdatePreview: push all form values to the active preview/edit task ──
        private void UpdatePreview()
        {
            if (_populatingForm) return;
            var target = _editingTask ?? _previewTask;
            if (target == null) return;

            target.Name        = FormNameBox.Text.Trim();
            target.Importance  = (int)FormImportanceSlider.Value;
            target.Description = string.IsNullOrWhiteSpace(FormDescriptionBox.Text)
                                     ? null : FormDescriptionBox.Text.Trim();
            target.Url         = string.IsNullOrWhiteSpace(FormUrlBox.Text)
                                     ? null : FormUrlBox.Text.Trim();

            if (FormDeadlineEnabledBox.IsChecked == true && FormDeadlineCal.SelectedDate.HasValue)
            {
                int hourIndex = FormDeadlineHourBox.SelectedIndex < 0 ? 0 : FormDeadlineHourBox.SelectedIndex;
                target.Deadline = DateTime.SpecifyKind(
                    FormDeadlineCal.SelectedDate.Value.Date.AddHours(hourIndex),
                    DateTimeKind.Local).ToUniversalTime();
            }
            else
            {
                target.Deadline = null;
            }
        }

        // Shared handler for fields with no other logic (description, url, hour dropdown)
        private void FormPreview_Changed(object sender, RoutedEventArgs e)
            => UpdatePreview();

        private void FormNameBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (FormSaveBtn != null)
                FormSaveBtn.IsEnabled = !string.IsNullOrWhiteSpace(FormNameBox.Text);
            UpdatePreview();
        }

        private void FormImportanceSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (FormImportanceLabel != null)
                FormImportanceLabel.Text = ((int)FormImportanceSlider.Value).ToString();
            UpdatePreview();
        }

        private void FormDeadlineEnabledBox_Changed(object sender, RoutedEventArgs e)
        {
            FormDeadlinePanel.Visibility = FormDeadlineEnabledBox.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
            UpdatePreview();
        }

        private void FormDatePickerBtn_Click(object sender, RoutedEventArgs e)
        {
            FormDatePopup.PlacementTarget = FormDatePickerBtn;
            FormDatePopup.IsOpen = true;
        }

        private void FormDeadlineCal_SelectedDatesChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (FormDeadlineCal.SelectedDate.HasValue)
            {
                FormDateInlineLabel.Text = "Date | " + FormDeadlineCal.SelectedDate.Value.ToString("dd/MM/yy");
                FormDatePopup.IsOpen = false;
                UpdatePreview();
            }
        }

        private void FormTickerEnabledBox_Changed(object sender, RoutedEventArgs e)
            => FormTickerPanel.Visibility = FormTickerEnabledBox.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;

        private void FormAutoScaleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (FormDeadlineEnabledBox.IsChecked != true || !FormDeadlineCal.SelectedDate.HasValue)
            {
                MessageBox.Show("Please set a deadline first.", "Importance Scaling",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int hourIndex  = FormDeadlineHourBox.SelectedIndex < 0 ? 0 : FormDeadlineHourBox.SelectedIndex;
            var date       = FormDeadlineCal.SelectedDate.Value.Date;
            var deadlineUtc = DateTime.SpecifyKind(date.AddHours(hourIndex), DateTimeKind.Local).ToUniversalTime();
            double hoursAvailable = (deadlineUtc - DateTime.UtcNow).TotalHours - 24;
            int    pointsNeeded   = 100 - (int)FormImportanceSlider.Value;

            if (hoursAvailable <= 0)
            {
                MessageBox.Show("The deadline is less than 24 hours away — auto-scale cannot be applied.",
                    "Importance Scaling", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (pointsNeeded <= 0)
            {
                MessageBox.Show("Importance is already at 100.", "Importance Scaling",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            FormTickerPointsBox.Text = "1";
            FormTickerHoursBox.Text  = Math.Round(hoursAvailable / pointsNeeded, 2)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private void FormSaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // Push final form state to the preview task (catches any last-second changes)
            UpdatePreview();

            // Handle ticker fields (not live-previewed — no card-visible effect)
            int?    tickerPts = null;
            double? tickerHrs = null;
            if (FormTickerEnabledBox.IsChecked == true)
            {
                if (int.TryParse(FormTickerPointsBox.Text.Trim(), out int pts) && pts > 0)
                    tickerPts = pts;
                if (double.TryParse(FormTickerHoursBox.Text.Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double hrs) && hrs > 0)
                    tickerHrs = hrs;
            }

            if (_editingTask != null)
            {
                bool hadTicker = _editSnapshot?.TickerPoints.HasValue ?? false;
                _editingTask.TickerPoints = tickerPts;
                _editingTask.TickerHours  = tickerHrs;
                if (tickerPts.HasValue && !hadTicker)
                    _editingTask.TickerLastApplied = DateTime.UtcNow;
                else if (!tickerPts.HasValue)
                    _editingTask.TickerLastApplied = null;
            }
            else if (_previewTask != null)
            {
                _previewTask.TickerPoints      = tickerPts;
                _previewTask.TickerHours       = tickerHrs;
                _previewTask.TickerLastApplied = tickerPts.HasValue ? DateTime.UtcNow : (DateTime?)null;
            }

            CloseTaskForm(save: true);
        }

        private void FormDeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var toDelete = _editingTask;
            CloseTaskForm(save: false);  // restore snapshot + end preview first
            if (toDelete != null)
                _vm.DeleteTask(toDelete);
        }

        // ── Per-card drag handlers (reliable: fires when cursor is over the card) ────
        private void Card_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(TaskItem))) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            var dragged = (TaskItem)e.Data.GetData(typeof(TaskItem));
            var card    = (TaskCard)sender;
            if (card.Task == null || card.Task.Status != dragged.Status) { e.Effects = DragDropEffects.None; e.Handled = true; return; }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            if (_indicatorCard != card)
            {
                ClearDropIndicator();
                if (card.Task != dragged)   // don’t show indicator on the card being dragged
                {
                    card.ShowDropIndicator = true;
                    _indicatorCard = card;
                }
            }
        }

        private void Card_DragLeave(object sender, DragEventArgs e)
        {
            // Only clear if cursor actually left the card’s bounds
            var card = (TaskCard)sender;
            var pos  = e.GetPosition(card);
            if (pos.X < 0 || pos.Y < 0 || pos.X > card.ActualWidth || pos.Y > card.ActualHeight)
                if (_indicatorCard == card) ClearDropIndicator();
        }

        private void Card_Drop(object sender, DragEventArgs e)
        {
            ClearDropIndicator();
            if (!e.Data.GetDataPresent(typeof(TaskItem))) return;
            var dragged = (TaskItem)e.Data.GetData(typeof(TaskItem));
            var card    = (TaskCard)sender;
            if (card.Task == null || card.Task == dragged) return;
            if (dragged.Status != card.Task.Status) return;

            _vm.ReorderTask(dragged, card.Task);
            e.Handled = true;
        }

        // ── List-level handlers (fire for empty space below all cards) ─────────
        private void List_DragOver(object sender, DragEventArgs e)
        {
            if (e.Handled) return;
            e.Effects = e.Data.GetDataPresent(typeof(TaskItem)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void List_Drop(object sender, DragEventArgs e)
        {
            if (e.Handled) return;  // card already handled it
            ClearDropIndicator();
            if (!e.Data.GetDataPresent(typeof(TaskItem))) return;
            var dragged      = (TaskItem)e.Data.GetData(typeof(TaskItem));
            var list         = (ItemsControl)sender;
            var targetStatus = list == DoingList ? TaskStatus.Doing : TaskStatus.ToDo;
            if (dragged.Status != targetStatus) return;

            _vm.ReorderTask(dragged, null);  // insert at end of the list
            e.Handled = true;
        }

        private void ClearDropIndicator()
        {
            if (_indicatorCard != null) { _indicatorCard.ShowDropIndicator = false; _indicatorCard = null; }
        }

        private static SolidColorBrush GetBadgeBrush(double t)
        {
            if (t < 0) return new SolidColorBrush(Color.FromRgb(0x64, 0xE0, 0xF6));
            t = Math.Clamp(t, 0, 1);
            (byte R, byte G, byte B)[] stops =
            {
                (0x57, 0xE0, 0x5A), (0xC8, 0xE0, 0x57), (0xE0, 0xC8, 0x57),
                (0xE0, 0x7B, 0x57), (0xE0, 0x5A, 0x57),
            };
            double pos = t * (stops.Length - 1);
            int    i0  = (int)Math.Floor(pos);
            int    i1  = Math.Min(i0 + 1, stops.Length - 1);
            double s   = pos - i0;
            static byte Lerp(byte a, byte b, double x) => (byte)Math.Round(a + (b - a) * x);
            return new SolidColorBrush(Color.FromRgb(
                Lerp(stops[i0].R, stops[i1].R, s),
                Lerp(stops[i0].G, stops[i1].G, s),
                Lerp(stops[i0].B, stops[i1].B, s)));
        }

        private bool _doneExpanded = true;
        private void DoneToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            _doneExpanded        = !_doneExpanded;
            DoneList.Visibility  = _doneExpanded ? Visibility.Visible : Visibility.Collapsed;
            DoneChevron.Text     = _doneExpanded ? "▾ " : "▸ ";
        }
    }
}
