using System.Globalization;
using System.Text;
using DocOps.Application.Abstractions;
using DocOps.Domain.Auditing;
using DocOps.Domain.Documents;
using DocOps.Domain.Identity;
using DocOps.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates the demonstration document repository: real files in the file store, real versions, real
/// permissions and a back-dated audit trail that matches the state of each document. The data is
/// internally consistent — an invoice waiting for approval has an audit entry saying so, and a
/// document at version 3 has the history of versions 1 and 2 behind it.
/// </summary>
public sealed class DocumentDemoSeeder(
    DocOpsDbContext dbContext,
    IFileStorage fileStorage,
    IDateTimeProvider clock,
    ILogger<DocumentDemoSeeder> logger)
{
    private sealed record GrantSeed(PermissionSubjectType SubjectType, string Subject, string[] Levels);

    private sealed record DocumentSeed(
        string TypeCode,
        string Title,
        string OwnerEmail,
        string DepartmentCode,
        DocumentStatus Status,
        int DaysAgo,
        (string Code, string Value)[] Metadata,
        string[] Tags,
        string? Description = null,
        SecurityClassification? Classification = null,
        int Versions = 1,
        GrantSeed[]? Grants = null,
        string? ContentFile = null);

    private const string BusinessEntity = "Asteria Group, S.A.";

    private static readonly DocumentSeed[] Seeds =
    [
        // --------------------------------------------------------------------- Por classificar
        // Files that arrived but that nobody has read yet. Their text is realistic — laid out like
        // real invoices and contracts — which is what the AI processing screen works from.
        new(DocumentTypeCodes.Unclassified, "Documento recebido - fatura-atlas-tecnologia", "ana.ferreira@asteria.example", "PRC",
            DocumentStatus.Draft, 0, [], ["recebido"],
            Description: "Recebido por correio eletrónico da Atlas Tecnologia; tipo por identificar.",
            ContentFile: "fatura-atlas-tecnologia.txt"),

        new(DocumentTypeCodes.Unclassified, "Documento recebido - contrato-nordhavn-logistica", "carla.nunes@asteria.example", "LEG",
            DocumentStatus.Draft, 1, [], ["recebido"],
            Description: "Minuta assinada enviada pela Nordhavn Logística.",
            ContentFile: "contrato-nordhavn-logistica.txt"),

        new(DocumentTypeCodes.Unclassified, "Documento recebido - invoice-brightline-uk", "ricardo.lopes@asteria.example", "FIN",
            DocumentStatus.Draft, 0, [], ["recebido"],
            Description: "Fatura em inglês de um fornecedor do Reino Unido.",
            ContentFile: "invoice-brightline-uk.txt"),

        new(DocumentTypeCodes.Unclassified, "Documento recebido - encomenda-verdemar", "ana.ferreira@asteria.example", "PRC",
            DocumentStatus.Draft, 1, [], ["recebido"],
            ContentFile: "encomenda-verdemar.txt"),

        // ---------------------------------------------------------------------------------- Faturas
        new("SupplierInvoice", "Fatura Northstar Supplies 2026/1184", "ricardo.lopes@asteria.example", "FIN",
            DocumentStatus.PendingApproval, 3,
            [("supplier", "Northstar Supplies"), ("invoiceNumber", "2026/1184"), ("invoiceDate", "2026-09-10"),
             ("dueDate", "2026-10-10"), ("netAmount", "12450.00"), ("vatAmount", "2863.50"),
             ("totalAmount", "15313.50"), ("currency", "EUR"), ("purchaseOrder", "ENC-2026-00031")],
            ["fatura", "aguarda-aprovação", "fornecedor"],
            Description: "Fornecimento de material de escritório e consumíveis para o terceiro trimestre.",
            Grants: [new GrantSeed(PermissionSubjectType.User, "contacto@northstar.example", ["view", "download"])]),

        new("SupplierInvoice", "Fatura Ibérica Logística 2026/0442", "ricardo.lopes@asteria.example", "FIN",
            DocumentStatus.PendingApproval, 6,
            [("supplier", "Ibérica Logística"), ("invoiceNumber", "2026/0442"), ("invoiceDate", "2026-09-05"),
             ("dueDate", "2026-10-05"), ("netAmount", "8200.00"), ("vatAmount", "1886.00"),
             ("totalAmount", "10086.00"), ("currency", "EUR"), ("purchaseOrder", "ENC-2026-00028")],
            ["fatura", "aguarda-aprovação", "logística"]),

        new("SupplierInvoice", "Fatura Atlas Tecnologia 2026/0771", "ana.ferreira@asteria.example", "PRC",
            DocumentStatus.PendingApproval, 2,
            [("supplier", "Atlas Tecnologia"), ("invoiceNumber", "2026/0771"), ("invoiceDate", "2026-09-12"),
             ("dueDate", "2026-10-12"), ("netAmount", "24800.00"), ("vatAmount", "5704.00"),
             ("totalAmount", "30504.00"), ("currency", "EUR")],
            ["fatura", "aguarda-aprovação", "equipamento"],
            Description: "Renovação de licenças de software de gestão documental.", Versions: 2),

        new("SupplierInvoice", "Fatura Verdemar Serviços 2026/0219", "ricardo.lopes@asteria.example", "FIN",
            DocumentStatus.Approved, 18,
            [("supplier", "Verdemar Serviços"), ("invoiceNumber", "2026/0219"), ("invoiceDate", "2026-08-20"),
             ("dueDate", "2026-09-19"), ("netAmount", "3150.00"), ("vatAmount", "724.50"),
             ("totalAmount", "3874.50"), ("currency", "EUR")],
            ["fatura", "aprovado", "manutenção"]),

        new("SupplierInvoice", "Fatura Lusitânia Papelaria 2026/0090", "ricardo.lopes@asteria.example", "FIN",
            DocumentStatus.Approved, 26,
            [("supplier", "Lusitânia Papelaria"), ("invoiceNumber", "2026/0090"), ("invoiceDate", "2026-08-12"),
             ("dueDate", "2026-09-11"), ("netAmount", "740.00"), ("vatAmount", "170.20"),
             ("totalAmount", "910.20"), ("currency", "EUR")],
            ["fatura", "aprovado"]),

        new("SupplierInvoice", "Fatura Nordvik Systems INV-8842", "ricardo.lopes@asteria.example", "FIN",
            DocumentStatus.Archived, 96,
            [("supplier", "Nordvik Systems"), ("invoiceNumber", "INV-8842"), ("invoiceDate", "2026-06-02"),
             ("dueDate", "2026-07-02"), ("netAmount", "18900.00"), ("vatAmount", "4347.00"),
             ("totalAmount", "23247.00"), ("currency", "EUR"), ("purchaseOrder", "ENC-2026-00012")],
            ["fatura", "arquivado", "pago"]),

        new("SupplierInvoice", "Fatura Atlas Tecnologia 2026/0655 (valores incorretos)", "ana.ferreira@asteria.example", "PRC",
            DocumentStatus.Rejected, 34,
            [("supplier", "Atlas Tecnologia"), ("invoiceNumber", "2026/0655"), ("invoiceDate", "2026-08-04"),
             ("totalAmount", "9990.00"), ("currency", "EUR")],
            ["fatura", "rejeitado", "divergência"],
            Description: "Rejeitada: o valor não corresponde à encomenda associada."),

        new("SupplierInvoice", "Fatura Ibérica Logística 2026/0388", "ricardo.lopes@asteria.example", "FIN",
            DocumentStatus.Archived, 120,
            [("supplier", "Ibérica Logística"), ("invoiceNumber", "2026/0388"), ("invoiceDate", "2026-05-18"),
             ("dueDate", "2026-06-17"), ("netAmount", "6400.00"), ("vatAmount", "1472.00"),
             ("totalAmount", "7872.00"), ("currency", "EUR")],
            ["fatura", "arquivado", "pago"]),

        // -------------------------------------------------------------------------------- Contratos
        new("Contract", "Contrato de fornecimento - Northstar Supplies", "carla.nunes@asteria.example", "LEG",
            DocumentStatus.Approved, 210,
            [("counterparty", "Northstar Supplies"), ("contractType", "Fornecimento"),
             ("startDate", "2026-01-01"), ("endDate", "2026-12-31"), ("contractValue", "180000.00"),
             ("noticeDays", "60"), ("governingLaw", "Lei portuguesa")],
            ["contrato", "fornecedor", "renovação"],
            Description: "Contrato anual de fornecimento de consumíveis, com renovação automática.",
            Versions: 3,
            Grants:
            [
                new GrantSeed(PermissionSubjectType.Department, "PRC", ["view", "download"]),
                new GrantSeed(PermissionSubjectType.User, "sofia.martins@asteria.example", ["view", "download", "edit"])
            ]),

        new("Contract", "Contrato de prestação de serviços - Atlas Tecnologia", "carla.nunes@asteria.example", "LEG",
            DocumentStatus.SignatureRequired, 12,
            [("counterparty", "Atlas Tecnologia"), ("contractType", "Prestação de serviços"),
             ("startDate", "2026-10-01"), ("endDate", "2027-09-30"), ("contractValue", "96000.00"),
             ("noticeDays", "90"), ("governingLaw", "Lei portuguesa")],
            ["contrato", "assinatura", "tecnologia"],
            Description: "Suporte e manutenção da plataforma de gestão documental.", Versions: 2),

        new("Contract", "Acordo de confidencialidade - Verdemar Serviços", "carla.nunes@asteria.example", "LEG",
            DocumentStatus.Signed, 64,
            [("counterparty", "Verdemar Serviços"), ("contractType", "Confidencialidade"),
             ("startDate", "2026-07-15"), ("endDate", "2029-07-14"), ("noticeDays", "30"),
             ("governingLaw", "Lei portuguesa")],
            ["contrato", "confidencialidade", "assinado"]),

        new("Contract", "Contrato de arrendamento - Armazém de Alverca", "admin@asteria.example", "MGT",
            DocumentStatus.Approved, 300,
            [("counterparty", "Predial Tejo, Lda."), ("contractType", "Arrendamento"),
             ("startDate", "2025-12-01"), ("endDate", "2030-11-30"), ("contractValue", "420000.00"),
             ("noticeDays", "180"), ("governingLaw", "Lei portuguesa")],
            ["contrato", "imóvel"],
            Classification: SecurityClassification.Restricted),

        new("Contract", "Contrato de parceria - Nordvik Systems", "carla.nunes@asteria.example", "LEG",
            DocumentStatus.InReview, 8,
            [("counterparty", "Nordvik Systems"), ("contractType", "Parceria"),
             ("startDate", "2026-11-01"), ("endDate", "2027-10-31"), ("contractValue", "0.00"),
             ("noticeDays", "60"), ("governingLaw", "Lei sueca")],
            ["contrato", "revisão-jurídica", "internacional"],
            Description: "Parceria tecnológica para integração de sistemas. Em revisão jurídica."),

        new("Contract", "Contrato de fornecimento - Lusitânia Papelaria (expirado)", "carla.nunes@asteria.example", "LEG",
            DocumentStatus.Archived, 400,
            [("counterparty", "Lusitânia Papelaria"), ("contractType", "Fornecimento"),
             ("startDate", "2024-09-01"), ("endDate", "2025-08-31"), ("contractValue", "45000.00"),
             ("noticeDays", "30"), ("governingLaw", "Lei portuguesa")],
            ["contrato", "arquivado", "expirado"]),

        // ------------------------------------------------------------------------------- Encomendas
        new("PurchaseOrder", "Encomenda ENC-2026-00031 - Northstar Supplies", "ana.ferreira@asteria.example", "PRC",
            DocumentStatus.Approved, 40,
            [("supplier", "Northstar Supplies"), ("orderNumber", "ENC-2026-00031"), ("orderDate", "2026-08-08"),
             ("totalAmount", "15313.50"), ("deliveryDate", "2026-09-01")],
            ["encomenda", "aprovado"]),

        new("PurchaseOrder", "Encomenda ENC-2026-00028 - Ibérica Logística", "ana.ferreira@asteria.example", "PRC",
            DocumentStatus.Approved, 48,
            [("supplier", "Ibérica Logística"), ("orderNumber", "ENC-2026-00028"), ("orderDate", "2026-08-01"),
             ("totalAmount", "10086.00"), ("deliveryDate", "2026-08-28")],
            ["encomenda", "aprovado", "logística"]),

        new("PurchaseOrder", "Encomenda ENC-2026-00045 - Atlas Tecnologia", "ana.ferreira@asteria.example", "PRC",
            DocumentStatus.PendingApproval, 4,
            [("supplier", "Atlas Tecnologia"), ("orderNumber", "ENC-2026-00045"), ("orderDate", "2026-09-11"),
             ("totalAmount", "30504.00"), ("deliveryDate", "2026-10-15")],
            ["encomenda", "aguarda-aprovação", "equipamento"]),

        new("PurchaseOrder", "Encomenda ENC-2026-00046 - Verdemar Serviços", "ana.ferreira@asteria.example", "PRC",
            DocumentStatus.Draft, 1,
            [("supplier", "Verdemar Serviços"), ("orderNumber", "ENC-2026-00046"), ("orderDate", "2026-09-17"),
             ("totalAmount", "2480.00"), ("deliveryDate", "2026-10-02")],
            ["encomenda", "rascunho"]),

        new("PurchaseOrder", "Encomenda ENC-2026-00012 - Nordvik Systems", "ana.ferreira@asteria.example", "PRC",
            DocumentStatus.Archived, 130,
            [("supplier", "Nordvik Systems"), ("orderNumber", "ENC-2026-00012"), ("orderDate", "2026-05-10"),
             ("totalAmount", "23247.00"), ("deliveryDate", "2026-06-01")],
            ["encomenda", "arquivado"]),

        // ----------------------------------------------------------------------- Documentos de RH
        new("EmployeeDocument", "Contrato de trabalho - Marta Oliveira", "joao.pereira@asteria.example", "HR",
            DocumentStatus.Signed, 75,
            [("employeeName", "Marta Oliveira"), ("documentKind", "Contrato de trabalho"), ("effectiveDate", "2026-07-01")],
            ["rh", "contratação", "assinado"]),

        new("EmployeeDocument", "Plano de integração - Marta Oliveira", "joao.pereira@asteria.example", "HR",
            DocumentStatus.Approved, 74,
            [("employeeName", "Marta Oliveira"), ("documentKind", "Onboarding"), ("effectiveDate", "2026-07-01")],
            ["rh", "onboarding"]),

        new("EmployeeDocument", "Avaliação de desempenho 2026 - Ana Ferreira", "joao.pereira@asteria.example", "HR",
            DocumentStatus.InReview, 20,
            [("employeeName", "Ana Ferreira"), ("documentKind", "Avaliação de desempenho"), ("effectiveDate", "2026-08-31")],
            ["rh", "desempenho"], Versions: 2),

        new("EmployeeDocument", "Registo de formação - Segurança da informação", "joao.pereira@asteria.example", "HR",
            DocumentStatus.Approved, 55,
            [("employeeName", "Equipa de Operações"), ("documentKind", "Formação"), ("effectiveDate", "2026-07-22")],
            ["rh", "formação", "segurança"]),

        new("EmployeeDocument", "Processo de cessação - Bruno Carvalho", "joao.pereira@asteria.example", "HR",
            DocumentStatus.Archived, 160,
            [("employeeName", "Bruno Carvalho"), ("documentKind", "Cessação"), ("effectiveDate", "2026-04-30")],
            ["rh", "arquivado"]),

        // -------------------------------------------------------------------- Comunicações internas
        new("InternalMemo", "Política de teletrabalho 2026", "admin@asteria.example", "MGT",
            DocumentStatus.Approved, 90,
            [("subject", "Política de teletrabalho"), ("audience", "Toda a organização")],
            ["comunicação", "política"],
            Classification: SecurityClassification.Public),

        new("InternalMemo", "Encerramento contabilístico do terceiro trimestre", "ricardo.lopes@asteria.example", "FIN",
            DocumentStatus.Approved, 14,
            [("subject", "Encerramento do 3.º trimestre"), ("audience", "Departamento")],
            ["comunicação", "financeiro"]),

        new("InternalMemo", "Novo circuito de aprovação de faturas", "sofia.martins@asteria.example", "OPS",
            DocumentStatus.Draft, 2,
            [("subject", "Circuito de aprovação de faturas"), ("audience", "Direção")],
            ["comunicação", "processos", "rascunho"],
            Description: "Proposta de revisão do circuito de aprovação com limiares por valor."),

        new("InternalMemo", "Plano de continuidade de negócio", "admin@asteria.example", "MGT",
            DocumentStatus.Approved, 150,
            [("subject", "Continuidade de negócio"), ("audience", "Direção")],
            ["comunicação", "continuidade"],
            Classification: SecurityClassification.Restricted),

        // --------------------------------------------------------------------------------- E-mails
        new("Email", "Reclamação de entrega - Ibérica Logística", "sofia.martins@asteria.example", "OPS",
            DocumentStatus.Approved, 22,
            [("fromAddress", "operacoes@iberica-logistica.example"), ("subject", "Atraso na entrega da encomenda ENC-2026-00028"),
             ("receivedAt", "2026-08-27")],
            ["email", "incidente", "logística"]),

        new("Email", "Proposta comercial - Nordvik Systems", "sofia.martins@asteria.example", "OPS",
            DocumentStatus.InReview, 9,
            [("fromAddress", "sales@nordvik.example"), ("subject", "Proposta de integração de sistemas"),
             ("receivedAt", "2026-09-09")],
            ["email", "proposta"]),

        new("Email", "Confirmação de pagamento - Verdemar Serviços", "ricardo.lopes@asteria.example", "FIN",
            DocumentStatus.Archived, 30,
            [("fromAddress", "financeiro@verdemar.example"), ("subject", "Confirmação de pagamento da fatura 2026/0219"),
             ("receivedAt", "2026-08-19")],
            ["email", "pagamento", "arquivado"]),

        // ------------------------------------------------------------------------ Pedidos internos
        new("RequestForm", "Pedido de acesso ao arquivo financeiro", "ana.ferreira@asteria.example", "PRC",
            DocumentStatus.PendingApproval, 5,
            [("requester", "Ana Ferreira"), ("requestType", "Acesso"), ("neededBy", "2026-09-25")],
            ["pedido", "aguarda-aprovação", "acesso"]),

        new("RequestForm", "Pedido de equipamento - portátil de substituição", "joao.pereira@asteria.example", "HR",
            DocumentStatus.Approved, 28,
            [("requester", "João Pereira"), ("requestType", "Equipamento"), ("neededBy", "2026-09-05")],
            ["pedido", "equipamento"]),

        new("RequestForm", "Pedido de formação - Gestão documental avançada", "ana.ferreira@asteria.example", "PRC",
            DocumentStatus.Draft, 1,
            [("requester", "Ana Ferreira"), ("requestType", "Formação"), ("neededBy", "2026-11-01")],
            ["pedido", "formação", "rascunho"]),

        // ------------------------------------------------------------------------------ Relatórios
        new("Report", "Relatório de tesouraria - agosto de 2026", "ricardo.lopes@asteria.example", "FIN",
            DocumentStatus.Approved, 16,
            [("period", "Agosto de 2026"), ("author", "Ricardo Lopes")],
            ["relatório", "financeiro"], Versions: 2),

        new("Report", "Relatório de operações - 3.º trimestre de 2026", "sofia.martins@asteria.example", "OPS",
            DocumentStatus.InReview, 7,
            [("period", "3.º trimestre de 2026"), ("author", "Sofia Martins")],
            ["relatório", "operações"]),

        new("Report", "Relatório de conformidade documental - 1.º semestre", "admin@asteria.example", "MGT",
            DocumentStatus.Approved, 80,
            [("period", "1.º semestre de 2026"), ("author", "Helena Vasconcelos")],
            ["relatório", "conformidade"],
            Grants: [new GrantSeed(PermissionSubjectType.Role, RoleNames.Manager, ["view", "download"])])
    ];

    public async Task SeedAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        bool alreadySeeded = await dbContext.Documents
            .IgnoreQueryFilters()
            .AnyAsync(d => d.TenantId == tenant.Id, cancellationToken);

        if (alreadySeeded)
        {
            return;
        }

        Dictionary<string, DocumentType> types = await dbContext.DocumentTypes
            .IgnoreQueryFilters()
            .Include(t => t.MetadataFields)
            .Where(t => t.TenantId == tenant.Id)
            .ToDictionaryAsync(t => t.Code, cancellationToken);

        Dictionary<string, Guid> departments = await dbContext.Departments
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id)
            .ToDictionaryAsync(d => d.Code, d => d.Id, cancellationToken);

        Dictionary<string, (Guid Id, string Name)> users = await dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenant.Id && u.Email != null)
            .ToDictionaryAsync(u => u.Email!, u => (u.Id, u.FirstName + " " + u.LastName), cancellationToken);

        Dictionary<string, Tag> tags = await dbContext.Tags
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenant.Id)
            .ToDictionaryAsync(t => t.Name, cancellationToken);

        Dictionary<string, int> sequences = [];
        List<AuditEvent> auditTrail = [];
        int created = 0;

        foreach (DocumentSeed seed in Seeds)
        {
            if (!types.TryGetValue(seed.TypeCode, out DocumentType? type)
                || !users.TryGetValue(seed.OwnerEmail, out (Guid Id, string Name) owner))
            {
                logger.LogWarning("Skipping demo document '{Title}': type or owner missing", seed.Title);
                continue;
            }

            DateTimeOffset createdAt = clock.UtcNow.AddDays(-seed.DaysAgo).AddHours(-2);
            sequences.TryGetValue(type.ReferencePrefix, out int sequence);
            sequences[type.ReferencePrefix] = ++sequence;

            Document document = new()
            {
                TenantId = tenant.Id,
                Title = seed.Title,
                Reference = $"{type.ReferencePrefix}-{createdAt.Year}-{sequence:D5}",
                Summary = seed.Description,
                DocumentTypeId = type.Id,
                DepartmentId = departments.TryGetValue(seed.DepartmentCode, out Guid departmentId) ? departmentId : null,
                BusinessEntity = BusinessEntity,
                Classification = seed.Classification ?? type.DefaultClassification,
                OwnerUserId = owner.Id,
                Status = seed.Status,
                RetentionPolicyId = type.DefaultRetentionPolicyId,
                CreatedAtUtc = createdAt,
                CreatedByUserId = owner.Id
            };

            dbContext.Documents.Add(document);
            created++;

            await AddVersionsAsync(document, type, seed, owner, createdAt, auditTrail, cancellationToken);
            AddMetadata(document, type, seed);
            AddTags(document, tenant, seed, tags, owner.Id, createdAt);
            AddGrants(document, seed, users, departments, owner.Id, createdAt);
            AddLifecycleAudit(document, seed, owner, createdAt, auditTrail);

            if (seed.Status == DocumentStatus.Archived)
            {
                document.PreArchiveStatus = DocumentStatus.Approved;
                document.ArchivedAtUtc = createdAt.AddDays(Math.Min(seed.DaysAgo, 30));
                document.RetentionDueAtUtc = document.ArchivedAtUtc?.AddMonths(120);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.AuditEvents.AddRange(auditTrail);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {DocumentCount} demo documents and {AuditCount} audit entries for tenant {Tenant}",
            created, auditTrail.Count, tenant.Name);
    }

    private async Task AddVersionsAsync(
        Document document,
        DocumentType type,
        DocumentSeed seed,
        (Guid Id, string Name) owner,
        DateTimeOffset createdAt,
        List<AuditEvent> auditTrail,
        CancellationToken cancellationToken)
    {
        for (int number = 1; number <= seed.Versions; number++)
        {
            bool isCurrent = number == seed.Versions;
            DateTimeOffset versionDate = createdAt.AddDays(number - 1);

            string content = seed.ContentFile is { } sample
                ? SampleDocuments.Read(sample)
                : BuildFileContent(document, type, seed, number);
            string fileName = seed.ContentFile ?? $"{document.Reference}-v{number}.txt";
            byte[] bytes = Encoding.UTF8.GetBytes(content);

            await using MemoryStream stream = new(bytes);

            StoredFile stored = await fileStorage.SaveAsync(
                stream,
                document.TenantId,
                document.Id,
                new FileDescriptor(fileName, "text/plain"),
                cancellationToken);

            DocumentVersion version = new()
            {
                TenantId = document.TenantId,
                DocumentId = document.Id,
                VersionNumber = number,
                StorageKey = stored.StorageKey,
                FileName = fileName,
                ContentType = "text/plain",
                FileSizeBytes = stored.SizeBytes,
                ContentHash = stored.ContentHash,
                Comment = number == 1 ? "Versão inicial" : $"Revisão {number}",
                ExtractedText = content,
                IsCurrent = isCurrent,
                CreatedAtUtc = versionDate,
                CreatedByUserId = owner.Id
            };

            document.Versions.Add(version);

            if (isCurrent)
            {
                document.CurrentVersionNumber = number;
                document.CurrentVersionId = version.Id;
                document.ModifiedAtUtc = seed.Versions > 1 ? versionDate : null;
            }

            if (number > 1)
            {
                auditTrail.Add(BuildAudit(
                    document, owner, versionDate, AuditActions.DocumentVersionCreated, $"Versão {number} criada"));
            }
        }
    }

    private static void AddMetadata(Document document, DocumentType type, DocumentSeed seed)
    {
        foreach ((string code, string value) in seed.Metadata)
        {
            DocumentMetadataField? field = type.MetadataFields
                .FirstOrDefault(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase));

            if (field is null)
            {
                continue;
            }

            DocumentMetadataValue metadataValue = new()
            {
                TenantId = document.TenantId,
                DocumentId = document.Id,
                FieldId = field.Id,
                CreatedAtUtc = document.CreatedAtUtc
            };

            MetadataValueBinder.Assign(field, metadataValue, value);
            document.MetadataValues.Add(metadataValue);
        }
    }

    private void AddTags(
        Document document,
        Tenant tenant,
        DocumentSeed seed,
        Dictionary<string, Tag> tags,
        Guid ownerId,
        DateTimeOffset createdAt)
    {
        foreach (string name in seed.Tags)
        {
            if (!tags.TryGetValue(name, out Tag? tag))
            {
                tag = new Tag { TenantId = tenant.Id, Name = name, CreatedAtUtc = createdAt };
                dbContext.Tags.Add(tag);
                tags[name] = tag;
            }

            document.Tags.Add(new DocumentTag
            {
                DocumentId = document.Id,
                TagId = tag.Id,
                Tag = tag,
                AssignedAtUtc = createdAt,
                AssignedByUserId = ownerId
            });
        }
    }

    private static void AddGrants(
        Document document,
        DocumentSeed seed,
        IReadOnlyDictionary<string, (Guid Id, string Name)> users,
        IReadOnlyDictionary<string, Guid> departments,
        Guid grantedBy,
        DateTimeOffset createdAt)
    {
        foreach (GrantSeed grant in seed.Grants ?? [])
        {
            (Guid? subjectId, string? roleName, string displayName) = grant.SubjectType switch
            {
                PermissionSubjectType.User when users.TryGetValue(grant.Subject, out (Guid Id, string Name) user) =>
                    ((Guid?)user.Id, (string?)null, user.Name),

                PermissionSubjectType.Department when departments.TryGetValue(grant.Subject, out Guid departmentId) =>
                    (departmentId, null, grant.Subject),

                PermissionSubjectType.Role => (null, grant.Subject, grant.Subject),

                _ => (null, null, string.Empty)
            };

            if (displayName.Length == 0)
            {
                continue;
            }

            // Mirrors the service rule: every grant carries at least View.
            DocumentAccessLevel level = DocumentAccessLevel.View;

            foreach (string name in grant.Levels)
            {
                level |= name switch
                {
                    "download" => DocumentAccessLevel.Download,
                    "edit" => DocumentAccessLevel.Edit,
                    "delete" => DocumentAccessLevel.Delete,
                    "share" => DocumentAccessLevel.Share,
                    "approve" => DocumentAccessLevel.Approve,
                    "administer" => DocumentAccessLevel.Administer,
                    _ => DocumentAccessLevel.View
                };
            }

            document.Permissions.Add(new DocumentPermission
            {
                TenantId = document.TenantId,
                DocumentId = document.Id,
                SubjectType = grant.SubjectType,
                SubjectId = subjectId,
                RoleName = roleName,
                SubjectDisplayName = displayName,
                AccessLevel = level,
                GrantedByUserId = grantedBy,
                GrantedAtUtc = createdAt,
                CreatedAtUtc = createdAt
            });
        }
    }

    /// <summary>
    /// Writes the history that explains the document's current state: created, then the transitions
    /// that must have happened for it to be where it is.
    /// </summary>
    private static void AddLifecycleAudit(
        Document document,
        DocumentSeed seed,
        (Guid Id, string Name) owner,
        DateTimeOffset createdAt,
        List<AuditEvent> auditTrail)
    {
        auditTrail.Add(BuildAudit(
            document, owner, createdAt, AuditActions.DocumentCreated, $"Documento criado por {owner.Name}"));

        DocumentStatus[] chain = seed.Status switch
        {
            DocumentStatus.Draft => [],
            DocumentStatus.InReview => [DocumentStatus.InReview],
            DocumentStatus.PendingApproval => [DocumentStatus.InReview, DocumentStatus.PendingApproval],
            DocumentStatus.Approved => [DocumentStatus.InReview, DocumentStatus.PendingApproval, DocumentStatus.Approved],
            DocumentStatus.Rejected => [DocumentStatus.InReview, DocumentStatus.PendingApproval, DocumentStatus.Rejected],
            DocumentStatus.SignatureRequired =>
                [DocumentStatus.InReview, DocumentStatus.PendingApproval, DocumentStatus.Approved, DocumentStatus.SignatureRequired],
            DocumentStatus.Signed =>
            [
                DocumentStatus.InReview, DocumentStatus.PendingApproval, DocumentStatus.Approved,
                DocumentStatus.SignatureRequired, DocumentStatus.Signed
            ],
            DocumentStatus.Archived =>
            [
                DocumentStatus.InReview, DocumentStatus.PendingApproval, DocumentStatus.Approved, DocumentStatus.Archived
            ],
            _ => []
        };

        DocumentStatus previous = DocumentStatus.Draft;
        int step = 1;

        foreach (DocumentStatus status in chain)
        {
            DateTimeOffset occurredAt = createdAt.AddHours(step * 6);

            auditTrail.Add(BuildAudit(
                document,
                owner,
                occurredAt,
                status == DocumentStatus.Archived ? AuditActions.DocumentArchived : AuditActions.DocumentStatusChanged,
                $"Estado alterado de '{previous}' para '{status}'"));

            previous = status;
            step++;
        }
    }

    private static AuditEvent BuildAudit(
        Document document, (Guid Id, string Name) actor, DateTimeOffset occurredAt, string action, string summary) =>
        new()
        {
            OccurredAtUtc = occurredAt,
            TenantId = document.TenantId,
            ActorUserId = actor.Id,
            ActorDisplayName = actor.Name,
            Action = action,
            EntityType = AuditEntityTypes.Document,
            EntityId = document.Id,
            EntityDisplayName = $"{document.Reference} · {document.Title}",
            Severity = AuditSeverity.Information,
            CorrelationId = "seed-" + document.Id.ToString("N")[..12],
            Summary = summary
        };

    private static string BuildFileContent(Document document, DocumentType type, DocumentSeed seed, int version)
    {
        StringBuilder builder = new();

        builder.AppendLine(CultureInfo.InvariantCulture, $"{type.Name.ToUpperInvariant()}");
        builder.AppendLine(new string('=', type.Name.Length));
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Referência interna : {document.Reference}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Título             : {document.Title}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Entidade           : {BusinessEntity}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Versão             : {version}");
        builder.AppendLine();
        builder.AppendLine("DADOS DO DOCUMENTO");
        builder.AppendLine("------------------");

        foreach ((string code, string value) in seed.Metadata)
        {
            DocumentMetadataField? field = type.MetadataFields
                .FirstOrDefault(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase));

            builder.AppendLine(CultureInfo.InvariantCulture, $"{field?.Label ?? code,-20}: {value}");
        }

        if (seed.Description is { Length: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine("OBSERVAÇÕES");
            builder.AppendLine("-----------");
            builder.AppendLine(seed.Description);
        }

        builder.AppendLine();
        builder.AppendLine("Documento fictício, gerado para demonstração do DocOps Control Tower.");

        return builder.ToString();
    }
}
