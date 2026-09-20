using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GraphCalculator
{
    public static class CalculatorEngine
    {
        internal enum TokenType
        {
            Number,
            Identifier,
            Operator,
            LeftParen,
            RightParen,
            Comma
        }

        internal readonly record struct Token(TokenType Type, string Text);

        public sealed class CompiledExpression
        {
            private readonly IReadOnlyList<Token> _rpn;

            internal CompiledExpression(
                string source,
                IReadOnlyList<Token> rpn,
                IReadOnlyCollection<string> variables)
            {
                Source = source;
                _rpn = rpn;
                Variables = variables;
                DependsOnX = variables.Contains("x", StringComparer.OrdinalIgnoreCase);
                DependsOnY = variables.Contains("y", StringComparer.OrdinalIgnoreCase);
            }

            public string Source { get; }
            public bool DependsOnX { get; }
            public bool DependsOnY { get; }
            public IReadOnlyCollection<string> Variables { get; }

            public double Evaluate()
            {
                return EvaluateRpn(_rpn, null, null, null);
            }

            public double Evaluate(double x)
            {
                return EvaluateRpn(_rpn, x, null, null);
            }

            public double Evaluate(double x, double y)
            {
                return EvaluateRpn(_rpn, x, y, null);
            }

            public double Evaluate(double x, IDictionary<string, double> variables)
            {
                return EvaluateRpn(_rpn, x, null, variables);
            }

            public double Evaluate(double x, double y, IDictionary<string, double> variables)
            {
                return EvaluateRpn(_rpn, x, y, variables);
            }

            public double Evaluate(IDictionary<string, double> variables)
            {
                return EvaluateRpn(_rpn, null, null, variables);
            }

            public string ToHlsl()
            {
                return BuildHlsl(_rpn);
            }
        }

        public static CompiledExpression Compile(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("Expression is empty");

            string normalized = NormalizeExpression(expression);
            var tokens = InsertImplicitMultiplication(Tokenize(normalized));
            var rpn = ToRpn(tokens);
            var variables = rpn
                .Where(token =>
                    token.Type == TokenType.Identifier
                    && !Functions.IsFunction(token.Text)
                    && !token.Text.Equals("pi", StringComparison.OrdinalIgnoreCase)
                    && !token.Text.Equals("e", StringComparison.OrdinalIgnoreCase))
                .Select(token => token.Text.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new CompiledExpression(normalized, rpn, variables);
        }

        public static double Evaluate(string expression, IDictionary<string, double>? variables = null)
        {
            var compiled = Compile(expression);
            return variables == null ? compiled.Evaluate() : compiled.Evaluate(variables);
        }

        private static string NormalizeExpression(string expression)
        {
            string text = expression.Trim()
                .Replace("π", "pi")
                .Replace('×', '*')
                .Replace('÷', '/')
                .Replace('−', '-');

            int equalsIndex = text.IndexOf('=');
            bool looksLikeAssignment = equalsIndex >= 0
                && (equalsIndex + 1 >= text.Length || text[equalsIndex + 1] != '=')
                && (equalsIndex == 0 || text[equalsIndex - 1] is not ('<' or '>' or '!' or '='));
            if (looksLikeAssignment)
            {
                string left = text[..equalsIndex].Replace(" ", string.Empty).ToLowerInvariant();
                if (left is "y" or "z" or "f(x)" or "f(x,y)")
                {
                    text = text[(equalsIndex + 1)..].Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Expression is empty");

            return text;
        }

        private static List<Token> Tokenize(string expression)
        {
            var tokens = new List<Token>();
            int i = 0;

            while (i < expression.Length)
            {
                char c = expression[i];

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && i + 1 < expression.Length && char.IsDigit(expression[i + 1])))
                {
                    int start = i;
                    bool seenDot = false;

                    if (c == '.')
                    {
                        seenDot = true;
                        i++;
                    }

                    while (i < expression.Length && char.IsDigit(expression[i])) i++;

                    if (i < expression.Length && expression[i] == '.' && !seenDot)
                    {
                        seenDot = true;
                        i++;
                        while (i < expression.Length && char.IsDigit(expression[i])) i++;
                    }

                    if (i < expression.Length && (expression[i] == 'e' || expression[i] == 'E'))
                    {
                        int exponentStart = i;
                        i++;
                        if (i < expression.Length && (expression[i] == '+' || expression[i] == '-')) i++;

                        int exponentDigits = i;
                        while (i < expression.Length && char.IsDigit(expression[i])) i++;

                        if (exponentDigits == i)
                        {
                            i = exponentStart;
                        }
                    }

                    tokens.Add(new Token(TokenType.Number, expression[start..i]));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i++;
                    while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_')) i++;
                    tokens.Add(new Token(TokenType.Identifier, expression[start..i]));
                    continue;
                }

                switch (c)
                {
                    case '+':
                    case '-':
                    case '*':
                    case '/':
                    case '^':
                    case '<':
                    case '>':
                    case '=':
                    case '!':
                    {
                        string op = c.ToString();
                        if (i + 1 < expression.Length && expression[i + 1] == '='
                            && c is '<' or '>' or '=' or '!')
                        {
                            op += "=";
                            i++;
                        }
                        else if (c == '=')
                        {
                            op = "==";
                        }
                        else if (c == '!')
                        {
                            throw new FormatException("Use != for inequality or not(...) for logical negation");
                        }

                        tokens.Add(new Token(TokenType.Operator, op));
                        i++;
                        break;
                    }
                    case '(':
                        tokens.Add(new Token(TokenType.LeftParen, "("));
                        i++;
                        break;
                    case ')':
                        tokens.Add(new Token(TokenType.RightParen, ")"));
                        i++;
                        break;
                    case ',':
                        tokens.Add(new Token(TokenType.Comma, ","));
                        i++;
                        break;
                    default:
                        throw new FormatException($"Unexpected character '{c}' at position {i}.");
                }
            }

            return tokens;
        }

        private static List<Token> InsertImplicitMultiplication(List<Token> tokens)
        {
            if (tokens.Count < 2) return tokens;

            var result = new List<Token>(tokens.Count + 8);

            for (int i = 0; i < tokens.Count; i++)
            {
                Token current = tokens[i];
                result.Add(current);

                if (i == tokens.Count - 1) continue;

                Token next = tokens[i + 1];
                bool currentCanEndValue = current.Type is TokenType.Number or TokenType.Identifier or TokenType.RightParen;
                bool nextCanStartValue = next.Type is TokenType.Number or TokenType.Identifier or TokenType.LeftParen;

                if (!currentCanEndValue || !nextCanStartValue) continue;

                bool knownFunctionCall = current.Type == TokenType.Identifier
                    && next.Type == TokenType.LeftParen
                    && Functions.IsFunction(current.Text);

                if (!knownFunctionCall)
                {
                    result.Add(new Token(TokenType.Operator, "*"));
                }
            }

            return result;
        }

        private static List<Token> ToRpn(List<Token> tokens)
        {
            var output = new List<Token>();
            var operators = new Stack<Token>();
            Token? previous = null;

            for (int i = 0; i < tokens.Count; i++)
            {
                Token token = tokens[i];

                switch (token.Type)
                {
                    case TokenType.Number:
                        output.Add(token);
                        break;

                    case TokenType.Identifier:
                        bool isFunction = i + 1 < tokens.Count
                            && tokens[i + 1].Type == TokenType.LeftParen
                            && Functions.IsFunction(token.Text);

                        if (isFunction)
                            operators.Push(token);
                        else
                            output.Add(token);
                        break;

                    case TokenType.Comma:
                        while (operators.Count > 0 && operators.Peek().Type != TokenType.LeftParen)
                        {
                            output.Add(operators.Pop());
                        }

                        if (operators.Count == 0)
                            throw new FormatException("Misplaced comma or mismatched parentheses");
                        break;

                    case TokenType.Operator:
                        string op = token.Text;
                        bool isUnary = op == "-" && (previous == null
                            || previous.Value.Type is TokenType.Operator or TokenType.LeftParen or TokenType.Comma);

                        if (isUnary)
                        {
                            operators.Push(new Token(TokenType.Operator, "u-"));
                            break;
                        }

                        int currentPrecedence = GetPrecedence(op);
                        bool rightAssociative = IsRightAssociative(op);

                        while (operators.Count > 0 && operators.Peek().Type == TokenType.Operator)
                        {
                            string top = operators.Peek().Text;
                            int topPrecedence = GetPrecedence(top);

                            bool shouldPop = rightAssociative
                                ? currentPrecedence < topPrecedence
                                : currentPrecedence <= topPrecedence;

                            if (!shouldPop) break;
                            output.Add(operators.Pop());
                        }

                        operators.Push(token);
                        break;

                    case TokenType.LeftParen:
                        operators.Push(token);
                        break;

                    case TokenType.RightParen:
                        while (operators.Count > 0 && operators.Peek().Type != TokenType.LeftParen)
                        {
                            output.Add(operators.Pop());
                        }

                        if (operators.Count == 0)
                            throw new FormatException("Mismatched parentheses");

                        operators.Pop();

                        if (operators.Count > 0 && operators.Peek().Type == TokenType.Identifier)
                        {
                            output.Add(operators.Pop());
                        }
                        break;
                }

                previous = token;
            }

            while (operators.Count > 0)
            {
                Token token = operators.Pop();
                if (token.Type is TokenType.LeftParen or TokenType.RightParen)
                    throw new FormatException("Mismatched parentheses");

                output.Add(token);
            }

            return output;
        }

        private static int GetPrecedence(string op)
        {
            return op switch
            {
                "^" => 5,
                "u-" => 4,
                "*" or "/" => 3,
                "+" or "-" => 2,
                "<" or ">" or "<=" or ">=" or "==" or "!=" => 1,
                _ => 0
            };
        }

        private static bool IsRightAssociative(string op)
        {
            return op is "^" or "u-";
        }

        private static double EvaluateRpn(
            IReadOnlyList<Token> rpn,
            double? x,
            double? y,
            IDictionary<string, double>? variables)
        {
            var stack = new double[Math.Max(4, rpn.Count)];
            int count = 0;

            foreach (Token token in rpn)
            {
                switch (token.Type)
                {
                    case TokenType.Number:
                        if (!double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                            throw new FormatException($"Invalid number '{token.Text}'");

                        stack[count++] = value;
                        break;

                    case TokenType.Identifier:
                        string name = token.Text.ToLowerInvariant();

                        if (Functions.TryGet(name, out var function))
                        {
                            if (count < function.Arity)
                                throw new FormatException($"Function {name} expects {function.Arity} arguments");

                            int argumentStart = count - function.Arity;
                            double functionValue = function.Evaluate(stack, argumentStart);
                            count = argumentStart;
                            stack[count++] = functionValue;
                        }
                        else if (name == "pi")
                        {
                            stack[count++] = Math.PI;
                        }
                        else if (name == "e")
                        {
                            stack[count++] = Math.E;
                        }
                        else if (name == "x" && x.HasValue)
                        {
                            stack[count++] = x.Value;
                        }
                        else if (name == "y" && y.HasValue)
                        {
                            stack[count++] = y.Value;
                        }
                        else if (variables != null && TryGetVariable(variables, name, out double variableValue))
                        {
                            stack[count++] = variableValue;
                        }
                        else
                        {
                            throw new KeyNotFoundException($"Unknown identifier '{token.Text}'");
                        }
                        break;

                    case TokenType.Operator:
                        if (token.Text == "u-")
                        {
                            if (count < 1) throw new FormatException("Insufficient values for unary -");
                            stack[count - 1] = -stack[count - 1];
                            break;
                        }

                        if (count < 2)
                            throw new FormatException($"Insufficient values for operator {token.Text}");

                        double right = stack[--count];
                        double left = stack[--count];
                        stack[count++] = ApplyOperator(token.Text, left, right);
                        break;

                    default:
                        throw new InvalidOperationException("Invalid token in compiled expression");
                }
            }

            if (count != 1)
                throw new FormatException("The expression could not be evaluated to a single value");

            return stack[0];
        }

        private static bool TryGetVariable(IDictionary<string, double> variables, string name, out double value)
        {
            if (variables.TryGetValue(name, out value)) return true;

            foreach (var pair in variables)
            {
                if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string BuildHlsl(IReadOnlyList<Token> rpn)
        {
            var stack = new Stack<string>();

            foreach (Token token in rpn)
            {
                if (token.Type == TokenType.Number)
                {
                    stack.Push(token.Text.Contains('.') || token.Text.IndexOf('e') >= 0 || token.Text.IndexOf('E') >= 0
                        ? token.Text
                        : token.Text + ".0");
                    continue;
                }

                if (token.Type == TokenType.Identifier)
                {
                    string name = token.Text.ToLowerInvariant();
                    if (name == "pi") { stack.Push("3.141592653589793"); continue; }
                    if (name == "e") { stack.Push("2.718281828459045"); continue; }

                    if (Functions.TryGet(name, out FunctionDefinition? function))
                    {
                        if (stack.Count < function.Arity) throw new FormatException($"Function {name} expects {function.Arity} arguments");
                        var args = new string[function.Arity];
                        for (int i = function.Arity - 1; i >= 0; i--) args[i] = stack.Pop();
                        stack.Push(FunctionToHlsl(name, args));
                    }
                    else
                    {
                        stack.Push(token.Text);
                    }
                    continue;
                }

                if (token.Type != TokenType.Operator) continue;
                if (token.Text == "u-")
                {
                    string value = stack.Pop();
                    stack.Push($"(-({value}))");
                    continue;
                }

                string right = stack.Pop();
                string left = stack.Pop();
                if (token.Text == "^") stack.Push($"pow(({left}), ({right}))");
                else if (token.Text is "<" or ">" or "<=" or ">=" or "==" or "!=")
                    stack.Push($"((({left}) {token.Text} ({right})) ? 1.0 : 0.0)");
                else stack.Push($"(({left}) {token.Text} ({right}))");
            }

            if (stack.Count != 1) throw new FormatException("Expression could not be translated to HLSL");
            return stack.Pop();
        }

        private static string FunctionToHlsl(string name, IReadOnlyList<string> a)
        {
            string Call(string functionName) => functionName + "(" + string.Join(", ", a) + ")";
            return name switch
            {
                "ln" => $"log({a[0]})",
                "log" => $"log10({a[0]})",
                "mod" => $"(({a[0]}) - ({a[1]}) * floor(({a[0]}) / ({a[1]})))",
                "repeat" => $"(({a[0]}) - floor(({a[0]}) / ({a[1]})) * ({a[1]}))",
                "pingpong" => $"gc_pingpong({a[0]}, {a[1]})",
                "inverselerp" => $"gc_inverseLerp({a[0]}, {a[1]}, {a[2]})",
                "smootherstep" => $"gc_smootherstep({a[0]}, {a[1]}, {a[2]})",
                "remap" => $"gc_remap({a[0]}, {a[1]}, {a[2]}, {a[3]}, {a[4]})",
                "noise" => $"gc_noise(float2({a[0]}, {a[1]}), 0.0)",
                "noiseseed" => $"gc_noise(float2({a[0]}, {a[1]}), {a[2]})",
                "fbm" => $"gc_fbm(float2({a[0]}, {a[1]}), {a[2]}, {a[3]}, {a[4]})",
                "if" or "select" => $"(({a[0]}) != 0.0 ? ({a[1]}) : ({a[2]}))",
                "and" => $"((({a[0]}) != 0.0 && ({a[1]}) != 0.0) ? 1.0 : 0.0)",
                "or" => $"((({a[0]}) != 0.0 || ({a[1]}) != 0.0) ? 1.0 : 0.0)",
                "not" => $"(({a[0]}) == 0.0 ? 1.0 : 0.0)",
                "bernoulli" => $"(({a[1]}) < saturate({a[0]}) ? 1.0 : 0.0)",
                "uniform" => $"lerp({a[0]}, {a[1]}, saturate({a[2]}))",
                "normal" => $"(({a[0]}) + ({a[1]}) * ({a[2]}))",
                _ => Call(name)
            };
        }

        private static double ApplyOperator(string op, double left, double right)
        {
            return op switch
            {
                "+" => left + right,
                "-" => left - right,
                "*" => left * right,
                "/" => right == 0 ? double.NaN : left / right,
                "^" => Math.Pow(left, right),
                "<" => left < right ? 1.0 : 0.0,
                ">" => left > right ? 1.0 : 0.0,
                "<=" => left <= right ? 1.0 : 0.0,
                ">=" => left >= right ? 1.0 : 0.0,
                "==" => Math.Abs(left - right) <= 1e-12 ? 1.0 : 0.0,
                "!=" => Math.Abs(left - right) > 1e-12 ? 1.0 : 0.0,
                _ => throw new InvalidOperationException($"Unknown operator '{op}'")
            };
        }

        private sealed record FunctionDefinition(int Arity, Func<double[], int, double> Evaluate);

        private static class Functions
        {
            private static readonly Dictionary<string, FunctionDefinition> Table = new(StringComparer.OrdinalIgnoreCase)
            {
                ["sin"] = Unary(Math.Sin),
                ["cos"] = Unary(Math.Cos),
                ["tan"] = Unary(Math.Tan),
                ["asin"] = Unary(Math.Asin),
                ["acos"] = Unary(Math.Acos),
                ["atan"] = Unary(Math.Atan),
                ["sqrt"] = Unary(value => value < 0 ? double.NaN : Math.Sqrt(value)),
                ["ln"] = Unary(value => value <= 0 ? double.NaN : Math.Log(value)),
                ["log"] = Unary(value => value <= 0 ? double.NaN : Math.Log10(value)),
                ["exp"] = Unary(Math.Exp),
                ["abs"] = Unary(Math.Abs),
                ["floor"] = Unary(Math.Floor),
                ["ceil"] = Unary(Math.Ceiling),
                ["round"] = Unary(Math.Round),
                ["sign"] = Unary(value => Math.Sign(value)),
                ["frac"] = Unary(value => value - Math.Floor(value)),
                ["saturate"] = Unary(value => Math.Clamp(value, 0.0, 1.0)),
                ["not"] = Unary(value => IsTruthy(value) ? 0.0 : 1.0),

                ["pow"] = Binary(Math.Pow),
                ["max"] = Binary(Math.Max),
                ["min"] = Binary(Math.Min),
                ["mod"] = Binary((a, b) => b == 0 ? double.NaN : a - b * Math.Floor(a / b)),
                ["step"] = Binary((edge, value) => value < edge ? 0.0 : 1.0),
                ["atan2"] = Binary(Math.Atan2),
                ["noise"] = Binary((x, y) => ValueNoise(x, y, 0)),
                ["repeat"] = Binary((value, length) => length == 0 ? double.NaN : value - Math.Floor(value / length) * length),
                ["pingpong"] = Binary((value, length) => PingPong(value, length)),
                ["and"] = Binary((a, b) => IsTruthy(a) && IsTruthy(b) ? 1.0 : 0.0),
                ["or"] = Binary((a, b) => IsTruthy(a) || IsTruthy(b) ? 1.0 : 0.0),
                ["bernoulli"] = Binary((p, r) => r < Math.Clamp(p, 0.0, 1.0) ? 1.0 : 0.0),

                ["clamp"] = Ternary((value, min, max) => min > max ? double.NaN : Math.Clamp(value, min, max)),
                ["lerp"] = Ternary((a, b, t) => a + (b - a) * t),
                ["inverselerp"] = Ternary((a, b, value) => Math.Abs(b - a) < 1e-15 ? 0.0 : (value - a) / (b - a)),
                ["smoothstep"] = Ternary((edge0, edge1, value) => SmoothStep(edge0, edge1, value)),
                ["smootherstep"] = Ternary((edge0, edge1, value) => SmootherStep(edge0, edge1, value)),
                ["noiseseed"] = Ternary((x, y, seed) => ValueNoise(x, y, seed)),
                ["if"] = Ternary((condition, whenTrue, whenFalse) => IsTruthy(condition) ? whenTrue : whenFalse),
                ["select"] = Ternary((condition, whenTrue, whenFalse) => IsTruthy(condition) ? whenTrue : whenFalse),
                ["uniform"] = Ternary((min, max, r) => min + (max - min) * Math.Clamp(r, 0.0, 1.0)),
                ["normal"] = Ternary((mean, stddev, gaussian) => mean + stddev * gaussian),

                ["remap"] = Nary(5, (values, offset) =>
                {
                    double inMin = values[offset];
                    double inMax = values[offset + 1];
                    if (Math.Abs(inMax - inMin) < 1e-15) return double.NaN;
                    double t = (values[offset + 4] - inMin) / (inMax - inMin);
                    return values[offset + 2] + (values[offset + 3] - values[offset + 2]) * t;
                }),
                ["fbm"] = Nary(5, (values, offset) => Fbm(
                    values[offset], values[offset + 1], values[offset + 2], values[offset + 3], values[offset + 4]))
            };

            public static bool IsFunction(string name) => Table.ContainsKey(name);

            public static bool TryGet(string name, out FunctionDefinition definition)
            {
                if (Table.TryGetValue(name, out FunctionDefinition? found))
                {
                    definition = found;
                    return true;
                }

                definition = null!;
                return false;
            }

            private static FunctionDefinition Unary(Func<double, double> function)
                => new(1, (values, offset) => function(values[offset]));

            private static FunctionDefinition Binary(Func<double, double, double> function)
                => new(2, (values, offset) => function(values[offset], values[offset + 1]));

            private static FunctionDefinition Ternary(Func<double, double, double, double> function)
                => new(3, (values, offset) => function(values[offset], values[offset + 1], values[offset + 2]));

            private static FunctionDefinition Nary(int arity, Func<double[], int, double> function)
                => new(arity, function);

            private static bool IsTruthy(double value)
            {
                return double.IsFinite(value) && Math.Abs(value) > 1e-12;
            }

            private static double SmoothStep(double edge0, double edge1, double value)
            {
                if (Math.Abs(edge1 - edge0) < 1e-15) return value < edge0 ? 0.0 : 1.0;
                double t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
                return t * t * (3.0 - 2.0 * t);
            }


            private static double SmootherStep(double edge0, double edge1, double value)
            {
                if (Math.Abs(edge1 - edge0) < 1e-15) return value < edge0 ? 0.0 : 1.0;
                double t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
                return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
            }

            private static double PingPong(double value, double length)
            {
                if (length <= 0) return double.NaN;
                double repeated = value - Math.Floor(value / (2.0 * length)) * (2.0 * length);
                return length - Math.Abs(repeated - length);
            }
            private static double ValueNoise(double x, double y, double seed)
            {
                int x0 = (int)Math.Floor(x);
                int y0 = (int)Math.Floor(y);
                int x1 = x0 + 1;
                int y1 = y0 + 1;

                double tx = Fade(x - x0);
                double ty = Fade(y - y0);

                double a = Hash01(x0, y0, seed);
                double b = Hash01(x1, y0, seed);
                double c = Hash01(x0, y1, seed);
                double d = Hash01(x1, y1, seed);

                double ab = a + (b - a) * tx;
                double cd = c + (d - c) * tx;
                return (ab + (cd - ab) * ty) * 2.0 - 1.0;
            }

            private static double Fbm(double x, double y, double octavesValue, double persistence, double lacunarity)
            {
                int octaves = Math.Clamp((int)Math.Round(octavesValue), 1, 10);
                persistence = Math.Clamp(persistence, 0.0, 1.0);
                lacunarity = Math.Clamp(lacunarity, 1.01, 8.0);

                double amplitude = 1.0;
                double frequency = 1.0;
                double total = 0.0;
                double weight = 0.0;

                for (int octave = 0; octave < octaves; octave++)
                {
                    total += ValueNoise(x * frequency, y * frequency, octave * 101.0) * amplitude;
                    weight += amplitude;
                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                return weight > 0 ? total / weight : 0.0;
            }

            private static double Fade(double t)
            {
                // Quintic fade avoids the visible slope break linear interpolation leaves at cell edges.
                return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
            }

            private static double Hash01(int x, int y, double seed)
            {
                double n = Math.Sin(x * 127.1 + y * 311.7 + seed * 74.7) * 43758.5453123;
                return n - Math.Floor(n);
            }
        }
    }
}
