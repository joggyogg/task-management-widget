using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
            // Load persisted tasks
            foreach (var t in TaskStorageService.Load())
            {
                Subscribe(t);
                AllTasks.Add(t);
            }

            // ── Doing view: status == Doing, sorted by Importance desc ────────────
            DoingView = (ListCollectionView)CollectionViewSource.GetDefaultView(AllTasks);
            DoingView = new ListCollectionView(AllTasks);
            DoingView.Filter = o => o is TaskItem t && t.Status == Models.TaskStatus.Doing;
            DoingView.SortDescriptions.Add(new SortDescription(nameof(TaskItem.Importance), ListSortDirection.Descending));

            // ── Todo view: status == ToDo, sorted by Importance desc then ManualOrder ─
            TodoView = new ListCollectionView(AllTasks);
            TodoView.Filter = o => o is TaskItem t && t.Status == Models.TaskStatus.ToDo;
            TodoView.SortDescriptions.Add(new SortDescription(nameof(TaskItem.Importance),  ListSortDirection.Descending));
            TodoView.SortDescriptions.Add(new SortDescription(nameof(TaskItem.ManualOrder), ListSortDirection.Ascending));

            // ── Done view: status == Completed ───────────────────────────────────
            DoneView = new ListCollectionView(AllTasks);
            DoneView.Filter = o => o is TaskItem t && t.Status == Models.TaskStatus.Completed;
            DoneView.SortDescriptions.Add(new SortDescription(nameof(TaskItem.Importance), ListSortDirection.Descending));

            AllTasks.CollectionChanged += OnCollectionChanged;
            RecalculateBadgeColors();
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

        // ── Reorder two ToDo tasks after a drag-drop ─────────────────────────────
        public void MoveToDoTask(TaskItem dragged, TaskItem target)
        {
            // Swap ManualOrder values and bump surrounding items so the dragged
            // item lands directly before the target.
            int targetOrder = target.ManualOrder;
            int draggedOrder = dragged.ManualOrder;

            if (draggedOrder < targetOrder)
            {
                // Moving down: shift items in between up
                foreach (var t in AllTasks)
                {
                    if (t == dragged) continue;
                    if (t.Status == Models.TaskStatus.ToDo
                        && t.ManualOrder > draggedOrder
                        && t.ManualOrder <= targetOrder)
                        t.ManualOrder--;
                }
            }
            else
            {
                // Moving up: shift items in between down
                foreach (var t in AllTasks)
                {
                    if (t == dragged) continue;
                    if (t.Status == Models.TaskStatus.ToDo
                        && t.ManualOrder >= targetOrder
                        && t.ManualOrder < draggedOrder)
                        t.ManualOrder++;
                }
            }

            dragged.ManualOrder = targetOrder;
            RefreshViews();
            Save();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private void Subscribe(TaskItem t)   => t.PropertyChanged += OnTaskPropertyChanged;
        private void Unsubscribe(TaskItem t) => t.PropertyChanged -= OnTaskPropertyChanged;

        private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
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

        public void Save() => TaskStorageService.Save(AllTasks);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
