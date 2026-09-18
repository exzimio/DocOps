using System.Globalization;
using System.Text;
using DocOps.Application.Abstractions;
using DocOps.Application.Workflows;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Workflows;

/// <summary>
/// The writing half of workflow design: creating, saving, duplicating and removing definitions.
///
/// The rule that shapes everything here is that a running process must keep the graph it started
/// with. A definition nobody has used yet is edited in place; the moment it has instances, saving it
/// produces a **new version** and retires the old one, which stays in the database precisely so its
/// instances still resolve their nodes.
/// </summary>
public sealed class WorkflowDesignService(
    DocOpsDbContext dbContext,
    IAuditService auditService,
    ITenantContext tenantContext,
    ILogger<WorkflowDesignService> logger)
{
    public async Task<WorkflowDefinition> CreateAsync(
        CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId;
        string code = await GenerateCodeAsync(tenantId, request.Name, cancellationToken);

        WorkflowDefinition definition = new()
        {
            TenantId = tenantId,
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            DocumentTypeId = request.DocumentTypeId,
            Trigger = WorkflowTriggerType.Manual,
            Version = 1,
            IsActive = false
        };

        // A new process starts as the smallest thing that can be drawn and understood: a beginning
        // and an end, already connected.
        definition.Nodes.Add(new WorkflowNode
        {
            TenantId = tenantId,
            Key = "start",
            Name = "Início",
            Type = WorkflowNodeType.Start,
            PositionX = 0,
            PositionY = 160
        });

        definition.Nodes.Add(new WorkflowNode
        {
            TenantId = tenantId,
            Key = "end",
            Name = "Fim",
            Type = WorkflowNodeType.End,
            PositionX = 360,
            PositionY = 160,
            ConfigJson = new NodeConfiguration { Outcome = "Concluído" }.ToJson()
        });

        definition.Edges.Add(new WorkflowEdge
        {
            TenantId = tenantId,
            FromNodeKey = "start",
            ToNodeKey = "end"
        });

        dbContext.WorkflowDefinitions.Add(definition);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.WorkflowDefinitionCreated,
                EntityType = AuditEntityTypes.WorkflowDefinition,
                EntityId = definition.Id,
                EntityDisplayName = definition.Name,
                Summary = "Workflow criado",
                After = new { definition.Code, definition.Name, definition.Version }
            },
            cancellationToken);

        return definition;
    }

    public async Task<WorkflowDefinition> SaveAsync(
        Guid definitionId, SaveWorkflowRequest request, CancellationToken cancellationToken)
    {
        WorkflowDefinition definition = await LoadAsync(definitionId, cancellationToken);

        EnsureLatestVersion(definition, await GetLatestVersionAsync(definition, cancellationToken));
        ValidateGraphShape(request);

        bool hasInstances = await dbContext.WorkflowInstances
            .AnyAsync(i => i.WorkflowDefinitionId == definition.Id, cancellationToken);

        // Optimistic concurrency against the version the designer was looking at. The row is marked
        // modified even when only its nodes changed: nodes and edges live in their own tables, so
        // without this a save built on a stale read would rewrite the graph without ever being checked.
        dbContext.Entry(definition).Property(d => d.RowVersion).OriginalValue =
            DecodeToken(request.ConcurrencyToken);
        dbContext.Entry(definition).State = EntityState.Modified;

        WorkflowDefinition target = hasInstances
            ? await SupersedeAsync(definition, cancellationToken)
            : definition;

        target.Name = request.Name.Trim();
        target.Description = request.Description?.Trim();
        target.DocumentTypeId = request.DocumentTypeId;
        target.Trigger = request.Trigger;

        ReplaceGraph(target, request);

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.WorkflowDefinitionUpdated,
                EntityType = AuditEntityTypes.WorkflowDefinition,
                EntityId = target.Id,
                EntityDisplayName = target.Name,
                Summary = hasInstances
                    ? $"Nova versão {target.Version} criada; a versão anterior mantém-se para os processos em curso"
                    : "Desenho do workflow guardado",
                After = new
                {
                    target.Version,
                    Nodes = target.Nodes.Count,
                    Edges = target.Edges.Count,
                    SupersededPrevious = hasInstances
                }
            },
            cancellationToken);

        logger.LogInformation(
            "Saved workflow {Code} version {Version} ({NodeCount} nodes)", target.Code, target.Version, target.Nodes.Count);

        return target;
    }

    public async Task<WorkflowDefinition> DuplicateAsync(
        Guid definitionId, DuplicateWorkflowRequest request, CancellationToken cancellationToken)
    {
        WorkflowDefinition source = await LoadAsync(definitionId, cancellationToken);
        Guid tenantId = tenantContext.TenantId;

        WorkflowDefinition copy = new()
        {
            TenantId = tenantId,
            Code = await GenerateCodeAsync(tenantId, request.Name, cancellationToken),
            Name = request.Name.Trim(),
            Description = source.Description,
            DocumentTypeId = source.DocumentTypeId,
            Trigger = source.Trigger,
            Version = 1,

            // A copy always starts inactive: duplicating a process is not a decision to run it.
            IsActive = false
        };

        foreach (WorkflowNode node in source.Nodes)
        {
            copy.Nodes.Add(new WorkflowNode
            {
                TenantId = tenantId,
                Key = node.Key,
                Name = node.Name,
                Description = node.Description,
                Type = node.Type,
                ConfigJson = node.ConfigJson,
                PositionX = node.PositionX,
                PositionY = node.PositionY
            });
        }

        foreach (WorkflowEdge edge in source.Edges)
        {
            copy.Edges.Add(new WorkflowEdge
            {
                TenantId = tenantId,
                FromNodeKey = edge.FromNodeKey,
                ToNodeKey = edge.ToNodeKey,
                Label = edge.Label,
                Branch = edge.Branch
            });
        }

        dbContext.WorkflowDefinitions.Add(copy);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.WorkflowDefinitionCreated,
                EntityType = AuditEntityTypes.WorkflowDefinition,
                EntityId = copy.Id,
                EntityDisplayName = copy.Name,
                Summary = $"Duplicado a partir de '{source.Name}'",
                After = new { copy.Code, copy.Name, SourceCode = source.Code }
            },
            cancellationToken);

        return copy;
    }

    public async Task DeleteAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        WorkflowDefinition definition = await LoadAsync(definitionId, cancellationToken);

        bool hasRunning = await dbContext.WorkflowInstances.AnyAsync(
            i => i.WorkflowDefinitionId == definition.Id
                && (i.Status == WorkflowInstanceStatus.Running || i.Status == WorkflowInstanceStatus.Waiting),
            cancellationToken);

        if (hasRunning)
        {
            throw new DomainException(
                "Existem processos em curso com este workflow. Cancele-os ou aguarde a conclusão.",
                "workflow_has_running_instances");
        }

        definition.IsActive = false;
        dbContext.WorkflowDefinitions.Remove(definition);

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.WorkflowDefinitionUpdated,
                EntityType = AuditEntityTypes.WorkflowDefinition,
                EntityId = definition.Id,
                EntityDisplayName = definition.Name,
                Severity = AuditSeverity.Warning,
                Summary = "Workflow eliminado (eliminação reversível)"
            },
            cancellationToken);
    }

    /// <summary>Whether this row is the newest version of its code, and whether it can be edited in place.</summary>
    public async Task<(bool IsLatest, bool EditableInPlace)> GetEditStateAsync(
        WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        int latest = await GetLatestVersionAsync(definition, cancellationToken);

        bool hasInstances = await dbContext.WorkflowInstances
            .AnyAsync(i => i.WorkflowDefinitionId == definition.Id, cancellationToken);

        return (definition.Version >= latest, !hasInstances);
    }

    // -----------------------------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Copies the definition into a new version row and retires the current one. The old row is kept
    /// — not archived, not deleted — because its running instances still look their nodes up in it.
    /// </summary>
    private async Task<WorkflowDefinition> SupersedeAsync(
        WorkflowDefinition current, CancellationToken cancellationToken)
    {
        int nextVersion = await GetLatestVersionAsync(current, cancellationToken) + 1;

        WorkflowDefinition next = new()
        {
            TenantId = current.TenantId,
            Code = current.Code,
            Name = current.Name,
            Description = current.Description,
            DocumentTypeId = current.DocumentTypeId,
            Trigger = current.Trigger,
            Version = nextVersion,
            IsActive = current.IsActive
        };

        // Only one version of a code is active at a time: the newest one takes over.
        current.IsActive = false;

        dbContext.WorkflowDefinitions.Add(next);

        return next;
    }

    private void ReplaceGraph(WorkflowDefinition definition, SaveWorkflowRequest request)
    {
        foreach (WorkflowNode node in definition.Nodes.ToList())
        {
            dbContext.WorkflowNodes.Remove(node);
        }

        foreach (WorkflowEdge edge in definition.Edges.ToList())
        {
            dbContext.WorkflowEdges.Remove(edge);
        }

        definition.Nodes.Clear();
        definition.Edges.Clear();

        foreach (SaveWorkflowNodeRequest node in request.Nodes)
        {
            definition.Nodes.Add(new WorkflowNode
            {
                TenantId = definition.TenantId,
                Key = node.Key.Trim(),
                Name = node.Name.Trim(),
                Description = node.Description?.Trim(),
                Type = node.Type,
                ConfigJson = NormaliseConfig(node.ConfigJson),
                PositionX = node.PositionX,
                PositionY = node.PositionY
            });
        }

        foreach (SaveWorkflowEdgeRequest edge in request.Edges)
        {
            definition.Edges.Add(new WorkflowEdge
            {
                TenantId = definition.TenantId,
                FromNodeKey = edge.FromNodeKey.Trim(),
                ToNodeKey = edge.ToNodeKey.Trim(),
                Label = edge.Label?.Trim(),
                Branch = edge.Branch?.Trim()
            });
        }
    }

    /// <summary>
    /// Structural checks that must hold before anything is written. Design-quality problems — an
    /// unreachable node, a missing branch — are reported by the validator and do not block saving:
    /// a half-drawn process is a normal state while designing.
    /// </summary>
    private static void ValidateGraphShape(SaveWorkflowRequest request)
    {
        HashSet<string> keys = [.. request.Nodes.Select(node => node.Key.Trim())];

        foreach (SaveWorkflowEdgeRequest edge in request.Edges)
        {
            if (!keys.Contains(edge.FromNodeKey.Trim()) || !keys.Contains(edge.ToNodeKey.Trim()))
            {
                throw new DomainException(
                    $"A ligação '{edge.FromNodeKey} → {edge.ToNodeKey}' refere um nó que não existe.",
                    "edge_references_unknown_node");
            }
        }

        foreach (SaveWorkflowNodeRequest node in request.Nodes.Where(n => !string.IsNullOrWhiteSpace(n.ConfigJson)))
        {
            try
            {
                NodeConfiguration.Parse(node.ConfigJson);
            }
            catch (System.Text.Json.JsonException)
            {
                throw new DomainException(
                    $"A configuração do nó '{node.Name}' não é JSON válido.", "invalid_node_configuration");
            }
        }
    }

    private static string? NormaliseConfig(string? configJson) =>
        string.IsNullOrWhiteSpace(configJson) ? null : NodeConfiguration.Parse(configJson).ToJson();

    private static void EnsureLatestVersion(WorkflowDefinition definition, int latestVersion)
    {
        if (definition.Version < latestVersion)
        {
            throw new DomainException(
                $"Esta é a versão {definition.Version} do workflow; existe uma versão mais recente ({latestVersion}).",
                "workflow_version_superseded");
        }
    }

    private Task<int> GetLatestVersionAsync(WorkflowDefinition definition, CancellationToken cancellationToken) =>
        dbContext.WorkflowDefinitions
            .Where(d => d.TenantId == definition.TenantId && d.Code == definition.Code)
            .MaxAsync(d => d.Version, cancellationToken);

    private async Task<WorkflowDefinition> LoadAsync(Guid definitionId, CancellationToken cancellationToken) =>
        await dbContext.WorkflowDefinitions
            .Include(d => d.Nodes)
            .Include(d => d.Edges)
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(WorkflowDefinition), definitionId);

    /// <summary>Builds a stable, readable code from the name, and makes it unique within the tenant.</summary>
    private async Task<string> GenerateCodeAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        string slug = Slugify(name);

        HashSet<string> taken =
        [
            .. await dbContext.WorkflowDefinitions
                .IgnoreQueryFilters()
                .Where(d => d.TenantId == tenantId && d.Code.StartsWith(slug))
                .Select(d => d.Code)
                .ToListAsync(cancellationToken)
        ];

        if (!taken.Contains(slug))
        {
            return slug;
        }

        for (int suffix = 2; suffix < 1000; suffix++)
        {
            string candidate = $"{slug}-{suffix}";

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{slug}-{Guid.CreateVersion7().ToString("N")[..6]}";
    }

    private static string Slugify(string value)
    {
        string normalised = value.Trim().Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(normalised.Length);
        bool lastWasHyphen = false;

        foreach (char character in normalised)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && builder.Length > 0)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        string slug = builder.ToString().Trim('-');

        return slug.Length switch
        {
            0 => "workflow",
            > 50 => slug[..50].TrimEnd('-'),
            _ => slug
        };
    }

    private static byte[] DecodeToken(string token)
    {
        try
        {
            return Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            throw new DomainException("Token de concorrência inválido. Recarregue o workflow.", "invalid_concurrency_token");
        }
    }
}
