using System.Windows;
using MouseMackros.Engine;
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
        ExpressionBox.Text = profile.Match.Expression ?? "";
        PriorityBox.Text = profile.Match.Priority.ToString();

        bool hasAdvanced = !string.IsNullOrWhiteSpace(profile.Match.TitleContains) ||
                           !string.IsNullOrWhiteSpace(profile.Match.WindowClass);
        if (hasAdvanced)
        {
            AdvancedCheck.IsChecked = true;
            AdvancedPanel.Visibility = Visibility.Visible;
        }
    }

    public bool Saved { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _profile.Match.Processes = ProcessesBox.Text
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _profile.Match.TitleContains = string.IsNullOrWhiteSpace(TitleBox.Text) ? null : TitleBox.Text.Trim();
        _profile.Match.WindowClass = string.IsNullOrWhiteSpace(ClassBox.Text) ? null : ClassBox.Text.Trim();

        var expression = ExpressionEvaluator.NormalizeCondition(ExpressionBox.Text);
        if (!string.IsNullOrWhiteSpace(expression) &&
            !ExpressionEvaluator.Validate(expression, out var error))
        {
            AppDialog.ShowError(error, "Ошибка условия");
            return;
        }
        _profile.Match.Expression = string.IsNullOrWhiteSpace(expression) ? null : expression;

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

    private void Advanced_Changed(object sender, RoutedEventArgs e)
    {
        AdvancedPanel.Visibility = AdvancedCheck.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Help_Click(object sender, RoutedEventArgs e) =>
        HelpWindow.ShowHelp(this, "Подсказки по условиям", HelpText);

    private const string HelpText =
        "УСЛОВИЕ (МИНИ-ЯЗЫК)\n\n" +
        "Поля:\n" +
        "  browser.url  или  url   — адрес страницы\n" +
        "  title                   — заголовок окна\n" +
        "  process  или  app       — имя процесса без .exe\n" +
        "  class                   — класс окна\n\n" +
        "Операторы:\n" +
        "  contains \"…\"        содержит подстроку\n" +
        "  startsWith \"…\"      начинается с\n" +
        "  endsWith \"…\"        заканчивается на\n" +
        "  ==  /  !=             равно / не равно (поддерживает * и ?)\n" +
        "  ~= \"regex\"           регулярное выражение\n" +
        "  &&  /  ||  /  !       И / ИЛИ / НЕ, скобки\n\n" +
        "Примеры:\n" +
        "  browser.url contains \"nexusmods.com\"\n" +
        "  process == \"firefox\" && browser.url startsWith \"https://\"\n" +
        "  title contains \"YouTube\"\n" +
        "  class == \"Chrome_WidgetWin_1\"\n" +
        "  browser.url ~= \"page=\\d+\"\n\n" +
        "ПРОГРАММЫ\n" +
        "Список процессов работает вместе с условием: профиль включится,\n" +
        "только если совпали и процесс, и условие.\n\n" +
        "ЗАГОЛОВОК И КЛАСС ОКНА\n" +
        "Это старые простые фильтры. Они не обязательны: то же самое можно\n" +
        "записать в условии через title contains \"…\" и class == \"…\".\n\n" +
        "ПРИОРИТЕТ\n" +
        "Если подходят несколько профилей, побеждает более глубокий\n" +
        "подпрофиль, а при равной глубине — профиль с большим приоритетом.";
}
