using DocOps.Domain.Documents;
using DocOps.Domain.Tenancy;
using DocOps.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates the demonstration workflow definitions. They are ordinary definitions — the same shape a
/// designer would produce — so every node they use is one the engine really executes.
/// </summary>
public sealed class WorkflowCatalogueSeeder(DocOpsDbContext dbContext)
{
    public const string InvoiceApprovalCode = "invoice-approval";
    public const string ContractReviewCode = "contract-review";
    public const string OnboardingCode = "employee-onboarding";

    private sealed record NodeSeed(
        string Key,
        string Name,
        WorkflowNodeType Type,
        double X,
        double Y,
        NodeConfiguration? Config = null,
        string? Description = null);

    private sealed record EdgeSeed(string From, string To, string? Branch = null, string? Label = null);

    private sealed record DefinitionSeed(
        string Code,
        string Name,
        string Description,
        string? DocumentTypeCode,
        NodeSeed[] Nodes,
        EdgeSeed[] Edges);

    private static DefinitionSeed[] Definitions =>
    [
        new(
            InvoiceApprovalCode,
            "Aprovação de fatura de fornecedor",
            "Validação de dados, aprovação financeira e — acima de 10 000 € — aprovação de direção.",
            "SupplierInvoice",
            [
                new("start", "Início", WorkflowNodeType.Start, 0, 190),

                new("validation", "Validar dados da fatura", WorkflowNodeType.HumanTask, 310, 190,
                    new NodeConfiguration
                    {
                        // Entering the process puts the document under review, which is also what makes
                        // a later rejection a legal transition of the document's own state machine.
                        DocumentStatus = DocumentStatus.InReview,
                        TaskTitle = "Validar dados da fatura",
                        TaskDescription = "Confirmar fornecedor, número, datas e total face à encomenda associada.",
                        Assignment = new AssignmentConfiguration
                        {
                            Strategy = AssignmentStrategy.Department,
                            DepartmentCode = "PRC"
                        },
                        DueInHours = 24,
                        Priority = WorkflowTaskPriority.Normal
                    },
                    "Conferência dos dados extraídos face ao documento original."),

                new("submitted", "Submeter a aprovação", WorkflowNodeType.Notification, 610, 190,
                    new NodeConfiguration
                    {
                        DocumentStatus = DocumentStatus.PendingApproval,
                        Notification = new NotificationConfiguration
                        {
                            Title = "Fatura submetida a aprovação",
                            Body = "Os dados foram validados e a fatura seguiu para decisão.",
                            Recipients = new AssignmentConfiguration { Strategy = AssignmentStrategy.DocumentOwner }
                        }
                    }),

                new("amount-check", "Valor acima de 10 000 €?", WorkflowNodeType.Condition, 920, 190,
                    new NodeConfiguration
                    {
                        Condition = new ConditionConfiguration
                        {
                            Field = "totalAmount",
                            Operator = ConditionOperator.GreaterThan,
                            Value = "10000"
                        }
                    },
                    "Faturas de valor elevado exigem também aprovação de direção."),

                new("management-approval", "Aprovação de direção", WorkflowNodeType.Approval, 1220, 70,
                    new NodeConfiguration
                    {
                        TaskTitle = "Aprovar fatura de valor elevado",
                        Assignment = new AssignmentConfiguration
                        {
                            Strategy = AssignmentStrategy.Role,
                            Role = "Manager"
                        },
                        DueInHours = 72,
                        EscalateAfterHours = 72,
                        Priority = WorkflowTaskPriority.High
                    }),

                new("finance-approval", "Aprovação financeira", WorkflowNodeType.Approval, 1220, 310,
                    new NodeConfiguration
                    {
                        TaskTitle = "Aprovar fatura",
                        Assignment = new AssignmentConfiguration
                        {
                            Strategy = AssignmentStrategy.Role,
                            Role = "FinanceApprover"
                        },
                        DueInHours = 48,
                        EscalateAfterHours = 48,
                        Priority = WorkflowTaskPriority.Normal
                    }),

                new("approved", "Registar aprovação", WorkflowNodeType.Notification, 1530, 190,
                    new NodeConfiguration
                    {
                        DocumentStatus = DocumentStatus.Approved,
                        Notification = new NotificationConfiguration
                        {
                            Title = "Fatura aprovada",
                            Body = "A fatura foi aprovada e segue para arquivo.",
                            Recipients = new AssignmentConfiguration { Strategy = AssignmentStrategy.DocumentOwner }
                        }
                    }),

                new("archive", "Arquivar", WorkflowNodeType.End, 1840, 190,
                    new NodeConfiguration
                    {
                        DocumentStatus = DocumentStatus.Archived,
                        Outcome = "Aprovada e arquivada"
                    }),

                new("rejected", "Rejeitada", WorkflowNodeType.End, 1530, 460,
                    new NodeConfiguration
                    {
                        DocumentStatus = DocumentStatus.Rejected,
                        Outcome = "Rejeitada",
                        EndsAsRejected = true
                    })
            ],
            [
                new("start", "validation"),
                new("validation", "submitted", "approved"),
                new("validation", "rejected", "rejected", "Dados incorretos"),
                new("submitted", "amount-check"),
                new("amount-check", "management-approval", "true", "> 10 000 €"),
                new("amount-check", "finance-approval", "false", "≤ 10 000 €"),
                new("management-approval", "approved", "approved"),
                new("management-approval", "rejected", "rejected"),
                new("finance-approval", "approved", "approved"),
                new("finance-approval", "rejected", "rejected"),
                new("approved", "archive")
            ]),

        new(
            ContractReviewCode,
            "Revisão de contrato",
            "Revisão jurídica, aprovação de direção e preparação do pedido de assinatura.",
            "Contract",
            [
                new("start", "Início", WorkflowNodeType.Start, 0, 140),

                new("legal-review", "Revisão jurídica", WorkflowNodeType.HumanTask, 310, 140,
                    new NodeConfiguration
                    {
                        DocumentStatus = DocumentStatus.InReview,
                        TaskTitle = "Rever cláusulas contratuais",
                        TaskDescription = "Verificar objeto, prazos, penalizações e lei aplicável.",
                        Assignment = new AssignmentConfiguration
                        {
                            Strategy = AssignmentStrategy.Department,
                            DepartmentCode = "LEG"
                        },
                        DueInHours = 72,
                        Priority = WorkflowTaskPriority.Normal
                    }),

                new("management-approval", "Aprovação de direção", WorkflowNodeType.Approval, 650, 140,
                    new NodeConfiguration
                    {
                        TaskTitle = "Aprovar contrato",
                        Assignment = new AssignmentConfiguration
                        {
                            Strategy = AssignmentStrategy.Role,
                            Role = "Manager"
                        },
                        DueInHours = 48,
                        EscalateAfterHours = 48,
                        Priority = WorkflowTaskPriority.High
                    }),

                new("signature-preparation", "Preparar pedido de assinatura", WorkflowNodeType.HumanTask, 990, 140,
                    new NodeConfiguration
                    {
                        TaskTitle = "Preparar pedido de assinatura",
                        TaskDescription = "Reunir signatários, ordem de assinatura e versão final do documento.",
                        Assignment = new AssignmentConfiguration { Strategy = AssignmentStrategy.DocumentOwner },
                        DueInHours = 120,
                        Priority = WorkflowTaskPriority.Normal
                    }),

                new("approved", "Registar aprovação", WorkflowNodeType.Notification, 1330, 140,
                    new NodeConfiguration
                    {
                        DocumentStatus = DocumentStatus.Approved,
                        Notification = new NotificationConfiguration
                        {
                            Title = "Contrato aprovado",
                            Body = "O contrato foi aprovado e está pronto para assinatura.",
                            Recipients = new AssignmentConfiguration { Strategy = AssignmentStrategy.DocumentOwner }
                        }
                    }),

                new("end", "Concluído", WorkflowNodeType.End, 1630, 140,
                    new NodeConfiguration { Outcome = "Contrato aprovado" }),

                new("rejected", "Rejeitado", WorkflowNodeType.End, 1330, 360,
                    new NodeConfiguration
                    {
                        DocumentStatus = DocumentStatus.Rejected,
                        Outcome = "Rejeitado em revisão",
                        EndsAsRejected = true
                    })
            ],
            [
                new("start", "legal-review"),
                new("legal-review", "management-approval", "approved"),
                new("legal-review", "rejected", "rejected"),
                new("management-approval", "signature-preparation", "approved"),
                new("management-approval", "rejected", "rejected"),
                new("signature-preparation", "approved", "approved"),
                new("approved", "end")
            ]),

        new(
            OnboardingCode,
            "Integração de novo colaborador",
            "Duas frentes em paralelo — recursos humanos e operações — que convergem antes da comunicação de boas-vindas.",
            "EmployeeDocument",
            [
                new("start", "Início", WorkflowNodeType.Start, 0, 190),
                new("split", "Preparar integração", WorkflowNodeType.ParallelBranch, 270, 190),

                new("hr-file", "Processo de recursos humanos", WorkflowNodeType.HumanTask, 580, 70,
                    new NodeConfiguration
                    {
                        TaskTitle = "Instruir processo individual",
                        TaskDescription = "Contrato, dados pessoais, segurança social e seguro.",
                        Assignment = new AssignmentConfiguration
                        {
                            Strategy = AssignmentStrategy.Department,
                            DepartmentCode = "HR"
                        },
                        DueInHours = 48
                    }),

                new("it-setup", "Preparar posto de trabalho", WorkflowNodeType.HumanTask, 580, 310,
                    new NodeConfiguration
                    {
                        TaskTitle = "Preparar equipamento e acessos",
                        TaskDescription = "Portátil, conta de correio e acessos aos sistemas.",
                        Assignment = new AssignmentConfiguration
                        {
                            Strategy = AssignmentStrategy.Department,
                            DepartmentCode = "OPS"
                        },
                        DueInHours = 48
                    }),

                new("join", "Integração preparada", WorkflowNodeType.Join, 880, 190),

                new("welcome", "Comunicar boas-vindas", WorkflowNodeType.Notification, 1190, 190,
                    new NodeConfiguration
                    {
                        Notification = new NotificationConfiguration
                        {
                            Title = "Novo colaborador integrado",
                            Body = "O processo de integração está concluído.",
                            Recipients = new AssignmentConfiguration
                            {
                                Strategy = AssignmentStrategy.Department,
                                DepartmentCode = "HR"
                            }
                        }
                    }),

                new("end", "Concluído", WorkflowNodeType.End, 1500, 190,
                    new NodeConfiguration { Outcome = "Integração concluída" })
            ],
            [
                new("start", "split"),
                new("split", "hr-file"),
                new("split", "it-setup"),
                new("hr-file", "join"),
                new("it-setup", "join"),
                new("join", "welcome"),
                new("welcome", "end")
            ])
    ];

    public async Task SeedAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        HashSet<string> existing =
        [
            .. await dbContext.WorkflowDefinitions
                .IgnoreQueryFilters()
                .Where(d => d.TenantId == tenant.Id)
                .Select(d => d.Code)
                .ToListAsync(cancellationToken)
        ];

        Dictionary<string, Guid> documentTypes = await dbContext.DocumentTypes
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenant.Id)
            .ToDictionaryAsync(t => t.Code, t => t.Id, cancellationToken);

        foreach (DefinitionSeed seed in Definitions.Where(d => !existing.Contains(d.Code)))
        {
            WorkflowDefinition definition = new()
            {
                TenantId = tenant.Id,
                Code = seed.Code,
                Name = seed.Name,
                Description = seed.Description,
                Version = 1,
                IsActive = true,
                Trigger = WorkflowTriggerType.Manual,
                DocumentTypeId = seed.DocumentTypeCode is { } code && documentTypes.TryGetValue(code, out Guid typeId)
                    ? typeId
                    : null
            };

            foreach (NodeSeed node in seed.Nodes)
            {
                definition.Nodes.Add(new WorkflowNode
                {
                    TenantId = tenant.Id,
                    Key = node.Key,
                    Name = node.Name,
                    Description = node.Description,
                    Type = node.Type,
                    ConfigJson = node.Config?.ToJson(),
                    PositionX = node.X,
                    PositionY = node.Y
                });
            }

            foreach (EdgeSeed edge in seed.Edges)
            {
                definition.Edges.Add(new WorkflowEdge
                {
                    TenantId = tenant.Id,
                    FromNodeKey = edge.From,
                    ToNodeKey = edge.To,
                    Branch = edge.Branch,
                    Label = edge.Label
                });
            }

            dbContext.WorkflowDefinitions.Add(definition);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
