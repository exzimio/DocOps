using DocOps.Domain.Workflows;

namespace DocOps.UnitTests.Workflows;

public class WorkflowDefinitionValidatorTests
{
    private static WorkflowNode Node(
        string key, WorkflowNodeType type, string? name = null, NodeConfiguration? config = null) =>
        new()
        {
            Key = key,
            Name = name ?? key,
            Type = type,
            ConfigJson = config?.ToJson()
        };

    private static WorkflowEdge Edge(string from, string to, string? branch = null) =>
        new() { FromNodeKey = from, ToNodeKey = to, Branch = branch };

    private static NodeConfiguration TaskConfig() => new()
    {
        Assignment = new AssignmentConfiguration { Strategy = AssignmentStrategy.Role, Role = "Manager" },
        DueInHours = 24
    };

    /// <summary>The smallest definition that is actually executable.</summary>
    private static WorkflowDefinition ValidDefinition()
    {
        WorkflowDefinition definition = new() { Code = "test", Name = "Teste" };

        definition.Nodes.Add(Node("start", WorkflowNodeType.Start));
        definition.Nodes.Add(Node("review", WorkflowNodeType.HumanTask, "Rever", TaskConfig()));
        definition.Nodes.Add(Node("end", WorkflowNodeType.End));

        definition.Edges.Add(Edge("start", "review"));
        definition.Edges.Add(Edge("review", "end"));

        return definition;
    }

    [Fact]
    public void A_well_formed_definition_has_no_issues_and_can_be_activated()
    {
        Assert.Empty(WorkflowDefinitionValidator.Validate(ValidDefinition()));
        Assert.True(WorkflowDefinitionValidator.CanActivate(ValidDefinition()));
    }

    [Fact]
    public void Reports_a_missing_start_node()
    {
        WorkflowDefinition definition = ValidDefinition();
        definition.Nodes.Remove(definition.Nodes.First(n => n.Type == WorkflowNodeType.Start));

        Assert.Contains(WorkflowDefinitionValidator.Validate(definition), issue => issue.Code == "missing_start");
        Assert.False(WorkflowDefinitionValidator.CanActivate(definition));
    }

    [Fact]
    public void Reports_more_than_one_start_node()
    {
        WorkflowDefinition definition = ValidDefinition();
        definition.Nodes.Add(Node("start-2", WorkflowNodeType.Start));
        definition.Edges.Add(Edge("start-2", "review"));

        Assert.Contains(WorkflowDefinitionValidator.Validate(definition), issue => issue.Code == "multiple_starts");
    }

    [Fact]
    public void Reports_a_missing_end_node()
    {
        WorkflowDefinition definition = ValidDefinition();
        definition.Nodes.Remove(definition.Nodes.First(n => n.Type == WorkflowNodeType.End));
        definition.Edges.Clear();
        definition.Edges.Add(Edge("start", "review"));

        Assert.Contains(WorkflowDefinitionValidator.Validate(definition), issue => issue.Code == "missing_end");
    }

    [Fact]
    public void Reports_a_node_that_would_strand_the_process()
    {
        WorkflowDefinition definition = ValidDefinition();
        definition.Edges.Remove(definition.Edges.First(e => e.FromNodeKey == "review"));

        WorkflowValidationIssue issue = Assert.Single(
            WorkflowDefinitionValidator.Validate(definition), i => i.Code == "node_without_exit");

        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal("review", issue.NodeKey);
    }

    [Fact]
    public void Reports_an_edge_pointing_at_a_node_that_does_not_exist()
    {
        WorkflowDefinition definition = ValidDefinition();
        definition.Edges.Add(Edge("review", "ghost"));

        Assert.Contains(WorkflowDefinitionValidator.Validate(definition), issue => issue.Code == "edge_unknown_target");
    }

    [Fact]
    public void Reports_an_unreachable_node_as_a_warning()
    {
        WorkflowDefinition definition = ValidDefinition();
        definition.Nodes.Add(Node("orphan", WorkflowNodeType.HumanTask, "Órfã", TaskConfig()));
        definition.Edges.Add(Edge("orphan", "end"));

        WorkflowValidationIssue issue = Assert.Single(
            WorkflowDefinitionValidator.Validate(definition), i => i.Code == "node_unreachable");

        Assert.Equal(ValidationSeverity.Warning, issue.Severity);

        // A warning does not stop activation; only errors do.
        Assert.True(WorkflowDefinitionValidator.CanActivate(definition));
    }

    [Fact]
    public void Reports_a_task_node_without_an_assignee()
    {
        WorkflowDefinition definition = ValidDefinition();
        definition.Nodes.First(n => n.Key == "review").ConfigJson = null;

        Assert.Contains(WorkflowDefinitionValidator.Validate(definition), issue => issue.Code == "assignment_missing");
    }

    [Fact]
    public void Warns_when_a_task_has_no_deadline_because_it_can_never_be_overdue()
    {
        WorkflowDefinition definition = ValidDefinition();

        definition.Nodes.First(n => n.Key == "review").ConfigJson = new NodeConfiguration
        {
            Assignment = new AssignmentConfiguration { Strategy = AssignmentStrategy.Role, Role = "Manager" }
        }.ToJson();

        Assert.Contains(WorkflowDefinitionValidator.Validate(definition), issue => issue.Code == "due_date_missing");
    }

    [Fact]
    public void Reports_a_condition_without_a_rule()
    {
        WorkflowDefinition definition = ValidDefinition();
        definition.Nodes.Add(Node("check", WorkflowNodeType.Condition));
        definition.Edges.Add(Edge("check", "end"));
        definition.Edges.Add(Edge("review", "check"));

        Assert.Contains(WorkflowDefinitionValidator.Validate(definition), issue => issue.Code == "condition_missing");
    }

    [Fact]
    public void Warns_when_a_condition_lacks_one_of_its_branches()
    {
        WorkflowDefinition definition = ValidDefinition();

        definition.Nodes.Add(Node("check", WorkflowNodeType.Condition, "Valor elevado?", new NodeConfiguration
        {
            Condition = new ConditionConfiguration
            {
                Field = "totalAmount",
                Operator = ConditionOperator.GreaterThan,
                Value = "10000"
            }
        }));

        definition.Edges.Add(Edge("review", "check"));
        definition.Edges.Add(Edge("check", "end", "true"));

        Assert.Contains(
            WorkflowDefinitionValidator.Validate(definition),
            issue => issue.Code == "condition_branch_missing");
    }

    [Fact]
    public void Refuses_to_activate_a_definition_that_uses_a_node_type_without_an_executor()
    {
        WorkflowDefinition definition = ValidDefinition();
        definition.Nodes.Add(Node("extract", WorkflowNodeType.AiExtraction, "Extrair dados"));
        definition.Edges.Add(Edge("review", "extract"));
        definition.Edges.Add(Edge("extract", "end"));

        WorkflowValidationIssue issue = Assert.Single(
            WorkflowDefinitionValidator.Validate(definition), i => i.Code == "node_type_not_implemented");

        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.False(WorkflowDefinitionValidator.CanActivate(definition));
    }

    [Fact]
    public void Warns_when_an_end_node_has_outgoing_edges_that_can_never_be_followed()
    {
        WorkflowDefinition definition = ValidDefinition();
        definition.Nodes.Add(Node("after-end", WorkflowNodeType.HumanTask, "Depois do fim", TaskConfig()));
        definition.Edges.Add(Edge("end", "after-end"));
        definition.Edges.Add(Edge("after-end", "end"));

        Assert.Contains(WorkflowDefinitionValidator.Validate(definition), issue => issue.Code == "end_with_exit");
    }
}
