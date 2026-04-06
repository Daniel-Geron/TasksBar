using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;
using System;
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
    public enum WidgetPosition { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }

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
        // Google API Service
        private TasksService _googleTasksService;
        private string _defaultTaskListId = "@default";
        private SnackbarService _snackbarService;
        private System.Windows.Forms.NotifyIcon _trayIcon;
        public TasksFlyoutWindow()
        {
            AppConfig.Load();

            // 1. Create the window in memory FIRST
            InitializeComponent();
            SetupTrayIcon();

            // 2. THE FIX: Now that the window exists, apply the theme to it!
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

            _snackbarService = new SnackbarService();
            _snackbarService.SetSnackbarPresenter(RootSnackbar);

            MyTasks = new ObservableCollection<TaskItem>();
            TasksListView.ItemsSource = MyTasks;

            this.Loaded += async (s, e) =>
            {
                PlaySlideAnimation();

                // THE FIX: Only attempt to log in if Sync is turned on!
                if (AppConfig.Settings.EnableGoogleSync)
                {
                    await InitializeGoogleTasksAsync();
                }
            };

            this.StateChanged += Window_StateChanged;
            this.Deactivated += Window_Deactivated;
        }
        private void HideAndFlushMemory()
        {
            this.Hide(); // Hide the window

            // Force the memory flush immediately after hiding
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
            }
            catch { }
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

          
            // This automatically extracts the beautiful .exe icon you set in the project properties!
            _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _trayIcon.Text = "TasksBar";
            _trayIcon.Visible = true;

            // Handle Left-Click to toggle the window
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    if (this.IsVisible)
                    {
                        // Flush the RAM when manually hidden via the tray!
                        HideAndFlushMemory();
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

            // Handle Right-Click to close the app completely
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Exit TasksBar", null, (s, e) => Application.Current.Shutdown());
            _trayIcon.ContextMenuStrip = contextMenu;
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
        // --- GOOGLE API INTEGRATION ---

        private async Task InitializeGoogleTasksAsync()
        {
            try
            {
                // THE FIX: Check if the file exists before trying to log in!
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

                // Tell the central manager to handle the token/login process
                await GoogleAuthManager.LoginAsync();

                // Grab the initialized service directly from the manager
                _googleTasksService = GoogleAuthManager.GetTasksService();

                await SyncTasksFromGoogle();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to connect to Google: {ex.Message}");
            }
        }
        private async Task SyncTasksFromGoogle()
        {
            if (_googleTasksService == null) return;

            // --- THE CLEANUP FIX ---
            // Before clearing the list, safely unhook the events so the RAM is freed!
            foreach (var oldTask in MyTasks)
            {
                oldTask.PropertyChanged -= OnTaskPropertyChanged; // Mathematically breaks the memory link

                oldTask.Title = null;
                oldTask.Details = null;
            }

            MyTasks.Clear();
      


            var request = _googleTasksService.Tasks.List(_defaultTaskListId);
            request.ShowHidden = false;
          

            var response = await request.ExecuteAsync();

            if (response.Items != null)
            {
                foreach (var gTask in response.Items)
                {
                    if (string.IsNullOrWhiteSpace(gTask.Title)) continue;
                 

                    var taskItem = new TaskItem
                    {
                        Id = gTask.Id,
                        Title = gTask.Title,
                        Details = gTask.Notes ?? "",
                        IsCompleted = gTask.Status == "completed"
                    };

                    // --- THE ATTACHMENT FIX ---
                    // Hook up the new named method instead of the lambda
                    taskItem.PropertyChanged += OnTaskPropertyChanged;

                    MyTasks.Add(taskItem);
                }
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

            // Note: If you have an AppConfig.Save() method, call it here!
        }
        private async Task TaskItem_PropertyChanged(TaskItem item, string propertyName)
        {
            if (!AppConfig.Settings.EnableGoogleSync || _googleTasksService == null) return;

            // SCENARIO 1: It's a new task (no ID) and you just finished typing the Title
            if (item.Id == null)
            {
                if ((propertyName == nameof(TaskItem.Title) || propertyName == nameof(TaskItem.Details)) && !string.IsNullOrWhiteSpace(item.Title))
                {
                    var newGTask = new Google.Apis.Tasks.v1.Data.Task { Title = item.Title, Notes = item.Details };
                    var createdTask = await _googleTasksService.Tasks.Insert(newGTask, _defaultTaskListId).ExecuteAsync();
                    item.Id = createdTask.Id; // Save the real Google ID
                }
                return;
            }

            // SCENARIO 2: It's an existing task being updated
            if (propertyName == nameof(TaskItem.IsCompleted) || propertyName == nameof(TaskItem.Details) || propertyName == nameof(TaskItem.Title))
            {
                var gTask = new Google.Apis.Tasks.v1.Data.Task
                {
                    Id = item.Id,
                    Title = item.Title,
                    Notes = item.Details,
                    Status = item.IsCompleted ? "completed" : "needsAction"
                };
                await _googleTasksService.Tasks.Update(gTask, _defaultTaskListId, item.Id).ExecuteAsync();
            }
        }

        // --- ADDING & DELETING ---


        private void AddTaskTop_Click(object sender, RoutedEventArgs e)
        {
            var taskItem = new TaskItem { IsNew = true, Title = "", Details = "", IsCompleted = false };
            taskItem.PropertyChanged += async (s, args) => await TaskItem_PropertyChanged(taskItem, args.PropertyName);

            // Insert at TOP (Index 0) instead of Add()
            MyTasks.Insert(0, taskItem);
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
   
        private async void CompleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.ToggleButton btn && btn.Tag is TaskItem task)
            {
                if (task.IsCompleted)
                {
                    // 1. Wait for the smooth strikethrough effect
                    await Task.Delay(1500);
                    if (!task.IsCompleted) return;

                    // 2. Remove from UI
                    MyTasks.Remove(task);

                    _snackbarService.Show(
                        "Task Completed",
                        task.Title,
                        Wpf.Ui.Controls.ControlAppearance.Dark,
                        new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24),
                        TimeSpan.FromSeconds(3)
                    );

                    // 3. THE FIX: Include Title and Notes so Google doesn't wipe them!
                    if (AppConfig.Settings.EnableGoogleSync && _googleTasksService != null && task.Id != null)
                    {
                        var gTask = new Google.Apis.Tasks.v1.Data.Task
                        {
                            Id = task.Id,
                            Title = task.Title,   // Keep the title!
                            Notes = task.Details, // Keep the notes!
                            Status = "completed"
                        };
                        await _googleTasksService.Tasks.Update(gTask, _defaultTaskListId, task.Id).ExecuteAsync();
                    }
                }
            }
        }

        private void MoreOptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn)
            {
                var menu = new System.Windows.Controls.ContextMenu();
                var deleteItem = new System.Windows.Controls.MenuItem { Header = "Delete" };

                deleteItem.Click += (s, args) =>
                {
                    if (btn.Tag is TaskItem taskToDelete)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            MyTasks.Remove(taskToDelete);
                            if (_googleTasksService != null && taskToDelete.Id != null)
                            {
                                await _googleTasksService.Tasks.Delete(_defaultTaskListId, taskToDelete.Id).ExecuteAsync();
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                };

                menu.Items.Add(deleteItem);
                menu.PlacementTarget = btn;
                menu.IsOpen = true;
            }
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

            // THE FIX: Track the Google Sync setting instead of the deleted Completed Tasks setting
            bool previousSyncState = AppConfig.Settings.EnableGoogleSync;

            _settingsWindow = new SettingsWindow();

            _settingsWindow.Closed += async (s, args) =>
            {
                this.WindowBackdropType = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;
                this.Topmost = AppConfig.Settings.StayOnTop;
                _currentPosition = AppConfig.Settings.Position;
                UpdatePinIcon();
                PlaySlideAnimation();

                // If Google Sync is enabled, always grab the latest service from the manager
                if (AppConfig.Settings.EnableGoogleSync)
                {
                    // Update our local service in case they just logged in via Settings
                    _googleTasksService = GoogleAuthManager.GetTasksService();

                    if (_googleTasksService == null)
                    {
                        // If the service is null, they haven't logged in at all yet, so run the full init
                        await InitializeGoogleTasksAsync();
                    }
                    else
                    {
                        // Otherwise, just refresh the list!
                        await SyncTasksFromGoogle();
                    }
                }
                else if (!AppConfig.Settings.EnableGoogleSync && previousSyncState)
                {
                    MyTasks.Clear(); // Clear the UI if they just toggled sync off
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
            if (this.WindowState == WindowState.Normal) PlaySlideAnimation();
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

            if (!AppConfig.Settings.StayOnTop && !_isOpeningSettings)
            {
                // THE FIX: Use our new centralized method
                HideAndFlushMemory();
            }
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
    }
}