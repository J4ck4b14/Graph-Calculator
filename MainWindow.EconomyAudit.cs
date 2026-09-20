using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GraphCalculator
{
    public partial class MainWindow
    {
        private static void ValidateEconomyProjectForLoad(EconomyProjectFile project)
        {
            if (!string.Equals(project.Format, "GraphCalculator.Economy", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This is not a Graph Calculator economy project.");
            if (project.Version > EconomyProjectFile.CurrentVersion)
                throw new InvalidDataException($"This economy uses project format v{project.Version}, but this build supports up to v{EconomyProjectFile.CurrentVersion}.");
            if (project.Version < 1)
                throw new InvalidDataException("The economy project has an invalid format version.");

            List<WorkspaceEconomyNode> nodes = project.Nodes ?? [];
            List<WorkspaceEconomyLink> links = project.Links ?? [];
            WorkspaceEconomyNode? unknownKind = nodes.FirstOrDefault(n => !Enum.TryParse(n.Kind, true, out EconomyNodeKind _));
            if (unknownKind != null)
                throw new InvalidDataException($"The economy contains an unsupported node kind '{unknownKind.Kind}'.");

            Guid[] ids = nodes.Where(n => n.Id != Guid.Empty).Select(n => n.Id).ToArray();
            if (ids.Distinct().Count() != ids.Length)
                throw new InvalidDataException("The economy contains duplicate node identifiers.");

            HashSet<Guid> nodeIds = nodes.Where(n => n.Id != Guid.Empty).Select(n => n.Id).ToHashSet();
            Guid[] linkIds = links.Where(l => l.Id != Guid.Empty).Select(l => l.Id).ToArray();
            if (linkIds.Distinct().Count() != linkIds.Length)
                throw new InvalidDataException("The economy contains duplicate flow identifiers.");

            WorkspaceEconomyLink? orphan = links.FirstOrDefault(l => l.SourceId == Guid.Empty || l.TargetId == Guid.Empty || !nodeIds.Contains(l.SourceId) || !nodeIds.Contains(l.TargetId));
            if (orphan != null)
                throw new InvalidDataException("The economy contains a flow whose source or target node is missing.");

            if (project.Version >= 2)
            {
                HashSet<Guid> knownLinks = links.Where(l => l.Id != Guid.Empty).Select(l => l.Id).ToHashSet();
                WorkspaceEconomyPendingTransfer? badPending = (project.PendingTransfers ?? [])
                    .FirstOrDefault(t => !knownLinks.Contains(t.LinkId) || !nodeIds.Contains(t.TargetId));
                if (badPending != null)
                    throw new InvalidDataException("The economy contains delayed runtime state for a missing flow or target node.");

                if ((project.NextActivations ?? []).Keys.Any(id => !knownLinks.Contains(id)) ||
                    (project.LinkRandomStates ?? []).Keys.Any(id => !knownLinks.Contains(id)))
                    throw new InvalidDataException("The economy contains runtime flow state for a missing flow.");
            }
        }

        private List<string> BuildEconomyModelAudit()
        {
            var warnings = new List<string>();
            Dictionary<Guid, EconomyNode> nodes = EconomyNodes.ToDictionary(n => n.Id);
            var incomingCount = EconomyNodes.ToDictionary(n => n.Id, _ => 0);
            var outgoingCount = EconomyNodes.ToDictionary(n => n.Id, _ => 0);

            foreach (EconomyLink link in EconomyLinks)
            {
                if (!nodes.TryGetValue(link.SourceId, out EconomyNode? source) || !nodes.TryGetValue(link.TargetId, out EconomyNode? target))
                {
                    warnings.Add("A flow points to a missing node.");
                    continue;
                }
                if (link.IsEnabled)
                {
                    outgoingCount[source.Id]++;
                    incomingCount[target.Id]++;
                }
                if (source.Kind == EconomyNodeKind.Sink)
                    warnings.Add($"{source.Name} is a Sink but starts a flow.");
                if (target.IsGenerator)
                    warnings.Add($"{target.Name} is a {target.Kind} but receives a flow; generator nodes should only emit resources.");
                if (link.SourceId == link.TargetId)
                    warnings.Add($"{source.Name} has a self-loop; make sure that feedback is intentional.");
                if (link.IsEnabled && (link.CompiledRate == null || link.CompiledCondition == null))
                    warnings.Add($"{source.Name} → {target.Name} has an invalid expression.");
                if (!source.Resource.Equals(target.Resource, StringComparison.OrdinalIgnoreCase) &&
                    source.Kind != EconomyNodeKind.Converter && target.Kind != EconomyNodeKind.Converter)
                    warnings.Add($"{source.Name} → {target.Name} converts {source.Resource} to {target.Resource} without a Converter node.");
                if (double.IsFinite(link.EndTime) && link.EndTime <= link.StartTime)
                    warnings.Add($"{source.Name} → {target.Name} has no active scheduling window.");
            }

            foreach (EconomyNode node in EconomyNodes)
            {
                int incoming = incomingCount[node.Id];
                int outgoing = outgoingCount[node.Id];
                if (node.IsGenerator && outgoing == 0) warnings.Add($"{node.Name} is a {node.Kind} with no outgoing flow.");
                if (node.Kind == EconomyNodeKind.Sink && incoming == 0) warnings.Add($"{node.Name} is a Sink with no incoming flow.");
                if (node.Kind == EconomyNodeKind.Converter && (incoming == 0 || outgoing == 0)) warnings.Add($"{node.Name} is a Converter but is not connected on both sides.");
                if (node.Kind == EconomyNodeKind.Gate && (incoming == 0 || outgoing == 0)) warnings.Add($"{node.Name} is a Gate but has no complete route through it.");
                if (node.Kind == EconomyNodeKind.Queue && (incoming == 0 || outgoing == 0)) warnings.Add($"{node.Name} is a Queue but is not connected on both sides.");
                if (node.Kind == EconomyNodeKind.Register && incoming == 0 && outgoing == 0) warnings.Add($"{node.Name} is a Register with no connections.");
                if (node.IsStorage && (!double.IsFinite(node.Capacity) || node.Capacity <= 0)) warnings.Add($"{node.Name} has an invalid capacity.");
            }

            HashSet<string> parameterNames = EconomyParameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (EconomyScenario scenario in EconomyScenarios)
                foreach (string name in scenario.ParameterValues.Keys)
                    if (!parameterNames.Contains(name)) warnings.Add($"Scenario '{scenario.Name}' still overrides removed parameter '{name}'.");
            foreach (EconomyCohort cohort in EconomyCohorts)
                foreach (string name in cohort.ParameterValues.Keys)
                    if (!parameterNames.Contains(name)) warnings.Add($"Cohort '{cohort.Name}' still overrides removed parameter '{name}'.");
            if (EconomyCohorts.Count > 0 && EconomyCohorts.All(c => c.Weight <= 0)) warnings.Add("All cohort weights are zero, so population-mix predictions cannot select a cohort.");

            foreach (EconomyTarget target in EconomyTargets)
                if (!nodes.ContainsKey(target.NodeId)) warnings.Add($"Balance target '{target.NodeName}' points to a removed node.");

            return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
