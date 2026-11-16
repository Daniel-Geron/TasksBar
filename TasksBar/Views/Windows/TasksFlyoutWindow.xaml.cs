using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks; // Explicitly using this for background Tasks
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui;
using Wpf.Ui.Controls;
using System.Diagnostics;

namespace TasksBar
{

    public partial class TasksFlyoutWindow : FluentWindow
    {

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize, int maximumWorkingSetSize);
        public ObservableCollection<TaskItem> MyTasks { get; set; }
        private WidgetPosition _currentPosition = WidgetPosition.BottomRight;
        private bool _isOpeningSettings = false;
        private SettingsWindow _settingsWindow;
        private bool _isCustomDragged = false;
        private double _customLeft = 0;
        private double _customTop = 0;
        // The connected sync service (Google Tasks or CalDAV/Tasks.org), null when tasks are local only
        private Services.Sync.ITaskBackend? _backend => Services.Sync.TaskSync.Current;
        private System.Windows.Forms.NotifyIcon _trayIcon;
        public TasksFlyoutWindow()
        {
            AppConfig.Load();
            Services.StickyNoteSync.Host = new Services.WpfStickyNoteHost();

            // 1. Create the window in memory FIRST
            InitializeComponent();
            SetupTrayIcon();

            // 2. Now that the window exists, apply the theme to it!
            if (AppConfig.Settings.AppTheme == 1)
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            else if (AppConfig.Settings.AppTheme == 2)
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            else
            {
                // Only use the System default and the System Watcher if they chose option 0
                Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
                Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
            }

            // Always apply the Windows Accent Color
            Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent();

            // Apply loaded settings to the window
            this.WindowBackdropType = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;
            this.Topmost = AppConfig.Settings.StayOnTop;
            _currentPosition = AppConfig.Settings.Position;

            // Force the Pin icon to match the loaded setting immediately!
            UpdatePinIcon();

          

            MyTasks = new ObservableCollection<TaskItem>();
            TasksListView.ItemsSource = MyTasks;

            // Drag tasks to reorder them. New (unsaved) and completed tasks stay put.
            _reorder = new TaskReorder(TasksListView, DragLayer, MyTasks,
                task => !task.IsCompleted && !string.IsNullOrWhiteSpace(task.Title) && (task.Id != null || !Services.Sync.TaskSync.IsEnabled),
                OnTaskReordered);
            TasksListView.PreviewKeyDown += TasksListView_PreviewKeyDown;
            _reorder.DragEnded += () =>
            {
                // A refresh that arrived mid-drag was held back so the list didn't change under the pointer
                if (_refreshAfterDrag)
                {
                    _refreshAfterDrag = false;
                    _ = SyncTasksFromRemote();
                }
            };

            this.Loaded += async (s, e) =>
            {
                PlaySlideAnimation();

                LocalDataManager.LoadNotes();
                Services.WpfInk.ConvertLegacyNotes();
                Services.NoteImageStore.RemoveUnused(LocalDataManager.ActiveNotes);
                foreach (var note in LocalDataManager.ActiveNotes.Where(n => n.OpenOnStartup))
                {
                    new StickyNoteWindow(note).Show();
                }

                Services.HotkeyManager.Initialize();

                if (Services.Sync.TaskSync.IsEnabled)
                {
                    await InitializeSyncAsync();
                }
                else
                {
                    // Load tasks from JSON if sync is off
                    LoadLocalTasks();
                }
            };

            this.StateChanged += Window_StateChanged;
            this.Deactivated += Window_Deactivated;
            this.Activated += Window_Activated;
        }
        public void LoadLocalTasks()
        {
            // Pass the SelectedListId so it knows which JSON file to pull from!
            var localTasks = LocalDataManager.LoadTasks(AppConfig.Settings.SelectedListId);
            MyTasks.Clear();

            foreach (var task in localTasks)
            {
                task.PropertyChanged += OnTaskPropertyChanged;
                MyTasks.Add(task);
            }

            _ = PopulateTrayMenu();
        }
        private void FlushMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
            }
            catch { }
        }
        private void HideAndFlushMemory()
        {
            this.Hide(); // Hide the window
            this.Topmost = AppConfig.Settings.StayOnTop;

            Services.TaskSoundPlayer.Release();
            FlushMemory();
        }
        private async void OnTaskPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 1. Check if the sender is actually a TaskItem
            if (sender is TaskItem taskItem)
            {
                // 2. Pass it to your existing logic
                await TaskItem_PropertyChanged(taskItem, e.PropertyName);
            }
        }
        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();
            _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _trayIcon.Text = "TasksBar";
            _trayIcon.Visible = true;

            // Add a default menu instantly so right-click is never broken
            _trayMenu = new System.Windows.Controls.ContextMenu();
            _trayMenu.Items.Add(CreateExitTrayItem());

            // Handle Left-Click to toggle the window, Right-Click for the themed list menu
            _trayIcon.MouseClick += async (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    ShowTrayMenu();
                }
                else if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    if (this.IsVisible && this.Opacity > 0)
                    {
                        PlaySlideOutAnimation();
                    }
                    else
                    {
                        this.Show();
                        this.Activate();
                        this.Focus();
                        PlaySlideAnimation();
                    }
                }
            };
        }

        // The tray menu is a WPF ContextMenu (instead of a WinForms one) so it uses the same
        // WPF-UI styling and light/dark theme as the rest of the app
        private System.Windows.Controls.ContextMenu _trayMenu;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void ShowTrayMenu()
        {
            if (_trayMenu.IsOpen) _trayMenu.IsOpen = false;

            _trayMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            _trayMenu.IsOpen = true;

            // The menu only closes on outside clicks if it owns the foreground, like a native tray menu
            if (PresentationSource.FromVisual(_trayMenu) is System.Windows.Interop.HwndSource source)
            {
                SetForegroundWindow(source.Handle);
            }
            _trayMenu.Focus();
        }

        // Every list gets a checkmark icon (invisible unless selected) so the titles stay aligned
        private System.Windows.Controls.MenuItem CreateTrayListItem(string title, bool isSelected)
        {
            return new System.Windows.Controls.MenuItem
            {
                Header = title,
                Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark16) { Opacity = isSelected ? 1 : 0 },
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal
            };
        }

        private System.Windows.Controls.MenuItem CreateExitTrayItem()
        {
            var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit TasksBar" };
            exitItem.Click += (s, e) => Application.Current.Shutdown();
            return exitItem;
        }

        // Ensure the icon cleans itself up when the app completely closes
        protected override void OnClosed(EventArgs e)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            base.OnClosed(e);
        }
        // --- TASK SYNC (Google Tasks or CalDAV / Tasks.org, see Services/Sync) ---

        private async Task InitializeSyncAsync()
        {
            try
            {
                if (Services.Sync.TaskSync.Provider == Services.Sync.TaskSync.Google)
                {
                    // Google needs the app's own API credentials next to the exe
                    string credsPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "credentials.json");
                    if (!System.IO.File.Exists(credsPath))
                    {
                        System.Windows.MessageBox.Show(
                            "Welcome to TasksBar!\n\nTo connect to Google, you need to provide your own API credentials.\n\nPlease place your 'credentials.json' file in the same folder as this app and try again.",
                            "Missing Credentials",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);

                        return;
                    }
                }

                // Signs in to Google (browser only if needed), or sets up the saved CalDAV account
                await Services.Sync.TaskSync.ConnectAsync();

                await SyncTasksFromRemote();
                await PopulateTrayMenu();
                await Services.StickyNoteSync.SyncAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to connect to {Services.Sync.TaskSync.ProviderName}: {ex.Message}");
            }
        }
        private bool _isSyncingTasks = false;
        private bool _syncRequested = false;
        private bool _isHandlingExpiredSignIn = false;

        // "@default" is Google's name for the main list. Other services have no such alias, so it's
        // swapped for their first list (and remembered) the first time tasks are loaded.
        private async Task<string?> ResolveSelectedListAsync()
        {
            if (_backend == null) return null;
            string listId = AppConfig.Settings.SelectedListId;
            if (listId != "@default" || Services.Sync.TaskSync.Provider == Services.Sync.TaskSync.Google) return listId;

            string? first = await _backend.GetDefaultListIdAsync();
            if (first != null)
            {
                AppConfig.Settings.SelectedListId = first;
                AppConfig.Save();
            }
            return first;
        }

        // --- KEYBOARD NAVIGATION ---
        // Up/Down walk through the text like one long document: once the caret is on the last line,
        // Down goes title → details → next task's title; Up goes the other way.

        private void TasksListView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Up && e.Key != Key.Down) return;
            if (Keyboard.Modifiers != ModifierKeys.None) return; // Shift+arrows still extend a selection
            if (e.OriginalSource is not System.Windows.Controls.TextBox box || box.DataContext is not TaskItem task) return;

            bool isTitle = box.Name == "TaskTitleInput";
            bool isDetails = box.Name == "TaskDetailsInput";
            if (!isTitle && !isDetails) return;

            // Still room to move inside this box (wrapped lines count as lines)
            bool down = e.Key == Key.Down;
            int line = box.GetLineIndexFromCharacterIndex(box.CaretIndex);
            int lines = box.LineCount;
            if (line >= 0 && lines > 0 && (down ? line < lines - 1 : line > 0)) return;

            int index = MyTasks.IndexOf(task);
            TaskItem? target;
            bool toDetails;
            if (down)
            {
                target = isTitle ? task : (index + 1 < MyTasks.Count ? MyTasks[index + 1] : null);
                toDetails = isTitle;
            }
            else
            {
                target = isDetails ? task : (index > 0 ? MyTasks[index - 1] : null);
                toDetails = !isDetails;
            }
            if (target == null) return; // First/last field of the list: nowhere to go

            // Keep the caret's horizontal position, like moving between lines of a text editor
            var caret = box.GetRectFromCharacterIndex(box.CaretIndex);
            double windowX = caret.IsEmpty ? double.NaN : box.TranslatePoint(new Point(caret.X, 0), this).X;

            e.Handled = true;
            FocusTaskField(target, toDetails, windowX, enterFromBelow: !down);
        }

        private void FocusTaskField(TaskItem task, bool details, double windowX, bool enterFromBelow)
        {
            TasksListView.ScrollIntoView(task);
            TasksListView.UpdateLayout();
            if (TasksListView.ItemContainerGenerator.ContainerFromItem(task) is not DependencyObject container) return;

            var title = FindNamed<System.Windows.Controls.TextBox>(container, "TaskTitleInput");
            var detailsBox = FindNamed<System.Windows.Controls.TextBox>(container, "TaskDetailsInput");
            if (title == null) return;

            // An empty details box only appears while its task is being edited, so focus the task first
            if (details && detailsBox != null && detailsBox.Visibility != Visibility.Visible)
            {
                title.Focus();
                TasksListView.UpdateLayout();
            }

            var box = details && detailsBox != null && detailsBox.Visibility == Visibility.Visible ? detailsBox : title;
            box.Focus();
            box.UpdateLayout();

            // Land on the first line coming from above, the last line coming from below, at the same x
            int length = box.Text?.Length ?? 0;
            int caretIndex = enterFromBelow ? length : 0;
            if (!double.IsNaN(windowX) && length > 0)
            {
                var lineRect = box.GetRectFromCharacterIndex(enterFromBelow ? length : 0);
                if (!lineRect.IsEmpty)
                {
                    double x = this.TranslatePoint(new Point(windowX, 0), box).X;
                    int hit = box.GetCharacterIndexFromPoint(new Point(x, lineRect.Top + lineRect.Height / 2), snapToText: true);
                    if (hit >= 0)
                    {
                        var hitRect = box.GetRectFromCharacterIndex(hit);
                        var hitEnd = box.GetRectFromCharacterIndex(hit, trailingEdge: true);
                        caretIndex = x > (hitRect.Left + hitEnd.Left) / 2 ? hit + 1 : hit;
                    }
                }
            }
            box.CaretIndex = Math.Min(caretIndex, length);
        }

        private static T? FindNamed<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match && match.Name == name) return match;
                var deeper = FindNamed<T>(child, name);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private TaskReorder _reorder;
        private bool _refreshAfterDrag = false;

        // Saves the order after a task was dragged to a new place
        private async void OnTaskReordered(TaskItem moved)
        {
            if (!Services.Sync.TaskSync.IsEnabled)
            {
                LocalDataManager.SaveTasks(MyTasks, AppConfig.Settings.SelectedListId);
                return;
            }

            var backend = _backend;
            if (backend == null || moved.Id == null) return;

            var order = MyTasks.Where(t => t.Id != null)
                               .Select(t => new Services.Sync.RemoteTask { Id = t.Id, Parent = t.ParentId })
                               .ToList();
            await RunSyncRequest(() => backend.ReorderTaskAsync(AppConfig.Settings.SelectedListId, order, moved.Id));
        }

        // Never throws: a failed refresh keeps the tasks already on screen instead of crashing the app
        public async Task SyncTasksFromRemote()
        {
            if (_backend == null) return;

            if (_reorder != null && _reorder.IsDragging)
            {
                _refreshAfterDrag = true;
                return;
            }

            // Two refreshes running at once would interleave their results, so queue one more pass instead
            if (_isSyncingTasks)
            {
                _syncRequested = true;
                return;
            }

            _isSyncingTasks = true;
            try
            {
                do
                {
                    _syncRequested = false;
                    await DownloadAndShowTasks();
                }
                while (_syncRequested && _backend != null);
            }
            catch (Services.Sync.RemoteNotFoundException) when (AppConfig.Settings.SelectedListId != "@default")
            {
                // The selected list was deleted elsewhere, so fall back to the default list
                AppConfig.Settings.SelectedListId = "@default";
                AppConfig.Save();
                _isSyncingTasks = false;
                await SyncTasksFromRemote();
                await PopulateTrayMenu();
            }
            catch (Exception ex)
            {
                await HandleSyncErrorAsync(ex);
            }
            finally
            {
                _isSyncingTasks = false;
            }
        }

        private async Task HandleSyncErrorAsync(Exception ex)
        {
            Debug.WriteLine($"Task sync request failed: {ex}");

            if (!Services.Sync.TaskSync.IsAuthError(ex) || _isHandlingExpiredSignIn) return;

            _isHandlingExpiredSignIn = true;
            try
            {
                string service = Services.Sync.TaskSync.ProviderName;
                await Services.Sync.TaskSync.ForgetSignInAsync();

                if (Services.Sync.TaskSync.Provider == Services.Sync.TaskSync.Google)
                {
                    var answer = System.Windows.MessageBox.Show(
                        "Your Google sign-in has expired.\n\nSign in again now?",
                        "TasksBar",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);

                    if (answer == System.Windows.MessageBoxResult.Yes)
                    {
                        await InitializeSyncAsync();
                    }
                }
                else
                {
                    // An app password can't be renewed from here; it has to be entered again
                    System.Windows.MessageBox.Show(
                        $"{service} no longer accepts the saved app password (it may have been revoked).\n\n" +
                        "Open Settings → Behavior & Sync and connect again with a new app password.",
                        "TasksBar",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
            finally
            {
                _isHandlingExpiredSignIn = false;
            }
        }

        // Runs a single task-level sync call. A task that was already deleted or completed elsewhere
        // just gets refreshed away, and any other failure is reported instead of crashing.
        private async Task RunSyncRequest(Func<Task> request)
        {
            try
            {
                await request();
            }
            catch (Services.Sync.RemoteNotFoundException)
            {
                await SyncTasksFromRemote();
            }
            catch (Exception ex)
            {
                await HandleSyncErrorAsync(ex);
            }
        }

        private async Task DownloadAndShowTasks()
        {
            var downloaded = new List<TaskItem>();

            string? listId = await ResolveSelectedListAsync();
            if (listId != null)
            {
                var tasks = await _backend!.GetTasksAsync(listId, AppConfig.Settings.ShowCompletedTasks);
                foreach (var remote in tasks)
                {
                    if (string.IsNullOrWhiteSpace(remote.Title)) continue;

                    downloaded.Add(new TaskItem
                    {
                        Id = remote.Id,
                        Title = remote.Title,
                        Details = remote.Notes,
                        IsCompleted = remote.Completed,
                        ParentId = remote.Parent
                    });
                }
            }

            // Only swap the visible list once everything has arrived
            foreach (var oldTask in MyTasks)
            {
                oldTask.PropertyChanged -= OnTaskPropertyChanged;
                oldTask.Title = null;
                oldTask.Details = null;
            }

            MyTasks.Clear();

            foreach (var taskItem in downloaded)
            {
                taskItem.PropertyChanged += OnTaskPropertyChanged;
                MyTasks.Add(taskItem);
            }
        }
        private void UpdatePinIcon()
        {
            // If pinned, show a filled pin. If unpinned, show an empty pin outline.
            PinButton.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Pin20)
            {
                Filled = AppConfig.Settings.StayOnTop
            };
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the setting
            AppConfig.Settings.StayOnTop = !AppConfig.Settings.StayOnTop;

            // Apply it to the window immediately
            this.Topmost = AppConfig.Settings.StayOnTop;

            // Update the visual icon
            UpdatePinIcon();

            // Remember it, otherwise the next launch reverts to whatever was last saved
            AppConfig.Save();
        }

        private void AddNote_Click(object sender, RoutedEventArgs e)
        {
            // Pass the coordinates so the window knows where to save itself!
            var noteWindow = new StickyNoteWindow(this.Left - 260, this.Top);
            noteWindow.Show();
        }
        // New tasks whose create request is still running, and those edited during it
        private readonly HashSet<TaskItem> _creatingTasks = new HashSet<TaskItem>();
        private readonly HashSet<TaskItem> _updateAfterCreate = new HashSet<TaskItem>();

        private async Task TaskItem_PropertyChanged(TaskItem item, string propertyName)
        {
            if (!Services.Sync.TaskSync.IsEnabled)
            {
                // Assign a fake ID so UI logic doesn't break, then save to JSON
                if (item.Id == null) item.Id = Guid.NewGuid().ToString();
                LocalDataManager.SaveTasks(MyTasks, AppConfig.Settings.SelectedListId);
                return;
            }

            var backend = _backend;
            if (backend == null) return;

            // SCENARIO 1: It's a new task (no ID) and you just finished typing the Title
            if (item.Id == null)
            {
                // Still being created: editing the details meanwhile must not create a second copy,
                // so remember to send the latest values once the create has finished
                if (_creatingTasks.Contains(item))
                {
                    _updateAfterCreate.Add(item);
                    return;
                }

                if ((propertyName == nameof(TaskItem.Title) || propertyName == nameof(TaskItem.Details)) && !string.IsNullOrWhiteSpace(item.Title))
                {
                    var newTask = new Services.Sync.RemoteTask { Title = item.Title, Notes = item.Details ?? "" };
                    _creatingTasks.Add(item);
                    try
                    {
                        await RunSyncRequest(async () =>
                        {
                            var createdTask = await backend.CreateTaskAsync(AppConfig.Settings.SelectedListId, newTask);
                            item.Id = createdTask.Id; // Save the real remote ID
                        });
                    }
                    finally
                    {
                        _creatingTasks.Remove(item);
                    }

                    if (_updateAfterCreate.Remove(item) && item.Id != null)
                    {
                        await TaskItem_PropertyChanged(item, nameof(TaskItem.Details));
                    }
                }
                return;
            }

            // SCENARIO 2: It's an existing task being updated
         
            if (propertyName == nameof(TaskItem.IsCompleted) || propertyName == nameof(TaskItem.Details) || propertyName == nameof(TaskItem.Title))
            {
                // THE GHOST FIX: If they completely erased the title, delete the task!
                if (string.IsNullOrWhiteSpace(item.Title))
                {
                await RunSyncRequest(() => backend.DeleteTaskAsync(AppConfig.Settings.SelectedListId, item.Id));

                    // Remove it from the UI thread safely
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (MyTasks.Contains(item)) MyTasks.Remove(item);
                    }));

                    return; // Stop here so it doesn't try to update!
                }

                var update = new Services.Sync.RemoteTask
                {
                    Id = item.Id,
                    Title = item.Title,
                    Notes = item.Details ?? "",
                    Completed = item.IsCompleted
                };
                await RunSyncRequest(() => backend.UpdateTaskAsync(AppConfig.Settings.SelectedListId, update));
            }
        }

        // --- ADDING & DELETING ---


        private void AddTaskTop_Click(object sender, RoutedEventArgs e)
        {
            var taskItem = new TaskItem { IsNew = true, Title = "", Details = "", IsCompleted = false };

            taskItem.PropertyChanged += OnTaskPropertyChanged;

            MyTasks.Insert(0, taskItem);

            // THE IRON WALL: Only save to local JSON if sync is completely OFF
            if (!Services.Sync.TaskSync.IsEnabled)
            {
                LocalDataManager.SaveTasks(MyTasks, AppConfig.Settings.SelectedListId);
            }

            TasksListView.ScrollIntoView(taskItem);
        }

        // Automatically puts the cursor in the Title box when a new task is created
        private void TaskTitleInput_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.TextBox tb && tb.DataContext is TaskItem task && task.IsNew)
            {
                tb.Focus();
                task.IsNew = false; // Reset so it doesn't steal focus later
            }
        }

        // Cleans up blank tasks if you click away without typing anything
        private void TaskCard_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border && border.DataContext is TaskItem task)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // If focus completely left the task card
                    if (!border.IsKeyboardFocusWithin)
                    {
                        // And it has no ID (never sent to Google) and no Title, delete it locally
                        if (task.Id == null && string.IsNullOrWhiteSpace(task.Title))
                        {
                            MyTasks.Remove(task);
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void CompleteTask_MouseEnter(object sender, MouseEventArgs e)
        {
            Services.TaskSoundPlayer.PlayHover();
        }

        private async void CompleteTask_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.ToggleButton btn && btn.Tag is TaskItem task)
            {
                // 1. Catch the click INSTANTLY before the layout has a chance to shift
                e.Handled = true;

                Services.TaskSoundPlayer.PlayClick();

                // 2. Safely force the previous text box to save its changes
                Keyboard.ClearFocus();

                // 3. Manually toggle the completion state since we intercepted the raw mouse click
                task.IsCompleted = !task.IsCompleted;
                btn.IsChecked = task.IsCompleted;

                // 4. Run your original completion logic
                if (task.IsCompleted)
                {
                    // Ripple + particle burst from the check circle
                    var center = btn.TranslatePoint(new Point(btn.ActualWidth / 2, btn.ActualHeight / 2), EffectsLayer);
                    var accent = (FindResource("AccentFillColorDefaultBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x00, 0xD5, 0xE1);
                    TaskCompletionEffects.PlayBurst(EffectsLayer, center, accent);

                    // Let the checkmark and strikethrough register before the task leaves
                    await Task.Delay(900);
                    if (!task.IsCompleted) return;

                    // Slide the row away and close the gap smoothly instead of the list jumping
                    if (TasksListView.ItemContainerGenerator.ContainerFromItem(task) is FrameworkElement row)
                    {
                        btn.IsHitTestVisible = false; // Too late to undo once it's leaving
                        await TaskCompletionEffects.SlideOutAndCollapseAsync(row);
                    }

                    // Remove from UI
                    MyTasks.Remove(task);
                    LocalDataManager.SaveTasks(MyTasks, AppConfig.Settings.SelectedListId);

                    ShowCustomToast(task.Title);

                    var backend = _backend;
                    if (Services.Sync.TaskSync.IsEnabled && backend != null && task.Id != null)
                    {
                        var completed = new Services.Sync.RemoteTask
                        {
                            Id = task.Id,
                            Title = task.Title,
                            Notes = task.Details ?? "",
                            Completed = true
                        };
                        await RunSyncRequest(() => backend.UpdateTaskAsync(AppConfig.Settings.SelectedListId, completed));
                    }
                }
            }
        }

        private void MoreOptions_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn)
            {
                // Stop the layout shift
                e.Handled = true;
                Keyboard.ClearFocus();

                var menu = new System.Windows.Controls.ContextMenu();
                var deleteItem = new System.Windows.Controls.MenuItem { Header = "Delete" };

                deleteItem.Click += (s, args) =>
                {
                    if (btn.Tag is TaskItem taskToDelete)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            MyTasks.Remove(taskToDelete);
                            if (Services.Sync.TaskSync.IsEnabled)
                            {
                                var backend = _backend;
                                if (backend != null && taskToDelete.Id != null)
                                {
                                    await RunSyncRequest(() => backend.DeleteTaskAsync(AppConfig.Settings.SelectedListId, taskToDelete.Id));
                                }
                            }
                            else
                            {
                                LocalDataManager.SaveTasks(MyTasks, AppConfig.Settings.SelectedListId);
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                };

                // "Move to..." fetches the lists only when its submenu opens, so newly created lists always show up
                var moveItem = new System.Windows.Controls.MenuItem { Header = "Move to..." };
                moveItem.Items.Add(new System.Windows.Controls.MenuItem { Header = "Loading...", IsEnabled = false });

                if (btn.Tag is TaskItem taskToMove)
                {
                    // A new task that hasn't been saved yet has nothing to move
                    moveItem.IsEnabled = taskToMove.Id != null;

                    bool listsLoaded = false;
                    moveItem.SubmenuOpened += async (s, args) =>
                    {
                        if (args.OriginalSource != moveItem || listsLoaded) return;
                        listsLoaded = true;

                        var targets = await GetMoveTargetListsAsync();
                        moveItem.Items.Clear();

                        if (targets.Count == 0)
                        {
                            moveItem.Items.Add(new System.Windows.Controls.MenuItem { Header = "No other lists", IsEnabled = false });
                            return;
                        }

                        foreach (var (listId, listTitle) in targets)
                        {
                            var listItem = new System.Windows.Controls.MenuItem { Header = listTitle };
                            listItem.Click += async (s2, args2) => await MoveTaskToList(taskToMove, listId, listTitle);
                            moveItem.Items.Add(listItem);
                        }
                    };
                }

                menu.Items.Add(moveItem);
                menu.Items.Add(deleteItem);
                menu.PlacementTarget = btn;
                menu.IsOpen = true;
            }
        }

        // Every list except the one being shown (and the hidden sticky notes list)
        private async Task<List<(string Id, string Title)>> GetMoveTargetListsAsync()
        {
            var targets = new List<(string Id, string Title)>();

            if (Services.Sync.TaskSync.IsEnabled)
            {
                var backend = _backend;
                if (backend == null) return targets;
                try
                {
                    foreach (var lst in await backend.GetListsAsync())
                    {
                        if (lst.Id == AppConfig.Settings.SelectedListId || Services.StickyNoteSync.IsNotesList(lst.Id, lst.Title)) continue;
                        targets.Add((lst.Id, lst.Title));
                    }
                }
                catch (Exception ex)
                {
                    await HandleSyncErrorAsync(ex);
                }
            }
            else
            {
                foreach (var lst in LocalDataManager.GetLocalLists())
                {
                    if (lst.Id == AppConfig.Settings.SelectedListId) continue;
                    targets.Add((lst.Id, lst.Title));
                }
            }

            return targets;
        }

        private async Task MoveTaskToList(TaskItem task, string destinationListId, string destinationTitle)
        {
            string sourceListId = AppConfig.Settings.SelectedListId;
            bool moved = false;

            if (Services.Sync.TaskSync.IsEnabled)
            {
                var backend = _backend;
                if (backend == null || task.Id == null) return;

                await RunSyncRequest(async () =>
                {
                    await backend.MoveTaskAsync(sourceListId, task.Id, destinationListId);
                    moved = true;
                });

                if (!moved && _backend != null)
                {
                    System.Windows.MessageBox.Show(
                        $"Couldn't move \"{task.Title}\" to {destinationTitle}.\n\nCheck your connection and try again.",
                        "TasksBar", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
            else
            {
                var destinationTasks = LocalDataManager.LoadTasks(destinationListId);
                destinationTasks.Insert(0, task);
                LocalDataManager.SaveTasks(destinationTasks, destinationListId);
                moved = true;
            }

            if (!moved) return;

            // The list may have changed while the request was running
            if (AppConfig.Settings.SelectedListId == sourceListId && MyTasks.Contains(task))
            {
                task.PropertyChanged -= OnTaskPropertyChanged;
                MyTasks.Remove(task);
                if (!Services.Sync.TaskSync.IsEnabled)
                {
                    LocalDataManager.SaveTasks(MyTasks, sourceListId);
                }
            }

            ShowCustomToast(task.Title, $"Moved to {destinationTitle}", celebrate: false);
        }

        // --- WINDOW MANAGEMENT & CALCULATED ANIMATIONS ---

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow != null && _settingsWindow.IsLoaded)
            {
                _settingsWindow.Focus();
                return;
            }

            _isOpeningSettings = true;

            // Remember which sync account was in use, to notice a switch (e.g. Google to Tasks.org)
            bool previousSyncState = Services.Sync.TaskSync.IsEnabled;
            string previousAccount = Services.Sync.TaskSync.AccountKey;

            _settingsWindow = new SettingsWindow();

            _settingsWindow.Closed += async (s, args) =>
            {
                this.WindowBackdropType = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;
                this.Topmost = AppConfig.Settings.StayOnTop;
                _currentPosition = AppConfig.Settings.Position;
                UpdatePinIcon();
                PlaySlideAnimation();

                if (Services.Sync.TaskSync.IsEnabled)
                {
                    bool accountChanged = previousAccount != Services.Sync.TaskSync.AccountKey;

                    // Picks up a Google login made in Settings without opening the browser again
                    if (_backend == null && !accountChanged) Services.Sync.TaskSync.ConnectWithoutPrompt();

                    if (_backend == null || accountChanged)
                    {
                        // Not connected yet, or a different service/account was chosen: run the full init
                        await InitializeSyncAsync();
                    }
                    else
                    {
                        // Otherwise, just refresh the list!
                        await SyncTasksFromRemote();
                        await PopulateTrayMenu();
                        await Services.StickyNoteSync.SyncAsync();
                    }
                }
                else if (previousSyncState)
                {
                    Services.Sync.TaskSync.Disconnect();
                    MyTasks.Clear();
                    LoadLocalTasks(); // Show the local lists if they just turned sync off
                }
            };

            _settingsWindow.Show();
            _isOpeningSettings = false;
        }
        private void TaskTitleInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // 1. Clear the keyboard focus to force the current task to save
                Keyboard.ClearFocus();

                // 2. Wait a split second, then click the "Add Task" button for them
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    AddTaskTop_Click(null, null);
                }), System.Windows.Threading.DispatcherPriority.Background);

                e.Handled = true;
            }
        }
        private void RootGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Same kill-switch for when you click the empty space inside the app
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null);
            Keyboard.ClearFocus();
        }
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                PlaySlideAnimation();
            }
            else if (this.WindowState == WindowState.Minimized)
            {
                // Force memory flush immediately upon OS minimization
                FlushMemory();
            }
        }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!AppConfig.Settings.IsLocked && e.ChangedButton == MouseButton.Left)
            {
                // 1. CLEAR the animation locks! 
                // This forces Windows to release the coordinates so DragMove can properly update them.
                this.BeginAnimation(Window.TopProperty, null);
                this.BeginAnimation(Window.OpacityProperty, null);

                // 2. Start the drag operation (Pauses code until you let go of the mouse)
                this.DragMove();

                // 3. Save the exact, true location to our memory variables
                _isCustomDragged = true;
                _customLeft = this.Left;
                _customTop = this.Top;
            }
        }
        private void Window_Deactivated(object sender, EventArgs e)
        {
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null);
            Keyboard.ClearFocus();

            // Check if the settings window is currently open
            bool isSettingsOpen = _settingsWindow != null && _settingsWindow.IsLoaded;

            // This perfectly translates to: "If unfocused AND not pinned"
            if (!AppConfig.Settings.StayOnTop && !_isOpeningSettings)
            {
                // Temporarily force the window to stay on top so it doesn't 
                // get hidden behind the app you just clicked while it tries to animate!
                this.Topmost = true;

                // Trigger the sleek exit animation
                PlaySlideOutAnimation();
            }
        }
        private void Window_Activated(object sender, EventArgs e)
        {
            // Windows 11 intentionally drops Acrylic when a window loses focus. 
            // Sometimes it "forgets" to redraw it when focus returns. 
            // Toggling to None and back instantly forces the DWM to redraw the blur!

            var expectedBackdrop = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;

            this.WindowBackdropType = WindowBackdropType.None;
            this.WindowBackdropType = expectedBackdrop;
        }
        private void PlaySlideAnimation()
        {
            var desktopWorkingArea = SystemParameters.WorkArea;
            double finalLeft = 0;
            double finalTop = 0;
            double startTop = 0;

            // 1. Check if the user dragged it to a custom spot
            if (_isCustomDragged)
            {
                finalLeft = _customLeft;
                finalTop = _customTop;

                // Smart animation direction based on where you dragged it
                if (finalTop < (desktopWorkingArea.Height / 2))
                    startTop = finalTop - 50; // Top half of screen -> slide down
                else
                    startTop = finalTop + 50; // Bottom half of screen -> slide up
            }
            // 2. Otherwise, use the standard Settings grid
            else
            {
                if (_currentPosition == WidgetPosition.TopLeft || _currentPosition == WidgetPosition.BottomLeft)
                    finalLeft = desktopWorkingArea.Left + 12;
                else if (_currentPosition == WidgetPosition.TopCenter || _currentPosition == WidgetPosition.BottomCenter)
                    finalLeft = desktopWorkingArea.Left + (desktopWorkingArea.Width / 2) - (this.Width / 2);
                else
                    finalLeft = desktopWorkingArea.Right - this.Width - 12;

                if (_currentPosition == WidgetPosition.TopLeft || _currentPosition == WidgetPosition.TopCenter || _currentPosition == WidgetPosition.TopRight)
                {
                    finalTop = desktopWorkingArea.Top + 12;
                    startTop = finalTop - 50;
                }
                else
                {
                    finalTop = desktopWorkingArea.Bottom - this.Height - 12;
                    startTop = finalTop + 50;
                }
            }

            this.Left = finalLeft;

            CubicEase cubicEaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

            DoubleAnimation slideAnim = new DoubleAnimation()
            {
                From = startTop,
                To = finalTop,
                Duration = new Duration(TimeSpan.FromMilliseconds(350)),
                EasingFunction = cubicEaseOut
            };

            DoubleAnimation fadeAnim = new DoubleAnimation()
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = cubicEaseOut
            };

            this.BeginAnimation(Window.TopProperty, slideAnim);
            this.BeginAnimation(Window.OpacityProperty, fadeAnim);
        }
        private void PlaySlideOutAnimation()
        {
            var desktopWorkingArea = SystemParameters.WorkArea;
            double startTop = this.Top; // Start from wherever it currently is
            double finalTop = 0;

            // Recalculate the "hidden" position based on where it is on the screen
            if (_isCustomDragged)
            {
                if (this.Top < (desktopWorkingArea.Height / 2))
                    finalTop = this.Top - 50; // Top half -> slide up
                else
                    finalTop = this.Top + 50; // Bottom half -> slide down
            }
            else
            {
                if (_currentPosition == WidgetPosition.TopLeft || _currentPosition == WidgetPosition.TopCenter || _currentPosition == WidgetPosition.TopRight)
                    finalTop = this.Top - 50;
                else
                    finalTop = this.Top + 50;
            }

            // EaseIn makes it start slow and accelerate out (perfect for exit animations)
            CubicEase cubicEaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };

            DoubleAnimation slideAnim = new DoubleAnimation()
            {
                From = startTop,
                To = finalTop,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)), // Slightly faster exit feels snappier
                EasingFunction = cubicEaseIn
            };

            DoubleAnimation fadeAnim = new DoubleAnimation()
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = cubicEaseIn
            };

            // Wait for the fade out to finish, THEN completely hide and flush the RAM
            fadeAnim.Completed += (s, args) =>
            {
                HideAndFlushMemory();
            };

            this.BeginAnimation(Window.TopProperty, slideAnim);
            this.BeginAnimation(Window.OpacityProperty, fadeAnim);
        }
        public async Task PopulateTrayMenu()
        {
            // 1. Fetch the data on the background thread FIRST
            List<Services.Sync.RemoteList>? remoteLists = null;
            var backend = _backend;

            if (Services.Sync.TaskSync.IsEnabled && backend != null)
            {
                try
                {
                    remoteLists = await backend.GetListsAsync();
                }
                catch { /* Failsafe if offline */ }
            }

            // 2. Build the menu strictly on the main thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                _trayMenu.Items.Clear();

                if (Services.Sync.TaskSync.IsEnabled && backend != null)
                {
                    if (remoteLists != null)
                    {
                        foreach (var lst in remoteLists)
                        {
                            if (Services.StickyNoteSync.IsNotesList(lst.Id, lst.Title)) continue;
                            var item = CreateTrayListItem(lst.Title, lst.Id == AppConfig.Settings.SelectedListId);
                            item.Click += async (s, e) =>
                            {
                                AppConfig.Settings.SelectedListId = lst.Id;
                                AppConfig.Save();
                                await SyncTasksFromRemote();
                                await PopulateTrayMenu();
                                OpenFlyoutFromTray();
                            };
                            _trayMenu.Items.Add(item);
                        }
                    }
                }
                else
                {
                    var localLists = LocalDataManager.GetLocalLists();
                    foreach (var lst in localLists)
                    {
                        var item = CreateTrayListItem(lst.Title, lst.Id == AppConfig.Settings.SelectedListId);
                        item.Click += async (s, e) =>
                        {
                            AppConfig.Settings.SelectedListId = lst.Id;
                            AppConfig.Save();
                            LoadLocalTasks();
                            await PopulateTrayMenu();
                            OpenFlyoutFromTray();
                        };
                        _trayMenu.Items.Add(item);
                    }
                }

                _trayMenu.Items.Add(new System.Windows.Controls.Separator());
                _trayMenu.Items.Add(CreateExitTrayItem());
            });
        }
        public void OpenFlyoutFromTray()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                this.Show();
                this.Activate();
                this.Focus();
                PlaySlideAnimation();
            });
        }
        private int _toastCounter = 0;
        private int _comboCount = 0;
        private DateTime _lastCompletionToast = DateTime.MinValue;
        private static readonly Random _toastRandom = new Random();
        private static readonly string[] _cheers =
        {
            "Task completed", "Nice work!", "Done!", "Crushed it!", "One less thing!", "Great job!", "Nailed it!", "Checked off!"
        };

        /// <summary>
        /// Shows the toast at the bottom of the flyout. Completions (<paramref name="celebrate"/>) get the full
        /// treatment: springy pop, check badge drawing in, a shine sweep, confetti and a combo counter when
        /// several tasks are finished in a row. Other messages (like "Moved to...") use a calmer version.
        /// </summary>
        private async void ShowCustomToast(string title, string header = null, bool celebrate = true)
        {
            _toastCounter++;
            int currentToast = _toastCounter;
            var accent = (TryFindResource("AccentFillColorDefaultBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x00, 0x78, 0xD4);

            if (celebrate)
            {
                var now = DateTime.Now;
                _comboCount = (now - _lastCompletionToast).TotalSeconds < 10 ? _comboCount + 1 : 1;
                _lastCompletionToast = now;
            }
            bool combo = celebrate && _comboCount >= 2;

            ToastHeader.Text = header ?? (_comboCount switch
            {
                2 => "Double!",
                3 => "Triple!",
                >= 4 => "On a roll!",
                _ => _cheers[_toastRandom.Next(_cheers.Length)]
            });
            ToastTaskTitle.Text = title;
            ToastCombo.Visibility = combo ? Visibility.Visible : Visibility.Collapsed;
            ToastComboText.Text = "×" + _comboCount;

            // Accent tint and outline so the card belongs to the (acrylic) window instead of being a dark slab
            ToastTint.Background = new LinearGradientBrush(
                TaskCompletionEffects.WithAlpha(accent, 0x50), TaskCompletionEffects.WithAlpha(accent, 0x0C), new Point(0, 0), new Point(1, 1));
            ToastOutline.BorderBrush = new LinearGradientBrush(
                TaskCompletionEffects.WithAlpha(accent, 0xC8), TaskCompletionEffects.WithAlpha(accent, 0x30), new Point(0, 0), new Point(1, 1));

            bool wasVisible = CustomToastPopup.Opacity > 0.5;

            // Card: springy pop in, or a little bump if it's already showing
            if (!wasVisible)
            {
                CustomToastPopup.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(160)));
                ToastTransform.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(28, 0, Ms(480)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 } });
                AnimateScale(ToastScale, new DoubleAnimation(0.72, 1, Ms(650))
                {
                    EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 6 }
                });
            }
            else
            {
                CustomToastPopup.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(120)));
                ToastTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Ms(200)));
                var bump = new DoubleAnimationUsingKeyFrames();
                bump.KeyFrames.Add(new EasingDoubleKeyFrame(1.09, KeyTime.FromTimeSpan(Ms(90)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                bump.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(Ms(520)),
                    new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 }));
                AnimateScale(ToastScale, bump);
            }

            // Glow flare behind the card
            var glow = new DoubleAnimationUsingKeyFrames();
            glow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            glow.KeyFrames.Add(new EasingDoubleKeyFrame(celebrate ? (combo ? 0.75 : 0.55) : 0.25, KeyTime.FromTimeSpan(Ms(180)),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            glow.KeyFrames.Add(new EasingDoubleKeyFrame(celebrate ? 0.12 : 0, KeyTime.FromTimeSpan(Ms(1100)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
            ToastGlow.BeginAnimation(OpacityProperty, glow);

            // Shine sweep
            ToastShineOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1.2, 1.2, Ms(750))
            {
                BeginTime = Ms(170),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });

            // Check badge: circle springs in, check draws, ring ripples out
            AnimateScale(ToastIconScale, new DoubleAnimation(0, 1, Ms(550))
            {
                BeginTime = Ms(70),
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 }
            });
            ToastCheck.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, new DoubleAnimation(8, 0, Ms(260))
            {
                BeginTime = Ms(240),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            AnimateScale(ToastIconRingScale, new DoubleAnimation(1, 2.1, Ms(650))
            {
                BeginTime = Ms(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            ToastIconRing.BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, Ms(650)) { BeginTime = Ms(220) });

            // Text slides in, staggered
            SlideInText(ToastHeader, ToastHeaderShift, Ms(110));
            SlideInText(ToastTaskTitle, ToastTitleShift, Ms(180));

            if (combo)
            {
                AnimateScale(ToastComboScale, new DoubleAnimation(0, 1, Ms(600))
                {
                    BeginTime = Ms(280),
                    EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 4 }
                });
            }

            // Time-left bar
            const int holdMs = 3200;
            ToastProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, Ms(holdMs)) { BeginTime = Ms(250) });

            // Confetti out of the badge, more for bigger combos
            int waited = 0;
            if (celebrate)
            {
                await Task.Delay(230);
                waited = 230;
                if (currentToast != _toastCounter) return;
                var center = ToastIcon.TranslatePoint(new Point(ToastIcon.ActualWidth / 2, ToastIcon.ActualHeight / 2), EffectsLayer);
                TaskCompletionEffects.PlayConfetti(EffectsLayer, center, accent, 20 + Math.Min(_comboCount - 1, 4) * 7);
            }

            await Task.Delay(holdMs + 250 - waited);

            // Only hide if nothing else was shown in the meantime
            if (currentToast == _toastCounter)
            {
                var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
                ToastTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 14, Ms(230)) { EasingFunction = easeIn });
                AnimateScale(ToastScale, new DoubleAnimation(1, 0.9, Ms(230)) { EasingFunction = easeIn });
                CustomToastPopup.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(230)));
                ToastGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(200)));
            }
        }

        private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

        private static void AnimateScale(ScaleTransform scale, AnimationTimeline animation)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private static void SlideInText(UIElement text, TranslateTransform shift, TimeSpan delay)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(12, 0, Ms(320)) { BeginTime = delay, EasingFunction = ease });
            text.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(260)) { BeginTime = delay });
        }

    }
}