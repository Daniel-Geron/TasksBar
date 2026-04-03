using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks; // Explicitly using this for background Tasks
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace GTasksBar
{
    public enum WidgetPosition { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }

    public partial class TasksFlyoutWindow : FluentWindow
    {
        public ObservableCollection<TaskItem> MyTasks { get; set; }
        private WidgetPosition _currentPosition = WidgetPosition.BottomRight;
        private bool _isCustomDragged = false;
        private double _customLeft = 0;
        private double _customTop = 0;
        // Google API Service
        private TasksService _googleTasksService;
        private string _defaultTaskListId = "@default";
        private SnackbarService _snackbarService;
        public TasksFlyoutWindow()
        {
            AppConfig.Load();

            // 1. Apply the system theme (Light/Dark) BEFORE building the UI
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();

            // 2. THE FIX: Explicitly apply the Windows Accent Color globally
            Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent();

            InitializeComponent();

            // 3. Keep the watcher so it auto-updates if you change colors in Windows settings
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, true);

            // Apply loaded settings to the window
            this.WindowBackdropType = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;
            this.Topmost = AppConfig.Settings.StayOnTop;
            _currentPosition = AppConfig.Settings.Position;

            _snackbarService = new SnackbarService();
            _snackbarService.SetSnackbarPresenter(RootSnackbar);

            MyTasks = new ObservableCollection<TaskItem>();
            TasksListView.ItemsSource = MyTasks;

            this.Loaded += async (s, e) =>
            {
                PlaySlideAnimation();
                await InitializeGoogleTasksAsync();
            };

            this.StateChanged += Window_StateChanged;
            this.Deactivated += Window_Deactivated;
        }

        // --- GOOGLE API INTEGRATION ---

        private async Task InitializeGoogleTasksAsync()
        {
            try
            {
                UserCredential credential;
                string[] scopes = { TasksService.Scope.Tasks };

                using (var stream = new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
                {
                    string credPath = "token.json";
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        scopes,
                        "user",
                        CancellationToken.None,
                        new Google.Apis.Util.Store.FileDataStore(credPath, true));
                }

                _googleTasksService = new TasksService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "GTasksBar",
                });

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

            MyTasks.Clear();
            var request = _googleTasksService.Tasks.List(_defaultTaskListId);
            request.ShowHidden = false;
            var response = await request.ExecuteAsync();

            if (response.Items != null)
            {
                foreach (var gTask in response.Items)
                {
                    // THE FIX: Ignore "Ghost" tasks that have no title!
                    if (string.IsNullOrWhiteSpace(gTask.Title)) continue;

                    var taskItem = new TaskItem
                    {
                        Id = gTask.Id,
                        Title = gTask.Title,
                        Details = gTask.Notes ?? "",
                        IsCompleted = gTask.Status == "completed"
                    };

                    taskItem.PropertyChanged += async (s, e) => await TaskItem_PropertyChanged(taskItem, e.PropertyName);
                    MyTasks.Add(taskItem);
                }
            }
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
                        Wpf.Ui.Controls.ControlAppearance.Primary,
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
            WidgetPosition previousPosition = AppConfig.Settings.Position;

            var settingsWin = new SettingsWindow();

            // 1. Tell Windows this Settings menu belongs to the main app
            settingsWin.Owner = this;

            // 2. Force the Settings menu to share the "Stay on Top" status so it never hides
            settingsWin.Topmost = this.Topmost;

            // Now open the window safely!
            settingsWin.ShowDialog();

            if (previousPosition != AppConfig.Settings.Position)
            {
                _isCustomDragged = false;
            }

            this.WindowBackdropType = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;
            this.Topmost = AppConfig.Settings.StayOnTop;
            _currentPosition = AppConfig.Settings.Position;

            PlaySlideAnimation();
        }
        private void TaskTitleInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // 1. Move focus away to trigger the "LostFocus" binding and save to Google
                FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), this);

                // 2. Small delay to let the UI breathe, then open a new task slot
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AddTaskTop_Click(null, null);
                }), System.Windows.Threading.DispatcherPriority.Background);

                e.Handled = true;
            }
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
            // If Stay on Top is true, we refuse to minimize!
            if (!AppConfig.Settings.StayOnTop)
            {
                this.WindowState = WindowState.Minimized;
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