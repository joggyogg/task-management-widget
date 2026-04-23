using System;
using System.Windows;
using System.Windows.Input;
using TaskManagementWidget.Models;
using TaskStatus = TaskManagementWidget.Models.TaskStatus;

namespace TaskManagementWidget.Views
{
    public partial class AddTaskWindow : Window
    {
        public TaskItem?  Result        { get; private set; }
        public bool       DeletePressed { get; private set; }

        // Optional: pre-existing task for edit mode
        private readonly TaskItem? _editing;

        // ── Add mode ─────────────────────────────────────────────────────────────
        public AddTaskWindow()
        {
            InitializeComponent();
            NameBox.Focus();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Owner == null) return;

            // Position to the left of the owner's content column (owner col 0 is 36px tab)
            double desiredLeft = Owner.Left + 36 - this.ActualWidth - 8;
            double desiredTop  = Owner.Top + 64;

            // Clamp to screen working area
            var helper = new System.Windows.Interop.WindowInteropHelper(Owner);
            var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle);
            double scaleX = screen.Bounds.Width  / SystemParameters.VirtualScreenWidth  * SystemParameters.VirtualScreenWidth  / screen.Bounds.Width;
            var wa = screen.WorkingArea;

            // Convert working area (physical px) to DIPs using WPF's DPI
            var src = PresentationSource.FromVisual(Owner);
            double dpiScaleX = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiScaleY = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            double waLeftDip  = wa.Left   / dpiScaleX;
            double waTopDip   = wa.Top    / dpiScaleY;
            double waRightDip = wa.Right  / dpiScaleX;
            double waBotDip   = wa.Bottom / dpiScaleY;

            this.Left = Math.Max(waLeftDip, Math.Min(desiredLeft, waRightDip  - this.ActualWidth));
            this.Top  = Math.Max(waTopDip,  Math.Min(desiredTop,  waBotDip - this.ActualHeight));
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        // ── Edit mode ────────────────────────────────────────────────────────────
        public AddTaskWindow(TaskItem existing) : this()
        {
            _editing     = existing;
            Title        = "Edit Task";
            TitleText.Text = "Edit Task";
            SaveBtn.Content = "Save Changes";
            DeleteBtn.Visibility = Visibility.Visible;

            // Pre-fill fields
            NameBox.Text             = existing.Name;
            ImportanceSlider.Value   = existing.Importance;
            DescriptionBox.Text      = existing.Description ?? string.Empty;
            UrlBox.Text              = existing.Url          ?? string.Empty;

            // Pre-fill ticker
            if (existing.TickerPoints.HasValue)
            {
                TickerEnabledBox.IsChecked = true;   // fires TickerEnabledBox_Changed
                TickerPointsBox.Text = existing.TickerPoints.Value.ToString();
                TickerHoursBox.Text  = (existing.TickerHours ?? 1).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }

            // Pre-fill deadline
            if (existing.Deadline.HasValue)
            {
                DeadlineEnabledBox.IsChecked = true;  // fires DeadlineEnabledBox_Changed
                var local = existing.Deadline.Value.ToLocalTime();
                DeadlineDatePicker.SelectedDate = local.Date;
                DeadlineTimeBox.Text            = local.ToString("HH:mm");
            }

            SaveBtn.IsEnabled = true;
        }

        private void TickerEnabledBox_Changed(object sender, RoutedEventArgs e)
            => TickerPanel.Visibility = TickerEnabledBox.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;

        private void DeadlineEnabledBox_Changed(object sender, RoutedEventArgs e)
            => DeadlinePanel.Visibility = DeadlineEnabledBox.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;

        private void NameBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (SaveBtn != null)
                SaveBtn.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
        }

        private void ImportanceSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (ImportanceLabel != null)
                ImportanceLabel.Text = ((int)ImportanceSlider.Value).ToString();
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // ── Read optional ticker ────────────────────────────────────────────────────
            int?    tickerPts = null;
            double? tickerHrs = null;
            if (TickerEnabledBox.IsChecked == true)
            {
                if (int.TryParse(TickerPointsBox.Text.Trim(), out int pts) && pts > 0)
                    tickerPts = pts;
                if (double.TryParse(TickerHoursBox.Text.Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double hrs) && hrs > 0)
                    tickerHrs = hrs;
            }

            // ── Read optional deadline (local → UTC) ────────────────────────────────────
            DateTime? deadline = null;
            if (DeadlineEnabledBox.IsChecked == true && DeadlineDatePicker.SelectedDate.HasValue)
            {
                var      date  = DeadlineDatePicker.SelectedDate.Value.Date;
                TimeSpan time  = TimeSpan.Zero;
                var      parts = DeadlineTimeBox.Text.Trim().Split(':');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out int h) && h >= 0 && h < 24
                    && int.TryParse(parts[1], out int m) && m >= 0 && m < 60)
                    time = new TimeSpan(h, m, 0);
                deadline = DateTime.SpecifyKind(date + time, DateTimeKind.Local).ToUniversalTime();
            }

            if (_editing != null)
            {
                bool hadTicker        = _editing.TickerPoints.HasValue;
                _editing.Name         = NameBox.Text.Trim();
                _editing.Importance   = (int)ImportanceSlider.Value;
                _editing.Description  = string.IsNullOrWhiteSpace(DescriptionBox.Text)
                                            ? null : DescriptionBox.Text.Trim();
                _editing.Url          = string.IsNullOrWhiteSpace(UrlBox.Text)
                                            ? null : UrlBox.Text.Trim();
                _editing.TickerPoints = tickerPts;
                _editing.TickerHours  = tickerHrs;
                _editing.Deadline     = deadline;
                // Reset baseline when ticker newly enabled; clear it when disabled
                if (tickerPts.HasValue && !hadTicker)
                    _editing.TickerLastApplied = DateTime.UtcNow;
                else if (!tickerPts.HasValue)
                    _editing.TickerLastApplied = null;
                Result = _editing;
            }
            else
            {
                Result = new TaskItem
                {
                    Name              = NameBox.Text.Trim(),
                    Importance        = (int)ImportanceSlider.Value,
                    Description       = string.IsNullOrWhiteSpace(DescriptionBox.Text)
                                            ? null : DescriptionBox.Text.Trim(),
                    Url               = string.IsNullOrWhiteSpace(UrlBox.Text)
                                            ? null : UrlBox.Text.Trim(),
                    Status            = TaskStatus.ToDo,
                    TickerPoints      = tickerPts,
                    TickerHours       = tickerHrs,
                    TickerLastApplied = tickerPts.HasValue ? DateTime.UtcNow : (DateTime?)null,
                    Deadline          = deadline
                };
            }
            DialogResult = true;
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            DeletePressed = true;
            DialogResult  = true;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}

