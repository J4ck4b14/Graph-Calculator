using System;
using System.Collections.Generic;
using System.Linq;

namespace GraphCalculator
{
    public sealed record EconomyNodeSpec(
        Guid Id,
        EconomyNodeKind Kind,
        string Name,
        string Resource,
        double InitialAmount,
        double Capacity);

    public sealed record EconomyLinkSpec(
        Guid Id,
        Guid SourceId,
        Guid TargetId,
        string RateExpression,
        string ConditionExpression,
        double Efficiency,
        double Chance,
        double Interval,
        double Delay,
        double StartTime,
        double EndTime,
        bool IsEnabled,
        CalculatorEngine.CompiledExpression Rate,
        CalculatorEngine.CompiledExpression Condition);


    public sealed record EconomyRecipeItemSpec(Guid NodeId, string Resource, double Amount);

    public sealed record EconomyRecipeSpec(
        Guid Id, Guid ConverterNodeId, string Name, string CraftRateExpression, bool IsEnabled,
        IReadOnlyList<EconomyRecipeItemSpec> Inputs, IReadOnlyList<EconomyRecipeItemSpec> Outputs,
        CalculatorEngine.CompiledExpression CraftRate);

    public sealed class EconomySimulationModel
    {
        public List<EconomyNodeSpec> Nodes { get; init; } = [];
        public List<EconomyLinkSpec> Links { get; init; } = [];
        public Dictionary<string, double> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<EconomyRecipeSpec> Recipes { get; init; } = [];

        public static EconomySimulationModel Capture(
            IEnumerable<EconomyNode> nodes,
            IEnumerable<EconomyLink> links,
            IEnumerable<EconomyParameter> parameters,
            IEnumerable<EconomyRecipe>? recipes = null)
        {
            var model = new EconomySimulationModel
            {
                Nodes = nodes.Select(n => new EconomyNodeSpec(
                    n.Id, n.Kind, n.Name, n.Resource, n.InitialAmount,
                    n.IsStorage && double.IsFinite(n.Capacity) ? Math.Max(0.0001, n.Capacity) : double.PositiveInfinity)).ToList(),
                Parameters = parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase)
            };

            foreach (EconomyLink link in links)
            {
                try
                {
                    CalculatorEngine.CompiledExpression rate = link.CompiledRate ?? CalculatorEngine.Compile(string.IsNullOrWhiteSpace(link.RateExpression) ? "0" : link.RateExpression);
                    CalculatorEngine.CompiledExpression condition = link.CompiledCondition ?? CalculatorEngine.Compile(string.IsNullOrWhiteSpace(link.ConditionExpression) ? "1" : link.ConditionExpression);
                    model.Links.Add(new EconomyLinkSpec(
                        link.Id, link.SourceId, link.TargetId, link.RateExpression, link.ConditionExpression,
                        link.Efficiency, link.Chance, link.Interval, link.Delay, link.StartTime, link.EndTime,
                        link.IsEnabled, rate, condition));
                }
                catch
                {
                    // A malformed flow remains editable in the diagram, but a prediction must not half-evaluate it.
                }
            }
            if (recipes != null)
            {
                foreach (EconomyRecipe recipe in recipes.Where(r => r.IsEnabled))
                {
                    try
                    {
                        CalculatorEngine.CompiledExpression rate = CalculatorEngine.Compile(string.IsNullOrWhiteSpace(recipe.CraftRateExpression) ? "1" : recipe.CraftRateExpression);
                        model.Recipes.Add(new EconomyRecipeSpec(
                            recipe.Id, recipe.ConverterNodeId, recipe.Name, recipe.CraftRateExpression, recipe.IsEnabled,
                            recipe.Inputs.Select(i => new EconomyRecipeItemSpec(i.NodeId, i.Resource, i.Amount)).ToList(),
                            recipe.Outputs.Select(i => new EconomyRecipeItemSpec(i.NodeId, i.Resource, i.Amount)).ToList(), rate));
                    }
                    catch { }
                }
            }
            return model;
        }
    }

    public sealed class EconomySimulationResult
    {
        public double Time { get; init; }
        public double RequestedHorizon { get; init; }
        public bool StepLimitHit { get; init; }
        public int PendingTransferCount { get; init; }
        public double PendingAmount { get; init; }
        public Dictionary<Guid, double> FinalAmounts { get; init; } = [];
        public Dictionary<Guid, double> MinimumAmounts { get; init; } = [];
        public Dictionary<Guid, double> MaximumAmounts { get; init; } = [];
        public HashSet<Guid> StarvedNodes { get; init; } = [];
        public HashSet<Guid> CapacityBoundNodes { get; init; } = [];
    }

    public readonly record struct EconomyRandomState(ulong State, bool HasSpareNormal, double SpareNormal);

    public sealed class EconomyDeterministicRandom
    {
        private ulong _state;
        private bool _hasSpareNormal;
        private double _spareNormal;

        public EconomyDeterministicRandom(long seed)
        {
            _state = unchecked((ulong)seed) + 0x9E3779B97F4A7C15UL;
            if (_state == 0) _state = 0xA24BAED4963EE407UL;
            // Warm it once; neighbouring integer seeds otherwise look needlessly similar in the first draw.
            NextUInt64();
        }

        public EconomyDeterministicRandom(EconomyRandomState state)
        {
            _state = state.State == 0 ? 0xA24BAED4963EE407UL : state.State;
            _hasSpareNormal = state.HasSpareNormal;
            _spareNormal = state.SpareNormal;
        }

        public EconomyRandomState CaptureState() => new(_state, _hasSpareNormal, _spareNormal);

        private ulong NextUInt64()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 2685821657736338717UL;
        }

        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

        public double NextNormal()
        {
            if (_hasSpareNormal)
            {
                _hasSpareNormal = false;
                return _spareNormal;
            }

            double u1 = Math.Max(1e-12, NextDouble());
            double u2 = NextDouble();
            double mag = Math.Sqrt(-2.0 * Math.Log(u1));
            double angle = 2.0 * Math.PI * u2;
            _spareNormal = mag * Math.Sin(angle);
            _hasSpareNormal = true;
            return mag * Math.Cos(angle);
        }

        public static long DeriveSeed(long baseSeed, Guid streamId)
        {
            ulong hash = 1469598103934665603UL ^ unchecked((ulong)baseSeed);
            foreach (byte value in streamId.ToByteArray())
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
            return unchecked((long)hash);
        }
    }

    public static class EconomySimulationEngine
    {
        private sealed record Pending(Guid LinkId, Guid TargetId, double DueTime, double Amount);
        private sealed record Proposal(EconomyLinkSpec Link, EconomyNodeSpec Source, EconomyNodeSpec Target, double Requested, double DueTime);

        public static EconomySimulationResult Run(
            EconomySimulationModel model,
            double horizon,
            double dt,
            long seed,
            IReadOnlyDictionary<string, double>? overrides = null,
            int runIndex = 0,
            int cohortIndex = 0)
        {
            horizon = Math.Max(0, horizon);
            dt = Math.Clamp(dt, 0.001, 10);

            Dictionary<Guid, EconomyNodeSpec> nodes = model.Nodes.ToDictionary(n => n.Id);
            var amount = new Dictionary<Guid, double>();
            var minAmount = new Dictionary<Guid, double>();
            var maxAmount = new Dictionary<Guid, double>();
            foreach (EconomyNodeSpec node in model.Nodes)
            {
                double initial = IsStorage(node.Kind) ? Math.Clamp(node.InitialAmount, 0, node.Capacity) : 0;
                amount[node.Id] = initial;
                minAmount[node.Id] = initial;
                maxAmount[node.Id] = initial;
            }

            var parameters = new Dictionary<string, double>(model.Parameters, StringComparer.OrdinalIgnoreCase);
            if (overrides != null)
                foreach (var pair in overrides) parameters[pair.Key] = pair.Value;

            // Each flow owns its random stream. Adding an unrelated stochastic link should not reshuffle every other result.
            var linkRandom = model.Links.ToDictionary(
                l => l.Id,
                l => new EconomyDeterministicRandom(EconomyDeterministicRandom.DeriveSeed(seed, l.Id)));
            var nextActivation = model.Links.ToDictionary(l => l.Id, l => Math.Max(0, l.StartTime));
            var pending = new List<Pending>();
            var starved = new HashSet<Guid>();
            var capped = new HashSet<Guid>();
            double time = 0;
            int stepCount = 0;
            const int MaxSimulationSteps = 2_000_000;

            while (time < horizon - 1e-12 && stepCount++ < MaxSimulationSteps)
            {
                double step = Math.Min(dt, horizon - time);
                double stepEnd = time + step;

                DeliverPendingThrough(time, step, nodes, amount, pending, capped);

                foreach (EconomyRecipeSpec recipe in model.Recipes)
                {
                    var vars = new Dictionary<string, double>(parameters, StringComparer.OrdinalIgnoreCase)
                    {
                        ["t"] = time, ["time"] = time, ["dt"] = step, ["run"] = runIndex, ["cohort"] = cohortIndex
                    };
                    double craftRate;
                    try { craftRate = recipe.CraftRate.Evaluate(vars); } catch { continue; }
                    if (!double.IsFinite(craftRate) || craftRate <= 0) continue;
                    double crafts = craftRate * step;
                    foreach (EconomyRecipeItemSpec input in recipe.Inputs)
                    {
                        if (!amount.TryGetValue(input.NodeId, out double have) || input.Amount <= 0) { crafts = 0; break; }
                        crafts = Math.Min(crafts, have / input.Amount);
                    }
                    foreach (EconomyRecipeItemSpec output in recipe.Outputs)
                    {
                        if (!nodes.TryGetValue(output.NodeId, out EconomyNodeSpec? target) || output.Amount <= 0) continue;
                        if (IsStorage(target.Kind)) crafts = Math.Min(crafts, Math.Max(0, target.Capacity - amount[target.Id]) / output.Amount);
                    }
                    if (crafts <= 1e-12) continue;
                    foreach (EconomyRecipeItemSpec input in recipe.Inputs)
                    {
                        amount[input.NodeId] = Math.Max(0, amount[input.NodeId] - input.Amount * crafts);
                        if (amount[input.NodeId] <= 1e-9) starved.Add(input.NodeId);
                    }
                    foreach (EconomyRecipeItemSpec output in recipe.Outputs)
                    {
                        if (!nodes.TryGetValue(output.NodeId, out EconomyNodeSpec? target)) continue;
                        double next = amount[output.NodeId] + output.Amount * crafts;
                        if (IsStorage(target.Kind) && next >= target.Capacity - 1e-9) capped.Add(target.Id);
                        amount[output.NodeId] = IsStorage(target.Kind) ? Math.Min(target.Capacity, next) : next;
                    }
                }

                var snapshot = new Dictionary<Guid, double>(amount);
                var proposals = new List<Proposal>();

                foreach (EconomyLinkSpec link in model.Links)
                {
                    if (!link.IsEnabled || !nodes.TryGetValue(link.SourceId, out EconomyNodeSpec? source) || !nodes.TryGetValue(link.TargetId, out EconomyNodeSpec? target)) continue;
                    if (stepEnd + 1e-9 < link.StartTime) continue;
                    if (double.IsFinite(link.EndTime) && time >= link.EndTime - 1e-9) continue;

                    EconomyDeterministicRandom rng = linkRandom[link.Id];
                    if (link.Interval > 1e-9)
                    {
                        double next = nextActivation[link.Id];
                        int activationGuard = 0;
                        while (next <= stepEnd + 1e-9 && activationGuard++ < 100000)
                        {
                            double activationTime = next;
                            next += link.Interval;
                            if (activationTime < time - 1e-9 || activationTime + 1e-9 < link.StartTime) continue;
                            if (double.IsFinite(link.EndTime) && activationTime >= link.EndTime - 1e-9) break;
                            TryAddProposal(link, source, target, parameters, snapshot, activationTime, link.Interval, rng, runIndex, cohortIndex, proposals);
                        }
                        nextActivation[link.Id] = next;
                    }
                    else
                    {
                        double activeStart = Math.Max(time, link.StartTime);
                        double activeEnd = double.IsFinite(link.EndTime) ? Math.Min(stepEnd, link.EndTime) : stepEnd;
                        double span = activeEnd - activeStart;
                        if (span > 1e-12)
                            TryAddProposal(link, source, target, parameters, snapshot, activeStart, span, rng, runIndex, cohortIndex, proposals);
                    }
                }

                foreach (var group in proposals.Where(p => IsStorage(p.Source.Kind)).GroupBy(p => p.Source.Id))
                {
                    double total = group.Sum(p => p.Requested);
                    double available = snapshot[group.Key];
                    if (total <= available || total <= 1e-12) continue;
                    starved.Add(group.Key);
                    double scale = available / total;
                    for (int i = 0; i < proposals.Count; i++)
                        if (proposals[i].Source.Id == group.Key) proposals[i] = proposals[i] with { Requested = proposals[i].Requested * scale };
                }

                // Delayed arrivals are capacity-checked when they arrive. Immediate arrivals still share today's room fairly.
                foreach (var group in proposals.Where(p => p.Link.Delay <= 1e-9 && IsStorage(p.Target.Kind)).GroupBy(p => p.Target.Id))
                {
                    double room = Math.Max(0, group.First().Target.Capacity - snapshot[group.Key]);
                    double delivered = group.Sum(p => p.Requested * p.Link.Efficiency);
                    if (delivered <= room || delivered <= 1e-12) continue;
                    capped.Add(group.Key);
                    double scale = room / delivered;
                    for (int i = 0; i < proposals.Count; i++)
                        if (proposals[i].Target.Id == group.Key && proposals[i].Link.Delay <= 1e-9)
                            proposals[i] = proposals[i] with { Requested = proposals[i].Requested * scale };
                }

                foreach (Proposal proposal in proposals)
                {
                    double taken = proposal.Requested;
                    double delivered = taken * proposal.Link.Efficiency;
                    if (IsStorage(proposal.Source.Kind)) amount[proposal.Source.Id] = Math.Max(0, amount[proposal.Source.Id] - taken);
                    else if (EconomyNode.Generates(proposal.Source.Kind)) amount[proposal.Source.Id] += taken;

                    if (proposal.Link.Delay > 1e-9)
                    {
                        pending.Add(new Pending(proposal.Link.Id, proposal.Target.Id, proposal.DueTime, delivered));
                    }
                    else if (IsStorage(proposal.Target.Kind))
                    {
                        amount[proposal.Target.Id] = Math.Min(proposal.Target.Capacity, amount[proposal.Target.Id] + delivered);
                    }
                    else if (proposal.Target.Kind == EconomyNodeKind.Sink)
                    {
                        amount[proposal.Target.Id] += delivered;
                    }
                }

                // A short delay should not silently become one whole dt longer just because it landed inside this step.
                DeliverPendingThrough(stepEnd, step, nodes, amount, pending, capped);

                time = stepEnd;
                foreach (EconomyNodeSpec node in model.Nodes)
                {
                    double value = amount[node.Id];
                    minAmount[node.Id] = Math.Min(minAmount[node.Id], value);
                    maxAmount[node.Id] = Math.Max(maxAmount[node.Id], value);
                    if (IsStorage(node.Kind))
                    {
                        if (value <= 1e-8 && time > step) starved.Add(node.Id);
                        if (double.IsFinite(node.Capacity) && value >= node.Capacity * 0.999) capped.Add(node.Id);
                    }
                }
            }

            bool limitHit = time < horizon - 1e-9;
            return new EconomySimulationResult
            {
                Time = time,
                RequestedHorizon = horizon,
                StepLimitHit = limitHit,
                PendingTransferCount = pending.Count,
                PendingAmount = pending.Sum(p => p.Amount),
                FinalAmounts = amount,
                MinimumAmounts = minAmount,
                MaximumAmounts = maxAmount,
                StarvedNodes = starved,
                CapacityBoundNodes = capped
            };
        }

        private static void TryAddProposal(
            EconomyLinkSpec link,
            EconomyNodeSpec source,
            EconomyNodeSpec target,
            IReadOnlyDictionary<string, double> parameters,
            IReadOnlyDictionary<Guid, double> snapshot,
            double eventTime,
            double span,
            EconomyDeterministicRandom random,
            int runIndex,
            int cohortIndex,
            List<Proposal> proposals)
        {
            var vars = BuildVariables(parameters, eventTime, span, snapshot[source.Id], snapshot[target.Id], source.Capacity, target.Capacity, random, runIndex, cohortIndex);
            double condition;
            try { condition = link.Condition.Evaluate(vars); }
            catch { return; }
            if (!double.IsFinite(condition) || condition <= 0) return;
            if (link.Chance < 1 && random.NextDouble() > link.Chance) return;

            // A condition and a rate are separate evaluations on purpose; each may use its own random sample.
            vars["rand"] = random.NextDouble();
            vars["gauss"] = random.NextNormal();
            double rate;
            try { rate = link.Rate.Evaluate(vars); }
            catch { return; }
            if (!double.IsFinite(rate) || rate <= 0) return;

            double requested = rate * span;
            double due = eventTime + Math.Max(0, link.Delay);
            proposals.Add(new Proposal(link, source, target, requested, due));
        }

        private static void DeliverPendingThrough(
            double throughTime,
            double retryStep,
            IReadOnlyDictionary<Guid, EconomyNodeSpec> nodes,
            IDictionary<Guid, double> amount,
            List<Pending> pending,
            ISet<Guid> capped)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending transfer = pending[i];
                if (transfer.DueTime > throughTime + 1e-9) continue;
                if (!nodes.TryGetValue(transfer.TargetId, out EconomyNodeSpec? target)) { pending.RemoveAt(i); continue; }

                double remaining = transfer.Amount;
                if (IsStorage(target.Kind))
                {
                    double room = Math.Max(0, target.Capacity - amount[target.Id]);
                    double delivered = Math.Min(room, remaining);
                    amount[target.Id] += delivered;
                    remaining -= delivered;
                    if (remaining > 1e-10)
                    {
                        capped.Add(target.Id);
                        pending[i] = transfer with { DueTime = throughTime + Math.Max(0.001, retryStep), Amount = remaining };
                        continue;
                    }
                }
                else if (target.Kind == EconomyNodeKind.Sink)
                {
                    amount[target.Id] += remaining;
                }
                pending.RemoveAt(i);
            }
        }

        private static Dictionary<string, double> BuildVariables(
            IReadOnlyDictionary<string, double> parameters,
            double time,
            double dt,
            double source,
            double target,
            double sourceCap,
            double targetCap,
            EconomyDeterministicRandom random,
            int runIndex,
            int cohortIndex)
        {
            var vars = new Dictionary<string, double>(parameters, StringComparer.OrdinalIgnoreCase)
            {
                ["t"] = time,
                ["time"] = time,
                ["dt"] = dt,
                ["source"] = source,
                ["target"] = target,
                ["sourcecap"] = double.IsFinite(sourceCap) ? sourceCap : 1e12,
                ["targetcap"] = double.IsFinite(targetCap) ? targetCap : 1e12,
                ["rand"] = random.NextDouble(),
                ["gauss"] = random.NextNormal(),
                ["run"] = runIndex,
                ["cohort"] = cohortIndex
            };
            return vars;
        }

        public static bool IsStorage(EconomyNodeKind kind) => EconomyNode.StoresAmount(kind);
        public static bool IsGenerator(EconomyNodeKind kind) => EconomyNode.Generates(kind);
    }
}
