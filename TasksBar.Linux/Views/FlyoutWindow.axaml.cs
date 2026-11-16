using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentIcons.Common;
using TasksBar.Linux.Effects;
using TasksBar.Services;
using TasksBar.Services.Sync;

namespace TasksBar.Linux.Views
{
    /// <summary>
    /// The task flyout, ported from the Windows app's TasksFlyoutWindow: the same layout, animations,
    /// sync behavior, drag-to-reorder and keyboard navigation.
    /// </summary>
    public partial class FlyoutWindow : Window
    {
        public ObservableCollection<TaskItem> MyTasks { get; } = new();

        private ITaskBackend? _backend => TaskSync.Current;
        private readonly TaskReorder _reorder;
        private bool _isCustomDragged;
        private PixelPoint _customPosition;
        private bool _hiding;

        public FlyoutWindow()
        {
            InitializeComponent();
            TasksList.ItemsSource = MyTasks;

            Topmost = AppConfig.Settings.StayOnTop;
            UpdatePinIcon();

            // Clicking empty space ends editing, like the Windows app
            RootGrid.AddHandler(PointerPressedEvent, (s, e) => ClearFocus(), RoutingStrategies.Bubble, handledEventsToo: false);
            Header.PointerPressed += Header_PointerPressed;
            Deactivated += (s, e) => OnDeactivated();
            Backdrop.Attach(this, Chrome);

            // Per-row wiring: Enter in a title adds the next task, empty new tasks vanish when left
            TasksList.AddHandler(KeyDownEvent, TasksList_KeyDown, RoutingStrategies.Tunnel);
            TasksList.AddHandler(LostFocusEvent, TasksList_LostFocus, RoutingStrategies.Bubble);
            TasksList.ContainerPrepared += (s, e) => Dispatcher.UIThread.Post(() => PrepareRow(e.Container), DispatcherPriority.Loaded);

            _reorder = new TaskReorder(TasksList, TasksScroll, DragLayer, MyTasks,
                task => !task.IsCompleted && !string.IsNullOrWhiteSpace(task.Title) && (task.Id != null || !TaskSync.IsEnabled),
                OnTaskReordered);
            _reorder.DragEnded += () =>
            {
                if (_refreshAfterDrag)
                {
                    _refreshAfterDrag = false;
                    _ = SyncTasksFromRemote();
                }
            };

            Dispatcher.UIThread.Post(async () => await StartAsync(), DispatcherPriority.Background);
        }

        private async Task StartAsync()
        {
            LocalDataManager.LoadNotes();
            NoteImageStore.RemoveUnused(LocalDataManager.ActiveNotes);
            foreach (var note in LocalDataManager.ActiveNotes.Where(n => n.OpenOnStartup).ToList())
            {
                new StickyNoteWindow(note).Show();
            }

            if (TaskSync.IsEnabled) await InitializeSyncAsync();
            else LoadLocalTasks();
        }

        // ------------------------------------------------------------------ window look and placement

        private PixelRect WorkArea => (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

        private double Scaling => (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.Scaling ?? 1;

        /// <summary>Where the flyout sits: the corner chosen in settings, or where the user dragged it.</summary>
        private PixelPoint TargetPosition(out bool fromTop)
        {
            var area = WorkArea;
            int width = (int)(Width * Scaling), height = (int)(Height * Scaling), gap = (int)(12 * Scaling);

            if (_isCustomDragged)
            {
                fromTop = _customPosition.Y < area.Y + area.Height / 2;
                return _customPosition;
            }

            var position = AppConfig.Settings.Position;
            int x = position switch
            {
                WidgetPosition.TopLeft or WidgetPosition.BottomLeft => area.X + gap,
                WidgetPosition.TopCenter or WidgetPosition.BottomCenter => area.X + (area.Width - width) / 2,
                _ => area.Right - width - gap
            };
            fromTop = position is WidgetPosition.TopLeft or WidgetPosition.TopCenter or WidgetPosition.TopRight;
            int y = fromTop ? area.Y + gap : area.Bottom - height - gap;
            return new PixelPoint(x, y);
        }

        /// <summary>Shows the flyout with the Windows app's slide-and-fade (the content slides, so it also works on Wayland).</summary>
        public void ShowFlyout()
        {
            _hiding = false;
            var target = TargetPosition(out bool fromTop);
            if (!IsVisible) Show();
            Position = target;
            Activate();

            var slide = SlideTransform();
            slide.Y(0, 350, Ease.CubicOut, from: fromTop ? -50 : 50);
            Chrome.Opacity(1, 250, Ease.CubicOut, from: 0);
        }

        public void HideFlyout()
        {
            if (!IsVisible || _hiding) return;
            _hiding = true;
            TargetPosition(out bool fromTop);
            SlideTransform().Y(fromTop ? -50 : 50, 200, Ease.CubicIn);
            Chrome.Opacity(0, 150, Ease.CubicIn, done: () =>
            {
                if (!_hiding) return;
                Hide();
                Topmost = AppConfig.Settings.StayOnTop;
                GC.Collect(); // Like the Windows app, give memory back while hidden
            });
        }

        public void ToggleFlyout()
        {
            if (IsVisible && !_hiding) HideFlyout();
            else ShowFlyout();
        }

        private TranslateTransform SlideTransform()
        {
            if (SlideRoot.RenderTransform is not TranslateTransform t)
                SlideRoot.RenderTransform = t = new TranslateTransform();
            return t;
        }

        private void OnDeactivated()
        {
            ClearFocus();
            if (!AppConfig.Settings.StayOnTop && !_suppressHide && !_reorder.IsDragging)
            {
                Topmost = true; // Stay visible above the app that was clicked while animating away
                HideFlyout();
            }
        }

        private bool _suppressHide;

        private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AppConfig.Settings.IsLocked || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Tween.Stop(SlideTransform(), "Y");
            SlideTransform().Y = 0;
            BeginMoveDrag(e);
            _isCustomDragged = true;
            _customPosition = Position;
        }

        private void ClearFocus() => FocusManager?.ClearFocus();

        // ------------------------------------------------------------------ header buttons

        private void UpdatePinIcon() =>
            PinIcon.IconVariant = AppConfig.Settings.StayOnTop ? IconVariant.Filled : IconVariant.Regular;

        private void PinButton_Click(object? sender, RoutedEventArgs e)
        {
            AppConfig.Settings.StayOnTop = !AppConfig.Settings.StayOnTop;
            Topmost = AppConfig.Settings.StayOnTop;
            UpdatePinIcon();
            AppConfig.Save();
        }

        private void AddNote_Click(object? sender, RoutedEventArgs e)
        {
            // Opens to the left of the flyout, like the Windows app
            StickyNoteWindow.OpenNew(this);
        }

        private Settings.SettingsWindow? _settingsWindow;

        private void Settings_Click(object? sender, RoutedEventArgs e) => OpenSettings();

        public void OpenSettings()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            // Remember which sync account was in use, to notice a switch (e.g. Google to Tasks.org)
            bool previousSyncState = TaskSync.IsEnabled;
            string previousAccount = TaskSync.AccountKey;
            var previousPosition = AppConfig.Settings.Position;

            _settingsWindow = new Settings.SettingsWindow();
            _settingsWindow.Closed += async (s, args) =>
            {
                _settingsWindow = null;
                Topmost = AppConfig.Settings.StayOnTop;
                UpdatePinIcon();
                if (previousPosition != AppConfig.Settings.Position) _isCustomDragged = false;
                ShowFlyout();

                if (TaskSync.IsEnabled)
                {
                    bool accountChanged = previousAccount != TaskSync.AccountKey;

                    // Picks up a Google login made in Settings without opening the browser again
                    if (_backend == null && !accountChanged) TaskSync.ConnectWithoutPrompt();

                    if (_backend == null || accountChanged)
                    {
                        await InitializeSyncAsync();
                    }
                    else
                    {
                        await SyncTasksFromRemote();
                        await PopulateTrayMenu();
                        await StickyNoteSync.SyncAsync();
                    }
                }
                else if (previousSyncState)
                {
                    TaskSync.Disconnect();
                    LoadLocalTasks(); // Show the local lists if sync was just turned off
                }
            };
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        /// <summary>The list shown changed in Settings: show its tasks.</summary>
        public async Task ReloadSelectedListAsync()
        {
            if (TaskSync.IsEnabled) await SyncTasksFromRemote();
            else LoadLocalTasks();
            await PopulateTrayMenu();
        }

        // ------------------------------------------------------------------ local tasks

        public void LoadLocalTasks()
        {
            var localTasks = LocalDataManager.LoadTasks(AppConfig.Settings.SelectedListId);
            foreach (var old in MyTasks) old.PropertyChanged -= OnTaskPropertyChanged;
            MyTasks.Clear();
            foreach (var task in localTasks)
            {
                task.PropertyChanged += OnTaskPropertyChanged;
                MyTasks.Add(task);
            }
            _ = PopulateTrayMenu();
        }

        // ------------------------------------------------------------------ sync

        private async Task InitializeSyncAsync()
        {
            try
            {
                if (TaskSync.Provider == TaskSync.Google && !AppPaths.HasCredentials)
                {
                    await MessageWindow.ShowAsync("Missing Credentials",
                        "Welcome to TasksBar!\n\nTo connect to Google, you need to provide your own API credentials.\n\n" +
                        $"Please place your 'credentials.json' file in {AppPaths.DataDirectory} and try again.");
                    return;
                }

                await TaskSync.ConnectAsync();
                await SyncTasksFromRemote();
                await PopulateTrayMenu();
                await StickyNoteSync.SyncAsync();
            }
            catch (Exception ex)
            {
                await MessageWindow.ShowAsync("TasksBar", $"Failed to connect to {TaskSync.ProviderName}: {ex.Message}");
            }
        }

        private bool _isSyncingTasks, _syncRequested, _isHandlingExpiredSignIn, _refreshAfterDrag;

        // "@default" is Google's name for the main list; other services get their first list instead
        private async Task<string?> ResolveSelectedListAsync()
        {
            if (_backend == null) return null;
            string listId = AppConfig.Settings.SelectedListId;
            if (listId != "@default" || TaskSync.Provider == TaskSync.Google) return listId;

            string? first = await _backend.GetDefaultListIdAsync();
            if (first != null)
            {
                AppConfig.Settings.SelectedListId = first;
                AppConfig.Save();
            }
            return first;
        }

        /// <summary>Never throws: a failed refresh keeps the tasks already on screen.</summary>
        public async Task SyncTasksFromRemote()
        {
            if (_backend == null) return;
            if (_reorder.IsDragging)
            {
                _refreshAfterDrag = true;
                return;
            }
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
            catch (RemoteNotFoundException) when (AppConfig.Settings.SelectedListId != "@default")
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

        private async Task DownloadAndShowTasks()
        {
            var downloaded = new List<TaskItem>();
            string? listId = await ResolveSelectedListAsync();
            if (listId != null)
            {
                foreach (var remote in await _backend!.GetTasksAsync(listId, AppConfig.Settings.ShowCompletedTasks))
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
            foreach (var old in MyTasks) old.PropertyChanged -= OnTaskPropertyChanged;
            MyTasks.Clear();
            foreach (var task in downloaded)
            {
                task.PropertyChanged += OnTaskPropertyChanged;
                MyTasks.Add(task);
            }
        }

        private async Task HandleSyncErrorAsync(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Task sync request failed: {ex}");
            if (!TaskSync.IsAuthError(ex) || _isHandlingExpiredSignIn) return;

            _isHandlingExpiredSignIn = true;
            try
            {
                string service = TaskSync.ProviderName;
                await TaskSync.ForgetSignInAsync();

                if (TaskSync.Provider == TaskSync.Google)
                {
                    if (await MessageWindow.AskAsync("TasksBar", "Your Google sign-in has expired.\n\nSign in again now?"))
                        await InitializeSyncAsync();
                }
                else
                {
                    await MessageWindow.ShowAsync("TasksBar",
                        $"{service} no longer accepts the saved app password (it may have been revoked).\n\n" +
                        "Connect again with a new app password in Settings → Behavior & Sync.");
                }
            }
            finally
            {
                _isHandlingExpiredSignIn = false;
            }
        }

        // A task that was already deleted or completed elsewhere just gets refreshed away
        private async Task RunSyncRequest(Func<Task> request)
        {
            try { await request(); }
            catch (RemoteNotFoundException) { await SyncTasksFromRemote(); }
            catch (Exception ex) { await HandleSyncErrorAsync(ex); }
        }

        // ------------------------------------------------------------------ editing tasks

        private readonly HashSet<TaskItem> _creatingTasks = new();
        private readonly HashSet<TaskItem> _updateAfterCreate = new();

        private async void OnTaskPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is TaskItem task) await TaskItem_PropertyChanged(task, e.PropertyName ?? "");
        }

        private async Task TaskItem_PropertyChanged(TaskItem item, string propertyName)
        {
            if (!TaskSync.IsEnabled)
            {
                item.Id ??= Guid.NewGuid().ToString();
                LocalDataManager.SaveTasks(MyTasks, AppConfig.Settings.SelectedListId);
                return;
            }

            var backend = _backend;
            if (backend == null) return;

            // A new task is created once it has a title. Edits made while that request runs are sent after it.
            if (item.Id == null)
            {
                if (_creatingTasks.Contains(item))
                {
                    _updateAfterCreate.Add(item);
                    return;
                }
                if ((propertyName == nameof(TaskItem.Title) || propertyName == nameof(TaskItem.Details)) && !string.IsNullOrWhiteSpace(item.Title))
                {
                    var newTask = new RemoteTask { Title = item.Title, Notes = item.Details ?? "" };
                    _creatingTasks.Add(item);
                    try
                    {
                        await RunSyncRequest(async () =>
                        {
                            var created = await backend.CreateTaskAsync(AppConfig.Settings.SelectedListId, newTask);
                            item.Id = created.Id;
                        });
                    }
                    finally
                    {
                        _creatingTasks.Remove(item);
                    }
                    if (_updateAfterCreate.Remove(item) && item.Id != null)
                        await TaskItem_PropertyChanged(item, nameof(TaskItem.Details));
                }
                return;
            }

            if (propertyName is nameof(TaskItem.IsCompleted) or nameof(TaskItem.Details) or nameof(TaskItem.Title))
            {
                // Erasing the whole title deletes the task
                if (string.IsNullOrWhiteSpace(item.Title))
                {
                    await RunSyncRequest(() => backend.DeleteTaskAsync(AppConfig.Settings.SelectedListId, item.Id));
                    Dispatcher.UIThread.Post(() => MyTasks.Remove(item));
                    return;
                }

                var update = new RemoteTask { Id = item.Id, Title = item.Title, Notes = item.Details ?? "", Completed = item.IsCompleted };
                await RunSyncRequest(() => backend.UpdateTaskAsync(AppConfig.Settings.SelectedListId, update));
            }
        }

        private void AddTaskTop_Click(object? sender, RoutedEventArgs e) => AddTask();

        private void AddTask()
        {
            var task = new TaskItem { IsNew = true, Title = "", Details = "", IsCompleted = false };
            task.PropertyChanged += OnTaskPropertyChanged;
            MyTasks.Insert(0, task);
            if (!TaskSync.IsEnabled) LocalDataManager.SaveTasks(MyTasks, AppConfig.Settings.SelectedListId);

            TasksScroll.Offset = new Vector(TasksScroll.Offset.X, 0);
            Dispatcher.UIThread.Post(() => FocusTaskField(task, details: false, double.NaN, enterFromBelow: false), DispatcherPriority.Loaded);
        }

        private void PrepareRow(Control container)
        {
            if (container.DataContext is TaskItem task && task.IsNew)
            {
                task.IsNew = false;
                FocusTaskField(task, details: false, double.NaN, enterFromBelow: false);
            }
            UpdateFlowDirection(container);
        }

        // Hebrew/Arabic text reads right to left, like the Windows app's AutoRtlConverter
        private static void UpdateFlowDirection(Control container)
        {
            foreach (var box in container.GetVisualDescendants().OfType<TextBox>())
            {
                box.FlowDirection = HasRtl(box.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                box.TextChanged -= OnBoxTextChanged;
                box.TextChanged += OnBoxTextChanged;
            }
        }

        private static void OnBoxTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is TextBox box) box.FlowDirection = HasRtl(box.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }

        internal static bool HasRtl(string? text) => !string.IsNullOrWhiteSpace(text) && text.Any(c => c >= 0x0590 && c <= 0x06FF);

        // Leaving a brand-new task without typing anything removes it
        private void TasksList_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (e.Source is not Control source || source.DataContext is not TaskItem task) return;
            var card = source.FindAncestorOfType<Border>(includeSelf: true);
            Dispatcher.UIThread.Post(() =>
            {
                if (card != null && card.IsKeyboardFocusWithin) return;
                if (task.Id == null && string.IsNullOrWhiteSpace(task.Title) && !_creatingTasks.Contains(task))
                    MyTasks.Remove(task);
            }, DispatcherPriority.Input);
        }

        // ------------------------------------------------------------------ completing

        private async void CheckCircle_Toggled(object? sender, EventArgs e)
        {
            if (sender is not CheckCircle circle || circle.DataContext is not TaskItem task) return;
            ClearFocus();
            task.IsCompleted = circle.IsChecked;

            var row = TasksList.ContainerFromItem(task);
            SetCompletedLook(row, task.IsCompleted);
            if (!task.IsCompleted) return;

            // Ripple + particle burst from the check circle
            var center = circle.TranslatePoint(new Point(9, 9), EffectsLayer) ?? new Point(0, 0);
            CompletionEffects.PlayBurst(EffectsLayer, center, AccentColor());

            // Let the checkmark and strikethrough register before the task leaves
            await Task.Delay(900);
            if (!task.IsCompleted) return;

            if (row != null)
            {
                circle.IsHitTestVisible = false; // Too late to undo once it's leaving
                await CompletionEffects.SlideOutAndCollapseAsync(row, 6);
            }

            task.PropertyChanged -= OnTaskPropertyChanged;
            MyTasks.Remove(task);
            LocalDataManager.SaveTasks(MyTasks, AppConfig.Settings.SelectedListId);
            ShowToast(task.Title);

            var backend = _backend;
            if (TaskSync.IsEnabled && backend != null && task.Id != null)
            {
                var completed = new RemoteTask { Id = task.Id, Title = task.Title, Notes = task.Details ?? "", Completed = true };
                await RunSyncRequest(() => backend.UpdateTaskAsync(AppConfig.Settings.SelectedListId, completed));
            }
        }

        /// <summary>Completed tasks fade to half opacity with a line drawn through the title.</summary>
        private void SetCompletedLook(Control? row, bool completed)
        {
            if (row == null) return;
            foreach (var box in row.GetVisualDescendants().OfType<TextBox>())
                box.Opacity(completed ? 0.5 : 1, completed ? 300 : 200);

            var title = row.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.Name == "TaskTitleInput");
            if (title == null) return;
            var layer = title.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
            if (layer?.TextLayout == null) return;

            // The strike line lives in the title's adorner layer, so it moves with the row
            var adorners = AdornerLayer.GetAdornerLayer(title);
            if (adorners == null) return;
            var existing = adorners.Children.OfType<Avalonia.Controls.Shapes.Rectangle>()
                                   .FirstOrDefault(r => AdornerLayer.GetAdornedElement(r) == title);
            if (!completed)
            {
                if (existing != null) existing.Opacity(0, 150, done: () => adorners.Children.Remove(existing));
                return;
            }

            var firstLine = layer.TextLayout.TextLines.FirstOrDefault();
            double textWidth = firstLine?.WidthIncludingTrailingWhitespace ?? title.Bounds.Width;
            double lineY = (firstLine?.Height ?? 19) / 2 + 1;
            var strike = existing ?? new Avalonia.Controls.Shapes.Rectangle
            {
                Height = 1.5, Width = 0, IsHitTestVisible = false,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                HorizontalAlignment = title.FlowDirection == FlowDirection.RightToLeft
                    ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left,
                Margin = new Thickness(0, lineY, 0, 0)
            };
            strike.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, title.GetResourceObservable("TextFillColorPrimaryBrush"));
            if (existing == null)
            {
                AdornerLayer.SetAdornedElement(strike, title);
                adorners.Children.Add(strike);
            }
            strike.Opacity = 0.5;
            Tween.To(strike, "Width", () => strike.Width, v => strike.Width = v, Math.Min(textWidth, title.Bounds.Width), 250, Ease.CubicOut, from: 0);
        }

        private Color AccentColor() =>
            (this.FindResource(ActualThemeVariant, "AccentFillColorDefaultBrush") as ISolidColorBrush)?.Color ?? Color.Parse("#4CC2FF");

        // ------------------------------------------------------------------ ⋯ menu: move and delete

        private void MoreOptions_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not TaskItem task) return;
            ClearFocus();

            var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedRight };
            var moveItem = new MenuItem { Header = "Move to...", IsEnabled = task.Id != null };
            moveItem.Items.Add(new MenuItem { Header = "Loading...", IsEnabled = false });
            bool listsLoaded = false;
            moveItem.SubmenuOpened += async (s, args) =>
            {
                if (listsLoaded) return;
                listsLoaded = true;
                var targets = await GetMoveTargetListsAsync();
                moveItem.Items.Clear();
                if (targets.Count == 0)
                {
                    moveItem.Items.Add(new MenuItem { Header = "No other lists", IsEnabled = false });
                    return;
                }
                foreach (var (listId, listTitle) in targets)
                {
                    var listItem = new MenuItem { Header = listTitle };
                    listItem.Click += async (s2, a2) => await MoveTaskToList(task, listId, listTitle);
                    moveItem.Items.Add(listItem);
                }
            };

            var deleteItem = new MenuItem { Header = "Delete" };
            deleteItem.Click += async (s, args) =>
            {
                task.PropertyChanged -= OnTaskPropertyChanged;
                MyTasks.Remove(task);
                if (TaskSync.IsEnabled)
                {
                    var backend = _backend;
                    if (backend != null && task.Id != null)
                        await RunSyncRequest(() => backend.DeleteTaskAsync(AppConfig.Settings.SelectedListId, task.Id));
                }
                else
                {
                    LocalDataManager.SaveTasks(MyTasks, AppConfig.Settings.SelectedListId);
                }
            };

            menu.Items.Add(moveItem);
            menu.Items.Add(deleteItem);
            _suppressHide = true;
            menu.Closed += (s, args) => _suppressHide = false;
            menu.Open(button);
        }

        private async Task<List<(string Id, string Title)>> GetMoveTargetListsAsync()
        {
            var targets = new List<(string Id, string Title)>();
            if (TaskSync.IsEnabled)
            {
                var backend = _backend;
                if (backend == null) return targets;
                try
                {
                    foreach (var list in await backend.GetListsAsync())
                    {
                        if (list.Id == AppConfig.Settings.SelectedListId || StickyNoteSync.IsNotesList(list.Id, list.Title)) continue;
                        targets.Add((list.Id, list.Title));
                    }
                }
                catch (Exception ex) { await HandleSyncErrorAsync(ex); }
            }
            else
            {
                foreach (var list in LocalDataManager.GetLocalLists())
                {
                    if (list.Id == AppConfig.Settings.SelectedListId) continue;
                    targets.Add((list.Id, list.Title));
                }
            }
            return targets;
        }

        private async Task MoveTaskToList(TaskItem task, string destinationListId, string destinationTitle)
        {
            string sourceListId = AppConfig.Settings.SelectedListId;
            bool moved = false;

            if (TaskSync.IsEnabled)
            {
                var backend = _backend;
                if (backend == null || task.Id == null) return;
                await RunSyncRequest(async () =>
                {
                    await backend.MoveTaskAsync(sourceListId, task.Id, destinationListId);
                    moved = true;
                });
                if (!moved && _backend != null)
                    await MessageWindow.ShowAsync("TasksBar", $"Couldn't move \"{task.Title}\" to {destinationTitle}.\n\nCheck your connection and try again.");
            }
            else
            {
                var destinationTasks = LocalDataManager.LoadTasks(destinationListId);
                destinationTasks.Insert(0, task);
                LocalDataManager.SaveTasks(destinationTasks, destinationListId);
                moved = true;
            }

            if (!moved) return;
            if (AppConfig.Settings.SelectedListId == sourceListId && MyTasks.Contains(task))
            {
                task.PropertyChanged -= OnTaskPropertyChanged;
                MyTasks.Remove(task);
                if (!TaskSync.IsEnabled) LocalDataManager.SaveTasks(MyTasks, sourceListId);
            }
            ShowToast(task.Title, $"Moved to {destinationTitle}", celebrate: false);
        }

        // ------------------------------------------------------------------ reordering

        private async void OnTaskReordered(TaskItem moved)
        {
            if (!TaskSync.IsEnabled)
            {
                LocalDataManager.SaveTasks(MyTasks, AppConfig.Settings.SelectedListId);
                return;
            }
            var backend = _backend;
            if (backend == null || moved.Id == null) return;

            var order = MyTasks.Where(t => t.Id != null).Select(t => new RemoteTask { Id = t.Id, Parent = t.ParentId }).ToList();
            await RunSyncRequest(() => backend.ReorderTaskAsync(AppConfig.Settings.SelectedListId, order, moved.Id));
        }

        // ------------------------------------------------------------------ tray menu

        public async Task PopulateTrayMenu()
        {
            List<RemoteList>? remoteLists = null;
            var backend = _backend;
            if (TaskSync.IsEnabled && backend != null)
            {
                try { remoteLists = await backend.GetListsAsync(); } catch { /* offline */ }
            }

            var tray = TrayIcon.GetIcons(Application.Current!)?.FirstOrDefault();
            if (tray == null) return;
            var menu = new NativeMenu();

            IEnumerable<(string Id, string Title)> lists = TaskSync.IsEnabled
                ? (remoteLists ?? new List<RemoteList>()).Where(l => !StickyNoteSync.IsNotesList(l.Id, l.Title)).Select(l => (l.Id, l.Title))
                : LocalDataManager.GetLocalLists().Select(l => (l.Id, l.Title));

            foreach (var (id, title) in lists)
            {
                // The current list gets a check, like the Windows tray menu
                var item = new NativeMenuItem(title)
                {
                    ToggleType = NativeMenuItemToggleType.Radio,
                    IsChecked = id == AppConfig.Settings.SelectedListId
                };
                item.Click += async (s, e) =>
                {
                    AppConfig.Settings.SelectedListId = id;
                    AppConfig.Save();
                    if (TaskSync.IsEnabled) await SyncTasksFromRemote();
                    else LoadLocalTasks();
                    await PopulateTrayMenu();
                    ShowFlyout();
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new NativeMenuItemSeparator());
            var exit = new NativeMenuItem("Exit TasksBar");
            exit.Click += (s, e) => (Application.Current as App)?.Quit();
            menu.Items.Add(exit);
            tray.Menu = menu;
        }
    }
}
