using System.Text.Json;
using DocOps.Application.Documents;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Documents;

/// <summary>Reference data for the document forms and filters, always scoped to the caller's tenant.</summary>
public sealed class DocumentCatalogue(DocOpsDbContext dbContext) : IDocumentCatalogue
{
    public async Task<IReadOnlyList<DocumentTypeDto>> GetTypesAsync(
        bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var types = await dbContext.DocumentTypes
            .Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id,
                t.Code,
                t.Name,
                t.Description,
                t.Icon,
                t.ReferencePrefix,
                t.DefaultClassification,
                t.IsActive,
                Fields = t.MetadataFields
                    .OrderBy(f => f.DisplayOrder)
                    .Select(f => new
                    {
                        f.Id,
                        f.Code,
                        f.Label,
                        f.FieldType,
                        f.IsRequired,
                        f.DisplayOrder,
                        f.HelpText,
                        f.OptionsJson
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. types.Select(t => new DocumentTypeDto(
                t.Id,
                t.Code,
                t.Name,
                t.Description,
                t.Icon,
                t.ReferencePrefix,
                t.DefaultClassification,
                t.IsActive,
                [
                    .. t.Fields.Select(f => new DocumentMetadataFieldDto(
                        f.Id,
                        f.Code,
                        f.Label,
                        f.FieldType,
                        f.IsRequired,
                        f.DisplayOrder,
                        f.HelpText,
                        ParseOptions(f.OptionsJson)))
                ]))
        ];
    }

    public async Task<IReadOnlyList<TagDto>> GetTagsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Tags
            .OrderBy(t => t.Name)
            .Select(t => new TagDto(t.Id, t.Name, t.Colour))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RetentionPolicyDto>> GetRetentionPoliciesAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.RetentionPolicies
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new RetentionPolicyDto(
                p.Id, p.Code, p.Name, p.Description, p.LegalBasis, p.RetentionMonths, p.Trigger.ToString()))
            .ToListAsync(cancellationToken);

    private static IReadOnlyList<string> ParseOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(optionsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
