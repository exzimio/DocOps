using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocOps.Domain.Documents;
using DocOps.Domain.Integrations;

namespace DocOps.Domain.Workflows;

public sealed record AssignmentConfiguration
{
    public AssignmentStrategy Strategy { get; init; } = AssignmentStrategy.Role;

    /// <summary>Used when <see cref="Strategy"/> is <see cref="AssignmentStrategy.User"/>.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Department code, e.g. "FIN". Codes travel better between environments than identifiers.</summary>
    public string? DepartmentCode { get; init; }

    public string? Role { get; init; }
}

public sealed record ConditionConfiguration
{
    /// <summary>Instance variable to test, e.g. "totalAmount".</summary>
    public required string Field { get; init; }

    public ConditionOperator Operator { get; init; } = ConditionOperator.Equals;
    public string? Value { get; init; }
}

public sealed record NotificationConfiguration
{
    public required string Title { get; init; }
    public string? Body { get; init; }
    public AssignmentConfiguration? Recipients { get; init; }
}

/// <summary>
/// Typed view of a node's <see cref="WorkflowNode.ConfigJson"/>. Every field is optional: which ones
/// matter is decided by the node type, and each executor reads only what it needs.
/// </summary>
public sealed record NodeConfiguration
{
    public static readonly NodeConfiguration Empty = new();

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public string? TaskTitle { get; init; }
    public string? TaskDescription { get; init; }
    public AssignmentConfiguration? Assignment { get; init; }
    public int? DueInHours { get; init; }
    public int? EscalateAfterHours { get; init; }
    public WorkflowTaskPriority? Priority { get; init; }

    public ConditionConfiguration? Condition { get; init; }
    public NotificationConfiguration? Notification { get; init; }

    /// <summary>Hours a delay node parks the instance for.</summary>
    public int? DelayHours { get; init; }

    /// <summary>Status applied to the linked document when the node runs. Null leaves the document alone.</summary>
    public DocumentStatus? DocumentStatus { get; init; }

    /// <summary>Which integration an integration step runs. Required by that node type.</summary>
    public IntegrationKind? IntegrationKind { get; init; }

    /// <summary>
    /// For a signature step: who is asked to sign. Reuses the same assignment shape as a human task,
    /// because "who does this" is the same question — but only a single named person can sign, so a
    /// department or role is resolved to one user when the step runs.
    /// </summary>
    public AssignmentConfiguration? Signer { get; init; }

    /// <summary>Shown to the signer before they decide.</summary>
    public string? SignatureReason { get; init; }

    /// <summary>Outcome recorded on the instance when an end node is reached.</summary>
    public string? Outcome { get; init; }

    /// <summary>True when reaching this end node means the process ended in rejection.</summary>
    public bool EndsAsRejected { get; init; }

    public static NodeConfiguration Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        return JsonSerializer.Deserialize<NodeConfiguration>(json, SerializerOptions) ?? Empty;
    }

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);
}

/// <summary>Instance variables: a flat string map, so they survive JSON round-trips without surprises.</summary>
public static class WorkflowVariables
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public const string Decision = "decision";
    public const string DocumentReference = "documentReference";
    public const string DocumentTitle = "documentTitle";
    public const string DocumentType = "documentType";
    public const string DepartmentCode = "departmentCode";

    public static Dictionary<string, string?> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, string?>? parsed =
            JsonSerializer.Deserialize<Dictionary<string, string?>>(json, SerializerOptions);

        return parsed is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(parsed, StringComparer.OrdinalIgnoreCase);
    }

    public static string ToJson(IReadOnlyDictionary<string, string?> variables) =>
        JsonSerializer.Serialize(variables, SerializerOptions);

    public static string[] ParseNodeKeys(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<string[]>(json, SerializerOptions) ?? [];
    }

    public static string ToNodeKeysJson(IEnumerable<string> keys) =>
        JsonSerializer.Serialize(keys.Distinct(StringComparer.Ordinal).ToArray(), SerializerOptions);

    /// <summary>
    /// Each timer node keeps its own moment, under <c>timer:</c> and its key. A single moment per
    /// instance would be shared by two timers on parallel branches, and the first to elapse would
    /// clear it for both.
    /// </summary>
    private const string TimerPrefix = "timer:";

    public static DateTimeOffset? TimerFor(IReadOnlyDictionary<string, string?> variables, string nodeKey) =>
        variables.TryGetValue(TimerPrefix + nodeKey, out string? raw)
        && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset at)
            ? at
            : null;

    public static void SetTimer(IDictionary<string, string?> variables, string nodeKey, DateTimeOffset at) =>
        variables[TimerPrefix + nodeKey] = at.ToString("O", CultureInfo.InvariantCulture);

    public static void ClearTimer(IDictionary<string, string?> variables, string nodeKey) =>
        variables.Remove(TimerPrefix + nodeKey);

    public static void ClearTimers(IDictionary<string, string?> variables)
    {
        foreach (string key in variables.Keys.Where(key => key.StartsWith(TimerPrefix, StringComparison.Ordinal)).ToList())
        {
            variables.Remove(key);
        }
    }

    /// <summary>The earliest pending timer, which is when the worker has to look at the instance again.</summary>
    public static DateTimeOffset? EarliestTimer(IReadOnlyDictionary<string, string?> variables) =>
        variables.Keys
            .Where(key => key.StartsWith(TimerPrefix, StringComparison.Ordinal))
            .Select(key => TimerFor(variables, key[TimerPrefix.Length..]))
            .Where(at => at is not null)
            .Min();
}

/// <summary>
/// Evaluates a condition node against the instance variables.
///
/// Deliberately not an expression language: a closed set of operators over a named variable is
/// enough for the routing rules a business user designs, and it cannot be turned into a way to
/// execute arbitrary code from stored data.
/// </summary>
public static class ConditionEvaluator
{
    public static bool Evaluate(ConditionConfiguration condition, IReadOnlyDictionary<string, string?> variables)
    {
        variables.TryGetValue(condition.Field, out string? raw);

        return condition.Operator switch
        {
            ConditionOperator.IsEmpty => string.IsNullOrWhiteSpace(raw),
            ConditionOperator.IsNotEmpty => !string.IsNullOrWhiteSpace(raw),
            ConditionOperator.Equals => string.Equals(raw?.Trim(), condition.Value?.Trim(), StringComparison.OrdinalIgnoreCase),
            ConditionOperator.NotEquals => !string.Equals(raw?.Trim(), condition.Value?.Trim(), StringComparison.OrdinalIgnoreCase),
            ConditionOperator.Contains => raw is not null
                && condition.Value is not null
                && raw.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            _ => CompareNumbers(condition.Operator, raw, condition.Value)
        };
    }

    private static bool CompareNumbers(ConditionOperator op, string? left, string? right)
    {
        if (!TryParse(left, out decimal leftValue) || !TryParse(right, out decimal rightValue))
        {
            // A numeric comparison against something that is not a number is false, not an error:
            // a missing invoice total must route down the "no" branch, not break the process.
            return false;
        }

        return op switch
        {
            ConditionOperator.GreaterThan => leftValue > rightValue,
            ConditionOperator.GreaterThanOrEqual => leftValue >= rightValue,
            ConditionOperator.LessThan => leftValue < rightValue,
            ConditionOperator.LessThanOrEqual => leftValue <= rightValue,
            _ => false
        };
    }

    private static bool TryParse(string? value, out decimal parsed) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed);
}
