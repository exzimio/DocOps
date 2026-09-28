using System.Text.Json;
using DocOps.Application.Abstractions;
using DocOps.Application.Administration;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Compliance;
using DocOps.Domain.Documents;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Administration;

/// <summary>
/// Administration of the document catalogue: the types and the metadata schema of each one.
///
/// This is not a table editor. A type and its fields are a promise already kept: documents exist
/// under them, classified, filled in and filtered by them. So every change is asked one question
/// before it is applied — <b>does it make something that is already written false?</b>
///
/// Widening is allowed: a new optional field, a relabelled one, another option in a list. Narrowing
/// is refused, and the refusal says how many documents it would have affected, because «this cannot
/// change» is not an answer and «31 documents hold a value in it» is.
/// </summary>
public sealed class DocumentCatalogueAdminService(
    DocOpsDbContext dbContext,
    IAuditService auditService,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IDocumentCatalogueAdminService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AdminDocumentTypeDto>> GetTypesAsync(
        bool includeInactive, CancellationToken cancellationToken = default) =>
        await dbContext.DocumentTypes
            .Where(type => includeInactive || type.IsActive)
            .OrderBy(type => type.Name)
            .Select(type => new AdminDocumentTypeDto
            {
                Id = type.Id,
                Code = type.Code,
                Name = type.Name,
                Description = type.Description,
                Icon = type.Icon,
                ReferencePrefix = type.ReferencePrefix,
                DefaultClassification = type.DefaultClassification,
                DefaultRetentionPolicyId = type.DefaultRetentionPolicyId,
                DefaultRetentionPolicyName = type.DefaultRetentionPolicy!.Name,
                IsActive = type.IsActive,
                FieldCount = dbContext.DocumentMetadataFields.Count(f => f.DocumentTypeId == type.Id),
                DocumentCount = dbContext.Documents.Count(d => d.DocumentTypeId == type.Id),
                IsIntakeType = type.Code == DocumentTypeCodes.Unclassified
            })
            .ToListAsync(cancellationToken);

    public async Task<AdminDocumentTypeDto> CreateTypeAsync(
        SaveDocumentTypeRequest request, CancellationToken cancellationToken = default)
    {
        string code = request.Code.Trim();
        string prefix = request.ReferencePrefix.Trim().ToUpperInvariant();

        await EnsureCodeIsFreeAsync(code, prefix, null, cancellationToken);
        await EnsureRetentionPolicyBelongsAsync(request.DefaultRetentionPolicyId, cancellationToken);

        DocumentType type = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId(),
            Code = code,
            Name = request.Name.Trim(),
            Description = Clean(request.Description),
            Icon = Clean(request.Icon),
            ReferencePrefix = prefix,
            DefaultClassification = request.DefaultClassification,
            DefaultRetentionPolicyId = request.DefaultRetentionPolicyId,
            CreatedAtUtc = clock.UtcNow
        };

        dbContext.DocumentTypes.Add(type);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditTypeAsync(AuditActions.DocumentTypeCreated, type, cancellationToken);

        return await SingleTypeAsync(type.Id, cancellationToken);
    }

    public async Task<AdminDocumentTypeDto> UpdateTypeAsync(
        Guid typeId, SaveDocumentTypeRequest request, CancellationToken cancellationToken = default)
    {
        DocumentType type = await LoadTypeAsync(typeId, cancellationToken);

        string code = request.Code.Trim();
        string prefix = request.ReferencePrefix.Trim().ToUpperInvariant();

        // The intake type is referred to by code from inside the product — the upload path, the
        // classifier and the seeder all look it up that way. Renaming it in the interface is
        // harmless; changing its code detaches it from the code that depends on it.
        if (type.Code == DocumentTypeCodes.Unclassified && code != type.Code)
        {
            throw new DomainException(
                "O código do tipo de entrada não se altera: é por ele que o carregamento de ficheiros o encontra.",
                "intake_type_code_fixed");
        }

        await EnsureCodeIsFreeAsync(code, prefix, typeId, cancellationToken);
        await EnsureRetentionPolicyBelongsAsync(request.DefaultRetentionPolicyId, cancellationToken);

        type.Code = code;
        type.Name = request.Name.Trim();
        type.Description = Clean(request.Description);
        type.Icon = Clean(request.Icon);
        type.ReferencePrefix = prefix;
        type.DefaultClassification = request.DefaultClassification;
        type.DefaultRetentionPolicyId = request.DefaultRetentionPolicyId;

        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditTypeAsync(AuditActions.DocumentTypeUpdated, type, cancellationToken);

        return await SingleTypeAsync(typeId, cancellationToken);
    }

    public async Task<AdminDocumentTypeDto> SetTypeActiveAsync(
        Guid typeId, bool isActive, CancellationToken cancellationToken = default)
    {
        DocumentType type = await LoadTypeAsync(typeId, cancellationToken);

        // Deactivating the intake type closes the door every file comes through — including the
        // ones that arrive by e-mail and through the portal, where nobody is watching to notice.
        if (!isActive && type.Code == DocumentTypeCodes.Unclassified)
        {
            throw new ForbiddenOperationException(
                "O tipo de entrada não se desativa: é por ele que entra um ficheiro antes de alguém saber o que é.");
        }

        type.IsActive = isActive;

        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditTypeAsync(AuditActions.DocumentTypeActivationChanged, type, cancellationToken);

        return await SingleTypeAsync(typeId, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminMetadataFieldDto>> GetFieldsAsync(
        Guid typeId, CancellationToken cancellationToken = default)
    {
        await LoadTypeAsync(typeId, cancellationToken);

        var rows = await dbContext.DocumentMetadataFields
            .Where(field => field.DocumentTypeId == typeId)
            .OrderBy(field => field.DisplayOrder).ThenBy(field => field.Label)
            .Select(field => new
            {
                Field = field,
                ValueCount = dbContext.DocumentMetadataValues.Count(value => value.FieldId == field.Id)
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => Present(row.Field, row.ValueCount))];
    }

    public async Task<AdminMetadataFieldDto> CreateFieldAsync(
        Guid typeId, SaveMetadataFieldRequest request, CancellationToken cancellationToken = default)
    {
        DocumentType type = await LoadTypeAsync(typeId, cancellationToken);
        string code = request.Code.Trim();

        if (await dbContext.DocumentMetadataFields
                .AnyAsync(f => f.DocumentTypeId == typeId && f.Code == code, cancellationToken))
        {
            throw new DomainException("Já existe um campo com este código neste tipo.", "field_code_used");
        }

        EnsureIntakeTypeStaysOptional(type, request.IsRequired);

        if (request.IsRequired)
        {
            await EnsureNothingBecomesInvalidAsync(typeId, null, cancellationToken);
        }

        DocumentMetadataField field = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId(),
            DocumentTypeId = typeId,
            Code = code,
            Label = request.Label.Trim(),
            FieldType = request.FieldType,
            IsRequired = request.IsRequired,
            DisplayOrder = request.DisplayOrder,
            HelpText = Clean(request.HelpText),
            OptionsJson = SerialiseOptions(request)
        };

        dbContext.DocumentMetadataFields.Add(field);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditFieldAsync(AuditActions.MetadataFieldCreated, type, field, cancellationToken);

        return Present(field, 0);
    }

    public async Task<AdminMetadataFieldDto> UpdateFieldAsync(
        Guid typeId, Guid fieldId, SaveMetadataFieldRequest request, CancellationToken cancellationToken = default)
    {
        DocumentType type = await LoadTypeAsync(typeId, cancellationToken);
        DocumentMetadataField field = await LoadFieldAsync(typeId, fieldId, cancellationToken);

        string code = request.Code.Trim();

        if (await dbContext.DocumentMetadataFields
                .AnyAsync(f => f.DocumentTypeId == typeId && f.Code == code && f.Id != fieldId, cancellationToken))
        {
            throw new DomainException("Já existe um campo com este código neste tipo.", "field_code_used");
        }

        int valueCount = await dbContext.DocumentMetadataValues
            .CountAsync(value => value.FieldId == fieldId, cancellationToken);

        // A value lives in the typed column of its kind. Changing the kind does not move it: the
        // number stays in NumberValue and the field starts reading TextValue, so the document goes
        // silently blank — or, worse, shows something that was never written there.
        if (request.FieldType != field.FieldType && valueCount > 0)
        {
            throw new DomainException(
                $"Este campo já tem valor em {valueCount} documento(s) e por isso não muda de tipo. "
                + "O que lá está foi guardado na coluna do tipo atual e deixaria de ser legível.",
                "field_type_locked");
        }

        EnsureIntakeTypeStaysOptional(type, request.IsRequired);

        // Making a field required is a claim about documents that already exist. If any of them is
        // missing the value, the claim is false the moment it is saved.
        if (request.IsRequired && !field.IsRequired)
        {
            await EnsureNothingBecomesInvalidAsync(typeId, fieldId, cancellationToken);
        }

        field.Code = code;
        field.Label = request.Label.Trim();
        field.FieldType = request.FieldType;
        field.IsRequired = request.IsRequired;
        field.DisplayOrder = request.DisplayOrder;
        field.HelpText = Clean(request.HelpText);
        field.OptionsJson = SerialiseOptions(request);

        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditFieldAsync(AuditActions.MetadataFieldUpdated, type, field, cancellationToken);

        return Present(field, valueCount);
    }

    public async Task DeleteFieldAsync(Guid typeId, Guid fieldId, CancellationToken cancellationToken = default)
    {
        DocumentType type = await LoadTypeAsync(typeId, cancellationToken);
        DocumentMetadataField field = await LoadFieldAsync(typeId, fieldId, cancellationToken);

        int valueCount = await dbContext.DocumentMetadataValues
            .CountAsync(value => value.FieldId == fieldId, cancellationToken);

        // Deleting the definition would delete what documents say through it. Unlike an account or
        // a type, a field has nowhere to be deactivated to — so the honest answer is to refuse and
        // say what would have been lost.
        if (valueCount > 0)
        {
            throw new DomainException(
                $"Este campo tem valor em {valueCount} documento(s). Eliminá-lo apagaria o que esses documentos dizem.",
                "field_in_use");
        }

        dbContext.DocumentMetadataFields.Remove(field);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditFieldAsync(AuditActions.MetadataFieldDeleted, type, field, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminRetentionPolicyDto>> GetRetentionPoliciesAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.RetentionPolicies
            .Where(policy => policy.IsActive)
            .OrderBy(policy => policy.Name)
            .Select(policy => new AdminRetentionPolicyDto(
                policy.Id, policy.Code, policy.Name, policy.RetentionMonths, policy.Trigger))
            .ToListAsync(cancellationToken);

    // ---------------------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------------------

    private Guid TenantId() =>
        currentUser.TenantId ?? throw new DomainException("Sessão sem organização.", "tenant_missing");

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<DocumentType> LoadTypeAsync(Guid typeId, CancellationToken cancellationToken) =>
        await dbContext.DocumentTypes.FirstOrDefaultAsync(type => type.Id == typeId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(DocumentType), typeId);

    private async Task<DocumentMetadataField> LoadFieldAsync(
        Guid typeId, Guid fieldId, CancellationToken cancellationToken) =>
        await dbContext.DocumentMetadataFields
            .FirstOrDefaultAsync(f => f.Id == fieldId && f.DocumentTypeId == typeId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(DocumentMetadataField), fieldId);

    private async Task<AdminDocumentTypeDto> SingleTypeAsync(Guid typeId, CancellationToken cancellationToken) =>
        (await GetTypesAsync(includeInactive: true, cancellationToken)).Single(type => type.Id == typeId);

    private async Task EnsureCodeIsFreeAsync(
        string code, string prefix, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (await dbContext.DocumentTypes.AnyAsync(t => t.Code == code && t.Id != exceptId, cancellationToken))
        {
            throw new DomainException("Já existe um tipo de documento com este código.", "type_code_used");
        }

        // Two types sharing a prefix produce two documents with the same reference, and the
        // reference is what people quote to each other.
        if (await dbContext.DocumentTypes
                .AnyAsync(t => t.ReferencePrefix == prefix && t.Id != exceptId, cancellationToken))
        {
            throw new DomainException(
                "Já existe um tipo com este prefixo de referência: dois tipos com o mesmo prefixo geram referências iguais.",
                "type_prefix_used");
        }
    }

    private async Task EnsureRetentionPolicyBelongsAsync(Guid? policyId, CancellationToken cancellationToken)
    {
        if (policyId is null)
        {
            return;
        }

        if (!await dbContext.RetentionPolicies.AnyAsync(policy => policy.Id == policyId, cancellationToken))
        {
            throw new EntityNotFoundException(nameof(RetentionPolicy), policyId.Value);
        }
    }

    /// <summary>
    /// The intake type is where a file lands before anyone knows what it is. A required field there
    /// would mean refusing the upload until someone answers a question that only reading the
    /// document can answer — which is the order this product deliberately does not impose.
    /// </summary>
    private static void EnsureIntakeTypeStaysOptional(DocumentType type, bool isRequired)
    {
        if (isRequired && type.Code == DocumentTypeCodes.Unclassified)
        {
            throw new DomainException(
                "O tipo de entrada não aceita campos obrigatórios: um ficheiro entra antes de alguém o ter lido.",
                "intake_type_requires_nothing");
        }
    }

    /// <summary>
    /// Counts the documents of a type that would fail a newly required field, and refuses if any
    /// exist. <paramref name="fieldId"/> is null when the field is being created — then every
    /// document of the type is missing it, by definition.
    /// </summary>
    private async Task EnsureNothingBecomesInvalidAsync(
        Guid typeId, Guid? fieldId, CancellationToken cancellationToken)
    {
        int missing = await dbContext.Documents
            .Where(document => document.DocumentTypeId == typeId)
            .CountAsync(
                document => fieldId == null || !dbContext.DocumentMetadataValues.Any(value =>
                    value.DocumentId == document.Id
                    && value.FieldId == fieldId
                    && (value.TextValue != null
                        || value.NumberValue != null
                        || value.DateValue != null
                        || value.BooleanValue != null)),
                cancellationToken);

        if (missing > 0)
        {
            throw new DomainException(
                $"{missing} documento(s) deste tipo ficariam inválidos: não têm valor neste campo. "
                + "Preencha-os antes de tornar o campo obrigatório.",
                "field_would_invalidate_documents");
        }
    }

    private static string? SerialiseOptions(SaveMetadataFieldRequest request)
    {
        if (request.FieldType != MetadataFieldType.Select)
        {
            return null;
        }

        string[] options = [.. request.Options
            .Select(option => option.Trim())
            .Where(option => option.Length > 0)
            .Distinct(StringComparer.Ordinal)];

        // A list to choose from with nothing in it is a field nobody can fill.
        if (options.Length == 0)
        {
            throw new DomainException(
                "Um campo de escolha precisa de pelo menos uma opção.", "select_field_needs_options");
        }

        return JsonSerializer.Serialize(options, Json);
    }

    private static AdminMetadataFieldDto Present(DocumentMetadataField field, int valueCount) =>
        new()
        {
            Id = field.Id,
            DocumentTypeId = field.DocumentTypeId,
            Code = field.Code,
            Label = field.Label,
            FieldType = field.FieldType,
            IsRequired = field.IsRequired,
            DisplayOrder = field.DisplayOrder,
            HelpText = field.HelpText,
            Options = ReadOptions(field.OptionsJson),
            ValueCount = valueCount,
            CanChangeType = valueCount == 0,
            CanDelete = valueCount == 0
        };

    private static IReadOnlyList<string> ReadOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(optionsJson, Json) ?? [];
        }
        catch (JsonException)
        {
            // Written by an older shape of this feature, or by hand. An unreadable list of options
            // is not a reason to fail the whole screen.
            return [];
        }
    }

    private Task AuditTypeAsync(string action, DocumentType type, CancellationToken cancellationToken) =>
        auditService.WriteAsync(
            new AuditEntry
            {
                Action = action,
                EntityType = AuditEntityTypes.DocumentType,
                EntityId = type.Id,
                EntityDisplayName = type.Name,
                Summary = $"Tipo de documento {type.Code}.",
                Metadata = new Dictionary<string, object?> { ["isActive"] = type.IsActive }
            },
            cancellationToken);

    private Task AuditFieldAsync(
        string action, DocumentType type, DocumentMetadataField field, CancellationToken cancellationToken) =>
        auditService.WriteAsync(
            new AuditEntry
            {
                Action = action,
                EntityType = AuditEntityTypes.MetadataField,
                EntityId = field.Id,
                EntityDisplayName = field.Label,
                Summary = $"Campo {field.Code} do tipo {type.Code}.",
                Metadata = new Dictionary<string, object?>
                {
                    ["documentTypeId"] = type.Id,
                    ["fieldType"] = field.FieldType.ToString(),
                    ["isRequired"] = field.IsRequired
                }
            },
            cancellationToken);
}
