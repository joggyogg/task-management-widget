using System.Windows;
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

        // ── Edit mode ────────────────────────────────────────────────────────────
        public AddTaskWindow(TaskItem existing) : this()
        {
            _editing     = existing;
            Title        = "Edit Task";
            SaveBtn.Content = "Save Changes";
            DeleteBtn.Visibility = Visibility.Visible;

            // Pre-fill fields
            NameBox.Text             = existing.Name;
            ImportanceSlider.Value   = existing.Importance;
            DescriptionBox.Text      = existing.Description ?? string.Empty;
            UrlBox.Text              = existing.Url          ?? string.Empty;

            SaveBtn.IsEnabled = true;
        }

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
            if (_editing != null)
            {
                // Apply edits in-place so the existing object in the collection updates
                _editing.Name        = NameBox.Text.Trim();
                _editing.Importance  = (int)ImportanceSlider.Value;
                _editing.Description = string.IsNullOrWhiteSpace(DescriptionBox.Text)
                                           ? null : DescriptionBox.Text.Trim();
                _editing.Url         = string.IsNullOrWhiteSpace(UrlBox.Text)
                                           ? null : UrlBox.Text.Trim();
                Result = _editing;
            }
            else
            {
                Result = new TaskItem
                {
                    Name        = NameBox.Text.Trim(),
                    Importance  = (int)ImportanceSlider.Value,
                    Description = string.IsNullOrWhiteSpace(DescriptionBox.Text)
                                      ? null : DescriptionBox.Text.Trim(),
                    Url         = string.IsNullOrWhiteSpace(UrlBox.Text)
                                      ? null : UrlBox.Text.Trim(),
                    Status      = TaskStatus.ToDo
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

