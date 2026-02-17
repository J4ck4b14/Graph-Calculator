using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WpfTestApp
{
    // Simple expression evaluator that supports numbers, variables, functions, parentheses and operators.
    // It implements a tokenizer -> shunting-yard -> RPN evaluator pipeline.
    // Supported operators: + - * / ^ and unary minus.
    // Supported functions: sin, cos, tan, asin, acos, atan, sqrt, ln, log, exp, abs, floor, ceil, pow, max, min
    // Supported constants: pi, e
    public static class CalculatorEngine
    {
        private enum TokenType { Number, Identifier, Operator, LeftParen, RightParen, Comma }
        private record Token(TokenType Type, string Text);

        public static double Evaluate(string expression, IDictionary<string, double>? variables = null)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("Expression is empty");

            var tokens = Tokenize(expression);
            var rpn = ToRpn(tokens);
            return EvalRpn(rpn, variables ?? new Dictionary<string, double>());
        }

        private static List<Token> Tokenize(string s)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (char.IsDigit(c) || c == '.')
                {
                    int start = i;
                    bool seenDot = c == '.';
                    i++;
                    while (i < s.Length && (char.IsDigit(s[i]) || (!seenDot && s[i] == '.')))
                    {
                        if (s[i] == '.') seenDot = true;
                        i++;
                    }
                    tokens.Add(new Token(TokenType.Number, s.Substring(start, i - start)));
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i; i++;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    tokens.Add(new Token(TokenType.Identifier, s.Substring(start, i - start)));
                    continue;
                }
                switch (c)
                {
                    case '+': case '-': case '*': case '/': case '^':
                        tokens.Add(new Token(TokenType.Operator, c.ToString())); i++; break;
                    case '(':
                        tokens.Add(new Token(TokenType.LeftParen, "(")); i++; break;
                    case ')':
                        tokens.Add(new Token(TokenType.RightParen, ")")); i++; break;
                    case ',':
                        tokens.Add(new Token(TokenType.Comma, ",")); i++; break;
                    default:
                        throw new FormatException($"Unexpected character '{c}' at position {i}.");
                }
            }
            return tokens;
        }

        private static List<Token> ToRpn(List<Token> tokens)
        {
            var output = new List<Token>();
            var ops = new Stack<Token>();

            Token? prev = null;
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                switch (t.Type)
                {
                    case TokenType.Number:
                        output.Add(t);
                        break;
                    case TokenType.Identifier:
                        // If identifier is followed by '(' it's a function, otherwise variable/constant
                        bool isFunc = (i + 1 < tokens.Count) && tokens[i + 1].Type == TokenType.LeftParen;
                        if (isFunc)
                        {
                            ops.Push(new Token(TokenType.Identifier, t.Text)); // function marker on ops stack
                        }
                        else
                        {
                            output.Add(t); // variable or constant
                        }
                        break;
                    case TokenType.Comma:
                        // pop until left paren
                        while (ops.Count > 0 && ops.Peek().Type != TokenType.LeftParen)
                        {
                            output.Add(ops.Pop());
                        }
                        if (ops.Count == 0) throw new FormatException("Misplaced comma or mismatched parentheses");
                        break;
                    case TokenType.Operator:
                        string op = t.Text;
                        // detect unary minus: if prev is null or prev is operator/leftparen/comma => unary
                        if (op == "-" && (prev == null || prev.Type == TokenType.Operator || prev.Type == TokenType.LeftParen || prev.Type == TokenType.Comma))
                        {
                            // represent unary minus as identifier 'u-'
                            ops.Push(new Token(TokenType.Operator, "u-"));
                        }
                        else
                        {
                            var curPrec = GetPrecedence(op);
                            var curRight = IsRightAssociative(op);
                            while (ops.Count > 0 && ops.Peek().Type == TokenType.Operator)
                            {
                                var top = ops.Peek().Text;
                                var topPrec = GetPrecedence(top);
                                if ((curRight && curPrec < topPrec) || (!curRight && curPrec <= topPrec))
                                {
                                    output.Add(ops.Pop());
                                }
                                else break;
                            }
                            ops.Push(t);
                        }
                        break;
                    case TokenType.LeftParen:
                        ops.Push(t);
                        break;
                    case TokenType.RightParen:
                        while (ops.Count > 0 && ops.Peek().Type != TokenType.LeftParen)
                        {
                            output.Add(ops.Pop());
                        }
                        if (ops.Count == 0) throw new FormatException("Mismatched parentheses");
                        ops.Pop(); // pop '('
                        // if top is a function identifier, pop it to output
                        if (ops.Count > 0 && ops.Peek().Type == TokenType.Identifier)
                        {
                            output.Add(ops.Pop());
                        }
                        break;
                }
                prev = t;
            }

            while (ops.Count > 0)
            {
                var t = ops.Pop();
                if (t.Type == TokenType.LeftParen || t.Type == TokenType.RightParen)
                    throw new FormatException("Mismatched parentheses");
                output.Add(t);
            }
            return output;
        }

        private static int GetPrecedence(string op)
        {
            return op switch
            {
                "u-" => 5,
                "^" => 4,
                "*" or "/" => 3,
                "+" or "-" => 2,
                _ => 0
            };
        }
        private static bool IsRightAssociative(string op) => op == "^" || op == "u-";

        private static double EvalRpn(List<Token> rpn, IDictionary<string, double> vars)
        {
            var stack = new Stack<double>();
            foreach (var t in rpn)
            {
                switch (t.Type)
                {
                    case TokenType.Number:
                        if (!double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                            throw new FormatException($"Invalid number '{t.Text}'");
                        stack.Push(v);
                        break;
                    case TokenType.Identifier:
                        // function or variable/constant
                        string name = t.Text.ToLowerInvariant();
                        if (Functions.IsFunction(name))
                        {
                            var arity = Functions.GetArity(name);
                            if (stack.Count < arity) throw new FormatException($"Function {name} expects {arity} arguments");
                            var args = new double[arity];
                            for (int i = arity - 1; i >= 0; i--) args[i] = stack.Pop();
                            var res = Functions.Invoke(name, args);
                            stack.Push(res);
                        }
                        else
                        {
                            if (name == "pi") stack.Push(Math.PI);
                            else if (name == "e") stack.Push(Math.E);
                            else if (vars != null && vars.TryGetValue(name, out var varv)) stack.Push(varv);
                            else throw new KeyNotFoundException($"Unknown identifier '{t.Text}'");
                        }
                        break;
                    case TokenType.Operator:
                        string op = t.Text;
                        if (op == "u-")
                        {
                            if (stack.Count < 1) throw new FormatException("Insufficient values for unary -");
                            var a = stack.Pop(); stack.Push(-a);
                        }
                        else
                        {
                            if (stack.Count < 2) throw new FormatException($"Insufficient values for operator {op}");
                            var b = stack.Pop(); var a = stack.Pop();
                            stack.Push(ApplyOp(op, a, b));
                        }
                        break;
                    default:
                        throw new InvalidOperationException("Invalid token in RPN");
                }
            }
            if (stack.Count != 1) throw new FormatException("The expression could not be evaluated to a single value");
            return stack.Pop();
        }

        private static double ApplyOp(string op, double a, double b)
        {
            return op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => b == 0 ? double.NaN : a / b,
                "^" => Math.Pow(a, b),
                _ => throw new InvalidOperationException($"Unknown operator '{op}'")
            };
        }

        private static class Functions
        {
            private static readonly Dictionary<string, (int arity, Func<double[], double> impl)> table =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    { "sin", (1, args => Math.Sin(args[0])) },
                    { "cos", (1, args => Math.Cos(args[0])) },
                    { "tan", (1, args => Math.Tan(args[0])) },
                    { "asin", (1, args => Math.Asin(args[0])) },
                    { "acos", (1, args => Math.Acos(args[0])) },
                    { "atan", (1, args => Math.Atan(args[0])) },
                    { "sqrt", (1, args => args[0] < 0 ? double.NaN : Math.Sqrt(args[0])) },
                    { "ln", (1, args => args[0] <= 0 ? double.NaN : Math.Log(args[0])) },
                    { "log", (1, args => Math.Log10(args[0])) },
                    { "exp", (1, args => Math.Exp(args[0])) },
                    { "abs", (1, args => Math.Abs(args[0])) },
                    { "floor", (1, args => Math.Floor(args[0])) },
                    { "ceil", (1, args => Math.Ceiling(args[0])) },
                    { "pow", (2, args => Math.Pow(args[0], args[1])) },
                    { "max", (2, args => Math.Max(args[0], args[1])) },
                    { "min", (2, args => Math.Min(args[0], args[1])) }
                };

            public static bool IsFunction(string name) => table.ContainsKey(name);
            public static int GetArity(string name) => table[name].arity;
            public static double Invoke(string name, double[] args) => table[name].impl(args);
        }
    }
}
