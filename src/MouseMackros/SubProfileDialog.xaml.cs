using System.Windows;
using MouseMackros.Engine;

namespace MouseMackros;

public partial class SubProfileDialog : Window
{
    public SubProfileDialog(string parentName, string suggestedName, string suggestedCondition,
        string currentTitle, string currentUrl)
    {
        InitializeComponent();

        ParentText.Text = parentName;
        NameBox.Text = suggestedName;
        ConditionBox.Text = suggestedCondition;
        CurrentTitleText.Text = string.IsNullOrWhiteSpace(currentTitle)
            ? "Текущее окно не определено — впишите условие вручную."
            : "Текущий заголовок: " + currentTitle;
        CurrentUrlText.Text = string.IsNullOrWhiteSpace(currentUrl)
            ? "URL не определён (для Firefox/Chrome/Edge нужен доступ к адресной строке)."
            : "Текущий URL: " + currentUrl;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string ProfileName => NameBox.Text;

    /// <summary>Normalized condition expression.</summary>
    public string Condition { get; private set; } = "";

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var condition = ExpressionEvaluator.NormalizeCondition(ConditionBox.Text);
        if (string.IsNullOrWhiteSpace(condition))
        {
            AppDialog.ShowError(
                "Укажите условие. Без него подпрофиль будет совпадать всегда\n" +
                "и полностью заменит родительский профиль.",
                "Нужно условие");
            return;
        }

        if (!ExpressionEvaluator.Validate(condition, out var error))
        {
            AppDialog.ShowError(error, "Ошибка условия");
            return;
        }

        Condition = condition;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
