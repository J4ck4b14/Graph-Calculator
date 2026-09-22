using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace GraphCalculator
{
    // Complex/calculus/iterative expressions use this evaluator; simple real maths stays on the RPN path.
    internal static class AdvancedMathEngine
    {
        private const double ImaginaryTolerance = 1e-11;
        private const int MaxLoopIterations = 100_000;
        private const int MaxQuadratureSteps = 8_192;

        private static readonly HashSet<string> AdvancedMarkers = new(StringComparer.OrdinalIgnoreCase)
        {
            "i", "z", "complex", "cis", "polar", "re", "real", "im", "imag", "arg", "phase", "conj",
            "sinh", "cosh", "tanh",
            "csqrt", "clog", "cexp", "csin", "ccos", "ctan",
            "diff", "derivative", "integral", "primitive", "sum", "product",
            "iterate", "iter", "recurrence", "recur", "sequence", "escape", "mandelbrot", "mandelbrotiter", "mandelbrotsmooth",
            "julia", "juliaiter", "juliasmooth"
        };

        private static readonly HashSet<string> FunctionNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "sinh", "cosh", "tanh",
            "sqrt", "ln", "log", "exp", "abs", "floor", "ceil", "round", "sign", "frac", "saturate",
            "pow", "max", "min", "mod", "step", "noise", "repeat", "pingpong", "and", "or", "not",
            "bernoulli", "clamp", "lerp", "inverselerp", "smoothstep", "smootherstep", "noiseseed",
            "if", "select", "uniform", "normal", "remap", "fbm",
            "complex", "cis", "polar", "re", "real", "im", "imag", "arg", "phase", "conj",
            "csqrt", "clog", "cexp", "csin", "ccos", "ctan",
            "diff", "derivative", "integral", "primitive", "sum", "product",
            "iterate", "iter", "recurrence", "recur", "sequence", "escape", "mandelbrot", "mandelbrotiter", "mandelbrotsmooth",
            "julia", "juliaiter", "juliasmooth"
        };

        internal static bool ShouldUse(string expression)
        {
            if (LooksLikeRecurrenceNotation(expression)) return true;
            foreach (Token token in TokenizeRaw(expression))
            {
                if (token.Type == TokenType.Identifier && AdvancedMarkers.Contains(token.Text)) return true;
            }
            return false;
        }

        internal static CompiledProgram Compile(string expression)
        {
            expression = RewriteRecurrenceNotation(expression);
            if (LooksLikeBareRecurrenceDefinition(expression))
            {
                throw new FormatException(
                    "A recurrence needs an initial value and an iteration count. " +
                    "Write recurrence(z[n+1]=z[n]^2+c, z[0]=0, 80), or use iterate(z^2+c,0,80).");
            }

            var parser = new Parser(InsertImplicitMultiplication(TokenizeRaw(expression)));
            Node root = parser.Parse();
            var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            root.CollectVariables(variables, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return new CompiledProgram(root, variables.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        internal sealed class CompiledProgram
        {
            private readonly Node _root;

            internal CompiledProgram(Node root, IReadOnlyCollection<string> variables)
            {
                _root = root;
                Variables = variables;
                IsPotentiallyComplex = root.PotentiallyComplex;
            }

            internal IReadOnlyCollection<string> Variables { get; }
            internal bool IsPotentiallyComplex { get; }

            internal Complex Evaluate(double? x, double? y, IDictionary<string, double>? variables)
            {
                var context = new EvalContext(x, y, variables);
                return _root.Evaluate(context);
            }

            internal string ToHlsl(bool complexPlane, out bool returnsComplex)
            {
                HlslValue value = _root.ToHlsl(new HlslContext(complexPlane));
                returnsComplex = value.Kind == HlslKind.Complex;
                return value.Code;
            }
        }

        // Recurrence notation is rewritten to the same bounded iterate/escape primitives.
        private static bool LooksLikeRecurrenceNotation(string expression)
        {
            return Regex.IsMatch(expression, @"(?i)\b(?:recurrence|recur|sequence|iterate|iter|escape)\s*\(")
                || Regex.IsMatch(expression, @"(?i)\b[A-Za-z][A-Za-z0-9_]*\s*(?:\[\s*n\s*\+\s*1\s*\]|_\{\s*n\s*\+\s*1\s*\})\s*=");
        }

        private static bool LooksLikeBareRecurrenceDefinition(string expression)
        {
            return Regex.IsMatch(expression.Trim(), @"(?i)^\s*[A-Za-z][A-Za-z0-9_]*\s*(?:\[\s*n\s*\+\s*1\s*\]|_\{\s*n\s*\+\s*1\s*\})\s*=");
        }

        private static string RewriteRecurrenceNotation(string expression)
        {
            string text = expression;
            var callPattern = new Regex(@"(?i)\b(?<name>recurrence|recur|sequence|iterate|iter|escape)\s*\(");
            for (int pass = 0; pass < 16; pass++)
            {
                Match match = callPattern.Match(text);
                bool changed = false;
                while (match.Success)
                {
                    int open = text.IndexOf('(', match.Index);
                    int close = FindMatchingParen(text, open);
                    if (close < 0) break;

                    string name = match.Groups["name"].Value.ToLowerInvariant();
                    string body = text[(open + 1)..close];
                    List<string> parts = SplitTopLevelRaw(body);
                    int expected = name == "escape" ? 4 : 3;
                    if (parts.Count == expected
                        && TryParseRecurrenceRelation(parts[0], out string symbol, out string recurrenceBody))
                    {
                        string seed = TryParseRecurrenceSeed(parts[1], symbol, out string parsedSeed)
                            ? parsedSeed
                            : parts[1].Trim();
                        recurrenceBody = ReplaceSequenceReferences(recurrenceBody, symbol);
                        string count = StripNamedArgument(parts[2], "n", "count", "iterations");
                        string target = name == "escape" ? "escape" : "iterate";
                        string replacement = target == "escape"
                            ? $"escape(({recurrenceBody}),({seed}),({count}),({StripNamedArgument(parts[3], "radius", "escape", "r")}))"
                            : $"iterate(({recurrenceBody}),({seed}),({count}))";
                        text = text[..match.Index] + replacement + text[(close + 1)..];
                        changed = true;
                        break;
                    }

                    // Ordinary iterate(expr, seed, count) calls are already valid.
                    match = callPattern.Match(text, close + 1);
                }
                if (!changed) break;
            }
            return text;
        }

        private static int FindMatchingParen(string text, int open)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0) return i;
            }
            return -1;
        }

        private static List<string> SplitTopLevelRaw(string text)
        {
            var result = new List<string>();
            int paren = 0, bracket = 0, brace = 0, start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '(': paren++; break;
                    case ')': paren--; break;
                    case '[': bracket++; break;
                    case ']': bracket--; break;
                    case '{': brace++; break;
                    case '}': brace--; break;
                    case ',' when paren == 0 && bracket == 0 && brace == 0:
                        result.Add(text[start..i].Trim());
                        start = i + 1;
                        break;
                }
            }
            result.Add(text[start..].Trim());
            return result;
        }

        private static bool TryParseRecurrenceRelation(string text, out string symbol, out string body)
        {
            Match relation = Regex.Match(text,
                @"(?is)^\s*(?<name>[A-Za-z][A-Za-z0-9_]*)\s*(?:\[\s*n\s*\+\s*1\s*\]|_\{\s*n\s*\+\s*1\s*\})\s*=\s*(?<body>.+?)\s*$");
            if (!relation.Success)
            {
                symbol = string.Empty;
                body = string.Empty;
                return false;
            }
            symbol = relation.Groups["name"].Value;
            body = relation.Groups["body"].Value.Trim();
            return body.Length > 0;
        }

        private static bool TryParseRecurrenceSeed(string text, string symbol, out string seed)
        {
            string escaped = Regex.Escape(symbol);
            Match initial = Regex.Match(text,
                $@"(?is)^\s*{escaped}\s*(?:\[\s*0\s*\]|_\{{\s*0\s*\}}|_0)\s*=\s*(?<seed>.+?)\s*$");
            seed = initial.Success ? initial.Groups["seed"].Value.Trim() : string.Empty;
            return initial.Success && seed.Length > 0;
        }

        private static string ReplaceSequenceReferences(string body, string symbol)
        {
            string escaped = Regex.Escape(symbol);
            string result = Regex.Replace(body,
                $@"(?i)\b{escaped}\s*(?:\[\s*n\s*\]|_\{{\s*n\s*\}}|_n)", "z");
            // Keep an invalid next-item reference visible so the normal unknown-variable error catches it.
            result = Regex.Replace(result,
                $@"(?i)\b{escaped}\s*(?:\[\s*n\s*\+\s*1\s*\]|_\{{\s*n\s*\+\s*1\s*\}})", "next_value");
            return result;
        }

        private static string StripNamedArgument(string text, params string[] names)
        {
            foreach (string name in names)
            {
                Match match = Regex.Match(text, $@"(?is)^\s*{Regex.Escape(name)}\s*=\s*(?<value>.+)$");
                if (match.Success) return match.Groups["value"].Value.Trim();
            }
            return text.Trim();
        }

        private enum TokenType { Number, Identifier, Operator, LeftParen, RightParen, Comma }
        private readonly record struct Token(TokenType Type, string Text);

        private static List<Token> TokenizeRaw(string expression)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (i < expression.Length)
            {
                char c = expression[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsDigit(c) || (c == '.' && i + 1 < expression.Length && char.IsDigit(expression[i + 1])))
                {
                    int start = i;
                    bool seenDot = false;
                    if (c == '.') { seenDot = true; i++; }
                    while (i < expression.Length && char.IsDigit(expression[i])) i++;
                    if (i < expression.Length && expression[i] == '.' && !seenDot)
                    {
                        i++;
                        while (i < expression.Length && char.IsDigit(expression[i])) i++;
                    }
                    if (i < expression.Length && (expression[i] == 'e' || expression[i] == 'E'))
                    {
                        int exponentStart = i++;
                        if (i < expression.Length && (expression[i] == '+' || expression[i] == '-')) i++;
                        int digits = i;
                        while (i < expression.Length && char.IsDigit(expression[i])) i++;
                        if (digits == i) i = exponentStart;
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
                    case '+': case '-': case '*': case '/': case '^': case '<': case '>': case '=': case '!':
                    {
                        string op = c.ToString();
                        if (i + 1 < expression.Length && expression[i + 1] == '=' && c is '<' or '>' or '=' or '!')
                        {
                            op += "=";
                            i++;
                        }
                        else if (c == '=') op = "==";
                        else if (c == '!') throw new FormatException("Use != for inequality or not(...) for logical negation");
                        tokens.Add(new Token(TokenType.Operator, op));
                        i++;
                        break;
                    }
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

        private static List<Token> InsertImplicitMultiplication(List<Token> tokens)
        {
            var result = new List<Token>(tokens.Count + 8);
            for (int i = 0; i < tokens.Count; i++)
            {
                Token current = tokens[i];
                result.Add(current);
                if (i == tokens.Count - 1) continue;

                Token next = tokens[i + 1];
                bool currentCanEnd = current.Type is TokenType.Number or TokenType.Identifier or TokenType.RightParen;
                bool nextCanStart = next.Type is TokenType.Number or TokenType.Identifier or TokenType.LeftParen;
                if (!currentCanEnd || !nextCanStart) continue;

                bool functionCall = current.Type == TokenType.Identifier
                    && next.Type == TokenType.LeftParen
                    && FunctionNames.Contains(current.Text);
                if (!functionCall) result.Add(new Token(TokenType.Operator, "*"));
            }
            return result;
        }

        private sealed class Parser
        {
            private readonly IReadOnlyList<Token> _tokens;
            private int _position;

            internal Parser(IReadOnlyList<Token> tokens) => _tokens = tokens;

            internal Node Parse()
            {
                if (_tokens.Count == 0) throw new FormatException("Expression is empty");
                Node node = ParseComparison();
                if (_position != _tokens.Count) throw new FormatException($"Unexpected token '{_tokens[_position].Text}'");
                return node;
            }

            private Node ParseComparison()
            {
                Node left = ParseAdditive();
                while (MatchOperator("<", ">", "<=", ">=", "==", "!="))
                {
                    string op = Previous.Text;
                    left = new BinaryNode(op, left, ParseAdditive());
                }
                return left;
            }

            private Node ParseAdditive()
            {
                Node left = ParseMultiplicative();
                while (MatchOperator("+", "-"))
                {
                    string op = Previous.Text;
                    left = new BinaryNode(op, left, ParseMultiplicative());
                }
                return left;
            }

            private Node ParseMultiplicative()
            {
                Node left = ParseUnary();
                while (MatchOperator("*", "/"))
                {
                    string op = Previous.Text;
                    left = new BinaryNode(op, left, ParseUnary());
                }
                return left;
            }

            private Node ParseUnary()
            {
                if (MatchOperator("-")) return new UnaryNode(ParseUnary());
                return ParsePower();
            }

            private Node ParsePower()
            {
                Node left = ParsePrimary();
                if (MatchOperator("^")) return new BinaryNode("^", left, ParseUnary());
                return left;
            }

            private Node ParsePrimary()
            {
                if (Match(TokenType.Number))
                {
                    if (!double.TryParse(Previous.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                        throw new FormatException($"Invalid number '{Previous.Text}'");
                    return new ConstantNode(new Complex(value, 0));
                }

                if (Match(TokenType.Identifier))
                {
                    string name = Previous.Text;
                    if (Match(TokenType.LeftParen))
                    {
                        if (!FunctionNames.Contains(name)) throw new FormatException($"Unknown function '{name}'");
                        var args = new List<Node>();
                        if (!Check(TokenType.RightParen))
                        {
                            do { args.Add(ParseComparison()); } while (Match(TokenType.Comma));
                        }
                        Consume(TokenType.RightParen, "Expected ')' after function arguments");
                        return new FunctionNode(name, args);
                    }
                    if (name.Equals("pi", StringComparison.OrdinalIgnoreCase)) return new ConstantNode(new Complex(Math.PI, 0));
                    if (name.Equals("e", StringComparison.OrdinalIgnoreCase)) return new ConstantNode(new Complex(Math.E, 0));
                    if (name.Equals("i", StringComparison.OrdinalIgnoreCase)) return new ConstantNode(Complex.ImaginaryOne);
                    return new VariableNode(name);
                }

                if (Match(TokenType.LeftParen))
                {
                    Node value = ParseComparison();
                    Consume(TokenType.RightParen, "Expected ')' after expression");
                    return value;
                }

                string token = _position < _tokens.Count ? _tokens[_position].Text : "end of expression";
                throw new FormatException($"Expected a value before '{token}'");
            }

            private bool Match(TokenType type)
            {
                if (!Check(type)) return false;
                _position++;
                return true;
            }

            private bool MatchOperator(params string[] operators)
            {
                if (!Check(TokenType.Operator)) return false;
                string text = _tokens[_position].Text;
                if (!operators.Contains(text, StringComparer.Ordinal)) return false;
                _position++;
                return true;
            }

            private bool Check(TokenType type) => _position < _tokens.Count && _tokens[_position].Type == type;
            private Token Previous => _tokens[_position - 1];

            private void Consume(TokenType type, string message)
            {
                if (!Match(type)) throw new FormatException(message);
            }
        }

        internal sealed class EvalContext
        {
            internal EvalContext(double? x, double? y, IDictionary<string, double>? variables)
            {
                X = x;
                Y = y;
                Variables = variables;
            }

            internal double? X { get; }
            internal double? Y { get; }
            internal IDictionary<string, double>? Variables { get; }
            internal Dictionary<string, Complex> Locals { get; } = new(StringComparer.OrdinalIgnoreCase);

            internal Complex Resolve(string name)
            {
                if (Locals.TryGetValue(name, out Complex local)) return local;
                if (Variables != null && TryGetVariable(Variables, name, out double supplied)) return new Complex(supplied, 0);
                if (name.Equals("x", StringComparison.OrdinalIgnoreCase) && X.HasValue) return new Complex(X.Value, 0);
                if (name.Equals("y", StringComparison.OrdinalIgnoreCase) && Y.HasValue) return new Complex(Y.Value, 0);
                if (name.Equals("z", StringComparison.OrdinalIgnoreCase) && X.HasValue && Y.HasValue) return new Complex(X.Value, Y.Value);
                throw new KeyNotFoundException($"Unknown identifier '{name}'");
            }
        }

        internal abstract class Node
        {
            internal abstract Complex Evaluate(EvalContext context);
            internal abstract void CollectVariables(HashSet<string> variables, HashSet<string> bound);
            internal abstract bool PotentiallyComplex { get; }
            internal abstract HlslValue ToHlsl(HlslContext context);
        }

        private sealed class ConstantNode : Node
        {
            private readonly Complex _value;
            internal ConstantNode(Complex value) => _value = value;
            internal override Complex Evaluate(EvalContext context) => _value;
            internal override void CollectVariables(HashSet<string> variables, HashSet<string> bound) { }
            internal override bool PotentiallyComplex => Math.Abs(_value.Imaginary) > ImaginaryTolerance;
            internal override HlslValue ToHlsl(HlslContext context)
            {
                if (Math.Abs(_value.Imaginary) <= ImaginaryTolerance)
                    return HlslValue.Real(FormatHlslNumber(_value.Real));
                return HlslValue.Complex($"float2({FormatHlslNumber(_value.Real)}, {FormatHlslNumber(_value.Imaginary)})");
            }
        }

        private sealed class VariableNode : Node
        {
            internal VariableNode(string name) => Name = name;
            internal string Name { get; }
            internal override Complex Evaluate(EvalContext context) => context.Resolve(Name);
            internal override void CollectVariables(HashSet<string> variables, HashSet<string> bound)
            {
                if (!bound.Contains(Name)) variables.Add(Name.ToLowerInvariant());
            }
            internal override bool PotentiallyComplex => Name.Equals("z", StringComparison.OrdinalIgnoreCase);
            internal override HlslValue ToHlsl(HlslContext context)
            {
                if (Name.Equals("z", StringComparison.OrdinalIgnoreCase) && context.ComplexPlane)
                    return HlslValue.Complex("float2(x, y)");
                return HlslValue.Real(Name);
            }
        }

        private sealed class UnaryNode : Node
        {
            private readonly Node _value;
            internal UnaryNode(Node value) => _value = value;
            internal override Complex Evaluate(EvalContext context) => -_value.Evaluate(context);
            internal override void CollectVariables(HashSet<string> variables, HashSet<string> bound) => _value.CollectVariables(variables, bound);
            internal override bool PotentiallyComplex => _value.PotentiallyComplex;
            internal override HlslValue ToHlsl(HlslContext context)
            {
                HlslValue value = _value.ToHlsl(context);
                return new HlslValue($"(-({value.Code}))", value.Kind);
            }
        }

        private sealed class BinaryNode : Node
        {
            private readonly string _operator;
            private readonly Node _left;
            private readonly Node _right;

            internal BinaryNode(string op, Node left, Node right)
            {
                _operator = op;
                _left = left;
                _right = right;
            }

            internal override Complex Evaluate(EvalContext context)
            {
                Complex left = _left.Evaluate(context);
                Complex right = _right.Evaluate(context);
                return _operator switch
                {
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" => Complex.Abs(right) <= 1e-300 ? new Complex(double.NaN, double.NaN) : left / right,
                    "^" => Complex.Pow(left, right),
                    "<" => Bool(RequireReal(left, "comparison") < RequireReal(right, "comparison")),
                    ">" => Bool(RequireReal(left, "comparison") > RequireReal(right, "comparison")),
                    "<=" => Bool(RequireReal(left, "comparison") <= RequireReal(right, "comparison")),
                    ">=" => Bool(RequireReal(left, "comparison") >= RequireReal(right, "comparison")),
                    "==" => Bool(Complex.Abs(left - right) <= 1e-12),
                    "!=" => Bool(Complex.Abs(left - right) > 1e-12),
                    _ => throw new InvalidOperationException($"Unknown operator '{_operator}'")
                };
            }

            internal override void CollectVariables(HashSet<string> variables, HashSet<string> bound)
            {
                _left.CollectVariables(variables, bound);
                _right.CollectVariables(variables, bound);
            }

            internal override bool PotentiallyComplex => _operator is not ("<" or ">" or "<=" or ">=" or "==" or "!=")
                && (_left.PotentiallyComplex || _right.PotentiallyComplex);

            internal override HlslValue ToHlsl(HlslContext context)
            {
                HlslValue left = _left.ToHlsl(context);
                HlslValue right = _right.ToHlsl(context);
                if (_operator is "<" or ">" or "<=" or ">=" or "==" or "!=")
                {
                    if (left.Kind == HlslKind.Complex || right.Kind == HlslKind.Complex)
                    {
                        if (_operator is "==" or "!=")
                        {
                            string distance = $"length({Promote(left)} - {Promote(right)})";
                            string test = _operator == "==" ? $"({distance} <= 1e-6)" : $"({distance} > 1e-6)";
                            return HlslValue.Real($"({test} ? 1.0 : 0.0)");
                        }
                        throw new NotSupportedException("Ordering comparisons are defined only for real values.");
                    }
                    return HlslValue.Real($"((({left.Code}) {_operator} ({right.Code})) ? 1.0 : 0.0)");
                }

                if (left.Kind == HlslKind.Real && right.Kind == HlslKind.Real)
                {
                    if (_operator == "^") return HlslValue.Real($"pow(({left.Code}), ({right.Code}))");
                    return HlslValue.Real($"(({left.Code}) {_operator} ({right.Code}))");
                }

                string a = Promote(left);
                string b = Promote(right);
                return _operator switch
                {
                    "+" => HlslValue.Complex($"({a} + {b})"),
                    "-" => HlslValue.Complex($"({a} - {b})"),
                    "*" => HlslValue.Complex($"gc_cmul({a}, {b})"),
                    "/" => HlslValue.Complex($"gc_cdiv({a}, {b})"),
                    "^" => HlslValue.Complex($"gc_cpow({a}, {b})"),
                    _ => throw new NotSupportedException($"Complex operator '{_operator}' cannot be exported to HLSL")
                };
            }
        }

        private sealed class FunctionNode : Node
        {
            private readonly string _name;
            private readonly IReadOnlyList<Node> _arguments;

            internal FunctionNode(string name, IReadOnlyList<Node> arguments)
            {
                _name = name.ToLowerInvariant();
                _arguments = arguments;
                ValidateArity();
            }

            internal override Complex Evaluate(EvalContext context)
            {
                switch (_name)
                {
                    case "if": case "select":
                    {
                        Complex condition = _arguments[0].Evaluate(context);
                        return IsTruthy(condition) ? _arguments[1].Evaluate(context) : _arguments[2].Evaluate(context);
                    }
                    case "and":
                        return Bool(IsTruthy(_arguments[0].Evaluate(context)) && IsTruthy(_arguments[1].Evaluate(context)));
                    case "or":
                        return Bool(IsTruthy(_arguments[0].Evaluate(context)) || IsTruthy(_arguments[1].Evaluate(context)));
                    case "not":
                        return Bool(!IsTruthy(_arguments[0].Evaluate(context)));
                    case "diff": case "derivative":
                        return EvaluateDerivative(context);
                    case "integral":
                        return EvaluateIntegral(context, primitive: false);
                    case "primitive":
                        return EvaluateIntegral(context, primitive: true);
                    case "sum":
                        return EvaluateSeries(context, product: false);
                    case "product":
                        return EvaluateSeries(context, product: true);
                    case "iterate": case "iter": case "recurrence": case "recur": case "sequence":
                        return EvaluateIteration(context, returnEscapeCount: false);
                    case "escape":
                        return EvaluateIteration(context, returnEscapeCount: true);
                }

                Complex[] a = _arguments.Select(argument => argument.Evaluate(context)).ToArray();
                return EvaluateOrdinary(a);
            }

            private Complex EvaluateOrdinary(IReadOnlyList<Complex> a)
            {
                return _name switch
                {
                    "sin" or "csin" => Complex.Sin(a[0]),
                    "cos" or "ccos" => Complex.Cos(a[0]),
                    "tan" or "ctan" => Complex.Tan(a[0]),
                    "asin" => Complex.Asin(a[0]),
                    "acos" => Complex.Acos(a[0]),
                    "atan" => Complex.Atan(a[0]),
                    "sinh" => Complex.Sinh(a[0]),
                    "cosh" => Complex.Cosh(a[0]),
                    "tanh" => Complex.Tanh(a[0]),
                    "sqrt" or "csqrt" => Complex.Sqrt(a[0]),
                    "ln" or "clog" => Complex.Log(a[0]),
                    "log" => Complex.Log(a[0]) / Math.Log(10.0),
                    "exp" or "cexp" => Complex.Exp(a[0]),
                    "abs" => new Complex(Complex.Abs(a[0]), 0),
                    "re" or "real" => new Complex(a[0].Real, 0),
                    "im" or "imag" => new Complex(a[0].Imaginary, 0),
                    "arg" or "phase" => new Complex(Math.Atan2(a[0].Imaginary, a[0].Real), 0),
                    "conj" => Complex.Conjugate(a[0]),
                    "complex" => new Complex(RequireReal(a[0], "complex"), RequireReal(a[1], "complex")),
                    "cis" => Complex.FromPolarCoordinates(1.0, RequireReal(a[0], "cis")),
                    "polar" => Complex.FromPolarCoordinates(RequireReal(a[0], "polar"), RequireReal(a[1], "polar")),
                    "floor" => new Complex(Math.Floor(RequireReal(a[0], "floor")), 0),
                    "ceil" => new Complex(Math.Ceiling(RequireReal(a[0], "ceil")), 0),
                    "round" => new Complex(Math.Round(RequireReal(a[0], "round")), 0),
                    "sign" => new Complex(Math.Sign(RequireReal(a[0], "sign")), 0),
                    "frac" => new Complex(Frac(RequireReal(a[0], "frac")), 0),
                    "saturate" => new Complex(Math.Clamp(RequireReal(a[0], "saturate"), 0, 1), 0),
                    "pow" => Complex.Pow(a[0], a[1]),
                    "max" => new Complex(Math.Max(RequireReal(a[0], "max"), RequireReal(a[1], "max")), 0),
                    "min" => new Complex(Math.Min(RequireReal(a[0], "min"), RequireReal(a[1], "min")), 0),
                    "mod" => new Complex(FloorMod(RequireReal(a[0], "mod"), RequireReal(a[1], "mod")), 0),
                    "step" => Bool(RequireReal(a[1], "step") >= RequireReal(a[0], "step")),
                    "atan2" => new Complex(Math.Atan2(RequireReal(a[0], "atan2"), RequireReal(a[1], "atan2")), 0),
                    "noise" => new Complex(ValueNoise(RequireReal(a[0], "noise"), RequireReal(a[1], "noise"), 0), 0),
                    "repeat" => new Complex(Repeat(RequireReal(a[0], "repeat"), RequireReal(a[1], "repeat")), 0),
                    "pingpong" => new Complex(PingPong(RequireReal(a[0], "pingpong"), RequireReal(a[1], "pingpong")), 0),
                    "bernoulli" => Bool(RequireReal(a[1], "bernoulli") < Math.Clamp(RequireReal(a[0], "bernoulli"), 0, 1)),
                    "clamp" => new Complex(Clamp(RequireReal(a[0], "clamp"), RequireReal(a[1], "clamp"), RequireReal(a[2], "clamp")), 0),
                    "lerp" => a[0] + (a[1] - a[0]) * RequireReal(a[2], "lerp"),
                    "inverselerp" => new Complex(InverseLerp(RequireReal(a[0], "inverselerp"), RequireReal(a[1], "inverselerp"), RequireReal(a[2], "inverselerp")), 0),
                    "smoothstep" => new Complex(SmoothStep(RequireReal(a[0], "smoothstep"), RequireReal(a[1], "smoothstep"), RequireReal(a[2], "smoothstep")), 0),
                    "smootherstep" => new Complex(SmootherStep(RequireReal(a[0], "smootherstep"), RequireReal(a[1], "smootherstep"), RequireReal(a[2], "smootherstep")), 0),
                    "noiseseed" => new Complex(ValueNoise(RequireReal(a[0], "noiseseed"), RequireReal(a[1], "noiseseed"), RequireReal(a[2], "noiseseed")), 0),
                    "uniform" => new Complex(RequireReal(a[0], "uniform") + (RequireReal(a[1], "uniform") - RequireReal(a[0], "uniform")) * Math.Clamp(RequireReal(a[2], "uniform"), 0, 1), 0),
                    "normal" => new Complex(RequireReal(a[0], "normal") + RequireReal(a[1], "normal") * RequireReal(a[2], "normal"), 0),
                    "remap" => new Complex(Remap(a), 0),
                    "fbm" => new Complex(Fbm(a), 0),
                    "mandelbrot" => new Complex(MandelbrotMask(a), 0),
                    "mandelbrotiter" => new Complex(MandelbrotIteration(a, smooth: false), 0),
                    "mandelbrotsmooth" => new Complex(MandelbrotIteration(a, smooth: true), 0),
                    "julia" => new Complex(JuliaMask(a), 0),
                    "juliaiter" => new Complex(JuliaIteration(a, smooth: false), 0),
                    "juliasmooth" => new Complex(JuliaIteration(a, smooth: true), 0),
                    _ => throw new InvalidOperationException($"Unknown function '{_name}'")
                };
            }

            private Complex EvaluateDerivative(EvalContext context)
            {
                VariableNode variable = RequireVariableArgument(1);
                Complex at = context.Resolve(variable.Name);
                double h = _arguments.Count >= 3
                    ? Math.Abs(RequireReal(_arguments[2].Evaluate(context), "derivative step"))
                    : Math.Max(1e-6, Complex.Abs(at) * 1e-6);
                if (h <= 0 || !double.IsFinite(h)) h = 1e-6;

                bool had = context.Locals.TryGetValue(variable.Name, out Complex old);
                try
                {
                    // Complex derivatives sample along the real direction; non-holomorphic cases are directional.
                    context.Locals[variable.Name] = at - h;
                    Complex left = _arguments[0].Evaluate(context);
                    context.Locals[variable.Name] = at + h;
                    Complex right = _arguments[0].Evaluate(context);
                    return (right - left) / (2 * h);
                }
                finally { RestoreLocal(context, variable.Name, had, old); }
            }

            private Complex EvaluateIntegral(EvalContext context, bool primitive)
            {
                VariableNode variable = RequireVariableArgument(1);
                Complex start;
                Complex end;
                int stepArgument;
                if (primitive)
                {
                    start = _arguments[2].Evaluate(context);
                    end = context.Resolve(variable.Name);
                    stepArgument = 3;
                }
                else
                {
                    start = _arguments[2].Evaluate(context);
                    end = _arguments[3].Evaluate(context);
                    stepArgument = 4;
                }

                int steps = _arguments.Count > stepArgument
                    ? ClampSteps(RequireReal(_arguments[stepArgument].Evaluate(context), "integration steps"))
                    : 256;
                if ((steps & 1) == 1) steps++;

                Complex delta = end - start;
                if (Complex.Abs(delta) < 1e-15) return Complex.Zero;
                double h = 1.0 / steps;
                bool had = context.Locals.TryGetValue(variable.Name, out Complex old);
                try
                {
                    Complex sum = Complex.Zero;
                    for (int k = 0; k <= steps; k++)
                    {
                        double t = k * h;
                        context.Locals[variable.Name] = start + delta * t;
                        Complex fx = _arguments[0].Evaluate(context);
                        int weight = k == 0 || k == steps ? 1 : (k % 2 == 0 ? 2 : 4);
                        sum += fx * weight;
                    }
                    // Straight-line contour integral; real bounds reduce to the usual Simpson rule.
                    return sum * delta * (h / 3.0);
                }
                finally { RestoreLocal(context, variable.Name, had, old); }
            }

            private Complex EvaluateSeries(EvalContext context, bool product)
            {
                VariableNode variable = RequireVariableArgument(1);
                int start = ClampLoopIndex(RequireReal(_arguments[2].Evaluate(context), "series start"));
                int end = ClampLoopIndex(RequireReal(_arguments[3].Evaluate(context), "series end"));
                int direction = start <= end ? 1 : -1;
                long count = Math.Abs((long)end - start) + 1;
                if (count > MaxLoopIterations) throw new InvalidOperationException($"Series is limited to {MaxLoopIterations:N0} terms");

                bool had = context.Locals.TryGetValue(variable.Name, out Complex old);
                try
                {
                    Complex result = product ? Complex.One : Complex.Zero;
                    for (int k = start; ; k += direction)
                    {
                        context.Locals[variable.Name] = new Complex(k, 0);
                        Complex value = _arguments[0].Evaluate(context);
                        result = product ? result * value : result + value;
                        if (k == end) break;
                    }
                    return result;
                }
                finally { RestoreLocal(context, variable.Name, had, old); }
            }

            private Complex EvaluateIteration(EvalContext context, bool returnEscapeCount)
            {
                int iterations = Math.Clamp((int)Math.Floor(RequireReal(_arguments[2].Evaluate(context), "iteration count")), 0, 10_000);
                double radius = returnEscapeCount ? Math.Abs(RequireReal(_arguments[3].Evaluate(context), "escape radius")) : double.PositiveInfinity;
                if (returnEscapeCount && radius <= 0) throw new InvalidOperationException("Escape radius must be greater than zero");

                Complex value = _arguments[1].Evaluate(context);
                bool hadZ = context.Locals.TryGetValue("z", out Complex oldZ);
                bool hadN = context.Locals.TryGetValue("n", out Complex oldN);
                bool hadC = context.Locals.TryGetValue("c", out Complex oldC);
                try
                {
                    if (context.X.HasValue && context.Y.HasValue) context.Locals["c"] = new Complex(context.X.Value, context.Y.Value);
                    for (int n = 0; n < iterations; n++)
                    {
                        context.Locals["z"] = value;
                        context.Locals["n"] = new Complex(n, 0);
                        value = _arguments[0].Evaluate(context);
                        if (returnEscapeCount && Complex.Abs(value) > radius) return new Complex(n + 1, 0);
                    }
                    // Survivors need a value distinct from an escape on the final iteration.
                    return returnEscapeCount ? new Complex(iterations + 1, 0) : value;
                }
                finally
                {
                    RestoreLocal(context, "z", hadZ, oldZ);
                    RestoreLocal(context, "n", hadN, oldN);
                    RestoreLocal(context, "c", hadC, oldC);
                }
            }

            internal override void CollectVariables(HashSet<string> variables, HashSet<string> bound)
            {
                if (_name is "diff" or "derivative" or "integral" or "primitive" or "sum" or "product")
                {
                    VariableNode variable = RequireVariableArgument(1);
                    var inner = new HashSet<string>(bound, StringComparer.OrdinalIgnoreCase) { variable.Name };
                    _arguments[0].CollectVariables(variables, inner);
                    for (int i = 2; i < _arguments.Count; i++) _arguments[i].CollectVariables(variables, bound);
                    if (_name is "diff" or "derivative" or "primitive")
                    {
                        if (!bound.Contains(variable.Name)) variables.Add(variable.Name.ToLowerInvariant());
                    }
                    return;
                }

                if (_name is "iterate" or "iter" or "recurrence" or "recur" or "sequence" or "escape")
                {
                    var inner = new HashSet<string>(bound, StringComparer.OrdinalIgnoreCase) { "z", "n", "c" };
                    _arguments[0].CollectVariables(variables, inner);
                    for (int i = 1; i < _arguments.Count; i++) _arguments[i].CollectVariables(variables, bound);
                    return;
                }

                foreach (Node argument in _arguments) argument.CollectVariables(variables, bound);
            }

            internal override bool PotentiallyComplex
            {
                get
                {
                    if (_name is "re" or "real" or "im" or "imag" or "arg" or "phase" or "abs"
                        or "floor" or "ceil" or "round" or "sign" or "frac" or "saturate"
                        or "max" or "min" or "mod" or "step" or "atan2" or "noise" or "repeat" or "pingpong"
                        or "bernoulli" or "clamp" or "inverselerp" or "smoothstep" or "smootherstep" or "noiseseed"
                        or "uniform" or "normal" or "remap" or "fbm" or "escape"
                        or "mandelbrot" or "mandelbrotiter" or "mandelbrotsmooth" or "julia" or "juliaiter" or "juliasmooth")
                        return false;
                    if (_name is "complex" or "cis" or "polar" or "conj" or "csqrt" or "clog" or "cexp" or "csin" or "ccos" or "ctan")
                        return true;
                    return _arguments.Any(argument => argument.PotentiallyComplex);
                }
            }

            internal override HlslValue ToHlsl(HlslContext context)
            {
                if (_name is "diff" or "derivative" or "integral" or "primitive" or "sum" or "product" or "iterate" or "iter" or "recurrence" or "recur" or "sequence" or "escape")
                    throw new NotSupportedException($"{_name}(...) is evaluated by the CPU expression engine and cannot yet be emitted as a generic HLSL expression. Use the dedicated mandelbrot/julia helpers or write the loop in HLSL Lab.");

                HlslValue[] a = _arguments.Select(argument => argument.ToHlsl(context)).ToArray();
                string R(int index) => RequireHlslReal(a[index], _name);
                string C(int index) => Promote(a[index]);

                switch (_name)
                {
                    case "complex": return HlslValue.Complex($"float2({R(0)}, {R(1)})");
                    case "cis": return HlslValue.Complex($"float2(cos({R(0)}), sin({R(0)}))");
                    case "polar": return HlslValue.Complex($"float2(cos({R(1)}), sin({R(1)})) * ({R(0)})");
                    case "re": case "real": return HlslValue.Real($"({C(0)}).x");
                    case "im": case "imag": return HlslValue.Real($"({C(0)}).y");
                    case "arg": case "phase": return HlslValue.Real($"atan2(({C(0)}).y, ({C(0)}).x)");
                    case "conj": return HlslValue.Complex($"float2(({C(0)}).x, -({C(0)}).y)");
                    case "abs": return a[0].Kind == HlslKind.Complex ? HlslValue.Real($"length({a[0].Code})") : HlslValue.Real($"abs({a[0].Code})");
                    case "sqrt": case "csqrt": return a[0].Kind == HlslKind.Complex ? HlslValue.Complex($"gc_csqrt({C(0)})") : HlslValue.Real($"sqrt({R(0)})");
                    case "ln": case "clog": return a[0].Kind == HlslKind.Complex ? HlslValue.Complex($"gc_clog({C(0)})") : HlslValue.Real($"log({R(0)})");
                    case "log": return a[0].Kind == HlslKind.Complex ? HlslValue.Complex($"gc_clog({C(0)}) / 2.302585093") : HlslValue.Real($"log10({R(0)})");
                    case "exp": case "cexp": return a[0].Kind == HlslKind.Complex ? HlslValue.Complex($"gc_cexp({C(0)})") : HlslValue.Real($"exp({R(0)})");
                    case "sin": case "csin": return a[0].Kind == HlslKind.Complex ? HlslValue.Complex($"gc_csin({C(0)})") : HlslValue.Real($"sin({R(0)})");
                    case "cos": case "ccos": return a[0].Kind == HlslKind.Complex ? HlslValue.Complex($"gc_ccos({C(0)})") : HlslValue.Real($"cos({R(0)})");
                    case "tan": case "ctan": return a[0].Kind == HlslKind.Complex ? HlslValue.Complex($"gc_cdiv(gc_csin({C(0)}), gc_ccos({C(0)}))") : HlslValue.Real($"tan({R(0)})");
                    case "pow":
                        return a[0].Kind == HlslKind.Complex || a[1].Kind == HlslKind.Complex
                            ? HlslValue.Complex($"gc_cpow({C(0)}, {C(1)})")
                            : HlslValue.Real($"pow({R(0)}, {R(1)})");
                    case "lerp":
                        return a[0].Kind == HlslKind.Complex || a[1].Kind == HlslKind.Complex
                            ? HlslValue.Complex($"lerp({C(0)}, {C(1)}, {R(2)})")
                            : HlslValue.Real($"lerp({R(0)}, {R(1)}, {R(2)})");
                    case "if": case "select":
                    {
                        HlslKind kind = a[1].Kind == HlslKind.Complex || a[2].Kind == HlslKind.Complex ? HlslKind.Complex : HlslKind.Real;
                        string whenTrue = kind == HlslKind.Complex ? Promote(a[1]) : a[1].Code;
                        string whenFalse = kind == HlslKind.Complex ? Promote(a[2]) : a[2].Code;
                        return new HlslValue($"(({R(0)}) != 0.0 ? ({whenTrue}) : ({whenFalse}))", kind);
                    }
                    case "and": return HlslValue.Real($"((({R(0)}) != 0.0 && ({R(1)}) != 0.0) ? 1.0 : 0.0)");
                    case "or": return HlslValue.Real($"((({R(0)}) != 0.0 || ({R(1)}) != 0.0) ? 1.0 : 0.0)");
                    case "not": return HlslValue.Real($"(({R(0)}) == 0.0 ? 1.0 : 0.0)");
                    case "mod": return HlslValue.Real($"(({R(0)}) - ({R(1)}) * floor(({R(0)}) / ({R(1)})))");
                    case "repeat": return HlslValue.Real($"(({R(0)}) - floor(({R(0)}) / ({R(1)})) * ({R(1)}))");
                    case "pingpong": return HlslValue.Real($"gc_pingpong({R(0)}, {R(1)})");
                    case "inverselerp": return HlslValue.Real($"gc_inverseLerp({R(0)}, {R(1)}, {R(2)})");
                    case "smootherstep": return HlslValue.Real($"gc_smootherstep({R(0)}, {R(1)}, {R(2)})");
                    case "remap": return HlslValue.Real($"gc_remap({R(0)}, {R(1)}, {R(2)}, {R(3)}, {R(4)})");
                    case "noise": return HlslValue.Real($"gc_noise(float2({R(0)}, {R(1)}), 0.0)");
                    case "noiseseed": return HlslValue.Real($"gc_noise(float2({R(0)}, {R(1)}), {R(2)})");
                    case "fbm": return HlslValue.Real($"gc_fbm(float2({R(0)}, {R(1)}), {R(2)}, {R(3)}, {R(4)})");
                    case "bernoulli": return HlslValue.Real($"(({R(1)}) < saturate({R(0)}) ? 1.0 : 0.0)");
                    case "uniform": return HlslValue.Real($"lerp({R(0)}, {R(1)}, saturate({R(2)}))");
                    case "normal": return HlslValue.Real($"(({R(0)}) + ({R(1)}) * ({R(2)}))");
                    case "mandelbrot": return HlslValue.Real($"gc_mandelbrotMask({R(0)}, {R(1)}, {R(2)})");
                    case "mandelbrotiter": return HlslValue.Real($"gc_mandelbrotIter({R(0)}, {R(1)}, {R(2)}, 0.0)");
                    case "mandelbrotsmooth": return HlslValue.Real($"gc_mandelbrotIter({R(0)}, {R(1)}, {R(2)}, 1.0)");
                    case "julia": return HlslValue.Real($"gc_juliaMask({R(0)}, {R(1)}, {R(2)}, {R(3)}, {R(4)})");
                    case "juliaiter": return HlslValue.Real($"gc_juliaIter({R(0)}, {R(1)}, {R(2)}, {R(3)}, {R(4)}, 0.0)");
                    case "juliasmooth": return HlslValue.Real($"gc_juliaIter({R(0)}, {R(1)}, {R(2)}, {R(3)}, {R(4)}, 1.0)");
                    default:
                        if (a.Any(v => v.Kind == HlslKind.Complex))
                            throw new NotSupportedException($"{_name}(...) has no complex HLSL mapping yet");
                        return HlslValue.Real(_name switch
                        {
                            "ln" => $"log({R(0)})",
                            _ => _name + "(" + string.Join(", ", a.Select(v => v.Code)) + ")"
                        });
                }
            }

            private void ValidateArity()
            {
                int n = _arguments.Count;
                bool valid = _name switch
                {
                    "sin" or "cos" or "tan" or "asin" or "acos" or "atan" or "sinh" or "cosh" or "tanh"
                    or "sqrt" or "ln" or "log" or "exp" or "abs" or "floor" or "ceil" or "round" or "sign"
                    or "frac" or "saturate" or "not" or "cis" or "re" or "real" or "im" or "imag" or "arg"
                    or "phase" or "conj" or "csqrt" or "clog" or "cexp" or "csin" or "ccos" or "ctan" => n == 1,

                    "pow" or "max" or "min" or "mod" or "step" or "atan2" or "noise" or "repeat" or "pingpong"
                    or "and" or "or" or "bernoulli" or "complex" or "polar" => n == 2,

                    "clamp" or "lerp" or "inverselerp" or "smoothstep" or "smootherstep" or "noiseseed"
                    or "if" or "select" or "uniform" or "normal" or "iterate" or "iter" or "recurrence" or "recur" or "sequence" => n == 3,

                    "remap" or "fbm" => n == 5,
                    "diff" or "derivative" => n is 2 or 3,
                    "integral" => n is 4 or 5,
                    "primitive" => n is 3 or 4,
                    "sum" or "product" => n == 4,
                    "escape" => n == 4,
                    "mandelbrot" or "mandelbrotiter" or "mandelbrotsmooth" => n == 3,
                    "julia" or "juliaiter" or "juliasmooth" => n == 5,
                    _ => false
                };
                if (!valid) throw new FormatException($"Function {_name} received {n} argument(s); check its signature in the function reference");

                if (_name is "diff" or "derivative" or "integral" or "primitive" or "sum" or "product")
                    _ = RequireVariableArgument(1);
            }

            private VariableNode RequireVariableArgument(int index)
            {
                if (index >= _arguments.Count || _arguments[index] is not VariableNode variable)
                    throw new FormatException($"{_name} expects a variable name as argument {index + 1}");
                return variable;
            }
        }

        internal enum HlslKind { Real, Complex }
        internal readonly record struct HlslValue(string Code, HlslKind Kind)
        {
            internal static HlslValue Real(string code) => new(code, HlslKind.Real);
            internal static HlslValue Complex(string code) => new(code, HlslKind.Complex);
        }
        internal readonly record struct HlslContext(bool ComplexPlane);

        private static string Promote(HlslValue value) => value.Kind == HlslKind.Complex ? value.Code : $"float2({value.Code}, 0.0)";
        private static string RequireHlslReal(HlslValue value, string function)
        {
            if (value.Kind == HlslKind.Complex) throw new NotSupportedException($"{function}(...) expects a real value in HLSL export");
            return value.Code;
        }

        private static double RequireReal(Complex value, string operation)
        {
            if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary)) return double.NaN;
            if (Math.Abs(value.Imaginary) > ImaginaryTolerance * Math.Max(1, Math.Abs(value.Real)))
                throw new InvalidOperationException($"{operation} requires a real value; got {FormatComplex(value)}");
            return value.Real;
        }

        internal static double ToRealOrNaN(Complex value)
        {
            if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary)) return double.NaN;
            return Math.Abs(value.Imaginary) <= ImaginaryTolerance * Math.Max(1, Math.Abs(value.Real)) ? value.Real : double.NaN;
        }

        internal static string FormatComplex(Complex value)
        {
            if (Math.Abs(value.Imaginary) <= ImaginaryTolerance) return NumberFormatting.Format(value.Real);
            if (Math.Abs(value.Real) <= ImaginaryTolerance) return NumberFormatting.Format(value.Imaginary) + "i";
            string sign = value.Imaginary >= 0 ? " + " : " - ";
            return NumberFormatting.Format(value.Real) + sign + NumberFormatting.Format(Math.Abs(value.Imaginary)) + "i";
        }

        private static Complex Bool(bool value) => value ? Complex.One : Complex.Zero;
        private static bool IsTruthy(Complex value) => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary) && Complex.Abs(value) > 1e-12;

        private static bool TryGetVariable(IDictionary<string, double> variables, string name, out double value)
        {
            if (variables.TryGetValue(name, out value)) return true;
            foreach (var pair in variables)
            {
                if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = pair.Value; return true; }
            }
            value = default;
            return false;
        }

        private static void RestoreLocal(EvalContext context, string name, bool hadValue, Complex oldValue)
        {
            if (hadValue) context.Locals[name] = oldValue;
            else context.Locals.Remove(name);
        }

        private static int ClampSteps(double value) => Math.Clamp((int)Math.Round(value), 4, MaxQuadratureSteps);
        private static int ClampLoopIndex(double value) => Math.Clamp((int)Math.Round(value), -1_000_000_000, 1_000_000_000);
        private static double Frac(double value) => value - Math.Floor(value);
        private static double FloorMod(double a, double b) => Math.Abs(b) < 1e-300 ? double.NaN : a - b * Math.Floor(a / b);
        private static double Repeat(double value, double length) => Math.Abs(length) < 1e-300 ? double.NaN : value - Math.Floor(value / length) * length;
        private static double PingPong(double value, double length)
        {
            if (length <= 0) return double.NaN;
            double repeated = value - Math.Floor(value / (2 * length)) * (2 * length);
            return length - Math.Abs(repeated - length);
        }
        private static double Clamp(double value, double min, double max) => min > max ? double.NaN : Math.Clamp(value, min, max);
        private static double InverseLerp(double a, double b, double value) => Math.Abs(b - a) < 1e-15 ? 0 : (value - a) / (b - a);
        private static double SmoothStep(double a, double b, double value)
        {
            double t = Math.Clamp(InverseLerp(a, b, value), 0, 1);
            return t * t * (3 - 2 * t);
        }
        private static double SmootherStep(double a, double b, double value)
        {
            double t = Math.Clamp(InverseLerp(a, b, value), 0, 1);
            return t * t * t * (t * (t * 6 - 15) + 10);
        }
        private static double Remap(IReadOnlyList<Complex> a)
        {
            double inMin = RequireReal(a[0], "remap"), inMax = RequireReal(a[1], "remap");
            if (Math.Abs(inMax - inMin) < 1e-15) return double.NaN;
            double t = (RequireReal(a[4], "remap") - inMin) / (inMax - inMin);
            return RequireReal(a[2], "remap") + (RequireReal(a[3], "remap") - RequireReal(a[2], "remap")) * t;
        }
        private static double Fbm(IReadOnlyList<Complex> a)
        {
            double x = RequireReal(a[0], "fbm"), y = RequireReal(a[1], "fbm");
            int octaves = Math.Clamp((int)Math.Round(RequireReal(a[2], "fbm")), 1, 10);
            double persistence = Math.Clamp(RequireReal(a[3], "fbm"), 0, 1);
            double lacunarity = Math.Clamp(RequireReal(a[4], "fbm"), 1.01, 8);
            double amplitude = 1, frequency = 1, total = 0, weight = 0;
            for (int octave = 0; octave < octaves; octave++)
            {
                total += ValueNoise(x * frequency, y * frequency, octave * 101.0) * amplitude;
                weight += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            return weight > 0 ? total / weight : 0;
        }

        private static double MandelbrotMask(IReadOnlyList<Complex> a)
        {
            int max = Math.Clamp((int)Math.Floor(RequireReal(a[2], "mandelbrot")), 1, 10_000);
            int escaped = EscapeMandelbrot(RequireReal(a[0], "mandelbrot"), RequireReal(a[1], "mandelbrot"), max, out _);
            return escaped > max ? 1.0 : 0.0;
        }
        private static double MandelbrotIteration(IReadOnlyList<Complex> a, bool smooth)
        {
            int max = Math.Clamp((int)Math.Floor(RequireReal(a[2], "mandelbrot")), 1, 10_000);
            int escaped = EscapeMandelbrot(RequireReal(a[0], "mandelbrot"), RequireReal(a[1], "mandelbrot"), max, out Complex z);
            if (escaped > max) return 1.0;
            double normalization = max + 1.0; // Keep a distinct value for points that survived all iterations.
            if (!smooth) return escaped / normalization;
            double mag = Math.Max(Complex.Abs(z), 2.0000001);
            double mu = escaped + 1.0 - Math.Log(Math.Log(mag)) / Math.Log(2.0);
            return Math.Clamp(mu / normalization, 0, 0.999999);
        }
        private static int EscapeMandelbrot(double x, double y, int max, out Complex z)
        {
            Complex c = new(x, y);
            z = Complex.Zero;
            for (int i = 0; i < max; i++)
            {
                z = z * z + c;
                if (z.Real * z.Real + z.Imaginary * z.Imaginary > 4.0) return i + 1;
            }
            return max + 1;
        }

        private static double JuliaMask(IReadOnlyList<Complex> a)
        {
            int max = Math.Clamp((int)Math.Floor(RequireReal(a[4], "julia")), 1, 10_000);
            int escaped = EscapeJulia(RequireReal(a[0], "julia"), RequireReal(a[1], "julia"), RequireReal(a[2], "julia"), RequireReal(a[3], "julia"), max, out _);
            return escaped > max ? 1.0 : 0.0;
        }
        private static double JuliaIteration(IReadOnlyList<Complex> a, bool smooth)
        {
            int max = Math.Clamp((int)Math.Floor(RequireReal(a[4], "julia")), 1, 10_000);
            int escaped = EscapeJulia(RequireReal(a[0], "julia"), RequireReal(a[1], "julia"), RequireReal(a[2], "julia"), RequireReal(a[3], "julia"), max, out Complex z);
            if (escaped > max) return 1.0;
            double normalization = max + 1.0;
            if (!smooth) return escaped / normalization;
            double mag = Math.Max(Complex.Abs(z), 2.0000001);
            double mu = escaped + 1.0 - Math.Log(Math.Log(mag)) / Math.Log(2.0);
            return Math.Clamp(mu / normalization, 0, 0.999999);
        }
        private static int EscapeJulia(double x, double y, double cr, double ci, int max, out Complex z)
        {
            z = new Complex(x, y);
            Complex c = new(cr, ci);
            for (int i = 0; i < max; i++)
            {
                z = z * z + c;
                if (z.Real * z.Real + z.Imaginary * z.Imaginary > 4.0) return i + 1;
            }
            return max + 1;
        }

        private static double ValueNoise(double x, double y, double seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y), x1 = x0 + 1, y1 = y0 + 1;
            double tx = Fade(x - x0), ty = Fade(y - y0);
            double a = Hash01(x0, y0, seed), b = Hash01(x1, y0, seed), c = Hash01(x0, y1, seed), d = Hash01(x1, y1, seed);
            return ((a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty) * 2 - 1;
        }
        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static double Hash01(int x, int y, double seed)
        {
            double n = Math.Sin(x * 127.1 + y * 311.7 + seed * 74.7) * 43758.5453123;
            return n - Math.Floor(n);
        }

        private static string FormatHlslNumber(double value)
        {
            if (double.IsNaN(value)) return "NAN";
            if (double.IsPositiveInfinity(value)) return "INFINITY";
            if (double.IsNegativeInfinity(value)) return "(-INFINITY)";
            string text = value.ToString("R", CultureInfo.InvariantCulture);
            return text.Contains('.') || text.Contains('E') || text.Contains('e') ? text : text + ".0";
        }
    }
}
