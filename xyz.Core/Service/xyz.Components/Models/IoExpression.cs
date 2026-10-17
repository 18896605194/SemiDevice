using System.Globalization;
using System.Text.RegularExpressions;

namespace xyz.Components.Models;

internal readonly record struct IoReference(string Type, int Index)
{
    public bool IsBoolean => Type is "DI" or "DO";
    public override string ToString() => $"{Type}{Index}";
}

/// <summary>只解释 IO 点、常量和运算符，不执行脚本。编译时检查数值/布尔类型。</summary>
internal sealed class IoExpression
{
    private abstract record Node(bool IsBoolean)
    {
        public abstract double? Evaluate(Func<IoReference, double?> read);
    }

    private sealed record Constant(double Value, bool Boolean) : Node(Boolean)
    {
        public override double? Evaluate(Func<IoReference, double?> read) => Value;
    }

    private sealed record Reference(IoReference Point) : Node(Point.IsBoolean)
    {
        public override double? Evaluate(Func<IoReference, double?> read) => read(Point);
    }

    private sealed record Unary(string Operator, Node Operand) : Node(Operand.IsBoolean)
    {
        public override double? Evaluate(Func<IoReference, double?> read)
        {
            double? value = Operand.Evaluate(read);
            if (value is null)
            {
                return null;
            }

            switch (Operator)
            {
                case "-":
                    return -value.Value;
                case "!":
                    return value.Value == 0 ? 1 : 0;
                default:
                    return value.Value;
            }
        }
    }

    private sealed record Binary(string Operator, Node Left, Node Right, bool Boolean) : Node(Boolean)
    {
        public override double? Evaluate(Func<IoReference, double?> read)
        {
            // 所有来源都必须有效；不会把 false && 无效来源 当成有效 false。
            double? left = Left.Evaluate(read);
            double? right = Right.Evaluate(read);
            if (left is null || right is null || (Operator == "/" && right.Value == 0))
            {
                return null;
            }

            double a = left.Value;
            double b = right.Value;
            double result;
            switch (Operator)
            {
                case "+":
                    result = a + b;
                    break;
                case "-":
                    result = a - b;
                    break;
                case "*":
                    result = a * b;
                    break;
                case "/":
                    result = a / b;
                    break;
                case ">":
                    result = a > b ? 1 : 0;
                    break;
                case ">=":
                    result = a >= b ? 1 : 0;
                    break;
                case "<":
                    result = a < b ? 1 : 0;
                    break;
                case "<=":
                    result = a <= b ? 1 : 0;
                    break;
                case "==":
                    result = a == b ? 1 : 0;
                    break;
                case "!=":
                    result = a != b ? 1 : 0;
                    break;
                case "&&":
                    result = a != 0 && b != 0 ? 1 : 0;
                    break;
                case "||":
                    result = a != 0 || b != 0 ? 1 : 0;
                    break;
                default:
                    throw new InvalidOperationException($"未知运算符 {Operator}");
            }

            return double.IsFinite(result) ? result : null;
        }
    }

    private readonly Node _root;
    public IReadOnlyCollection<IoReference> References { get; }

    private IoExpression(Node root, IReadOnlyCollection<IoReference> references)
    {
        _root = root;
        References = references;
    }

    public double? Evaluate(Func<IoReference, double?> read) => _root.Evaluate(read);

    public static IoExpression Parse(string text, bool booleanResult)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
        {
            throw new InvalidOperationException("Expression 不能为空且不能超过 4096 个字符");
        }

        var parser = new Parser(text);
        var root = parser.Parse();
        if (root.IsBoolean != booleanResult)
        {
            throw new InvalidOperationException(booleanResult ? "虚拟 DI 表达式必须返回 bool" : "虚拟 AI 表达式必须返回数值");
        }

        return new IoExpression(root, parser.References.ToArray());
    }

    private sealed class Parser
    {
        private static readonly Regex Token = new(
            @"\G\s*(?:(?<point>(?:DI|DO|AI|AO)\s*[0-9]+)|(?<bool>true|false)|(?<number>(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)|(?<op>&&|\|\||>=|<=|==|!=|[+*/<>!()\-]))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private readonly string _text;
        private int _position;
        private string _kind = string.Empty;
        private string _token = string.Empty;
        public HashSet<IoReference> References { get; } = [];

        public Parser(string text)
        {
            _text = text;
            Next();
        }

        public Node Parse()
        {
            Node root = Expression(1, 0);
            if (_token.Length != 0)
            {
                throw Error($"多余的内容 {_token}");
            }

            return root;
        }

        private Node Expression(int minimum, int depth)
        {
            if (depth > 128)
            {
                throw Error("表达式嵌套过深");
            }

            Node left = Primary(depth + 1);
            while (Precedence(_token) >= minimum)
            {
                string op = _token;
                int precedence = Precedence(op);
                Next();
                Node right = Expression(precedence + 1, depth + 1);
                bool logical = op is "&&" or "||";
                bool equality = op is "==" or "!=";
                bool comparison = op is ">" or ">=" or "<" or "<=";
                if ((logical && (!left.IsBoolean || !right.IsBoolean))
                    || (equality && left.IsBoolean != right.IsBoolean)
                    || (!logical && !equality && (left.IsBoolean || right.IsBoolean)))
                {
                    throw Error($"运算符 {op} 两侧的数值/布尔类型不匹配");
                }

                left = new Binary(op, left, right, logical || equality || comparison);
            }

            return left;
        }

        private Node Primary(int depth)
        {
            if (depth > 128)
            {
                throw Error("表达式嵌套过深");
            }

            string token = _token;
            string kind = _kind;
            Next();
            if (token == "(")
            {
                Node node = Expression(1, depth + 1);
                if (_token != ")")
                {
                    throw Error("缺少右括号");
                }

                Next();
                return node;
            }

            if (token is "+" or "-" or "!")
            {
                Node node = Primary(depth + 1);
                if ((token == "!") != node.IsBoolean)
                {
                    throw Error($"一元运算符 {token} 的操作数类型错误");
                }

                return new Unary(token, node);
            }

            if (kind == "point")
            {
                string type = token[..2].ToUpperInvariant();
                if (!int.TryParse(token[2..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                {
                    throw Error($"无效索引 {token}");
                }

                if (index >= 3000 && !(type == "DI" && index is >= 4000 and < 5000)
                    && !(type == "AI" && index is >= 6000 and < 7000))
                {
                    throw Error($"{token} 不在已支持的虚拟 DI/AI 区间");
                }

                var point = new IoReference(type, index);
                References.Add(point);
                return new Reference(point);
            }

            if (kind == "bool")
            {
                return new Constant(bool.Parse(token) ? 1 : 0, true);
            }

            if (kind == "number" && double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && double.IsFinite(value))
            {
                return new Constant(value, false);
            }

            throw Error($"无效操作数 {token}");
        }

        private void Next()
        {
            if (_position == _text.Length || string.IsNullOrWhiteSpace(_text[_position..]))
            {
                _position = _text.Length;
                _token = _kind = string.Empty;
                return;
            }

            var match = Token.Match(_text, _position);
            if (!match.Success)
            {
                throw Error("无法识别的字符");
            }

            _position = match.Index + match.Length;
            foreach (string kind in new[] { "point", "bool", "number", "op" })
            {
                if (match.Groups[kind].Success)
                {
                    _kind = kind;
                    _token = match.Groups[kind].Value;
                    return;
                }
            }
        }

        private static int Precedence(string token)
        {
            switch (token)
            {
                case "||":
                    return 1;
                case "&&":
                    return 2;
                case "==":
                case "!=":
                    return 3;
                case ">":
                case ">=":
                case "<":
                case "<=":
                    return 4;
                case "+":
                case "-":
                    return 5;
                case "*":
                case "/":
                    return 6;
                default:
                    return 0;
            }
        }

        private InvalidOperationException Error(string message) => new($"Expression 第 {_position + 1} 字符附近：{message}");
    }
}
