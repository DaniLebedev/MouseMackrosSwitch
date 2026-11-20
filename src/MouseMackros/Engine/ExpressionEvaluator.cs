using System.Text;
using System.Text.RegularExpressions;

namespace MouseMackros.Engine;

/// <summary>
/// Tiny condition language for sub-profiles:
///
///   browser.url contains "youtube.com"
///   browser.url == "https://example.com/page"
///   process == "firefox" &amp;&amp; title contains "YouTube"
///   title ~= "^(Video|Clip)" || browser.url startsWith "https://"
///
/// Fields: browser.url/url, title, process/app, class.
/// Operators: ==, !=, contains, startsWith, endsWith, ~= (regex), !, &amp;&amp;, ||, ().
/// </summary>
public static class ExpressionEvaluator
{
    public static bool TryEvaluate(string expression, WindowInfo window, out bool result, out string error)
    {
        result = false;
        error = "";
        try
        {
            var parser = new Parser(Tokenize(expression ?? ""));
            var node = parser.ParseOr();
            parser.ExpectEnd();
            result = node.Evaluate(window);
            return true;
        }
        catch (ExpressionException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = "Ошибка условия: " + ex.Message;
            return false;
        }
    }

    public static bool Validate(string expression, out string error)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Условие пустое.";
            return false;
        }

        // Parse against a dummy window so only syntax is checked.
        return TryEvaluate(expression,
            new WindowInfo(IntPtr.Zero, "", null, "", "", ""), out _, out error);
    }

    /// <summary>
    /// A bare string is treated as a title substring; a bare domain/URL is
    /// treated as a URL substring. Full expressions are kept as written.
    /// </summary>
    public static string NormalizeCondition(string? input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0) return "";
        if (Validate(text, out _)) return text;

        var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        bool looksLikeUrl = !text.Contains(' ') &&
                            (text.Contains("://") || text.Contains('.'));
        return looksLikeUrl
            ? $"browser.url contains \"{escaped}\""
            : $"title contains \"{escaped}\"";
    }

    private sealed class ExpressionException : Exception
    {
        public ExpressionException(string message) : base(message) { }
    }

    private enum TokenKind { Field, String, Operator, LParen, RParen, End }

    private sealed record Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '(') { tokens.Add(new Token(TokenKind.LParen, "(")); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokenKind.RParen, ")")); i++; continue; }

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
                if (!closed) throw new ExpressionException("Не закрыта кавычка в условии.");
                tokens.Add(new Token(TokenKind.String, sb.ToString()));
                continue;
            }

            if (i + 1 < text.Length)
            {
                var two = text.Substring(i, 2);
                if (two is "&&" or "||" or "==" or "!=" or "~=")
                {
                    tokens.Add(new Token(TokenKind.Operator, two));
                    i += 2;
                    continue;
                }
            }

            if (c == '!')
            {
                tokens.Add(new Token(TokenKind.Operator, "!"));
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
                if (word.Equals("contains", StringComparison.OrdinalIgnoreCase) ||
                    word.Equals("startswith", StringComparison.OrdinalIgnoreCase) ||
                    word.Equals("endswith", StringComparison.OrdinalIgnoreCase) ||
                    word.Equals("startsWith", StringComparison.OrdinalIgnoreCase) ||
                    word.Equals("endsWith", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new Token(TokenKind.Operator, word.ToLowerInvariant()));
                }
                else
                {
                    tokens.Add(new Token(TokenKind.Field, word.ToLowerInvariant()));
                }
                continue;
            }

            throw new ExpressionException($"Неизвестный символ «{c}» в условии.");
        }

        tokens.Add(new Token(TokenKind.End, ""));
        return tokens;
    }

    private abstract class Node
    {
        public abstract bool Evaluate(WindowInfo window);
    }

    private sealed class BinaryNode : Node
    {
        private readonly Node _left, _right;
        private readonly bool _or;
        public BinaryNode(Node left, Node right, bool or) { _left = left; _right = right; _or = or; }
        public override bool Evaluate(WindowInfo window) =>
            _or ? _left.Evaluate(window) || _right.Evaluate(window)
                : _left.Evaluate(window) && _right.Evaluate(window);
    }

    private sealed class NotNode : Node
    {
        private readonly Node _inner;
        public NotNode(Node inner) => _inner = inner;
        public override bool Evaluate(WindowInfo window) => !_inner.Evaluate(window);
    }

    private sealed class CompareNode : Node
    {
        private readonly string _field;
        private readonly string _op;
        private readonly string _expected;

        public CompareNode(string field, string op, string expected)
        {
            _field = field;
            _op = op;
            _expected = expected;
        }

        public override bool Evaluate(WindowInfo window)
        {
            var actual = FieldValue(_field, window);

            return _op switch
            {
                "==" => MatchesExact(actual, _expected),
                "!=" => !MatchesExact(actual, _expected),
                "contains" => actual.Contains(_expected, StringComparison.OrdinalIgnoreCase),
                "startswith" => actual.StartsWith(_expected, StringComparison.OrdinalIgnoreCase),
                "endswith" => actual.EndsWith(_expected, StringComparison.OrdinalIgnoreCase),
                "~=" => Regex.IsMatch(actual, _expected, RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(150)),
                _ => false
            };
        }

        private static bool MatchesExact(string actual, string expected)
        {
            if (expected.IndexOf('*') < 0 && expected.IndexOf('?') < 0)
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

            var pattern = "^" + Regex.Escape(expected)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return Regex.IsMatch(actual, pattern, RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(150));
        }

        private static string FieldValue(string field, WindowInfo window) => field switch
        {
            "url" or "browser.url" => window.Url ?? "",
            "title" => window.Title,
            "process" or "app" => window.ProcessName,
            "class" => window.ClassName,
            _ => throw new ExpressionException($"Неизвестное поле «{field}».")
        };
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

        public Node ParseOr()
        {
            var left = ParseAnd();
            while (TakeOperator("||"))
                left = new BinaryNode(left, ParseAnd(), true);
            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseUnary();
            while (TakeOperator("&&"))
                left = new BinaryNode(left, ParseUnary(), false);
            return left;
        }

        private Node ParseUnary()
        {
            if (TakeOperator("!")) return new NotNode(ParseUnary());

            if (Current.Kind == TokenKind.LParen)
            {
                _index++;
                var node = ParseOr();
                if (Current.Kind != TokenKind.RParen)
                    throw new ExpressionException("Не закрыта скобка в условии.");
                _index++;
                return node;
            }

            if (Current.Kind != TokenKind.Field)
                throw new ExpressionException("Ожидалось поле условия (browser.url, title, process, class).");

            var field = Current.Text;
            _index++;

            if (Current.Kind != TokenKind.Operator ||
                Current.Text is "&&" or "||" or "!")
            {
                throw new ExpressionException($"После «{field}» ожидался оператор (==, contains, startsWith...).");
            }

            var op = Current.Text.ToLowerInvariant();
            _index++;

            if (Current.Kind != TokenKind.String)
                throw new ExpressionException("Ожидалась строка в кавычках.");

            var expected = Current.Text;
            _index++;
            return new CompareNode(field, op, expected);
        }

        public void ExpectEnd()
        {
            if (Current.Kind != TokenKind.End)
                throw new ExpressionException("Лишний текст в конце условия.");
        }
    }
}
