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
            DoingList.ItemsSource = _vm.DoingView;
            TodoList.ItemsSource  = _vm.TodoView;
            DoneList.ItemsSource  = _vm.DoneView;

            _vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(MainViewModel.HasDoingTasks)
                                      or nameof(MainViewModel.HasTodoTasks)
                                      or nameof(MainViewModel.HasDoneTasks))
                    UpdateSectionVisibility();
            };

            UpdateSectionVisibility();
            UpdateMaxHeight();
            InitTrayIcon();

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
                Text             = "Task Widget",
                ContextMenuStrip = menu
            };
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
            SetWindowPos(hwnd, HWND_BOTTOM, _anchorX, _anchorY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
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
            DoingSection.Visibility = _vm.HasDoingTasks ? Visibility.Visible : Visibility.Collapsed;
            TodoSection.Visibility  = _vm.HasTodoTasks  ? Visibility.Visible : Visibility.Collapsed;
            DoneSection.Visibility  = _vm.HasDoneTasks  ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddTaskBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AddTaskWindow { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Result != null)
                _vm.AddTask(dlg.Result);
        }

        private void Card_StatusChangeRequested(object sender, TaskItem task) => _vm.CycleStatus(task);

        private void Card_EditRequested(object sender, TaskItem task)
        {
            var dlg = new AddTaskWindow(task) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                if (dlg.DeletePressed)
                    _vm.DeleteTask(task);
                else
                    _vm.Save(); // edits applied in-place by AddTaskWindow
            }
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
