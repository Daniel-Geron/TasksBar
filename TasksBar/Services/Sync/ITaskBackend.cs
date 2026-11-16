using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TasksBar.Services.Sync
{
    /// <summary>A task list on the sync service (a Google task list, or a CalDAV calendar that holds tasks).</summary>
    public class RemoteList
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }

    /// <summary>A task as TasksBar sees it, whichever service it lives on.</summary>
    public class RemoteTask
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool Completed { get; set; }

        /// <summary>Id of the parent task, for subtasks. Null for top-level tasks.</summary>
        public string? Parent { get; set; }

        /// <summary>Changes whenever the task changes on the server (Google's "updated", CalDAV's ETag).</summary>
        public string? Updated { get; set; }
    }

    /// <summary>The task or list no longer exists on the server (deleted or moved elsewhere).</summary>
    public class RemoteNotFoundException : Exception
    {
        public RemoteNotFoundException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>The server rejected the saved sign-in (expired token, revoked app password...).</summary>
    public class RemoteAuthException : Exception
    {
        public RemoteAuthException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Everything TasksBar needs from a sync service. Google Tasks and CalDAV (Tasks.org, Nextcloud...)
    /// both implement it, so the flyout and sticky note sync don't care which one is in use.
    /// Missing tasks/lists throw <see cref="RemoteNotFoundException"/>.
    /// </summary>
    public interface ITaskBackend
    {
        /// <summary>Shown to the user, e.g. "Google Tasks" or "Tasks.org".</summary>
        string DisplayName { get; }

        Task<List<RemoteList>> GetListsAsync();
        Task<RemoteList> CreateListAsync(string title);

        /// <summary>The list to show when none is selected yet (or the selected one is gone).</summary>
        Task<string?> GetDefaultListIdAsync();

        /// <param name="includeNotes">False lets the service skip the (possibly large) details when it can.</param>
        Task<List<RemoteTask>> GetTasksAsync(string listId, bool includeCompleted, bool includeNotes = true);
        Task<RemoteTask> GetTaskAsync(string listId, string taskId);

        /// <summary>Creates the task (as a subtask when <see cref="RemoteTask.Parent"/> is set) and returns it with its new id.</summary>
        Task<RemoteTask> CreateTaskAsync(string listId, RemoteTask task);

        /// <summary>Saves the title, notes and completion state, leaving anything else on the task untouched.</summary>
        Task<RemoteTask> UpdateTaskAsync(string listId, RemoteTask task);

        Task DeleteTaskAsync(string listId, string taskId);
        Task MoveTaskAsync(string fromListId, string taskId, string toListId);

        /// <summary>
        /// Saves a new position for <paramref name="movedTaskId"/> after the user dragged it.
        /// <paramref name="newOrder"/> is the list as it now appears (ids and parents), moved task included.
        /// </summary>
        Task ReorderTaskAsync(string listId, IReadOnlyList<RemoteTask> newOrder, string movedTaskId);
    }
}
