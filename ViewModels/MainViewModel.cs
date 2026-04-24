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
        public ListCollectionView DoingView   { get; }
        public ListCollectionView OverdueView { get; }
        public ListCollectionView TodoView    { get; }
        public ListCollectionView DoneView    { get; }

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

            // ── Doing view: status == Doing AND not overdue, sorted by manual drag order ─
            DoingView = (ListCollectionView)CollectionViewSource.GetDefaultView(AllTasks);
            DoingView = new ListCollectionView(AllTasks);
            DoingView.Filter = o => o is TaskItem t && t.Status == Models.TaskStatus.Doing
                                    && !IsOverdue(t);
            DoingView.SortDescriptions.Add(new SortDescription(nameof(TaskItem.ManualOrder), ListSortDirection.Ascending));

            // ── Overdue view: non-completed tasks with a past deadline ────────────────
            OverdueView = new ListCollectionView(AllTasks);
            OverdueView.Filter = o => o is TaskItem t && IsOverdue(t);
            OverdueView.SortDescriptions.Add(new SortDescription(nameof(TaskItem.Importance), ListSortDirection.Descending));

            // ── Todo view: status == ToDo AND not overdue, sorted by manual drag order ──
            TodoView = new ListCollectionView(AllTasks);
            TodoView.Filter = o => o is TaskItem t && t.Status == Models.TaskStatus.ToDo
                                   && !IsOverdue(t);
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
            tickerTimer.Tick += (_, _) => { ApplyTickers(); RefreshViews(); };
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

        // ── Set status directly ──────────────────────────────────────────────────
        public void SetStatus(TaskItem task, Models.TaskStatus status)
        {
            if (task.Status == status) return;
            task.Status = status;
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
        // Soft delete: stamp a tombstone so the deletion propagates via Drive sync,
        // then drop the task from the live collection. The tombstone record stays
        // in tasks.json until the TTL expires (see TaskStorageService).
        public void DeleteTask(TaskItem task)
        {
            task.DeletedAt = DateTime.UtcNow;
            task.UpdatedAt = task.DeletedAt.Value;
            Unsubscribe(task);
            AllTasks.Remove(task);
            // OnCollectionChanged saves AllTasks (live items only); we also need the tombstone
            // to land on disk, so re-save the union of live + tombstones explicitly.
            SaveWithTombstones(task);
        }

        // Includes a soft-deleted task plus all live tasks, then persists.
        private void SaveWithTombstones(params TaskItem[] tombstones)
        {
            var snapshot = new System.Collections.Generic.List<TaskItem>(AllTasks);
            // Pull in any prior tombstones from disk so we don't lose them on save.
            foreach (var prior in TaskStorageService.LoadAll())
                if (prior.DeletedAt != null && !snapshot.Any(s => s.Id == prior.Id))
                    snapshot.Add(prior);
            foreach (var t in tombstones)
                if (!snapshot.Contains(t)) snapshot.Add(t);
            TaskStorageService.Save(snapshot);
            if (TaskStorageService.LastSaveError is { } err)
                System.Windows.MessageBox.Show($"Tasks could not be saved:\n\n{err}",
                    "Save Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            Services.SyncCoordinator.Instance?.RequestSync();
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
            // Don't save to disk while a preview/edit is active — only commit on explicit Save
            if (sender is TaskItem t && !t.IsPreview)
                Save();
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Don't persist while a preview/edit is mid-flight — the preview ghost is in the
            // collection but should not hit disk or trigger sync until commit.
            if (HasActivePreview()) return;
            Save();
        }

        private bool HasActivePreview()
        {
            foreach (var t in AllTasks)
                if (t.IsPreview) return true;
            return false;
        }

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
            OverdueView.Refresh();
            TodoView.Refresh();
            DoneView.Refresh();
            OnPropertyChanged(nameof(HasDoingTasks));
            OnPropertyChanged(nameof(HasOverdueTasks));
            OnPropertyChanged(nameof(HasTodoTasks));
            OnPropertyChanged(nameof(HasDoneTasks));
        }

        public bool HasDoingTasks   => DoingView.Count   > 0;
        public bool HasOverdueTasks => OverdueView.Count > 0;
        public bool HasTodoTasks    => TodoView.Count    > 0;
        public bool HasDoneTasks    => DoneView.Count    > 0;

        // ── Overdue helper ───────────────────────────────────────────────────────
        private static bool IsOverdue(TaskItem t)
            => t.Deadline.HasValue
               && t.Deadline.Value < DateTime.UtcNow
               && t.Status != Models.TaskStatus.Completed;

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
            // Preserve any tombstones (deleted-task records) from disk so that a regular Save()
            // doesn't wipe them out — they need to stick around until they sync to other devices.
            var snapshot = new System.Collections.Generic.List<TaskItem>(AllTasks);
            foreach (var prior in TaskStorageService.LoadAll())
                if (prior.DeletedAt != null && !snapshot.Any(s => s.Id == prior.Id))
                    snapshot.Add(prior);

            TaskStorageService.Save(snapshot);
            if (TaskStorageService.LastSaveError is { } err)
                System.Windows.MessageBox.Show(
                    $"Tasks could not be saved:\n\n{err}",
                    "Save Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            Services.SyncCoordinator.Instance?.RequestSync();
        }

        /// <summary>Replace the entire live task collection with a merged set (e.g. after a sync
        /// pull). Tombstones in <paramref name="merged"/> are persisted but not shown in the UI.
        /// Caller is responsible for already having merged remote+local data.</summary>
        public void ReplaceAll(System.Collections.Generic.IEnumerable<TaskItem> merged)
        {
            var list = merged.ToList();

            // Preserve any in-flight preview/edit ghosts so the user's add/edit doesn't vanish
            // mid-action. Their committed counterparts (same Id) take precedence — only ghosts
            // not represented in the merged set are kept.
            var previews = AllTasks.Where(t => t.IsPreview).ToList();

            // Persist everything (live + tombstones) without re-triggering RequestSync —
            // that's the caller's job (sync engine).
            TaskStorageService.Save(list);

            _suppressNotifications = true;
            try
            {
                foreach (var t in AllTasks.ToList()) Unsubscribe(t);
                AllTasks.Clear();
                foreach (var t in list.Where(x => x.DeletedAt == null))
                {
                    Subscribe(t);
                    AllTasks.Add(t);
                }
                // Re-attach any preview ghosts that weren't already represented in `merged`.
                foreach (var p in previews)
                {
                    if (list.Any(x => x.Id == p.Id)) continue;
                    Subscribe(p);
                    AllTasks.Add(p);
                }
            }
            finally
            {
                _suppressNotifications = false;
            }

            RefreshViews();
        }

        // ── Live preview management ───────────────────────────────────────────────

        /// <summary>Add a ghost task to the Todo list for add-mode live preview.
        /// Switches TodoView sort to Importance so the card moves with the slider.</summary>
        public void AddPreviewTask(TaskItem task)
        {
            task.IsPreview   = true;
            task.Status      = Models.TaskStatus.ToDo;
            task.CreatedAt   = DateTime.UtcNow;
            task.ManualOrder = AllTasks.Count;
            Subscribe(task);
            AllTasks.Add(task);
            SetPreviewSort(true);
            RefreshViews();
        }

        /// <summary>Remove the ghost task on cancel (add mode).</summary>
        public void RemovePreviewTask(TaskItem task)
        {
            Unsubscribe(task);
            AllTasks.Remove(task);
            SetPreviewSort(false);
            RefreshViews();
        }

        /// <summary>Commit the ghost task: strip IsPreview, assign ManualOrder by importance,
        /// restore sort, and save to disk.</summary>
        public void CommitPreviewTask(TaskItem task)
        {
            task.IsPreview = false;
            task.CreatedAt = DateTime.UtcNow;
            AssignManualOrderByImportance(task);
            SetPreviewSort(false);
            RefreshViews();
            Save();
        }

        /// <summary>Mark an existing task as preview (edit mode) and switch views to importance sort.</summary>
        public void BeginEditPreview(TaskItem task)
        {
            task.IsPreview = true;
            SetPreviewSort(true);
            RefreshViews();
        }

        /// <summary>End edit preview. If save=false, caller has already restored snapshot values.
        /// Assigns ManualOrder, restores sort, and optionally saves.</summary>
        public void EndEditPreview(TaskItem task, bool save)
        {
            task.IsPreview = false;
            AssignManualOrderByImportance(task);
            SetPreviewSort(false);
            RefreshViews();
            if (save) Save();
        }

        private void SetPreviewSort(bool previewActive)
        {
            var sort = previewActive
                ? new SortDescription(nameof(TaskItem.Importance), ListSortDirection.Descending)
                : new SortDescription(nameof(TaskItem.ManualOrder), ListSortDirection.Ascending);

            TodoView.SortDescriptions.Clear();
            TodoView.SortDescriptions.Add(sort);
            DoingView.SortDescriptions.Clear();
            DoingView.SortDescriptions.Add(sort);
        }

        private void AssignManualOrderByImportance(TaskItem task)
        {
            var peers = AllTasks
                .Where(t => t.Status == task.Status && !t.IsPreview && t != task)
                .OrderByDescending(t => t.Importance)
                .ToList();

            int insertIdx = peers.FindIndex(t => t.Importance < task.Importance);
            if (insertIdx < 0) insertIdx = peers.Count;
            peers.Insert(insertIdx, task);
            for (int i = 0; i < peers.Count; i++)
                peers[i].ManualOrder = i;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
