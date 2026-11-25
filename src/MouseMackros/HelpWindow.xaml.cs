using System.Windows;

namespace MouseMackros;

public partial class HelpWindow : Window
{
    private HelpWindow()
    {
        InitializeComponent();
    }

    public static void ShowHelp(Window owner, string title, string text)
    {
        var window = new HelpWindow { Owner = owner, Title = title };
        window.TitleText.Text = title;
        window.BodyBox.Text = text;
        window.BodyBox.ScrollToHome();
        window.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
