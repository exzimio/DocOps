using DocOps.Domain.Workflows;

namespace DocOps.UnitTests.Workflows;

public class ConditionEvaluatorTests
{
    private static Dictionary<string, string?> Variables(params (string Key, string? Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

    private static ConditionConfiguration Condition(string field, ConditionOperator op, string? value = null) =>
        new() { Field = field, Operator = op, Value = value };

    [Theory]
    [InlineData("15000", true)]
    [InlineData("10000.01", true)]
    [InlineData("10000", false)]
    [InlineData("500", false)]
    public void Compares_numbers_using_the_invariant_format(string amount, bool expected)
    {
        bool result = ConditionEvaluator.Evaluate(
            Condition("totalAmount", ConditionOperator.GreaterThan, "10000"),
            Variables(("totalAmount", amount)));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void A_numeric_comparison_against_a_missing_value_is_false_rather_than_an_error()
    {
        // An invoice with no total must route down the "no" branch, not break the process.
        Assert.False(ConditionEvaluator.Evaluate(
            Condition("totalAmount", ConditionOperator.GreaterThan, "10000"),
            Variables()));
    }

    [Fact]
    public void A_numeric_comparison_against_text_is_false()
    {
        Assert.False(ConditionEvaluator.Evaluate(
            Condition("totalAmount", ConditionOperator.LessThan, "10000"),
            Variables(("totalAmount", "por confirmar"))));
    }

    [Theory]
    [InlineData(ConditionOperator.GreaterThanOrEqual, "10000", true)]
    [InlineData(ConditionOperator.LessThanOrEqual, "10000", true)]
    [InlineData(ConditionOperator.LessThan, "10000", false)]
    public void Supports_the_inclusive_comparisons(ConditionOperator op, string value, bool expected)
    {
        Assert.Equal(
            expected,
            ConditionEvaluator.Evaluate(Condition("totalAmount", op, value), Variables(("totalAmount", "10000"))));
    }

    [Fact]
    public void Text_comparison_ignores_case_and_surrounding_spaces()
    {
        Assert.True(ConditionEvaluator.Evaluate(
            Condition("departmentCode", ConditionOperator.Equals, "fin"),
            Variables(("departmentCode", " FIN "))));
    }

    [Fact]
    public void Not_equals_is_the_exact_opposite_of_equals()
    {
        Dictionary<string, string?> variables = Variables(("decision", "approved"));

        Assert.False(ConditionEvaluator.Evaluate(Condition("decision", ConditionOperator.NotEquals, "approved"), variables));
        Assert.True(ConditionEvaluator.Evaluate(Condition("decision", ConditionOperator.NotEquals, "rejected"), variables));
    }

    [Fact]
    public void Contains_matches_a_fragment_of_the_value()
    {
        Assert.True(ConditionEvaluator.Evaluate(
            Condition("supplier", ConditionOperator.Contains, "northstar"),
            Variables(("supplier", "Northstar Supplies"))));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("valor", false)]
    public void Detects_empty_values(string? value, bool expected)
    {
        Assert.Equal(
            expected,
            ConditionEvaluator.Evaluate(Condition("purchaseOrder", ConditionOperator.IsEmpty), Variables(("purchaseOrder", value))));

        Assert.Equal(
            !expected,
            ConditionEvaluator.Evaluate(Condition("purchaseOrder", ConditionOperator.IsNotEmpty), Variables(("purchaseOrder", value))));
    }

    [Fact]
    public void Variable_lookup_is_case_insensitive()
    {
        Assert.True(ConditionEvaluator.Evaluate(
            Condition("TotalAmount", ConditionOperator.GreaterThan, "100"),
            Variables(("totalamount", "500"))));
    }
}

public class NodeConfigurationTests
{
    [Fact]
    public void Round_trips_through_json()
    {
        NodeConfiguration original = new()
        {
            TaskTitle = "Aprovar fatura",
            DueInHours = 48,
            Priority = WorkflowTaskPriority.High,
            Assignment = new AssignmentConfiguration { Strategy = AssignmentStrategy.Role, Role = "FinanceApprover" },
            DocumentStatus = Domain.Documents.DocumentStatus.Approved
        };

        NodeConfiguration parsed = NodeConfiguration.Parse(original.ToJson());

        Assert.Equal(original.TaskTitle, parsed.TaskTitle);
        Assert.Equal(original.DueInHours, parsed.DueInHours);
        Assert.Equal(original.Priority, parsed.Priority);
        Assert.Equal(AssignmentStrategy.Role, parsed.Assignment?.Strategy);
        Assert.Equal("FinanceApprover", parsed.Assignment?.Role);
        Assert.Equal(Domain.Documents.DocumentStatus.Approved, parsed.DocumentStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_configuration_parses_to_the_empty_one(string? json)
    {
        Assert.Same(NodeConfiguration.Empty, NodeConfiguration.Parse(json));
    }

    [Fact]
    public void Variables_survive_a_json_round_trip_and_stay_case_insensitive()
    {
        Dictionary<string, string?> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["totalAmount"] = "1234.56",
            ["supplier"] = "Northstar Supplies",
            ["missing"] = null
        };

        Dictionary<string, string?> parsed = WorkflowVariables.Parse(WorkflowVariables.ToJson(variables));

        Assert.Equal("1234.56", parsed["TOTALAMOUNT"]);
        Assert.Null(parsed["missing"]);
        Assert.Equal(3, parsed.Count);
    }

    [Fact]
    public void Node_keys_survive_a_json_round_trip_without_duplicates()
    {
        string json = WorkflowVariables.ToNodeKeysJson(["a", "b", "a"]);

        Assert.Equal(["a", "b"], WorkflowVariables.ParseNodeKeys(json));
    }
}
