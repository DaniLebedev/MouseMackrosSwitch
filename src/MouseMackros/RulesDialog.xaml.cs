using System.Windows;
using MouseMackros.Model;

namespace MouseMackros;

public partial class RulesDialog : Window
{
    private readonly Profile _profile;

    public RulesDialog(Profile profile)
    {
        InitializeComponent();
        _profile = profile;

        ProcessesBox.Text = string.Join(", ", profile.Match.Processes);
        TitleBox.Text = profile.Match.TitleContains ?? "";
        ClassBox.Text = profile.Match.WindowClass ?? "";
        PriorityBox.Text = profile.Match.Priority.ToString();
    }

    public bool Saved { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _profile.Match.Processes = ProcessesBox.Text
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _profile.Match.TitleContains = string.IsNullOrWhiteSpace(TitleBox.Text) ? null : TitleBox.Text.Trim();
        _profile.Match.WindowClass = string.IsNullOrWhiteSpace(ClassBox.Text) ? null : ClassBox.Text.Trim();
        _profile.Match.Priority = int.TryParse(PriorityBox.Text.Trim(), out var priority) ? priority : 0;

        Saved = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
