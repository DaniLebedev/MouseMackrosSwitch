using System.Windows;
using System.Windows.Input;

namespace MouseMackros;

public partial class AnnotationDialog : Window
{
    private AnnotationDialog(string title, string hint, string current)
    {
        InitializeComponent();
        TitleText.Text = title;
        HintText.Text = hint;
        ValueBox.Text = current;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string? Result { get; private set; }

    public static string? Edit(Window owner, string title, string hint, string current)
    {
        var dialog = new AnnotationDialog(title, hint, current) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            return;
        }
        base.OnKeyDown(e);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = ValueBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
