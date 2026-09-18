using System.Text.Json;
using DocOps.Application.Abstractions;
using DocOps.Domain.Auditing;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Auditing;

/// <summary>
/// Writes audit rows on a dedicated context instance, so an audit record is never rolled back with
/// the business transaction it describes, and a failed business transaction still leaves a trace.
/// </summary>
public sealed class AuditService(
    DbContextOptions<DocOpsDbContext> contextOptions,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IDateTimeProvider clock,
    ILogger<AuditService> logger) : IAuditService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        MaxDepth = 8
    };

    private static readonly string[] SensitiveKeyFragments =
        ["password", "secret", "token", "apikey", "connectionstring"];

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        AuditEvent auditEvent = new()
        {
            OccurredAtUtc = clock.UtcNow,
            TenantId = entry.TenantIdOverride ?? currentUser.TenantId,
            ActorUserId = entry.ActorUserIdOverride ?? currentUser.UserId,
            ActorDisplayName = entry.ActorDisplayNameOverride ?? currentUser.DisplayName ?? "Sistema",
            ActorEmail = entry.ActorEmailOverride ?? currentUser.Email,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            EntityDisplayName = Truncate(entry.EntityDisplayName, 400),
            Severity = entry.Severity,
            IpAddress = requestContext.IpAddress,
            UserAgent = requestContext.UserAgent,
            CorrelationId = requestContext.CorrelationId,
            Summary = Truncate(entry.Summary, 1000),
            BeforeJson = Serialize(entry.Before),
            AfterJson = Serialize(entry.After),
            MetadataJson = Serialize(Redact(entry.Metadata))
        };

        // A dedicated context: no pending business changes are flushed by this save.
        await using DocOpsDbContext context = new(contextOptions);
        context.AuditEvents.Add(auditEvent);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Audit {Action} on {EntityType} {EntityId} by {ActorUserId} (correlation {CorrelationId})",
            auditEvent.Action, auditEvent.EntityType, auditEvent.EntityId, auditEvent.ActorUserId, auditEvent.CorrelationId);
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, SerializerOptions);

    private static IReadOnlyDictionary<string, object?>? Redact(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        Dictionary<string, object?> redacted = new(metadata.Count, StringComparer.OrdinalIgnoreCase);

        foreach ((string key, object? value) in metadata)
        {
            bool sensitive = SensitiveKeyFragments.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase));
            redacted[key] = sensitive ? "***" : value;
        }

        return redacted;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
