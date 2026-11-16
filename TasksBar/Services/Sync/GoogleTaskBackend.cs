using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Google.Apis.Tasks.v1;
using GTask = Google.Apis.Tasks.v1.Data.Task;

namespace TasksBar.Services.Sync
{
    /// <summary>Google Tasks through the official API client (sign-in handled by <see cref="GoogleAuthManager"/>).</summary>
    public class GoogleTaskBackend : ITaskBackend
    {
        private readonly TasksService _service;

        public GoogleTaskBackend(TasksService service) => _service = service;

        public string DisplayName => "Google Tasks";

        public async Task<List<RemoteList>> GetListsAsync()
        {
            var lists = new List<RemoteList>();
            var request = _service.Tasklists.List();
            request.MaxResults = 100;
            do
            {
                var page = await Run(() => request.ExecuteAsync());
                foreach (var list in page.Items ?? new List<Google.Apis.Tasks.v1.Data.TaskList>())
                    lists.Add(new RemoteList { Id = list.Id, Title = list.Title ?? "" });
                request.PageToken = page.NextPageToken;
            } while (!string.IsNullOrEmpty(request.PageToken));
            return lists;
        }

        public async Task<RemoteList> CreateListAsync(string title)
        {
            var created = await Run(() => _service.Tasklists.Insert(new Google.Apis.Tasks.v1.Data.TaskList { Title = title }).ExecuteAsync());
            return new RemoteList { Id = created.Id, Title = created.Title ?? title };
        }

        public Task<string?> GetDefaultListIdAsync() => Task.FromResult<string?>("@default");

        public async Task<List<RemoteTask>> GetTasksAsync(string listId, bool includeCompleted, bool includeNotes = true)
        {
            var tasks = new List<RemoteTask>();
            var request = _service.Tasks.List(listId);
            request.ShowHidden = false;
            request.ShowDeleted = false;
            request.ShowCompleted = includeCompleted;
            request.MaxResults = 100;
            if (!includeNotes) request.Fields = "nextPageToken,items(id,title,parent,updated,status,position)";
            var positions = new Dictionary<string, string>();
            do
            {
                var page = await Run(() => request.ExecuteAsync());
                foreach (var task in page.Items ?? new List<GTask>())
                {
                    var remote = ToRemote(task);
                    if (!includeCompleted && remote.Completed) continue;
                    tasks.Add(remote);
                    positions[task.Id] = task.Position ?? "";
                }
                request.PageToken = page.NextPageToken;
            } while (!string.IsNullOrEmpty(request.PageToken));

            // Same order as the Google Tasks app: by position, each task followed by its subtasks
            string Pos(RemoteTask t) => positions.TryGetValue(t.Id, out var p) ? p : "";
            var ids = new HashSet<string>(tasks.Select(t => t.Id));
            var children = tasks.Where(t => t.Parent != null && ids.Contains(t.Parent))
                                .GroupBy(t => t.Parent!)
                                .ToDictionary(g => g.Key, g => g.OrderBy(Pos, StringComparer.Ordinal).ToList());
            var ordered = new List<RemoteTask>();
            foreach (var top in tasks.Where(t => t.Parent == null || !ids.Contains(t.Parent)).OrderBy(Pos, StringComparer.Ordinal))
            {
                ordered.Add(top);
                if (children.TryGetValue(top.Id, out var kids)) ordered.AddRange(kids);
            }
            return ordered;
        }

        public async Task ReorderTaskAsync(string listId, IReadOnlyList<RemoteTask> newOrder, string movedTaskId)
        {
            int index = newOrder.ToList().FindIndex(t => t.Id == movedTaskId);
            if (index < 0) return;
            var moved = newOrder[index];

            // Google orders tasks among their siblings: it goes right after the closest sibling above it
            string? previous = null;
            for (int i = index - 1; i >= 0; i--)
            {
                if (newOrder[i].Parent == moved.Parent) { previous = newOrder[i].Id; break; }
            }

            var request = _service.Tasks.Move(listId, movedTaskId);
            if (!string.IsNullOrEmpty(moved.Parent)) request.Parent = moved.Parent;
            if (previous != null) request.Previous = previous;
            await Run(() => request.ExecuteAsync());
        }

        public async Task<RemoteTask> GetTaskAsync(string listId, string taskId) =>
            ToRemote(await Run(() => _service.Tasks.Get(listId, taskId).ExecuteAsync()));

        public async Task<RemoteTask> CreateTaskAsync(string listId, RemoteTask task)
        {
            var insert = _service.Tasks.Insert(ToGoogle(task), listId);
            if (!string.IsNullOrEmpty(task.Parent)) insert.Parent = task.Parent;
            return ToRemote(await Run(() => insert.ExecuteAsync()));
        }

        public async Task<RemoteTask> UpdateTaskAsync(string listId, RemoteTask task)
        {
            // Patch only touches the fields we send, so due dates etc. set in Google stay intact
            var body = ToGoogle(task);
            body.Id = task.Id;
            if (!task.Completed) body.Completed = null;
            return ToRemote(await Run(() => _service.Tasks.Patch(body, listId, task.Id).ExecuteAsync()));
        }

        public Task DeleteTaskAsync(string listId, string taskId) =>
            Run(() => _service.Tasks.Delete(listId, taskId).ExecuteAsync());

        public async Task MoveTaskAsync(string fromListId, string taskId, string toListId)
        {
            var request = _service.Tasks.Move(fromListId, taskId);
            request.DestinationTasklist = toListId;
            await Run(() => request.ExecuteAsync());
        }

        private static GTask ToGoogle(RemoteTask task) => new GTask
        {
            Title = task.Title,
            Notes = task.Notes,
            Status = task.Completed ? "completed" : "needsAction"
        };

        private static RemoteTask ToRemote(GTask task) => new RemoteTask
        {
            Id = task.Id,
            Title = task.Title ?? "",
            Notes = task.Notes ?? "",
            Completed = task.Status == "completed",
            Parent = string.IsNullOrEmpty(task.Parent) ? null : task.Parent,
            Updated = task.Updated
        };

        // Translates Google's errors into the shared ones the rest of the app understands
        private static async Task<T> Run<T>(Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound || ex.HttpStatusCode == HttpStatusCode.Gone)
            {
                throw new RemoteNotFoundException(ex.Message, ex);
            }
            catch (Exception ex) when (GoogleAuthManager.IsSignInExpired(ex))
            {
                throw new RemoteAuthException("Your Google sign-in has expired.", ex);
            }
        }

        private static Task Run(Func<Task<string>> call) => Run<string>(call);
    }
}
