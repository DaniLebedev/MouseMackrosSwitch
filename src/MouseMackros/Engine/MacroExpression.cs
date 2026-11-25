using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MouseMackros.Engine;

/// <summary>Per-run state shared by all steps of one macro invocation.</summary>
public sealed class MacroContext
{
    public MacroVariables Variables { get; } = new();
    public int DefaultTypingDelayMs { get; set; } = 3;
}

public sealed class MacroVariables
{
    private readonly Dictionary<string, MacroValue> _values =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string name, out MacroValue value) =>
        _values.TryGetValue(name, out value);

    public void Set(string name, MacroValue value)
    {
        if (!string.IsNullOrWhiteSpace(name)) _values[name] = value;
    }
}

public enum MacroValueKind
{
    String,
    Number,
    Boolean
}

public readonly struct MacroValue
{
    private readonly string _text;
    private readonly double _number;
    private readonly bool _boolean;

    private MacroValue(MacroValueKind kind, string text, double number, bool boolean)
    {
        Kind = kind;
        _text = text;
        _number = number;
        _boolean = boolean;
    }

    public MacroValueKind Kind { get; }

    public static MacroValue FromString(string? value) =>
        new(MacroValueKind.String, value ?? "", 0, false);

    public static MacroValue FromNumber(double value) =>
        new(MacroValueKind.Number, "", value, false);

    public static MacroValue FromBoolean(bool value) =>
        new(MacroValueKind.Boolean, "", 0, value);

    public bool IsString => Kind == MacroValueKind.String;

    public string AsString() => Kind switch
    {
        MacroValueKind.Number => FormatNumber(_number),
        MacroValueKind.Boolean => _boolean ? "true" : "false",
        _ => _text ?? ""
    };

    public bool TryGetNumber(out double value)
    {
        switch (Kind)
        {
            case MacroValueKind.Number:
                value = _number;
                return true;
            case MacroValueKind.Boolean:
                value = _boolean ? 1 : 0;
                return true;
            default:
                return double.TryParse((_text ?? "").Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value);
        }
    }

    public double AsNumber() => TryGetNumber(out var value) ? value : 0;

    public bool AsBoolean() => Kind switch
    {
        MacroValueKind.Boolean => _boolean,
        MacroValueKind.Number => Math.Abs(_number) > 0.0000001,
        _ => IsTruthyText(_text)
    };

    private static bool IsTruthyText(string? text)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;
        if (double.TryParse(trimmed, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number) &&
            Math.Abs(number) < 0.0000001)
            return false;
        return true;
    }

    private static string FormatNumber(double number)
    {
        if (Math.Abs(number - Math.Round(number)) < 0.0000001)
            return ((long)Math.Round(number)).ToString(CultureInfo.InvariantCulture);
        return number.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Expression language for macro conditions and assignments. It extends the
/// profile condition language with variables, numbers and arithmetic:
///
///   counter &lt; 10
///   browser.url contains "nexus" &amp;&amp; counter &gt;= 3
///   set next = counter + 1
///   if name == "page-*" { ... }
/// </summary>
public static class MacroExpression
{
    public static bool TryEvaluate(string expression, WindowInfo? window,
        MacroVariables variables, out bool result, out string error)
    {
        result = false;
        error = "";
        try
        {
            var node = Parse(expression);
            result = node.Evaluate(window, variables).AsBoolean();
            return true;
        }
        catch (MacroExpressionException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = "Ошибка выражения: " + ex.Message;
            return false;
        }
    }

    public static bool TryEvaluateValue(string expression, WindowInfo? window,
        MacroVariables variables, out MacroValue value, out string error)
    {
        value = MacroValue.FromString("");
        error = "";
        try
        {
            var node = Parse(expression);
            value = node.Evaluate(window, variables);
            return true;
        }
        catch (MacroExpressionException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = "Ошибка выражения: " + ex.Message;
            return false;
        }
    }

    private static Node Parse(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new MacroExpressionException("Выражение пустое.");

        var parser = new Parser(Tokenize(expression));
        var node = parser.ParseAll();
        return node;
    }

    private sealed class MacroExpressionException : Exception
    {
        public MacroExpressionException(string message) : base(message) { }
    }

    private enum TokenKind
    {
        Identifier,
        Number,
        String,
        Operator,
        LParen,
        RParen,
        End
    }

    private sealed record Token(TokenKind Kind, string Text, double Number = 0);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '(')
            {
                tokens.Add(new Token(TokenKind.LParen, "("));
                i++;
                continue;
            }
            if (c == ')')
            {
                tokens.Add(new Token(TokenKind.RParen, ")"));
                i++;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                char quote = c;
                i++;
                var sb = new StringBuilder();
                bool closed = false;
                while (i < text.Length)
                {
                    char ch = text[i++];
                    if (ch == quote) { closed = true; break; }
                    if (ch == '\\' && i < text.Length)
                    {
                        char next = text[i++];
                        sb.Append(next switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            _ => next
                        });
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
                if (!closed)
                    throw new MacroExpressionException("Не закрыта кавычка в выражении.");
                tokens.Add(new Token(TokenKind.String, sb.ToString()));
                continue;
            }

            if (char.IsDigit(c))
            {
                int start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                var numberText = text[start..i];
                if (!double.TryParse(numberText, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var number))
                {
                    throw new MacroExpressionException($"Некорректное число «{numberText}».");
                }
                tokens.Add(new Token(TokenKind.Number, numberText, number));
                continue;
            }

            if (i + 1 < text.Length)
            {
                var two = text.Substring(i, 2);
                if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||" or "~=")
                {
                    tokens.Add(new Token(TokenKind.Operator, two));
                    i += 2;
                    continue;
                }
            }

            if (c is '<' or '>' or '!' or '+' or '-' or '*' or '/' or '%')
            {
                tokens.Add(new Token(TokenKind.Operator, c.ToString()));
                i++;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < text.Length &&
                       (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.'))
                {
                    i++;
                }

                var word = text[start..i];
                var lower = word.ToLowerInvariant();
                if (lower is "contains" or "startswith" or "endswith")
                    tokens.Add(new Token(TokenKind.Operator, lower));
                else if (lower == "and")
                    tokens.Add(new Token(TokenKind.Operator, "&&"));
                else if (lower == "or")
                    tokens.Add(new Token(TokenKind.Operator, "||"));
                else if (lower == "not")
                    tokens.Add(new Token(TokenKind.Operator, "!"));
                else
                    tokens.Add(new Token(TokenKind.Identifier, word));
                continue;
            }

            throw new MacroExpressionException($"Неизвестный символ «{c}» в выражении.");
        }

        tokens.Add(new Token(TokenKind.End, ""));
        return tokens;
    }

    private abstract class Node
    {
        public abstract MacroValue Evaluate(WindowInfo? window, MacroVariables variables);
    }

    private sealed class LiteralNode : Node
    {
        private readonly MacroValue _value;
        public LiteralNode(MacroValue value) => _value = value;
        public override MacroValue Evaluate(WindowInfo? window, MacroVariables variables) => _value;
    }

    private sealed class IdentifierNode : Node
    {
        private readonly string _name;
        public IdentifierNode(string name) => _name = name;

        public override MacroValue Evaluate(WindowInfo? window, MacroVariables variables)
        {
            switch (_name.ToLowerInvariant())
            {
                case "url":
                case "browser.url":
                    return MacroValue.FromString(window?.Url ?? "");
                case "title":
                    return MacroValue.FromString(window?.Title ?? "");
                case "process":
                case "app":
                    return MacroValue.FromString(window?.ProcessName ?? "");
                case "class":
                    return MacroValue.FromString(window?.ClassName ?? "");
                case "true":
                    return MacroValue.FromBoolean(true);
                case "false":
                    return MacroValue.FromBoolean(false);
            }

            if (variables.TryGet(_name, out var value)) return value;
            throw new MacroExpressionException($"Неизвестная переменная «{_name}».");
        }
    }

    private sealed class UnaryNode : Node
    {
        private readonly string _op;
        private readonly Node _inner;

        public UnaryNode(string op, Node inner)
        {
            _op = op;
            _inner = inner;
        }

        public override MacroValue Evaluate(WindowInfo? window, MacroVariables variables)
        {
            var value = _inner.Evaluate(window, variables);
            return _op switch
            {
                "!" => MacroValue.FromBoolean(!value.AsBoolean()),
                "-" => MacroValue.FromNumber(-value.AsNumber()),
                "+" => MacroValue.FromNumber(value.AsNumber()),
                _ => value
            };
        }
    }

    private sealed class BinaryNode : Node
    {
        private readonly string _op;
        private readonly Node _left;
        private readonly Node _right;

        public BinaryNode(string op, Node left, Node right)
        {
            _op = op;
            _left = left;
            _right = right;
        }

        public override MacroValue Evaluate(WindowInfo? window, MacroVariables variables)
        {
            if (_op == "&&")
            {
                return MacroValue.FromBoolean(
                    _left.Evaluate(window, variables).AsBoolean() &&
                    _right.Evaluate(window, variables).AsBoolean());
            }

            if (_op == "||")
            {
                return MacroValue.FromBoolean(
                    _left.Evaluate(window, variables).AsBoolean() ||
                    _right.Evaluate(window, variables).AsBoolean());
            }

            var left = _left.Evaluate(window, variables);
            var right = _right.Evaluate(window, variables);

            switch (_op)
            {
                case "==":
                case "!=":
                {
                    bool equal = Equal(left, right);
                    return MacroValue.FromBoolean(_op == "==" ? equal : !equal);
                }
                case "<":
                case "<=":
                case ">":
                case ">=":
                    return MacroValue.FromBoolean(Compare(left, right, _op));
                case "contains":
                    return MacroValue.FromBoolean(
                        left.AsString().Contains(right.AsString(), StringComparison.OrdinalIgnoreCase));
                case "startswith":
                    return MacroValue.FromBoolean(
                        left.AsString().StartsWith(right.AsString(), StringComparison.OrdinalIgnoreCase));
                case "endswith":
                    return MacroValue.FromBoolean(
                        left.AsString().EndsWith(right.AsString(), StringComparison.OrdinalIgnoreCase));
                case "~=":
                {
                    try
                    {
                        return MacroValue.FromBoolean(Regex.IsMatch(left.AsString(), right.AsString(),
                            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150)));
                    }
                    catch (Exception ex)
                    {
                        throw new MacroExpressionException("Ошибка regex: " + ex.Message);
                    }
                }
                case "+":
                    if (left.IsString || right.IsString)
                        return MacroValue.FromString(left.AsString() + right.AsString());
                    return MacroValue.FromNumber(left.AsNumber() + right.AsNumber());
                case "-":
                    return MacroValue.FromNumber(left.AsNumber() - right.AsNumber());
                case "*":
                    return MacroValue.FromNumber(left.AsNumber() * right.AsNumber());
                case "/":
                {
                    double divisor = right.AsNumber();
                    if (Math.Abs(divisor) < 0.0000001)
                        throw new MacroExpressionException("Деление на ноль.");
                    return MacroValue.FromNumber(left.AsNumber() / divisor);
                }
                case "%":
                {
                    double divisor = right.AsNumber();
                    if (Math.Abs(divisor) < 0.0000001)
                        throw new MacroExpressionException("Деление на ноль.");
                    return MacroValue.FromNumber(left.AsNumber() % divisor);
                }
                default:
                    throw new MacroExpressionException($"Неизвестный оператор «{_op}».");
            }
        }

        private static bool Equal(MacroValue left, MacroValue right)
        {
            if (left.Kind == MacroValueKind.Boolean || right.Kind == MacroValueKind.Boolean)
                return left.AsBoolean() == right.AsBoolean();

            if (left.TryGetNumber(out var ln) && right.TryGetNumber(out var rn))
                return Math.Abs(ln - rn) < 0.0000001;

            var actual = left.AsString();
            var expected = right.AsString();
            if (expected.IndexOf('*') < 0 && expected.IndexOf('?') < 0)
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

            var pattern = "^" + Regex.Escape(expected)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return Regex.IsMatch(actual, pattern, RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(150));
        }

        private static bool Compare(MacroValue left, MacroValue right, string op)
        {
            int comparison;
            if (left.TryGetNumber(out var ln) && right.TryGetNumber(out var rn))
                comparison = ln.CompareTo(rn);
            else
                comparison = string.Compare(left.AsString(), right.AsString(),
                    StringComparison.OrdinalIgnoreCase);

            return op switch
            {
                "<" => comparison < 0,
                "<=" => comparison <= 0,
                ">" => comparison > 0,
                ">=" => comparison >= 0,
                _ => false
            };
        }
    }

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _index;

        public Parser(List<Token> tokens) => _tokens = tokens;

        private Token Current => _tokens[_index];

        private bool TakeOperator(string op)
        {
            if (Current.Kind == TokenKind.Operator &&
                string.Equals(Current.Text, op, StringComparison.OrdinalIgnoreCase))
            {
                _index++;
                return true;
            }
            return false;
        }

        public Node ParseAll()
        {
            var node = ParseOr();
            if (Current.Kind != TokenKind.End)
                throw new MacroExpressionException("Лишний текст в конце выражения.");
            return node;
        }

        private Node ParseOr()
        {
            var left = ParseAnd();
            while (TakeOperator("||"))
                left = new BinaryNode("||", left, ParseAnd());
            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseEquality();
            while (TakeOperator("&&"))
                left = new BinaryNode("&&", left, ParseEquality());
            return left;
        }

        private Node ParseEquality()
        {
            var left = ParseRelational();
            while (true)
            {
                if (TakeOperator("==")) { left = new BinaryNode("==", left, ParseRelational()); continue; }
                if (TakeOperator("!=")) { left = new BinaryNode("!=", left, ParseRelational()); continue; }
                if (TakeOperator("contains")) { left = new BinaryNode("contains", left, ParseRelational()); continue; }
                if (TakeOperator("startswith")) { left = new BinaryNode("startswith", left, ParseRelational()); continue; }
                if (TakeOperator("endswith")) { left = new BinaryNode("endswith", left, ParseRelational()); continue; }
                return left;
            }
        }

        private Node ParseRelational()
        {
            var left = ParseAdditive();
            while (true)
            {
                if (TakeOperator("<")) { left = new BinaryNode("<", left, ParseAdditive()); continue; }
                if (TakeOperator("<=")) { left = new BinaryNode("<=", left, ParseAdditive()); continue; }
                if (TakeOperator(">")) { left = new BinaryNode(">", left, ParseAdditive()); continue; }
                if (TakeOperator(">=")) { left = new BinaryNode(">=", left, ParseAdditive()); continue; }
                if (TakeOperator("~=")) { left = new BinaryNode("~=", left, ParseAdditive()); continue; }
                return left;
            }
        }

        private Node ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                if (TakeOperator("+")) { left = new BinaryNode("+", left, ParseMultiplicative()); continue; }
                if (TakeOperator("-")) { left = new BinaryNode("-", left, ParseMultiplicative()); continue; }
                return left;
            }
        }

        private Node ParseMultiplicative()
        {
            var left = ParseUnary();
            while (true)
            {
                if (TakeOperator("*")) { left = new BinaryNode("*", left, ParseUnary()); continue; }
                if (TakeOperator("/")) { left = new BinaryNode("/", left, ParseUnary()); continue; }
                if (TakeOperator("%")) { left = new BinaryNode("%", left, ParseUnary()); continue; }
                return left;
            }
        }

        private Node ParseUnary()
        {
            if (TakeOperator("!")) return new UnaryNode("!", ParseUnary());
            if (TakeOperator("-")) return new UnaryNode("-", ParseUnary());
            if (TakeOperator("+")) return new UnaryNode("+", ParseUnary());
            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            if (Current.Kind == TokenKind.LParen)
            {
                _index++;
                var node = ParseOr();
                if (Current.Kind != TokenKind.RParen)
                    throw new MacroExpressionException("Не закрыта скобка в выражении.");
                _index++;
                return node;
            }

            if (Current.Kind == TokenKind.Number)
            {
                var value = Current.Number;
                _index++;
                return new LiteralNode(MacroValue.FromNumber(value));
            }

            if (Current.Kind == TokenKind.String)
            {
                var text = Current.Text;
                _index++;
                return new LiteralNode(MacroValue.FromString(text));
            }

            if (Current.Kind == TokenKind.Identifier)
            {
                var name = Current.Text;
                _index++;
                return new IdentifierNode(name);
            }

            throw new MacroExpressionException("Ожидалось значение, переменная или скобка.");
        }
    }
}
