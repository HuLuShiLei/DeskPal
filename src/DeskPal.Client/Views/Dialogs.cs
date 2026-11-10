using System.Windows;
using System.Windows.Controls;
using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Views;

/// <summary>Small modal dialogs built in code.</summary>
public static class Dialogs
{
    public static string? Prompt(Window? owner, string title, string message, string initial = "", bool readOnly = false)
    {
        var box = new TextBox { Text = initial, IsReadOnly = readOnly, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(4) };
        var ok = Show(owner, title, Stack(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, box), box);
        return ok ? box.Text.Trim() : null;
    }

    public static T? Choose<T>(Window? owner, string title, string message, IReadOnlyList<(string Label, T Value)> items) where T : struct
    {
        var combo = new ComboBox { Margin = new Thickness(0, 8, 0, 0), ItemsSource = items.Select(i => i.Label).ToList(), SelectedIndex = 0 };
        var ok = Show(owner, title, Stack(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, combo), combo);
        return ok && combo.SelectedIndex >= 0 ? items[combo.SelectedIndex].Value : null;
    }

    public static (ActivityCategory Category, bool ShareToRoom)? Correction(Window? owner, string target, ActivityCategory current, string? roomName)
    {
        var categories = Enum.GetValues<ActivityCategory>().Where(c => c != ActivityCategory.None).ToList();
        var combo = new ComboBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            ItemsSource = categories.Select(PresenceFormatter.CategoryName).ToList(),
            SelectedIndex = Math.Max(0, categories.IndexOf(current)),
        };
        var share = new CheckBox
        {
            Content = roomName is null ? "同步给 Room（当前没有 Room）" : $"同步给 Room「{roomName}」的所有成员",
            IsChecked = roomName is not null,
            IsEnabled = roomName is not null,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var text = new TextBlock { Text = $"把「{target}」归类为：", TextWrapping = TextWrapping.Wrap };
        var ok = Show(owner, "这条分错了", Stack(text, combo, share), combo);
        return ok ? (categories[combo.SelectedIndex], share.IsChecked == true) : null;
    }

    public static PrivacyProfile? Privacy(Window? owner, string roomName, PrivacyProfile profile)
    {
        var levels = Enum.GetValues<PrivacyLevel>().ToList();
        var categories = Enum.GetValues<ActivityCategory>().Where(c => c != ActivityCategory.None).ToList();
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var combos = new List<ComboBox>();
        for (var i = 0; i < categories.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = PresenceFormatter.CategoryName(categories[i]), VerticalAlignment = VerticalAlignment.Center };
            var combo = new ComboBox
            {
                ItemsSource = levels.Select(PresenceFormatter.PrivacyLevelName).ToList(),
                SelectedIndex = levels.IndexOf(profile.LevelFor(categories[i])),
                Margin = new Thickness(0, 2, 0, 2),
            };
            Grid.SetRow(label, i);
            Grid.SetRow(combo, i);
            Grid.SetColumn(combo, 1);
            grid.Children.Add(label);
            grid.Children.Add(combo);
            combos.Add(combo);
        }

        var intro = new TextBlock
        {
            Text = $"设置 Room「{roomName}」的成员能看到你多少信息。只影响这个 Room。",
            TextWrapping = TextWrapping.Wrap,
        };
        if (!Show(owner, "隐私设置", Stack(intro, grid), combos[0]))
        {
            return null;
        }

        var result = new PrivacyProfile();
        for (var i = 0; i < categories.Count; i++)
        {
            result.Levels[categories[i]] = levels[combos[i].SelectedIndex];
        }

        return result;
    }

    public static void Error(Window? owner, string message) =>
        MessageBox.Show(owner ?? Application.Current.MainWindow!, message, "DeskPal", MessageBoxButton.OK, MessageBoxImage.Warning);

    public static bool Confirm(Window? owner, string message) =>
        MessageBox.Show(owner ?? Application.Current.MainWindow!, message, "DeskPal", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    private static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    private static bool Show(Window? owner, string title, UIElement content, Control focus)
    {
        var window = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner is { IsVisible: true } ? owner : null,
            ShowInTaskbar = owner is not { IsVisible: true },
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
        };
        var ok = new Button { Content = "确定", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 80, IsCancel = true };
        ok.Click += (_, _) => window.DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(content);
        root.Children.Add(buttons);
        window.Content = root;
        window.Loaded += (_, _) => focus.Focus();
        return window.ShowDialog() == true;
    }
}