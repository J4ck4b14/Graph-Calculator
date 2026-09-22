using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GraphCalculator
{
    public static class PlotExpressionParser
    {
        private static readonly Regex IdentifierPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
        private static readonly Regex FunctionLeftPattern = new(
            @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<args>.*)\)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool TryParseParametric(
            string source,
            out GraphExpressionKind kind,
            out IReadOnlyList<string> components,
            out string? error)
        {
            kind = GraphExpressionKind.Scalar;
            components = Array.Empty<string>();
            error = null;

            string text = NormalizeAuthoringText(source.Trim());

            // Explicit wrappers override inference and keep older workspaces compatible.
            if (TryAnyCall(text, ["curve", "param"], out string? curveBody, out _))
            {
                return Finish(GraphExpressionKind.Parametric2D, curveBody!, 2, "curve", out kind, out components, out error);
            }

            if (TryAnyCall(text, ["polar"], out string? polarBody, out _))
            {
                // One argument is plotting shorthand; the two-argument form belongs to complex maths.
                List<string> polarParts = SplitTopLevel(polarBody!);
                if (polarParts.Count == 1)
                {
                    if (string.IsNullOrWhiteSpace(polarParts[0])) { error = "polar expects a radius expression in t"; kind = GraphExpressionKind.Parametric2D; return true; }
                    kind = GraphExpressionKind.Parametric2D; components = [$"({polarParts[0]})*cos(t)", $"({polarParts[0]})*sin(t)"]; return true;
                }
            }

            if (TryAnyCall(text, ["cyl", "cylindrical"], out string? cylindricalBody, out _))
            {
                List<string> parts = SplitTopLevel(cylindricalBody!);
                kind = GraphExpressionKind.Parametric3D;
                if (parts.Count != 2) { error = "cylindrical expects radius(t), z(t)"; return true; }
                components = [$"({parts[0]})*cos(t)", $"({parts[0]})*sin(t)", parts[1]]; return true;
            }

            if (TryAnyCall(text, ["sphere", "spherical"], out string? sphericalBody, out _))
            {
                string radius = string.IsNullOrWhiteSpace(sphericalBody) ? "1" : sphericalBody!;
                kind = GraphExpressionKind.ParametricSurface3D;
                components = [$"({radius})*sin(u)*cos(v)", $"({radius})*sin(u)*sin(v)", $"({radius})*cos(u)"]; return true;
            }

            if (TryAnyCall(text, ["curve3", "param3"], out string? curve3Body, out _))
            {
                return Finish(GraphExpressionKind.Parametric3D, curve3Body!, 3, "curve3", out kind, out components, out error);
            }

            if (TryAnyCall(text, ["surface", "surface3", "surf", "paramsurface"], out string? surfaceBody, out _))
            {
                return Finish(GraphExpressionKind.ParametricSurface3D, surfaceBody!, 3, "surface", out kind, out components, out error);
            }

            if (TryAnyCall(text, ["field", "vector", "vectorfield"], out string? fieldBody, out _))
            {
                return Finish(GraphExpressionKind.VectorField2D, fieldBody!, 2, "field", out kind, out components, out error);
            }
            if (TryAnyCall(text, ["implicit", "contour", "implicit2"], out string? implicitBody, out _))
            {
                return Finish(GraphExpressionKind.Implicit2D, implicitBody!, 1, "implicit", out kind, out components, out error);
            }

            if (TryAnyCall(text, ["implicit3", "sdf", "isosurface"], out string? implicit3Body, out _))
            {
                return Finish(GraphExpressionKind.Implicit3D, implicit3Body!, 1, "implicit3", out kind, out components, out error);
            }

            if (TryAnyCall(text, ["complexmap", "domain", "domaincolor", "complexfield"], out string? complexBody, out _))
            {
                return Finish(GraphExpressionKind.ComplexField2D, complexBody!, 1, "complexmap", out kind, out components, out error);
            }

            if (TryAnyCall(text, ["texture", "mask", "heatmap"], out string? textureBody, out _))
            {
                return Finish(GraphExpressionKind.TextureField2D, textureBody!, 1, "texture", out kind, out components, out error);
            }

            // Parse ODE blocks before general equations; state names are not restricted to x/y/z.
            if (TryParseDifferentialSystem(text, out kind, out components, out error))
            {
                return true;
            }

            // Natural recurrence notation is lowered to the generic iterative evaluator.
            if (TryParseNaturalRecurrence(text, out kind, out components, out error))
            {
                return true;
            }

            // Vector-function arguments are mapped onto the coordinate names used by the samplers.
            if (TryParseVectorFunctionDefinition(text, out kind, out components, out error))
            {
                return true;
            }

            // A named f(z) definition defaults to complex-plane rendering.
            if (TryParseComplexFunctionDefinition(text, out kind, out components, out error))
            {
                return true;
            }

            // Scalar function arguments are normalized to the evaluator coordinates.
            if (TryParseScalarFunctionDefinition(text, out kind, out components, out error))
            {
                return true;
            }

            // General equations become implicit plots; explicit y= and z= stay on the faster scalar path.
            if (TryParseNaturalEquation(text, out kind, out components, out error))
            {
                return true;
            }

            return false;
        }

        private static bool TryParseDifferentialSystem(
            string text,
            out GraphExpressionKind kind,
            out IReadOnlyList<string> components,
            out string? error)
        {
            kind = GraphExpressionKind.Scalar;
            components = Array.Empty<string>();
            error = null;

            List<string> statements = SplitTopLevelStatements(text);
            var derivatives = new List<(string Name, string Body)>();

            foreach (string statement in statements)
            {
                Match derivative = Regex.Match(statement,
                    @"(?is)^\s*d\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*/\s*d\s*t\s*=\s*(?<body>.+?)\s*$",
                    RegexOptions.CultureInvariant);
                if (!derivative.Success)
                {
                    derivative = Regex.Match(statement,
                        @"(?is)^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*'\s*=\s*(?<body>.+?)\s*$",
                        RegexOptions.CultureInvariant);
                }

                if (!derivative.Success) continue;
                string name = derivative.Groups["name"].Value.Trim();
                if (name.Equals("t", StringComparison.OrdinalIgnoreCase) || name.Equals("time", StringComparison.OrdinalIgnoreCase))
                {
                    error = "t/time is the independent integration variable and cannot also be a state variable";
                    return true;
                }
                if (derivatives.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    error = $"The derivative for '{name}' is defined more than once";
                    return true;
                }
                derivatives.Add((name, derivative.Groups["body"].Value.Trim()));
            }

            if (derivatives.Count == 0) return false;
            if (derivatives.Count > 3)
            {
                error = "Plotted dynamical systems currently support up to 3 state variables. Reduce a higher-dimensional system to the coordinates you want to visualise.";
                return true;
            }

            var initialValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var localDefinitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? initialTime = null;
            foreach (string statement in statements)
            {
                Match initial = Regex.Match(statement,
                    @"(?is)^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(\s*(?<time>[^()]+?)\s*\)\s*=\s*(?<value>.+?)\s*$",
                    RegexOptions.CultureInvariant);
                if (initial.Success)
                {
                    string name = initial.Groups["name"].Value.Trim();
                    if (!derivatives.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    string time = initial.Groups["time"].Value.Trim();
                    string value = initial.Groups["value"].Value.Trim();
                    if (initialTime == null) initialTime = time;
                    else if (!NormalizeComparableExpression(initialTime).Equals(NormalizeComparableExpression(time), StringComparison.OrdinalIgnoreCase))
                    {
                        error = "All initial conditions in one dynamical system must use the same initial time";
                        return true;
                    }
                    initialValues[name] = value;
                    continue;
                }

                Match local = Regex.Match(statement,
                    @"(?is)^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.+?)\s*$",
                    RegexOptions.CultureInvariant);
                if (!local.Success) continue;
                string localName = local.Groups["name"].Value.Trim();
                if (derivatives.Any(item => item.Name.Equals(localName, StringComparison.OrdinalIgnoreCase)))
                {
                    error = $"'{localName}' is a state variable. Give its starting value as {localName}(0)=... instead of assigning it directly.";
                    return true;
                }
                if (localName.Equals("t", StringComparison.OrdinalIgnoreCase) || localName.Equals("time", StringComparison.OrdinalIgnoreCase))
                {
                    error = "t/time is the integration variable and cannot be assigned inside a dynamical-system block";
                    return true;
                }
                localDefinitions[localName] = local.Groups["value"].Value.Trim();
            }

            string[] missing = derivatives
                .Select(item => item.Name)
                .Where(name => !initialValues.ContainsKey(name))
                .ToArray();
            if (missing.Length > 0)
            {
                error = "Missing initial condition for " + string.Join(", ", missing) + ". Example: " + missing[0] + "(0)=1";
                return true;
            }

            initialTime ??= "0";
            string[] internalNames = ["x", "y", "z"];
            (string from, string to)[] replacements = derivatives
                .Select((item, index) => (item.Name, internalNames[index]))
                .ToArray();

            string ExpandLocals(string expression)
            {
                string expanded = expression;
                // Resolve short constant chains without adding a separate statement VM.
                for (int pass = 0; pass < Math.Max(1, localDefinitions.Count + 1); pass++)
                {
                    string before = expanded;
                    foreach ((string name, string value) in localDefinitions)
                    {
                        string resolvedValue = value;
                        foreach ((string otherName, string otherValue) in localDefinitions)
                        {
                            if (!otherName.Equals(name, StringComparison.OrdinalIgnoreCase))
                                resolvedValue = ReplaceIdentifier(resolvedValue, otherName, $"({otherValue})");
                        }
                        expanded = ReplaceIdentifier(expanded, name, $"({resolvedValue})");
                    }
                    if (expanded == before) break;
                }
                return expanded;
            }

            var compiledParts = new List<string>();
            foreach ((string _, string body) in derivatives)
                compiledParts.Add(ReplaceIdentifiers(ExpandLocals(body), replacements));
            foreach ((string name, string _) in derivatives)
                compiledParts.Add(ReplaceIdentifiers(ExpandLocals(initialValues[name]), replacements));
            compiledParts.Add(ReplaceIdentifiers(ExpandLocals(initialTime), replacements));

            kind = derivatives.Count switch
            {
                1 => GraphExpressionKind.DifferentialEquation1D,
                2 => GraphExpressionKind.DynamicalSystem2D,
                _ => GraphExpressionKind.DynamicalSystem3D
            };
            components = compiledParts;
            return true;
        }

        private static string NormalizeComparableExpression(string text)
            => Regex.Replace(text, @"\s+", string.Empty);

        private static bool TryParseNaturalRecurrence(
            string text,
            out GraphExpressionKind kind,
            out IReadOnlyList<string> components,
            out string? error)
        {
            kind = GraphExpressionKind.Scalar;
            components = Array.Empty<string>();
            error = null;

            if (!Regex.IsMatch(text,
                    @"(?i)\b[A-Za-z_][A-Za-z0-9_]*\s*(?:\[\s*n\s*\+\s*1\s*\]|_\{\s*n\s*\+\s*1\s*\})\s*="))
            {
                return false;
            }

            List<string> statements = SplitTopLevelStatements(text);
            string? symbol = null;
            string? body = null;
            string? seed = null;
            string? count = null;
            string? radius = null;
            var localDefinitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string statement in statements)
            {
                Match relation = Regex.Match(statement,
                    @"(?is)^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\[\s*n\s*\+\s*1\s*\]|_\{\s*n\s*\+\s*1\s*\})\s*=\s*(?<body>.+?)\s*$");
                if (relation.Success)
                {
                    symbol = relation.Groups["name"].Value;
                    body = relation.Groups["body"].Value.Trim();
                    continue;
                }

                Match assignment = Regex.Match(statement,
                    @"(?is)^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.+?)\s*$");
                if (assignment.Success)
                {
                    string name = assignment.Groups["name"].Value;
                    string value = assignment.Groups["value"].Value.Trim();
                    if (name.Equals("n", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("N", StringComparison.Ordinal)
                        || name.Equals("iterations", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("steps", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("count", StringComparison.OrdinalIgnoreCase))
                    {
                        count = value;
                    }
                    else if (name.Equals("radius", StringComparison.OrdinalIgnoreCase)
                             || name.Equals("escape", StringComparison.OrdinalIgnoreCase))
                    {
                        radius = value;
                    }
                    else
                    {
                        localDefinitions[name] = value;
                    }
                }
            }

            if (symbol == null || body == null) return false;

            foreach (string statement in statements)
            {
                string escaped = Regex.Escape(symbol);
                Match initial = Regex.Match(statement,
                    $@"(?is)^\s*{escaped}\s*(?:\[\s*0\s*\]|_\{{\s*0\s*\}}|_0)\s*=\s*(?<seed>.+?)\s*$");
                if (initial.Success)
                {
                    seed = initial.Groups["seed"].Value.Trim();
                    break;
                }
            }

            if (seed == null || count == null)
            {
                error = "A recurrence needs an initial value and an iteration count. Example: z[n+1]=z[n]^2+c; z[0]=0; N=80";
                return true;
            }

            string recurrenceBody = ReplaceSequenceReference(body, symbol);
            foreach ((string name, string value) in localDefinitions)
            {
                // Leave the sequence symbol alone; its seed is handled separately.
                if (name.Equals(symbol, StringComparison.OrdinalIgnoreCase)) continue;
                recurrenceBody = ReplaceIdentifier(recurrenceBody, name, $"({value})");
                seed = ReplaceIdentifier(seed, name, $"({value})");
            }

            bool complexPlane = symbol.Equals("z", StringComparison.OrdinalIgnoreCase)
                || ContainsIdentifier(recurrenceBody, "c")
                || ContainsIdentifier(recurrenceBody, "i")
                || ContainsIdentifier(seed, "i");

            if (radius != null)
            {
                // Normalize escape count for direct texture use.
                kind = GraphExpressionKind.TextureField2D;
                components = [$"escape(({recurrenceBody}),({seed}),({count}),({radius}))/max(1,(({count})+1))"];
                return true;
            }

            if (complexPlane)
            {
                kind = GraphExpressionKind.ComplexField2D;
                components = [$"iterate(({recurrenceBody}),({seed}),({count}))"];
                return true;
            }

            kind = GraphExpressionKind.Scalar;
            components = [$"iterate(({recurrenceBody}),({seed}),({count}))"];
            return true;
        }

        private static string ReplaceSequenceReference(string source, string symbol)
        {
            string escaped = Regex.Escape(symbol);
            string result = Regex.Replace(source,
                $@"(?i)(?<![A-Za-z0-9_]){escaped}\s*(?:\[\s*n\s*\]|_\{{\s*n\s*\}}|_n)(?![A-Za-z0-9_])",
                "z");
            return result;
        }

        private static List<string> SplitTopLevelStatements(string text)
        {
            var result = new List<string>();
            int paren = 0, bracket = 0, brace = 0;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '(': paren++; break;
                    case ')': paren--; break;
                    case '[': bracket++; break;
                    case ']': bracket--; break;
                    case '{': brace++; break;
                    case '}': brace--; break;
                }

                bool topLevel = paren == 0 && bracket == 0 && brace == 0;
                bool separator = c == ';' || c == '\n' || c == '\r';
                if (!topLevel || !separator) continue;

                if (i > start) result.Add(text[start..i].Trim());
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                start = i + 1;
            }
            if (start < text.Length) result.Add(text[start..].Trim());
            return result.Where(part => part.Length > 0).ToList();
        }

        private static bool TryParseVectorFunctionDefinition(
            string text,
            out GraphExpressionKind kind,
            out IReadOnlyList<string> components,
            out string? error)
        {
            kind = GraphExpressionKind.Scalar;
            components = Array.Empty<string>();
            error = null;

            if (!TrySplitTopLevelEquation(text, out string left, out string right)) return false;
            Match function = FunctionLeftPattern.Match(left);
            if (!function.Success) return false;

            List<string> args = SplitTopLevel(function.Groups["args"].Value);
            if (args.Count is < 1 or > 2 || args.Any(arg => !IdentifierPattern.IsMatch(arg.Trim()))) return false;
            if (!TryParseVectorLiteral(right, out List<string> vector)) return false;

            string[] cleanArgs = args.Select(arg => arg.Trim()).ToArray();
            if (vector.Count == 2 && cleanArgs.Length == 1)
            {
                kind = GraphExpressionKind.Parametric2D;
                components = vector.Select(component => ReplaceIdentifier(component, cleanArgs[0], "t")).ToArray();
                return true;
            }

            if (vector.Count == 3 && cleanArgs.Length == 1)
            {
                kind = GraphExpressionKind.Parametric3D;
                components = vector.Select(component => ReplaceIdentifier(component, cleanArgs[0], "t")).ToArray();
                return true;
            }

            if (vector.Count == 3 && cleanArgs.Length == 2)
            {
                kind = GraphExpressionKind.ParametricSurface3D;
                components = vector.Select(component => ReplaceIdentifiers(component,
                    (cleanArgs[0], "u"), (cleanArgs[1], "v"))).ToArray();
                return true;
            }

            if (vector.Count == 2 && cleanArgs.Length == 2)
            {
                kind = GraphExpressionKind.VectorField2D;
                components = vector.Select(component => ReplaceIdentifiers(component,
                    (cleanArgs[0], "x"), (cleanArgs[1], "y"))).ToArray();
                return true;
            }

            error = vector.Count switch
            {
                not (2 or 3) => "A plotted vector function must have 2 or 3 components",
                _ => "That vector-function signature cannot be mapped to a 2D/3D plot"
            };
            return true;
        }

        private static bool TryParseComplexFunctionDefinition(
            string text,
            out GraphExpressionKind kind,
            out IReadOnlyList<string> components,
            out string? error)
        {
            kind = GraphExpressionKind.Scalar;
            components = Array.Empty<string>();
            error = null;

            if (!TrySplitTopLevelEquation(text, out string left, out string right)) return false;
            Match function = FunctionLeftPattern.Match(left);
            if (!function.Success) return false;

            List<string> args = SplitTopLevel(function.Groups["args"].Value);
            if (args.Count != 1 || !args[0].Trim().Equals("z", StringComparison.OrdinalIgnoreCase)) return false;
            if (TryParseVectorLiteral(right, out _)) return false;

            kind = GraphExpressionKind.ComplexField2D;
            components = [right.Trim()];
            return true;
        }

        private static bool TryParseScalarFunctionDefinition(
            string text,
            out GraphExpressionKind kind,
            out IReadOnlyList<string> components,
            out string? error)
        {
            kind = GraphExpressionKind.Scalar;
            components = Array.Empty<string>();
            error = null;

            if (!TrySplitTopLevelEquation(text, out string left, out string right)) return false;
            Match function = FunctionLeftPattern.Match(left);
            if (!function.Success) return false;
            if (TryParseVectorLiteral(right, out _)) return false;

            List<string> args = SplitTopLevel(function.Groups["args"].Value);
            if (args.Count is < 1 or > 2 || args.Any(arg => !IdentifierPattern.IsMatch(arg.Trim()))) return false;
            string[] cleanArgs = args.Select(arg => arg.Trim()).ToArray();

            if (cleanArgs.Length == 1)
            {
                kind = GraphExpressionKind.Scalar;
                components = [ReplaceIdentifier(right, cleanArgs[0], "x")];
                return true;
            }

            kind = GraphExpressionKind.Scalar;
            components = [ReplaceIdentifiers(right, (cleanArgs[0], "x"), (cleanArgs[1], "y"))];
            return true;
        }

        private static bool TryParseNaturalEquation(
            string text,
            out GraphExpressionKind kind,
            out IReadOnlyList<string> components,
            out string? error)
        {
            kind = GraphExpressionKind.Scalar;
            components = Array.Empty<string>();
            error = null;

            if (!TrySplitTopLevelEquation(text, out string left, out string right)) return false;

            // Remaining function definitions belong to the scalar compiler.
            if (FunctionLeftPattern.IsMatch(left)) return false;

            string compactLeft = Regex.Replace(left, @"\s+", string.Empty).ToLowerInvariant();
            if (compactLeft == "y" && !ContainsIdentifier(right, "y") && !ContainsIdentifier(right, "z")) return false;
            if (compactLeft == "z" && !ContainsIdentifier(right, "z")) return false;

            bool usesX = ContainsIdentifier(left, "x") || ContainsIdentifier(right, "x");
            bool usesY = ContainsIdentifier(left, "y") || ContainsIdentifier(right, "y");
            bool usesZ = ContainsIdentifier(left, "z") || ContainsIdentifier(right, "z");
            if (!usesX && !usesY && !usesZ) return false;

            kind = usesZ ? GraphExpressionKind.Implicit3D : GraphExpressionKind.Implicit2D;
            components = [$"({left})-({right})"];
            return true;
        }

        private static bool TryParseVectorLiteral(string source, out List<string> components)
        {
            components = [];
            string text = source.Trim();
            if (!TryStripOneOuterPair(text, '(', ')', out string inner)
                && !TryStripOneOuterPair(text, '<', '>', out inner))
            {
                return false;
            }

            List<string> parts = SplitTopLevel(inner);
            if (parts.Count < 2 || parts.Any(string.IsNullOrWhiteSpace)) return false;
            components = parts;
            return true;
        }

        private static bool TryStripOneOuterPair(string text, char open, char close, out string inner)
        {
            inner = string.Empty;
            if (text.Length < 2 || text[0] != open || text[^1] != close) return false;

            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == open) depth++;
                else if (text[i] == close) depth--;
                if (depth == 0 && i != text.Length - 1) return false;
                if (depth < 0) return false;
            }

            if (depth != 0) return false;
            inner = text[1..^1].Trim();
            return true;
        }

        private static string ReplaceIdentifiers(string source, params (string from, string to)[] replacements)
        {
            // Placeholders prevent one argument rename from changing another.
            string result = source;
            for (int i = 0; i < replacements.Length; i++)
            {
                result = ReplaceIdentifier(result, replacements[i].from, $"__gc_arg_{i}__");
            }
            for (int i = 0; i < replacements.Length; i++)
            {
                result = ReplaceIdentifier(result, $"__gc_arg_{i}__", replacements[i].to);
            }
            return result;
        }

        private static string ReplaceIdentifier(string source, string identifier, string replacement)
        {
            return Regex.Replace(source,
                $@"(?<![A-Za-z0-9_]){Regex.Escape(identifier)}(?![A-Za-z0-9_])",
                replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool ContainsIdentifier(string source, string identifier)
        {
            return Regex.IsMatch(source,
                $@"(?<![A-Za-z0-9_]){Regex.Escape(identifier)}(?![A-Za-z0-9_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool TrySplitTopLevelEquation(string text, out string left, out string right)
        {
            left = string.Empty;
            right = string.Empty;
            int paren = 0, bracket = 0, brace = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '(': paren++; continue;
                    case ')': paren--; continue;
                    case '[': bracket++; continue;
                    case ']': bracket--; continue;
                    case '{': brace++; continue;
                    case '}': brace--; continue;
                }

                if (c != '=' || paren != 0 || bracket != 0 || brace != 0) continue;
                bool comparison = (i > 0 && text[i - 1] is '<' or '>' or '!' or '=')
                    || (i + 1 < text.Length && text[i + 1] == '=');
                if (comparison) continue;

                left = text[..i].Trim();
                right = text[(i + 1)..].Trim();
                return left.Length > 0 && right.Length > 0;
            }
            return false;
        }

        private static string NormalizeAuthoringText(string text)
        {
            return text
                .Replace('＝', '=')
                .Replace('−', '-')
                .Replace('×', '*')
                .Replace('÷', '/');
        }

        private static bool Finish(
            GraphExpressionKind targetKind,
            string body,
            int expected,
            string displayName,
            out GraphExpressionKind kind,
            out IReadOnlyList<string> components,
            out string? error)
        {
            kind = targetKind;
            components = Array.Empty<string>();
            error = null;

            List<string> parts = SplitTopLevel(body);
            if (parts.Count != expected)
            {
                error = $"{displayName} expects {expected} component expressions";
                return true;
            }

            if (parts.Exists(string.IsNullOrWhiteSpace))
            {
                error = $"{displayName} components cannot be empty";
                return true;
            }

            if (targetKind is GraphExpressionKind.Implicit2D or GraphExpressionKind.Implicit3D)
            {
                parts[0] = NormalizeImplicitEquation(parts[0]);
            }

            components = parts;
            return true;
        }

        private static string NormalizeImplicitEquation(string source)
        {
            if (TrySplitTopLevelEquation(source, out string left, out string right))
            {
                return $"({left})-({right})";
            }
            return source;
        }

        private static bool TryAnyCall(
            string text,
            IReadOnlyList<string> names,
            out string? body,
            out string? matchedName)
        {
            foreach (string name in names)
            {
                if (TryGetCall(text, name, out body))
                {
                    matchedName = name;
                    return true;
                }
            }

            body = null;
            matchedName = null;
            return false;
        }

        private static bool TryGetCall(string text, string name, out string? body)
        {
            body = null;
            if (!text.StartsWith(name + "(", StringComparison.OrdinalIgnoreCase) || !text.EndsWith(')'))
            {
                return false;
            }

            int open = name.Length;
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;

                if (depth == 0 && i != text.Length - 1)
                {
                    return false;
                }

                if (depth < 0) return false;
            }

            if (depth != 0) return false;
            body = text[(open + 1)..^1];
            return true;
        }

        private static List<string> SplitTopLevel(string body)
        {
            var parts = new List<string>();
            int paren = 0, bracket = 0, brace = 0;
            int start = 0;

            for (int i = 0; i < body.Length; i++)
            {
                switch (body[i])
                {
                    case '(':
                        paren++;
                        break;
                    case ')':
                        paren--;
                        break;
                    case '[':
                        bracket++;
                        break;
                    case ']':
                        bracket--;
                        break;
                    case '{':
                        brace++;
                        break;
                    case '}':
                        brace--;
                        break;
                    case ',' when paren == 0 && bracket == 0 && brace == 0:
                        parts.Add(body[start..i].Trim());
                        start = i + 1;
                        break;
                }
            }

            parts.Add(body[start..].Trim());
            return parts;
        }
    }
}
