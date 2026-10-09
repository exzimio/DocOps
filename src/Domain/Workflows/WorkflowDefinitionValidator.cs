namespace DocOps.Domain.Workflows;

public enum ValidationSeverity
{
    Warning,
    Error
}

public sealed record WorkflowValidationIssue(ValidationSeverity Severity, string Code, string Message, string? NodeKey);

/// <summary>
/// Checks a definition before it is allowed to run. The point is to turn "the process silently got
/// stuck" into "this definition cannot be activated, and here is why" — a workflow designer needs
/// that feedback while designing, not when an invoice is already waiting on it.
/// </summary>
public static class WorkflowDefinitionValidator
{
    public static IReadOnlyList<WorkflowValidationIssue> Validate(WorkflowDefinition definition)
    {
        List<WorkflowValidationIssue> issues = [];

        WorkflowNode[] startNodes = [.. definition.Nodes.Where(n => n.Type == WorkflowNodeType.Start)];
        WorkflowNode[] endNodes = [.. definition.Nodes.Where(n => n.Type == WorkflowNodeType.End)];

        if (startNodes.Length == 0)
        {
            issues.Add(new WorkflowValidationIssue(
                ValidationSeverity.Error, "missing_start", "O workflow não tem nó de início.", null));
        }
        else if (startNodes.Length > 1)
        {
            issues.Add(new WorkflowValidationIssue(
                ValidationSeverity.Error, "multiple_starts", "O workflow tem mais do que um nó de início.", null));
        }

        if (endNodes.Length == 0)
        {
            issues.Add(new WorkflowValidationIssue(
                ValidationSeverity.Error, "missing_end", "O workflow não tem nó de fim.", null));
        }

        HashSet<string> nodeKeys = [.. definition.Nodes.Select(n => n.Key)];

        foreach (WorkflowEdge edge in definition.Edges)
        {
            if (!nodeKeys.Contains(edge.FromNodeKey))
            {
                issues.Add(new WorkflowValidationIssue(
                    ValidationSeverity.Error, "edge_unknown_source",
                    $"A ligação parte de um nó inexistente ('{edge.FromNodeKey}').", edge.FromNodeKey));
            }

            if (!nodeKeys.Contains(edge.ToNodeKey))
            {
                issues.Add(new WorkflowValidationIssue(
                    ValidationSeverity.Error, "edge_unknown_target",
                    $"A ligação aponta para um nó inexistente ('{edge.ToNodeKey}').", edge.FromNodeKey));
            }
        }

        foreach (WorkflowNode node in definition.Nodes)
        {
            ValidateNode(definition, node, issues);
        }

        if (startNodes.Length == 1)
        {
            ValidateReachability(definition, startNodes[0], issues);
        }

        return issues;
    }

    public static bool CanActivate(WorkflowDefinition definition) =>
        !Validate(definition).Any(issue => issue.Severity == ValidationSeverity.Error);

    private static void ValidateNode(
        WorkflowDefinition definition, WorkflowNode node, List<WorkflowValidationIssue> issues)
    {
        bool hasOutgoing = definition.OutgoingEdges(node.Key).Any();

        if (node.Type != WorkflowNodeType.End && !hasOutgoing)
        {
            issues.Add(new WorkflowValidationIssue(
                ValidationSeverity.Error, "node_without_exit",
                $"O nó '{node.Name}' não tem ligação de saída: o processo ficaria parado aqui.", node.Key));
        }

        if (node.Type == WorkflowNodeType.End && hasOutgoing)
        {
            issues.Add(new WorkflowValidationIssue(
                ValidationSeverity.Warning, "end_with_exit",
                $"O nó de fim '{node.Name}' tem ligações de saída, que nunca serão seguidas.", node.Key));
        }

        NodeConfiguration config = NodeConfiguration.Parse(node.ConfigJson);

        switch (node.Type)
        {
            case WorkflowNodeType.Condition:
                ValidateCondition(definition, node, config, issues);
                break;

            case WorkflowNodeType.Integration:
                if (config.IntegrationKind is null)
                {
                    issues.Add(new WorkflowValidationIssue(
                        ValidationSeverity.Error, "integration_missing",
                        $"O nó '{node.Name}' não indica qual a integração a executar.", node.Key));
                }

                break;

            case WorkflowNodeType.SignatureRequest:
                if (config.Signer is null && config.Assignment is null)
                {
                    issues.Add(new WorkflowValidationIssue(
                        ValidationSeverity.Error, "signer_missing",
                        $"O nó '{node.Name}' não indica quem assina.", node.Key));
                }
                else if ((config.Signer ?? config.Assignment) is { Strategy: not AssignmentStrategy.User })
                {
                    // Resolving a department or a role to one person happens at run time, and may
                    // fail then. A warning rather than an error: it is a legitimate way to design a
                    // process, it just cannot be checked before it runs.
                    issues.Add(new WorkflowValidationIssue(
                        ValidationSeverity.Warning, "signer_not_a_person",
                        $"O nó '{node.Name}' aponta para um grupo. Uma assinatura é de uma pessoa: "
                        + "o grupo será resolvido para um signatário concreto quando o passo correr, "
                        + "e o processo falha se não resolver.",
                        node.Key));
                }

                break;

            case WorkflowNodeType.HumanTask or WorkflowNodeType.Approval:
                if (config.Assignment is null)
                {
                    issues.Add(new WorkflowValidationIssue(
                        ValidationSeverity.Error, "assignment_missing",
                        $"O nó '{node.Name}' não indica a quem atribuir a tarefa.", node.Key));
                }

                if (config.DueInHours is null)
                {
                    issues.Add(new WorkflowValidationIssue(
                        ValidationSeverity.Warning, "due_date_missing",
                        $"O nó '{node.Name}' não define prazo, pelo que a tarefa nunca fica em atraso.", node.Key));
                }

                break;

            case WorkflowNodeType.Notification when config.Notification is null:
                issues.Add(new WorkflowValidationIssue(
                    ValidationSeverity.Error, "notification_missing",
                    $"O nó '{node.Name}' não tem mensagem configurada.", node.Key));
                break;

            case WorkflowNodeType.Join when definition.IncomingEdges(node.Key).Count() < 2:
                issues.Add(new WorkflowValidationIssue(
                    ValidationSeverity.Warning, "join_single_branch",
                    $"A junção '{node.Name}' tem menos de dois ramos de entrada.", node.Key));
                break;
        }
    }

    private static void ValidateCondition(
        WorkflowDefinition definition, WorkflowNode node, NodeConfiguration config, List<WorkflowValidationIssue> issues)
    {
        if (config.Condition is null)
        {
            issues.Add(new WorkflowValidationIssue(
                ValidationSeverity.Error, "condition_missing",
                $"O nó de condição '{node.Name}' não tem regra configurada.", node.Key));

            return;
        }

        string[] branches =
        [
            .. definition.OutgoingEdges(node.Key)
                .Select(edge => edge.Branch?.ToLowerInvariant())
                .Where(branch => branch is not null)
                .Select(branch => branch!)
        ];

        foreach (string expected in new[] { "true", "false" })
        {
            if (!branches.Contains(expected))
            {
                issues.Add(new WorkflowValidationIssue(
                    ValidationSeverity.Warning, "condition_branch_missing",
                    $"A condição '{node.Name}' não tem saída para o ramo '{expected}'.", node.Key));
            }
        }
    }

    /// <summary>Flags nodes that can never be reached, which are almost always a design mistake.</summary>
    private static void ValidateReachability(
        WorkflowDefinition definition, WorkflowNode start, List<WorkflowValidationIssue> issues)
    {
        HashSet<string> reachable = [];
        Queue<string> pending = new([start.Key]);

        while (pending.Count > 0)
        {
            string key = pending.Dequeue();

            if (!reachable.Add(key))
            {
                continue;
            }

            foreach (WorkflowEdge edge in definition.OutgoingEdges(key))
            {
                pending.Enqueue(edge.ToNodeKey);
            }
        }

        foreach (WorkflowNode node in definition.Nodes.Where(n => !reachable.Contains(n.Key)))
        {
            issues.Add(new WorkflowValidationIssue(
                ValidationSeverity.Warning, "node_unreachable",
                $"O nó '{node.Name}' não é alcançável a partir do início.", node.Key));
        }
    }
}
