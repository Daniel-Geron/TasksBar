using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace TasksBar.Services
{
    public class StreamDeckServer
    {
        public static StreamDeckServer Instance { get; private set; }
        private readonly HttpListener _listener;
        private bool _isRunning;

        public StreamDeckServer()
        {
            Instance = this;
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://localhost:37542/api/");
        }

        public void Start()
        {
            _listener.Start();
            _isRunning = true;
            Task.Run(ListenAsync);
        }

        public void Stop()
        {
            _isRunning = false;
            _listener.Stop();
        }

        private async Task ListenAsync()
        {
            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequest(context));
                }
                catch { /* Ignore when stopping */ }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // IMPORTANT: CORS headers allow the Stream Deck UI to fetch data from this server
            response.AppendHeader("Access-Control-Allow-Origin", "*");
            response.AppendHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AppendHeader("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            string path = request.Url.AbsolutePath.ToLower();
            string requestBody = "";
            if (request.HasEntityBody)
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                requestBody = reader.ReadToEnd();
            }

            // --- NEW DYNAMIC GET ENDPOINTS ---

            if (path == "/api/get-lists")
            {
                var lists = new System.Collections.Generic.List<object>();
                if (AppConfig.Settings.EnableGoogleSync)
                {
                    var service = GoogleAuthManager.GetTasksService();
                    if (service != null)
                    {
                        try
                        {
                            // Fetch directly on background thread
                            var gLists = service.Tasklists.List().Execute();
                            foreach (var lst in gLists.Items)
                            {
                                lists.Add(new { id = lst.Id, title = lst.Title });
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    foreach (var lst in LocalDataManager.GetLocalLists())
                    {
                        lists.Add(new { id = lst.Id, title = lst.Title });
                    }
                }

                var json = JsonSerializer.Serialize(lists);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.StatusCode = 200;
                response.Close();
                return;
            }

            if (path == "/api/get-notes")
            {
                var json = JsonSerializer.Serialize(LocalDataManager.ActiveNotes);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.StatusCode = 200;
                response.Close();
                return;
            }

            // --- ORIGINAL POST ENDPOINTS ---

            Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = Application.Current.Windows.OfType<TasksFlyoutWindow>().FirstOrDefault();
                if (path == "/api/add-task")
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(requestBody);
                    string title = data.GetProperty("title").GetString();
                    string details = data.TryGetProperty("details", out var d) ? d.GetString() : "";

                    var taskItem = new TaskItem { Title = title, Details = details, IsCompleted = false };
                    mainWindow?.MyTasks.Insert(0, taskItem);

                    if (!AppConfig.Settings.EnableGoogleSync)
                        LocalDataManager.SaveTasks(mainWindow.MyTasks, AppConfig.Settings.SelectedListId);
                }
                else if (path == "/api/new-note")
                {
                    new StickyNoteWindow(mainWindow?.Left - 260 ?? 100, mainWindow?.Top ?? 100).Show();
                }
                else if (path == "/api/switch-list")
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(requestBody);
                    AppConfig.Settings.SelectedListId = data.GetProperty("listId").GetString();
                    AppConfig.Save();

                    if (mainWindow != null)
                    {
                        if (AppConfig.Settings.EnableGoogleSync)
                            _ = mainWindow.SyncTasksFromGoogle();
                        else
                            mainWindow.LoadLocalTasks();

                        // REMOVE mainWindow.Show() and mainWindow.Activate()
                        // REPLACE WITH THIS:
                        mainWindow.OpenFlyoutFromTray();
                    }
                }
                else if (path == "/api/toggle-note")
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(requestBody);
                    string noteId = data.GetProperty("id").GetString();

                    var existingNote = Application.Current.Windows.OfType<StickyNoteWindow>()
                        .FirstOrDefault(w => w.GetModel().Id == noteId);

                    if (existingNote != null)
                    {
                        if (existingNote.IsVisible) existingNote.Hide();
                        else { existingNote.Show(); existingNote.Focus(); }
                    }
                    else
                    {
                        var noteModel = LocalDataManager.ActiveNotes.FirstOrDefault(n => n.Id == noteId);
                        if (noteModel != null) new StickyNoteWindow(noteModel).Show();
                    }
                }
            });

            response.StatusCode = 200;
            response.Close();
        }
    }
}