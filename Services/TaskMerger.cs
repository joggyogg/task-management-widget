using System;
using System.Collections.Generic;
using System.Linq;
using TaskManagementWidget.Models;

namespace TaskManagementWidget.Services
{
    /// <summary>Pure last-write-wins merge of two task lists by Id.</summary>
    public static class TaskMerger
    {
        public static List<TaskItem> Merge(IEnumerable<TaskItem> local, IEnumerable<TaskItem> remote)
        {
            var byId = new Dictionary<Guid, TaskItem>();

            foreach (var t in local)
                byId[t.Id] = t;

            foreach (var r in remote)
            {
                if (!byId.TryGetValue(r.Id, out var existing))
                {
                    byId[r.Id] = r;
                    continue;
                }

                // Effective timestamp = max(UpdatedAt, DeletedAt). DeletedAt wins ties because
                // a tombstone is a terminal state.
                var existingT = EffectiveStamp(existing);
                var remoteT   = EffectiveStamp(r);

                if (remoteT > existingT)
                    byId[r.Id] = r;
                else if (remoteT == existingT && r.DeletedAt != null && existing.DeletedAt == null)
                    byId[r.Id] = r;
            }

            return byId.Values.ToList();
        }

        private static DateTime EffectiveStamp(TaskItem t)
        {
            if (t.DeletedAt is { } d && d > t.UpdatedAt) return d;
            return t.UpdatedAt;
        }
    }
}
