using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using TasksBar.Services;
using TasksBar.Services.Sync;

namespace TasksBar.Linux.Views.Settings
{
    /// <summary>Picks which list the flyout shows; without sync, also creates local lists.</summary>
    public partial class ListsPage : UserControl
    {
        public ListsPage()
        {
            InitializeComponent();
            NewListNameInput.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) CreateList_Click(this, new RoutedEventArgs());
            };
            LoadLists();
        }

        private async void LoadLists()
        {
            ListsPanel.Children.Clear();
            var lists = new List<(string Id, string Title)>();

            if (TaskSync.IsEnabled)
            {
                CreateListPanel.IsVisible = false;
                ShowStatus("Loading lists...");
                try
                {
                    var backend = TaskSync.Current;
                    if (backend != null)
                        lists.AddRange((await backend.GetListsAsync()).Where(l => !StickyNoteSync.IsNotesList(l.Id, l.Title)).Select(l => (l.Id, l.Title)));
                    ShowStatus(backend == null ? $"Not connected to {TaskSync.ProviderName} yet." : null);
                }
                catch (Exception ex)
                {
                    ShowStatus($"Couldn't load your lists: {ex.Message}");
                }
            }
            else
            {
                CreateListPanel.IsVisible = true;
                lists.AddRange(LocalDataManager.GetLocalLists().Select(l => (l.Id, l.Title)));
            }

            foreach (var (id, title) in lists) ListsPanel.Children.Add(CreateCard(id, title));
        }

        private void ShowStatus(string? text)
        {
            StatusText.Text = text;
            StatusText.IsVisible = text != null;
        }

        private Control CreateCard(string id, string title)
        {
            // Like WPF-UI's CardAction: the whole card is clickable, with a chevron on the right
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center });
            var chevron = new SymbolIcon { Symbol = Symbol.ChevronRight, FontSize = 16, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chevron, 1);
            grid.Children.Add(chevron);

            var card = new Border { Child = grid, MinHeight = 0, Padding = new Thickness(16), Margin = new Thickness(0, 0, 24, 8) };
            card.Classes.Add("card");
            card.Classes.Add("clickable");
            card.PointerPressed += async (s, e) =>
            {
                if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
                AppConfig.Settings.SelectedListId = id;
                AppConfig.Save();
                if ((Application.Current as App)?.Flyout is { } flyout) await flyout.ReloadSelectedListAsync();
            };
            return card;
        }

        private async void CreateList_Click(object? sender, RoutedEventArgs e)
        {
            string name = (NewListNameInput.Text ?? "").Trim();
            if (name.Length == 0) return;

            var localLists = LocalDataManager.GetLocalLists();
            var newList = new LocalTaskList { Id = Guid.NewGuid().ToString(), Title = name };
            localLists.Add(newList);
            LocalDataManager.SaveLocalLists(localLists);
            ListsPanel.Children.Add(CreateCard(newList.Id, newList.Title));
            NewListNameInput.Text = "";

            // The tray menu lists them too
            if ((Application.Current as App)?.Flyout is { } flyout) await flyout.PopulateTrayMenu();
        }
    }
}
