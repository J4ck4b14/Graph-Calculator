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

            public double Evaluate(IDictionary<string, double> variables)
            {
                return EvaluateRpn(_rpn, null, null, variables);
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
            if (equalsIndex >= 0)
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
                        tokens.Add(new Token(TokenType.Operator, c.ToString()));
                        i++;
                        break;
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
                "^" => 4,
                "u-" => 3,
                "*" or "/" => 2,
                "+" or "-" => 1,
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

                            if (function.Arity == 1)
                            {
                                double a = stack[--count];
                                stack[count++] = function.Unary!(a);
                            }
                            else
                            {
                                double b = stack[--count];
                                double a = stack[--count];
                                stack[count++] = function.Binary!(a, b);
                            }
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

        private static double ApplyOperator(string op, double left, double right)
        {
            return op switch
            {
                "+" => left + right,
                "-" => left - right,
                "*" => left * right,
                "/" => right == 0 ? double.NaN : left / right,
                "^" => Math.Pow(left, right),
                _ => throw new InvalidOperationException($"Unknown operator '{op}'")
            };
        }

        private sealed record FunctionDefinition(
            int Arity,
            Func<double, double>? Unary,
            Func<double, double, double>? Binary);

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
                ["pow"] = Binary(Math.Pow),
                ["max"] = Binary(Math.Max),
                ["min"] = Binary(Math.Min)
            };

            public static bool IsFunction(string name)
            {
                return Table.ContainsKey(name);
            }

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
            {
                return new FunctionDefinition(1, function, null);
            }

            private static FunctionDefinition Binary(Func<double, double, double> function)
            {
                return new FunctionDefinition(2, null, function);
            }
        }
    }
}
