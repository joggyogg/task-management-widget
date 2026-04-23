using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TaskManagementWidget.Models;
using TaskStatus = TaskManagementWidget.Models.TaskStatus;

namespace TaskManagementWidget.Controls
{
    public partial class TaskCard : UserControl
    {
        public static readonly DependencyProperty TaskProperty =
            DependencyProperty.Register(nameof(Task), typeof(TaskItem), typeof(TaskCard),
                new PropertyMetadata(null, OnTaskChanged));

        public TaskItem? Task
        {
            get => (TaskItem?)GetValue(TaskProperty);
            set => SetValue(TaskProperty, value);
        }

        public event EventHandler<TaskItem>? StatusChangeRequested;
        public event EventHandler<TaskItem>? EditRequested;

        // ── Drag-ghost feed (static so MainWindow can subscribe once) ────────────
        public static event Action<TaskItem>?            DragGhostStarted;
        public static event Action<System.Drawing.Point>? DragGhostMoved;
        public static event Action?                      DragEnded;

        // ── Drop indicator ───────────────────────────────────────────────────────
        public static readonly DependencyProperty ShowDropIndicatorProperty =
            DependencyProperty.Register(nameof(ShowDropIndicator), typeof(bool), typeof(TaskCard),
                new PropertyMetadata(false, (d, e) =>
                    ((TaskCard)d).DropIndicatorBorder.Visibility =
                        (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

        public bool ShowDropIndicator
        {
            get => (bool)GetValue(ShowDropIndicatorProperty);
            set => SetValue(ShowDropIndicatorProperty, value);
        }

        private Point  _dragStart;
        private bool   _isDragging;
        private bool   _expanded;
        private const double DragThreshold = 6;
        private readonly DispatcherTimer _ageTimer;

        public TaskCard()
        {
            InitializeComponent();
            _ageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _ageTimer.Tick += (_, _) => { if (Task != null) RefreshAge(Task); };
            Loaded   += (_, _) => _ageTimer.Start();
            Unloaded += (_, _) => _ageTimer.Stop();
        }

        private static void OnTaskChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var card = (TaskCard)d;
            if (e.OldValue is TaskItem old) old.PropertyChanged -= card.Task_PropertyChanged;
            if (e.NewValue is TaskItem t)
            {
                t.PropertyChanged += card.Task_PropertyChanged;
                card._expanded = false;
                card.Refresh(t);
            }
        }

        private void Task_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (Task != null) Refresh(Task);
        }

        private void Refresh(TaskItem t)
        {
            NameText.Text          = t.Name;
            ImportanceText.Text    = t.Importance.ToString();
            BadgeBorder.Background = GetBadgeBrush(t.BadgeT);

            bool hasDesc = !string.IsNullOrWhiteSpace(t.Description);
            ExpandButton.Visibility = hasDesc ? Visibility.Visible : Visibility.Collapsed;
            if (!hasDesc) _expanded = false;
            DescriptionPanel.Visibility = (_expanded && hasDesc) ? Visibility.Visible : Visibility.Collapsed;
            ExpandButton.Content        = _expanded ? "\u25B4" : "\u25BE";
            DescriptionText.Text        = t.Description ?? string.Empty;

            LinkButton.Visibility = t.HasUrl ? Visibility.Visible : Visibility.Collapsed;

            StatusButton.Content = t.Status switch
            {
                TaskStatus.Doing     => "Doing",
                TaskStatus.Completed => "Done",
                _                    => "To Do"
            };

            if (t.Status == TaskStatus.Completed)
            {
                StatusButton.Background  = new SolidColorBrush(Color.FromRgb(0x64, 0xE0, 0xF6));
                StatusButton.Foreground  = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
                StatusButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x64, 0xE0, 0xF6));
            }
            else
            {
                StatusButton.Background  = System.Windows.Media.Brushes.Transparent;
                StatusButton.Foreground  = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
                StatusButton.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
            }

            RefreshAge(t);
        }

        private void RefreshAge(TaskItem t)
        {
            // ── Deadline countdown takes priority over age ────────────────────────
            if (t.Deadline.HasValue)
            {
                AgeText.Visibility = Visibility.Visible;
                var remaining = t.Deadline.Value - DateTime.UtcNow;
                if (remaining.TotalSeconds <= 0)
                {
                    AgeText.Text       = "OVERDUE";
                    AgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x57));
                }
                else
                {
                    int dDays = (int)remaining.TotalDays;
                    AgeText.Text = dDays > 0
                        ? $"{dDays}d {remaining.Hours}h"
                        : remaining.TotalHours >= 1
                            ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
                            : $"{remaining.Minutes}m";

                    AgeText.Foreground = remaining.TotalDays switch
                    {
                        < 1 => new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x57)),
                        < 3 => new SolidColorBrush(Color.FromRgb(0xE0, 0x7B, 0x57)),
                        < 7 => new SolidColorBrush(Color.FromRgb(0xE0, 0xC8, 0x57)),
                        _   => new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF))
                    };
                }
                return;
            }

            // ── Age display ───────────────────────────────────────────────────────
            if (t.CreatedAt == default)
            {
                AgeText.Visibility = Visibility.Collapsed;
                return;
            }
            var age     = DateTime.UtcNow - t.CreatedAt;
            int days    = (int)age.TotalDays;
            int hours   = age.Hours;
            int minutes = age.Minutes;
            AgeText.Text       = $"{days:D2}:{hours:D2}:{minutes:D2}";
            AgeText.Visibility = Visibility.Visible;
            AgeText.Foreground = days switch
            {
                >= 14 => new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x57)),  // red
                >= 7  => new SolidColorBrush(Color.FromRgb(0xE0, 0x7B, 0x57)),  // orange
                >= 3  => new SolidColorBrush(Color.FromRgb(0xE0, 0xC8, 0x57)),  // yellow
                _     => new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)) // dim white
            };
        }

        private static SolidColorBrush GetBadgeBrush(double t)
        {
            if (t < 0) return new SolidColorBrush(Color.FromRgb(0x64, 0xE0, 0xF6));
            t = Math.Clamp(t, 0, 1);

            (byte R, byte G, byte B)[] stops =
            {
                (0x57, 0xE0, 0x5A),
                (0xC8, 0xE0, 0x57),
                (0xE0, 0xC8, 0x57),
                (0xE0, 0x7B, 0x57),
                (0xE0, 0x5A, 0x57),
            };

            double pos = t * (stops.Length - 1);
            int    i0  = (int)Math.Floor(pos);
            int    i1  = Math.Min(i0 + 1, stops.Length - 1);
            double s   = pos - i0;

            static byte Lerp(byte a, byte b, double s) => (byte)Math.Round(a + (b - a) * s);
            return new SolidColorBrush(Color.FromRgb(
                Lerp(stops[i0].R, stops[i1].R, s),
                Lerp(stops[i0].G, stops[i1].G, s),
                Lerp(stops[i0].B, stops[i1].B, s)));
        }

        private void Badge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Task?.Status == TaskStatus.Completed) return;  // Completed tasks are not draggable
            _dragStart  = e.GetPosition(this);
            _isDragging = false;
            BadgeBorder.CaptureMouse();
            e.Handled = true;
        }

        private void Badge_MouseMove(object sender, MouseEventArgs e)
        {
            if (!BadgeBorder.IsMouseCaptured) return;
            if (e.LeftButton != MouseButtonState.Pressed) { BadgeBorder.ReleaseMouseCapture(); return; }
            if (_isDragging) return;

            var diff = e.GetPosition(this) - _dragStart;
            if (Math.Abs(diff.X) < DragThreshold && Math.Abs(diff.Y) < DragThreshold) return;

            _isDragging = true;
            BadgeBorder.ReleaseMouseCapture();

            if (Task != null)
            {
                DragGhostStarted?.Invoke(Task);

                System.Windows.GiveFeedbackEventHandler feedbackHandler = (_, fe) =>
                {
                    DragGhostMoved?.Invoke(System.Windows.Forms.Cursor.Position);
                    fe.UseDefaultCursors = true;
                    fe.Handled           = false;
                };
                DragDrop.AddGiveFeedbackHandler(this, feedbackHandler);
                DragDrop.DoDragDrop(this, Task, DragDropEffects.Move);
                DragDrop.RemoveGiveFeedbackHandler(this, feedbackHandler);
                DragEnded?.Invoke();
            }

            _isDragging = false;
        }

        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _expanded = !_expanded;
            DescriptionPanel.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            ExpandButton.Content        = _expanded ? "\u25B4" : "\u25BE";
        }

        private void StatusButton_Click(object sender, RoutedEventArgs e)
            => StatusChangeRequested?.Invoke(this, Task!);

        private void EditButton_Click(object sender, RoutedEventArgs e)
            => EditRequested?.Invoke(this, Task!);

        private void LinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Task?.Url)) return;
            var url = Task.Url;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }
    }
}
