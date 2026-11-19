using System.Windows;

namespace MouseMackros;

public partial class SubProfileDialog : Window
{
    public SubProfileDialog(string parentName, string suggestedName, string suggestedTitle, string currentTitle)
    {
        InitializeComponent();

        ParentText.Text = parentName;
        NameBox.Text = suggestedName;
        TitleBox.Text = suggestedTitle;
        CurrentTitleText.Text = string.IsNullOrWhiteSpace(currentTitle)
            ? "Текущее окно не определено — впишите часть заголовка вручную."
            : "Текущий заголовок: " + currentTitle;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string ProfileName => NameBox.Text;

    public string TitleContains => TitleBox.Text;

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleContains))
        {
            AppDialog.ShowError(
                "Укажите часть заголовка окна. Без неё подпрофиль будет совпадать всегда\n" +
                "и полностью заменит родительский профиль.",
                "Нужно условие");
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
