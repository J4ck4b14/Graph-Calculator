using System;
using System.Collections.Generic;

namespace GraphCalculator
{
    public static class PlotExpressionParser
    {
        public static bool TryParseParametric(
            string source,
            out GraphExpressionKind kind,
            out IReadOnlyList<string> components,
            out string? error)
        {
            kind = GraphExpressionKind.Scalar;
            components = Array.Empty<string>();
            error = null;

            string text = source.Trim();

            if (TryAnyCall(text, ["curve", "param"], out string? curveBody, out _))
            {
                return Finish(GraphExpressionKind.Parametric2D, curveBody!, 2, "curve", out kind, out components, out error);
            }

            if (TryAnyCall(text, ["polar"], out string? polarBody, out _))
            {
                if (string.IsNullOrWhiteSpace(polarBody)) { error = "polar expects a radius expression in t"; kind = GraphExpressionKind.Parametric2D; return true; }
                kind = GraphExpressionKind.Parametric2D; components = [$"({polarBody})*cos(t)", $"({polarBody})*sin(t)"]; return true;
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

            if (TryAnyCall(text, ["texture", "mask", "heatmap"], out string? textureBody, out _))
            {
                return Finish(GraphExpressionKind.TextureField2D, textureBody!, 1, "texture", out kind, out components, out error);
            }

            return false;
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
            int depth = 0;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth--; continue; }
                if (c != '=' || depth != 0) continue;

                bool comparison = (i > 0 && source[i - 1] is '<' or '>' or '!' or '=')
                    || (i + 1 < source.Length && source[i + 1] == '=');
                if (comparison) continue;

                string left = source[..i].Trim();
                string right = source[(i + 1)..].Trim();
                if (left.Length == 0 || right.Length == 0) return source;
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
            int depth = 0;
            int start = 0;

            for (int i = 0; i < body.Length; i++)
            {
                switch (body[i])
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        depth--;
                        break;
                    case ',' when depth == 0:
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
