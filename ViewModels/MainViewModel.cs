using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using TaskManagementWidget.Models;
using TaskManagementWidget.Services;

namespace TaskManagementWidget.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // ── All tasks (source of truth) ──────────────────────────────────────────
        public ObservableCollection<TaskItem> AllTasks { get; } = new();

        // ── Filtered / sorted views ──────────────────────────────────────────────
        public ListCollectionView DoingView { get; }
        public ListCollectionView TodoView  { get; }
        public ListCollectionView DoneView  { get; }

        // ── Completed section collapse state ─────────────────────────────────────
        private bool _isCompletedExpanded = true;
        public bool IsCompletedExpanded
        {
            get => _isCompletedExpanded;
            set { _isCompletedExpanded = value; OnPropertyChanged(); }
        }

        // ── Constructor ──────────────────────────────────────────────────────────
        public MainViewModel()
        {
            // Load persisted tasks, re-sorted by importance so startup order is always clean
            var loaded = TaskStorageService.Load();

            // Re-assign ManualOrder by importance (desc) per status group
            var groups = new[] { Models.TaskStatus.Doing, Models.TaskStatus.ToDo, Models.TaskStatus.Completed };
            foreach (var status in groups)
            {
                int order = 0;
                foreach (var t in loaded.Where(x => x.Status == status).OrderByDescending(x => x.Importance))
                    t.ManualOrder = order++;
            }

            foreach (var t in loaded)
            {
                Subscribe(t);
                AllTasks.Add(t);
            }

            // ── Doing view: status == Doing, sorted by manual drag order ────────────
            DoingView = (ListCollectionView)CollectionViewSource.GetDefaultView(AllTasks);
            DoingView = new ListCollectionView(AllTasks);
            DoingView.Filter = o => o is TaskItem t && t.Status == Models.TaskStatus.Doing;
            DoingView.SortDescriptions.Add(new SortDescription(nameof(TaskItem.ManualOrder), ListSortDirection.Ascending));

            // ── Todo view: status == ToDo, sorted by manual drag order ─────────────────
            TodoView = new ListCollectionView(AllTasks);
            TodoView.Filter = o => o is TaskItem t && t.Status == Models.TaskStatus.ToDo;
            TodoView.SortDescriptions.Add(new SortDescription(nameof(TaskItem.ManualOrder), ListSortDirection.Ascending));

            // ── Done view: status == Completed ───────────────────────────────────
            DoneView = new ListCollectionView(AllTasks);
            DoneView.Filter = o => o is TaskItem t && t.Status == Models.TaskStatus.Completed;
            DoneView.SortDescriptions.Add(new SortDescription(nameof(TaskItem.Importance), ListSortDirection.Descending));

            AllTasks.CollectionChanged += OnCollectionChanged;
            RecalculateBadgeColors();

            // Apply any missed importance ticks from when the app was closed
            ApplyTickers();

            // Keep ticking every minute while the app is open
            var tickerTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMinutes(1) };
            tickerTimer.Tick += (_, _) => ApplyTickers();
            tickerTimer.Start();
        }

        // ── Add a new task ───────────────────────────────────────────────────────
        public void AddTask(TaskItem task)
        {
            task.ManualOrder = AllTasks.Count;
            if (task.CreatedAt == default) task.CreatedAt = DateTime.UtcNow;
            Subscribe(task);
            AllTasks.Add(task);
            RefreshViews();
            Save();
        }

        // ── Cycle status on a task ───────────────────────────────────────────────
        public void CycleStatus(TaskItem task)
        {
            task.Status = task.Status switch
            {
                Models.TaskStatus.ToDo      => Models.TaskStatus.Doing,
                Models.TaskStatus.Doing     => Models.TaskStatus.Completed,
                Models.TaskStatus.Completed => Models.TaskStatus.ToDo,
                _                           => Models.TaskStatus.ToDo
            };
            RefreshViews();
            Save();
        }

        // ── Delete a task ────────────────────────────────────────────────────────
        public void DeleteTask(TaskItem task)
        {
            Unsubscribe(task);
            AllTasks.Remove(task);
            Save();
        }

        // ── Reorder a task within its list, inheriting the importance of the item below ─
        public void ReorderTask(TaskItem dragged, TaskItem? insertBefore)
        {
            if (dragged == insertBefore) return;

            // Snapshot the current order of the same-status list
            var sameList = AllTasks
                .Where(t => t.Status == dragged.Status)
                .OrderBy(t => t.ManualOrder)
                .ToList();

            _suppressNotifications = true;
            try
            {
                // Inherit importance from the task that will sit below the dragged item
                if (insertBefore != null)
                    dragged.Importance = insertBefore.Importance;
                else
                {
                    var last = sameList.LastOrDefault(t => t != dragged);
                    if (last != null) dragged.Importance = last.Importance;
                }

                // Rebuild ManualOrder with dragged placed at its new position
                sameList.Remove(dragged);
                int idx = insertBefore != null ? sameList.IndexOf(insertBefore) : sameList.Count;
                if (idx < 0) idx = sameList.Count;
                sameList.Insert(idx, dragged);
                for (int i = 0; i < sameList.Count; i++)
                    sameList[i].ManualOrder = i;
            }
            finally
            {
                _suppressNotifications = false;
            }

            RefreshViews();
            Save();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        // Suppressed during batch reorder to prevent intermediate refreshes
        private bool _suppressNotifications;

        private void Subscribe(TaskItem t)   => t.PropertyChanged += OnTaskPropertyChanged;
        private void Unsubscribe(TaskItem t) => t.PropertyChanged -= OnTaskPropertyChanged;

        private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressNotifications) return;
            if (e.PropertyName == nameof(TaskItem.BadgeT)) return;
            RefreshViews();
            Save();
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Save();

        private void RecalculateBadgeColors()
        {
            int maxTodo = 0;
            foreach (var t in AllTasks)
                if (t.Status == Models.TaskStatus.ToDo && t.Importance > maxTodo)
                    maxTodo = t.Importance;

            foreach (var t in AllTasks)
                t.BadgeT = t.Status switch
                {
                    Models.TaskStatus.ToDo      => (t.Importance - 1) / 99.0,
                    Models.TaskStatus.Doing     => Math.Clamp((maxTodo - t.Importance) / 99.0, 0.0, 1.0),
                    Models.TaskStatus.Completed => -1.0,
                    _                           => 0.0
                };
        }

        private void RefreshViews()
        {
            RecalculateBadgeColors();
            DoingView.Refresh();
            TodoView.Refresh();
            DoneView.Refresh();
            OnPropertyChanged(nameof(HasDoingTasks));
            OnPropertyChanged(nameof(HasTodoTasks));
            OnPropertyChanged(nameof(HasDoneTasks));
        }

        public bool HasDoingTasks => DoingView.Count > 0;
        public bool HasTodoTasks  => TodoView.Count  > 0;
        public bool HasDoneTasks  => DoneView.Count  > 0;

        // ── Importance ticker ────────────────────────────────────────────────────
        private void ApplyTickers()
        {
            bool changed = false;
            var  now     = DateTime.UtcNow;

            foreach (var t in AllTasks)
            {
                if (t.TickerPoints == null || t.TickerPoints.Value <= 0) continue;
                if (t.TickerHours  == null || t.TickerHours.Value  <= 0) continue;
                if (t.Status == Models.TaskStatus.Completed)             continue;
                if (t.Importance >= 100)                                 continue;

                var baseline   = t.TickerLastApplied ?? (t.CreatedAt == default ? now : t.CreatedAt);
                double elapsed = (now - baseline).TotalHours;
                int    ticks   = (int)Math.Floor(elapsed / t.TickerHours.Value);
                if (ticks <= 0) continue;

                t.Importance        = Math.Min(100, t.Importance + ticks * t.TickerPoints.Value);
                t.TickerLastApplied = baseline.AddHours(ticks * t.TickerHours.Value);
                changed = true;
            }

            if (changed) { RefreshViews(); Save(); }
        }

        public void Save()
        {
            TaskStorageService.Save(AllTasks);
            if (TaskStorageService.LastSaveError is { } err)
                System.Windows.MessageBox.Show(
                    $"Tasks could not be saved:\n\n{err}",
                    "Save Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
