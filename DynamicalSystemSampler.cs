using System;
using System.Collections.Generic;
using System.Threading;

namespace GraphCalculator
{
    // RK4 sampler for first-order ODEs; parser state names are normalized to x/y/z.
    public static class DynamicalSystemSampler
    {
        internal sealed record Sample(double Time, double[] Values, double[] Derivatives);
        private const double MaxStep = 0.02;
        private const double BlowUpLimit = 1e12;

        public static List<GraphPoint> Sample1D(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            IDictionary<string, double> variables,
            double? minT,
            double? maxT,
            int targetPoints,
            CancellationToken cancellationToken)
        {
            if (components.Count != 3) return [];
            if (!TryInitialState(components, 1, variables, out double[] initial, out double t0)) return [];
            (double start, double end) = ResolveRange(minT, maxT, t0);
            List<StateSample> samples = SampleStates(components, 1, variables, initial, t0, start, end, targetPoints, cancellationToken);
            var result = new List<GraphPoint>(samples.Count);
            foreach (StateSample sample in samples)
                result.Add(IsFinite(sample.Values) ? new GraphPoint(sample.Time, sample.Values[0]) : new GraphPoint(double.NaN, double.NaN));
            return result;
        }

        public static List<GraphPoint> Sample2D(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            IDictionary<string, double> variables,
            double? minT,
            double? maxT,
            int targetPoints,
            CancellationToken cancellationToken)
        {
            if (components.Count != 5) return [];
            if (!TryInitialState(components, 2, variables, out double[] initial, out double t0)) return [];
            (double start, double end) = ResolveRange(minT, maxT, t0);
            List<StateSample> samples = SampleStates(components, 2, variables, initial, t0, start, end, targetPoints, cancellationToken);
            var result = new List<GraphPoint>(samples.Count);
            foreach (StateSample sample in samples)
                result.Add(IsFinite(sample.Values) ? new GraphPoint(sample.Values[0], sample.Values[1]) : new GraphPoint(double.NaN, double.NaN));
            return result;
        }

        public static List<GraphPoint3D> Sample3D(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            IDictionary<string, double> variables,
            double? minT,
            double? maxT,
            int targetPoints,
            CancellationToken cancellationToken)
        {
            if (components.Count != 7) return [];
            if (!TryInitialState(components, 3, variables, out double[] initial, out double t0)) return [];
            (double start, double end) = ResolveRange(minT, maxT, t0);
            List<StateSample> samples = SampleStates(components, 3, variables, initial, t0, start, end, targetPoints, cancellationToken);
            var result = new List<GraphPoint3D>(samples.Count);
            foreach (StateSample sample in samples)
                result.Add(IsFinite(sample.Values)
                    ? new GraphPoint3D(sample.Values[0], sample.Values[1], sample.Values[2])
                    : new GraphPoint3D(double.NaN, double.NaN, double.NaN));
            return result;
        }

        internal static List<Sample> SampleStateData(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            int dimensions,
            IDictionary<string, double> variables,
            double? minT,
            double? maxT,
            int targetPoints,
            CancellationToken cancellationToken)
        {
            if (dimensions < 1 || dimensions > 3 || components.Count != dimensions * 2 + 1) return [];
            if (!TryInitialState(components, dimensions, variables, out double[] initial, out double t0)) return [];
            (double start, double end) = ResolveRange(minT, maxT, t0);
            List<StateSample> states = SampleStates(components, dimensions, variables, initial, t0, start, end, targetPoints, cancellationToken);
            var result = new List<Sample>(states.Count);
            foreach (StateSample state in states)
            {
                double[] values = state.Values ?? Invalid(dimensions);
                double[] derivatives = IsFinite(values)
                    ? Derivative(components, dimensions, variables, values, state.Time)
                    : Invalid(dimensions);
                result.Add(new Sample(state.Time, (double[])values.Clone(), derivatives));
            }
            return result;
        }

        private static List<StateSample> SampleStates(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            int dimensions,
            IDictionary<string, double> variables,
            double[] initial,
            double t0,
            double start,
            double end,
            int targetPoints,
            CancellationToken cancellationToken)
        {
            int count = Math.Clamp(targetPoints, 120, dimensions == 3 ? 5000 : 4000);
            var times = new double[count];
            for (int i = 0; i < count; i++) times[i] = start + (end - start) * i / (count - 1.0);

            var output = new StateSample[count];
            int pivot = 0;
            double pivotDistance = double.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                double distance = Math.Abs(times[i] - t0);
                if (distance < pivotDistance) { pivotDistance = distance; pivot = i; }
            }

            // Start near t0, then integrate outward in both directions.
            double[] pivotState = (double[])initial.Clone();
            if (Math.Abs(times[pivot] - t0) > 1e-14)
                pivotState = IntegrateTo(components, dimensions, variables, initial, t0, times[pivot], cancellationToken);
            output[pivot] = new StateSample(times[pivot], pivotState);

            double[] forward = (double[])pivotState.Clone();
            double forwardTime = times[pivot];
            for (int i = pivot + 1; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                forward = IntegrateTo(components, dimensions, variables, forward, forwardTime, times[i], cancellationToken);
                forwardTime = times[i];
                output[i] = new StateSample(forwardTime, forward);
                if (!IsFinite(forward)) FillInvalid(output, i + 1, count, times, dimensions);
                if (!IsFinite(forward)) break;
            }

            double[] backward = (double[])pivotState.Clone();
            double backwardTime = times[pivot];
            for (int i = pivot - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                backward = IntegrateTo(components, dimensions, variables, backward, backwardTime, times[i], cancellationToken);
                backwardTime = times[i];
                output[i] = new StateSample(backwardTime, backward);
                if (!IsFinite(backward)) FillInvalid(output, 0, i + 1, times, dimensions);
                if (!IsFinite(backward)) break;
            }

            return new List<StateSample>(output);
        }

        private static void FillInvalid(StateSample[] output, int start, int end, double[] times, int dimensions)
        {
            for (int i = start; i < end; i++)
            {
                var values = new double[dimensions];
                Array.Fill(values, double.NaN);
                output[i] = new StateSample(times[i], values);
            }
        }

        private static double[] IntegrateTo(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            int dimensions,
            IDictionary<string, double> baseVariables,
            double[] state,
            double from,
            double to,
            CancellationToken cancellationToken)
        {
            double distance = to - from;
            if (Math.Abs(distance) < 1e-15) return (double[])state.Clone();

            int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(distance) / MaxStep));
            double h = distance / steps;
            double t = from;
            double[] current = (double[])state.Clone();

            for (int step = 0; step < steps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double[] k1 = Derivative(components, dimensions, baseVariables, current, t);
                if (!IsFinite(k1)) return Invalid(dimensions);

                double[] temp = Combine(current, k1, h * 0.5);
                double[] k2 = Derivative(components, dimensions, baseVariables, temp, t + h * 0.5);
                if (!IsFinite(k2)) return Invalid(dimensions);

                temp = Combine(current, k2, h * 0.5);
                double[] k3 = Derivative(components, dimensions, baseVariables, temp, t + h * 0.5);
                if (!IsFinite(k3)) return Invalid(dimensions);

                temp = Combine(current, k3, h);
                double[] k4 = Derivative(components, dimensions, baseVariables, temp, t + h);
                if (!IsFinite(k4)) return Invalid(dimensions);

                for (int i = 0; i < dimensions; i++)
                    current[i] += h * (k1[i] + 2.0 * k2[i] + 2.0 * k3[i] + k4[i]) / 6.0;
                if (!IsFinite(current)) return Invalid(dimensions);
                t += h;
            }

            return current;
        }

        private static double[] Derivative(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            int dimensions,
            IDictionary<string, double> baseVariables,
            double[] state,
            double time)
        {
            var values = new Dictionary<string, double>(baseVariables, StringComparer.OrdinalIgnoreCase)
            {
                ["t"] = time,
                ["time"] = time,
                ["x"] = state[0]
            };
            if (dimensions >= 2) values["y"] = state[1];
            if (dimensions >= 3) values["z"] = state[2];

            var derivative = new double[dimensions];
            try
            {
                for (int i = 0; i < dimensions; i++) derivative[i] = components[i].Evaluate(values);
            }
            catch
            {
                return Invalid(dimensions);
            }
            return derivative;
        }

        private static bool TryInitialState(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            int dimensions,
            IDictionary<string, double> variables,
            out double[] initial,
            out double t0)
        {
            initial = new double[dimensions];
            t0 = 0;
            try
            {
                for (int i = 0; i < dimensions; i++) initial[i] = components[dimensions + i].Evaluate(variables);
                t0 = components[dimensions * 2].Evaluate(variables);
                return IsFinite(initial) && double.IsFinite(t0);
            }
            catch
            {
                return false;
            }
        }

        private static (double start, double end) ResolveRange(double? minT, double? maxT, double t0)
        {
            double start = minT ?? t0;
            double end = maxT ?? (t0 + 10.0);
            if (!double.IsFinite(start) || !double.IsFinite(end) || start >= end) return (t0, t0 + 10.0);
            return (start, end);
        }

        private static double[] Combine(double[] state, double[] derivative, double scale)
        {
            var result = new double[state.Length];
            for (int i = 0; i < state.Length; i++) result[i] = state[i] + derivative[i] * scale;
            return result;
        }

        private static double[] Invalid(int dimensions)
        {
            var values = new double[dimensions];
            Array.Fill(values, double.NaN);
            return values;
        }

        private static bool IsFinite(double[] values)
        {
            foreach (double value in values)
            {
                if (!double.IsFinite(value) || Math.Abs(value) > BlowUpLimit) return false;
            }
            return true;
        }

        private readonly record struct StateSample(double Time, double[] Values);
    }
}
