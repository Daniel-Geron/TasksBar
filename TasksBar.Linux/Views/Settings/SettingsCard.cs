using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace TasksBar.Linux.Views.Settings
{
    /// <summary>
    /// One settings row, like WPF-UI's CardControl in the Windows app: an icon, a title with a gray
    /// description underneath, and the control (switch, list, buttons) on the right.
    /// </summary>
    public class SettingsCard : Border
    {
        private readonly SymbolIcon _icon;
        private readonly TextBlock _header;
        private readonly TextBlock _description;
        private readonly TextBlock _status;
        private readonly ContentControl _action;

        // Styled like any Border, so the "Border.card" style applies to it too
        protected override System.Type StyleKeyOverride => typeof(Border);

        public SettingsCard()
        {
            Classes.Add("card");

            _icon = new SymbolIcon { FontSize = 24, Width = 24, Height = 24, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            _header = new TextBlock { FontSize = 14, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap };
            _description = new TextBlock { FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, IsVisible = false };
            _status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
            _status.Bind(TextBlock.ForegroundProperty, _status.GetResourceObservable("SystemFillColorCriticalBrush"));
            _action = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _header, _description, _status } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            grid.Children.Add(_icon);
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            Grid.SetColumn(_action, 2);
            grid.Children.Add(_action);
            Child = grid;
        }

        public Symbol Icon
        {
            get => _icon.Symbol;
            set => _icon.Symbol = value;
        }

        public string? Header
        {
            get => _header.Text;
            set => _header.Text = value;
        }

        public string? Description
        {
            get => _description.Text;
            set
            {
                _description.Text = value;
                _description.IsVisible = !string.IsNullOrEmpty(value);
            }
        }

        /// <summary>A red line under the description for problems (e.g. a shortcut that's already taken).</summary>
        public string? Status
        {
            get => _status.Text;
            set
            {
                _status.Text = value;
                _status.IsVisible = !string.IsNullOrEmpty(value);
            }
        }

        [Content]
        public object? Action
        {
            get => _action.Content;
            set => _action.Content = value;
        }
    }
}
