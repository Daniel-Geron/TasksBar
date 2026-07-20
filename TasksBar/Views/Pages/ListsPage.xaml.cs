using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TasksBar
{
    public class ListDisplayModel
    {
        public string Id { get; set; }
        public string Title { get; set; }
    }

    public partial class ListsPage : Page
    {
        public ObservableCollection<ListDisplayModel> DisplayLists { get; set; } = new ObservableCollection<ListDisplayModel>();

        public ListsPage()
        {
            InitializeComponent();
            ListsListView.ItemsSource = DisplayLists;
            LoadLists();
        }

        private async void LoadLists()
        {
            DisplayLists.Clear();

            if (AppConfig.Settings.EnableGoogleSync)
            {
                CreateListPanel.Visibility = Visibility.Collapsed;
                var service = GoogleAuthManager.GetTasksService();
                if (service != null)
                {
                    try
                    {
                        var lists = await service.Tasklists.List().ExecuteAsync();
                        foreach (var lst in lists.Items)
                        {
                            DisplayLists.Add(new ListDisplayModel { Id = lst.Id, Title = lst.Title });
                        }
                    }
                    catch { /* Offline handling */ }
                }
            }
            else
            {
                CreateListPanel.Visibility = Visibility.Visible;
                var localLists = LocalDataManager.GetLocalLists();
                foreach (var lst in localLists)
                {
                    DisplayLists.Add(new ListDisplayModel { Id = lst.Id, Title = lst.Title });
                }
            }
        }

        private void CreateList_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NewListNameInput.Text)) return;

            var localLists = LocalDataManager.GetLocalLists();
            var newList = new LocalTaskList { Id = Guid.NewGuid().ToString(), Title = NewListNameInput.Text.Trim() };
            localLists.Add(newList);

            LocalDataManager.SaveLocalLists(localLists);
            DisplayLists.Add(new ListDisplayModel { Id = newList.Id, Title = newList.Title });

            NewListNameInput.Text = "";

            // Force tray menu update
            var mainWindow = Application.Current.Windows.Cast<Window>().OfType<TasksFlyoutWindow>().FirstOrDefault();
            if (mainWindow != null) _ = mainWindow.PopulateTrayMenu();
        }

        private void SelectList_Click(object sender, RoutedEventArgs e)
        {
            // THE FIX: Look for CardAction instead of Button
            if (sender is Wpf.Ui.Controls.CardAction card && card.Tag is ListDisplayModel selectedList)
            {
                AppConfig.Settings.SelectedListId = selectedList.Id;
                AppConfig.Save();

                var mainWindow = Application.Current.Windows.Cast<Window>().OfType<TasksFlyoutWindow>().FirstOrDefault();
                if (mainWindow != null)
                {
                    if (AppConfig.Settings.EnableGoogleSync)
                        _ = mainWindow.SyncTasksFromGoogle();
                    else
                        mainWindow.LoadLocalTasks();
                }
            }
        }
    }
}