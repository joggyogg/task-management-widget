using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TaskManagementWidget.Models
{
    public enum TaskStatus { ToDo, Doing, Completed }

    public class TaskItem : INotifyPropertyChanged
    {
        private string     _name        = string.Empty;
        private int        _importance  = 50;
        private string?    _description;
        private string?    _url;
        private TaskStatus _status      = TaskStatus.ToDo;
        private int        _manualOrder;

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); Touch(); }
        }

        public int Importance
        {
            get => _importance;
            set { _importance = value; OnPropertyChanged(); Touch(); }
        }

        public string? Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); Touch(); }
        }

        public string? Url
        {
            get => _url;
            set { _url = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUrl)); Touch(); }
        }

        [JsonIgnore]
        public bool HasUrl => !string.IsNullOrWhiteSpace(_url);

        private double _badgeT;
        [JsonIgnore]
        public double BadgeT
        {
            get => _badgeT;
            set { if (Math.Abs(_badgeT - value) > 0.001) { _badgeT = value; OnPropertyChanged(); } }
        }

        public TaskStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusLabel)); Touch(); }
        }

        [JsonIgnore]
        public string StatusLabel => _status switch
        {
            TaskStatus.Doing     => "Doing",
            TaskStatus.Completed => "Done",
            _                    => "To Do"
        };

        public int ManualOrder
        {
            get => _manualOrder;
            set { _manualOrder = value; OnPropertyChanged(); Touch(); }
        }

        private DateTime _createdAt;
        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }

        // ── Importance ticker ────────────────────────────────────────────────────
        private int?    _tickerPoints;
        private double? _tickerHours;

        /// <summary>Points added per tick.</summary>
        public int? TickerPoints
        {
            get => _tickerPoints;
            set { _tickerPoints = value; OnPropertyChanged(); Touch(); }
        }

        /// <summary>Hours between each tick.</summary>
        public double? TickerHours
        {
            get => _tickerHours;
            set { _tickerHours = value; OnPropertyChanged(); Touch(); }
        }

        /// <summary>UTC time of the last applied tick (bookkeeping — not displayed).</summary>
        public DateTime? TickerLastApplied { get; set; }

        // ── Deadline ─────────────────────────────────────────────────────────────
        private DateTime? _deadline;

        /// <summary>Optional UTC deadline. Shows countdown on card instead of age.</summary>
        public DateTime? Deadline
        {
            get => _deadline;
            set { _deadline = value; OnPropertyChanged(); Touch(); }
        }

        // ── Sync metadata ────────────────────────────────────────────────────────
        /// <summary>UTC timestamp of the most recent meaningful change. Used by the merge engine
        /// to resolve cross-device conflicts (last write wins).</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>If non-null, this task has been deleted on this device. The tombstone
        /// propagates via sync so other devices apply the deletion. Cleaned up after 30 days.</summary>
        public DateTime? DeletedAt { get; set; }

        /// <summary>Internal helper — bumps UpdatedAt during a setter, but never while a preview
        /// edit is in progress (those bumps happen at commit time).</summary>
        private void Touch()
        {
            if (_isPreview) return;
            UpdatedAt = DateTime.UtcNow;
        }

        private bool _isPreview;
        /// <summary>True while the task is a live-preview ghost (not yet committed / being edited).
        /// Never persisted to disk.</summary>
        [JsonIgnore]
        public bool IsPreview
        {
            get => _isPreview;
            set { _isPreview = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
