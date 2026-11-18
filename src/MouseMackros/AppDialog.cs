using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace MouseMackros;

/// <summary>Small dark themed message box.</summary>
public static class AppDialog
{
    public static void ShowError(string text, string title = "Ошибка") => Show(text, title, false);

    public static void ShowInfo(string text, string title = "MouseMackros") => Show(text, title, false);

    public static bool Confirm(string text, string title = "Подтверждение") => Show(text, title, true);

    private static bool Show(string text, string title, bool withCancel)
    {
        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.Height,
            Width = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x24)),
            Owner = Application.Current?.MainWindow,
            ShowInTaskbar = false
        };

        var root = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x30, 0x3A)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xF2)),
            Margin = new Thickness(0, 0, 0, 10)
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB6, 0xBE, 0xCE)),
            LineHeight = 19
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        bool result = false;

        var ok = MakeButton(withCancel ? "Да" : "ОК", true);
        ok.Click += (_, _) =>
        {
            result = true;
            window.Close();
        };
        buttons.Children.Add(ok);

        if (withCancel)
        {
            var cancel = MakeButton("Отмена", false);
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.Click += (_, _) => window.Close();
            buttons.Children.Add(cancel);
        }

        panel.Children.Add(buttons);
        root.Child = panel;
        window.Content = root;
        window.ShowDialog();
        return result;
    }

    private static Button MakeButton(string text, bool accent)
    {
        var style = Application.Current.TryFindResource("FlatButton") as Style;
        var button = new Button
        {
            Content = text,
            MinWidth = 90,
            Style = style,
            Padding = new Thickness(14, 7, 14, 7)
        };
        if (accent)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));
            button.Foreground = new SolidColorBrush(Color.FromRgb(0x0E, 0x16, 0x26));
            button.BorderBrush = button.Background;
        }
        return button;
    }
}
