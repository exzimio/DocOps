using System.Text.Json;
using DocOps.Domain.Compliance;
using DocOps.Domain.Documents;
using DocOps.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates the retention policies, document types and metadata schema for a tenant. Idempotent: it
/// adds what is missing and leaves anything an administrator has since changed untouched.
/// </summary>
public sealed class DocumentCatalogueSeeder(DocOpsDbContext dbContext)
{
    private sealed record FieldSeed(
        string Code,
        string Label,
        MetadataFieldType Type,
        bool Required = false,
        string[]? Options = null,
        string? HelpText = null);

    private sealed record TypeSeed(
        string Code,
        string Name,
        string Prefix,
        string Icon,
        SecurityClassification Classification,
        string RetentionCode,
        string Description,
        FieldSeed[] Fields);

    private sealed record PolicySeed(
        string Code, string Name, int Months, RetentionTrigger Trigger, string LegalBasis, string Description);

    private static readonly PolicySeed[] Policies =
    [
        new("FIN-10A", "Documentos fiscais — 10 anos", 120, RetentionTrigger.OnArchive,
            "Obrigação de conservação de documentos fiscais",
            "Faturas, notas de crédito e documentos de suporte contabilístico"),
        new("CTR-10A", "Contratos — 10 anos após término", 120, RetentionTrigger.OnArchive,
            "Prazo geral de prescrição contratual",
            "Contratos e adendas, contados a partir do termo da vigência"),
        new("RH-5A", "Recursos humanos — 5 anos", 60, RetentionTrigger.OnArchive,
            "Conservação de registos de pessoal",
            "Documentos de colaboradores após cessação do vínculo"),
        new("GER-3A", "Documentação corrente — 3 anos", 36, RetentionTrigger.OnArchive,
            "Política interna de arquivo",
            "Comunicações internas, pedidos e relatórios operacionais")
    ];

    private static readonly TypeSeed[] Types =
    [
        // The intake type. No required fields, so a file can be stored before anyone has read it;
        // sensitive by default, because until it is read nobody knows what it contains.
        new(DocumentTypeCodes.Unclassified, "Por classificar", "DOC", "inbox", SecurityClassification.Confidential, "GER-3A",
            "Documentos recebidos cujo tipo ainda não foi identificado",
            []),

        new("SupplierInvoice", "Fatura de fornecedor", "FAT", "receipt", SecurityClassification.Confidential, "FIN-10A",
            "Faturas recebidas de fornecedores, sujeitas a validação e aprovação financeira",
            [
                new("supplier", "Fornecedor", MetadataFieldType.Text, Required: true),
                new("invoiceNumber", "Número da fatura", MetadataFieldType.Text, Required: true),
                new("invoiceDate", "Data da fatura", MetadataFieldType.Date, Required: true),
                new("dueDate", "Data de vencimento", MetadataFieldType.Date),
                new("netAmount", "Valor sem IVA", MetadataFieldType.Currency),
                new("vatAmount", "IVA", MetadataFieldType.Currency),
                new("totalAmount", "Total", MetadataFieldType.Currency, Required: true),
                new("currency", "Moeda", MetadataFieldType.Select, Options: ["EUR", "USD", "GBP"]),
                new("purchaseOrder", "Encomenda associada", MetadataFieldType.Text,
                    HelpText: "Referência da encomenda que originou a fatura")
            ]),

        new("Contract", "Contrato", "CTR", "file-signature", SecurityClassification.Confidential, "CTR-10A",
            "Contratos com clientes, fornecedores e parceiros",
            [
                new("counterparty", "Contraparte", MetadataFieldType.Text, Required: true),
                new("contractType", "Tipo de contrato", MetadataFieldType.Select, Required: true,
                    Options: ["Prestação de serviços", "Fornecimento", "Confidencialidade", "Arrendamento", "Parceria"]),
                new("startDate", "Início de vigência", MetadataFieldType.Date, Required: true),
                new("endDate", "Fim de vigência", MetadataFieldType.Date),
                new("contractValue", "Valor contratual", MetadataFieldType.Currency),
                new("noticeDays", "Pré-aviso de renovação (dias)", MetadataFieldType.Number),
                new("governingLaw", "Lei aplicável", MetadataFieldType.Text)
            ]),

        new("PurchaseOrder", "Encomenda", "ENC", "shopping-cart", SecurityClassification.Internal, "FIN-10A",
            "Encomendas emitidas a fornecedores",
            [
                new("supplier", "Fornecedor", MetadataFieldType.Text, Required: true),
                new("orderNumber", "Número da encomenda", MetadataFieldType.Text, Required: true),
                new("orderDate", "Data da encomenda", MetadataFieldType.Date, Required: true),
                new("totalAmount", "Total", MetadataFieldType.Currency),
                new("deliveryDate", "Data de entrega prevista", MetadataFieldType.Date)
            ]),

        new("EmployeeDocument", "Documento de colaborador", "RHU", "user-round", SecurityClassification.Restricted, "RH-5A",
            "Documentação individual de colaboradores",
            [
                new("employeeName", "Colaborador", MetadataFieldType.Text, Required: true),
                new("documentKind", "Natureza", MetadataFieldType.Select, Required: true,
                    Options: ["Contrato de trabalho", "Avaliação de desempenho", "Formação", "Onboarding", "Cessação"]),
                new("effectiveDate", "Data de efeito", MetadataFieldType.Date, Required: true)
            ]),

        new("InternalMemo", "Comunicação interna", "COM", "megaphone", SecurityClassification.Internal, "GER-3A",
            "Comunicações e memorandos internos",
            [
                new("subject", "Assunto", MetadataFieldType.Text, Required: true),
                new("audience", "Destinatários", MetadataFieldType.Select,
                    Options: ["Toda a organização", "Direção", "Departamento", "Equipa"])
            ]),

        new("Email", "Mensagem de correio eletrónico", "EML", "mail", SecurityClassification.Internal, "GER-3A",
            "Mensagens arquivadas a partir da caixa de entrada",
            [
                new("fromAddress", "Remetente", MetadataFieldType.Text, Required: true),
                new("subject", "Assunto", MetadataFieldType.Text, Required: true),
                new("receivedAt", "Data de receção", MetadataFieldType.Date)
            ]),

        new("RequestForm", "Pedido interno", "PED", "clipboard-list", SecurityClassification.Internal, "GER-3A",
            "Pedidos submetidos por colaboradores",
            [
                new("requester", "Requerente", MetadataFieldType.Text, Required: true),
                new("requestType", "Tipo de pedido", MetadataFieldType.Select, Required: true,
                    Options: ["Compra", "Acesso", "Formação", "Deslocação", "Equipamento"]),
                new("neededBy", "Necessário até", MetadataFieldType.Date)
            ]),

        new("Report", "Relatório", "REL", "chart-column", SecurityClassification.Internal, "GER-3A",
            "Relatórios operacionais e de gestão",
            [
                new("period", "Período", MetadataFieldType.Text, Required: true),
                new("author", "Autor", MetadataFieldType.Text)
            ])
    ];

    public async Task SeedAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        Dictionary<string, RetentionPolicy> policies = await SeedPoliciesAsync(tenant, cancellationToken);
        await SeedTypesAsync(tenant, policies, cancellationToken);
    }

    private async Task<Dictionary<string, RetentionPolicy>> SeedPoliciesAsync(
        Tenant tenant, CancellationToken cancellationToken)
    {
        Dictionary<string, RetentionPolicy> existing = await dbContext.RetentionPolicies
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenant.Id)
            .ToDictionaryAsync(p => p.Code, cancellationToken);

        foreach (PolicySeed seed in Policies.Where(p => !existing.ContainsKey(p.Code)))
        {
            RetentionPolicy policy = new()
            {
                TenantId = tenant.Id,
                Code = seed.Code,
                Name = seed.Name,
                Description = seed.Description,
                LegalBasis = seed.LegalBasis,
                RetentionMonths = seed.Months,
                Trigger = seed.Trigger,
                IsActive = true
            };

            dbContext.RetentionPolicies.Add(policy);
            existing[seed.Code] = policy;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    private async Task SeedTypesAsync(
        Tenant tenant, IReadOnlyDictionary<string, RetentionPolicy> policies, CancellationToken cancellationToken)
    {
        HashSet<string> existingCodes =
        [
            .. await dbContext.DocumentTypes
                .IgnoreQueryFilters()
                .Where(t => t.TenantId == tenant.Id)
                .Select(t => t.Code)
                .ToListAsync(cancellationToken)
        ];

        foreach (TypeSeed seed in Types.Where(t => !existingCodes.Contains(t.Code)))
        {
            DocumentType type = new()
            {
                TenantId = tenant.Id,
                Code = seed.Code,
                Name = seed.Name,
                Description = seed.Description,
                Icon = seed.Icon,
                ReferencePrefix = seed.Prefix,
                DefaultClassification = seed.Classification,
                DefaultRetentionPolicyId = policies.TryGetValue(seed.RetentionCode, out RetentionPolicy? policy)
                    ? policy.Id
                    : null,
                IsActive = true
            };

            int order = 1;

            foreach (FieldSeed field in seed.Fields)
            {
                type.MetadataFields.Add(new DocumentMetadataField
                {
                    TenantId = tenant.Id,
                    Code = field.Code,
                    Label = field.Label,
                    FieldType = field.Type,
                    IsRequired = field.Required,
                    DisplayOrder = order++,
                    HelpText = field.HelpText,
                    OptionsJson = field.Options is { Length: > 0 } ? JsonSerializer.Serialize(field.Options) : null
                });
            }

            dbContext.DocumentTypes.Add(type);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
