using DocOps.Application.Integrations;
using DocOps.Domain.Integrations;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Integrations;

/// <summary>
/// The master data a back-office system would hold, as a fixed local list.
///
/// It is deliberately **incomplete** relative to the seeded repository: two of the suppliers that
/// really appear on invoices are missing from it, and one contract counterparty is missing from the
/// customer list. A reconciliation that always came back clean would prove nothing and teach nobody
/// how to read the screen.
/// </summary>
public static class SimulatedMasterData
{
    /// <summary>Suppliers the simulated ERP knows. "Predial Tejo" and "Nordvik" are absent on purpose.</summary>
    public static readonly IReadOnlySet<string> ErpSuppliers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Atlas Tecnologia",
        "Northstar Supplies",
        "Verdemar Serviços",
        "Ibérica Logística",
        "Lusitânia Papelaria"
    };

    /// <summary>Accounts the simulated CRM knows. "Predial Tejo" is absent on purpose.</summary>
    public static readonly IReadOnlySet<string> CrmAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Atlas Tecnologia",
        "Northstar Supplies",
        "Verdemar Serviços",
        "Nordvik Systems",
        "Lusitânia Papelaria",
        "Nordhavn Logística"
    };
}

/// <summary>
/// Reconciles one metadata field against a list of names a back-office system would hold.
///
/// The work is real: it reads the documents this organisation actually has, pulls the values people
/// really entered, and reports the ones the other system would not recognise. Only the far side is
/// simulated — and the card says so.
/// </summary>
public abstract class MasterDataReconciliationAdapter(DocOpsDbContext dbContext) : IIntegrationAdapter
{
    public abstract IntegrationAdapterInfo Info { get; }

    /// <summary>The metadata field whose values are reconciled, by code.</summary>
    protected abstract string FieldCode { get; }

    /// <summary>The names the far system knows.</summary>
    protected abstract IReadOnlySet<string> KnownNames { get; }

    /// <summary>What an unknown value is called in the log line, in the plural.</summary>
    protected abstract string SubjectPlural { get; }

    public async Task<IntegrationRunResult> RunAsync(
        IntegrationRunContext context, CancellationToken cancellationToken = default)
    {
        string fieldCode = FieldCode;

        // Explicitly by organisation: this also runs from the worker, where the global filter is open.
        var entries = await dbContext.DocumentMetadataValues
            .IgnoreQueryFilters()
            .Where(value =>
                value.TenantId == context.TenantId
                && !value.Document!.IsDeleted
                && value.TextValue != null
                && value.Field!.Code == fieldCode)
            .Select(value => new
            {
                Name = value.TextValue!,
                value.Document!.Reference
            })
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            return IntegrationRunResult.Skipped(
                $"Não há documentos com «{fieldCode}» para reconciliar.", IntegrationStatus.Connected);
        }

        List<IGrouping<string, string>> unknown =
        [
            .. entries
                .Where(entry => !KnownNames.Contains(entry.Name.Trim()))
                .GroupBy(entry => entry.Name.Trim(), entry => entry.Reference, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        ];

        List<IntegrationLogEntry> log =
        [
            new(
                IntegrationLogLevel.Information,
                "reconcile",
                $"{entries.Count} valor(es) de «{fieldCode}» reconciliado(s) em {entries.Select(e => e.Reference).Distinct().Count()} documento(s).",
                null,
                entries.Count)
        ];

        foreach (IGrouping<string, string> group in unknown)
        {
            log.Add(new IntegrationLogEntry(
                IntegrationLogLevel.Warning,
                "reconcile",
                $"«{group.Key}» não existe no sistema externo.",
                $"Documentos afetados: {string.Join(", ", group.Distinct().OrderBy(reference => reference))}."));
        }

        return new IntegrationRunResult
        {
            // Discrepancies are findings, not failures: the run did exactly what it was asked to do.
            Outcome = IntegrationRunOutcome.Succeeded,
            RecordsProcessed = entries.Count,
            Summary = unknown.Count == 0
                ? $"{entries.Count} registo(s) reconciliado(s), sem divergências."
                : $"{entries.Count} registo(s) reconciliado(s); {unknown.Count} {SubjectPlural} por registar.",
            Entries = log,
            Status = IntegrationStatus.Connected
        };
    }
}

public sealed class ErpIntegrationAdapter(DocOpsDbContext dbContext) : MasterDataReconciliationAdapter(dbContext)
{
    public override IntegrationAdapterInfo Info { get; } = new(
        IntegrationKind.Erp,
        "ERP",
        "Confronta os fornecedores das faturas com a lista de fornecedores registados no ERP.",
        RequiresCredentials: false,
        IsSimulated: true,
        SimulationNotice: "A lista de fornecedores do ERP é uma lista local fixa; não há ligação a "
            + "nenhum ERP. Os documentos e os fornecedores conferidos são os reais deste repositório.");

    protected override string FieldCode => "supplier";

    protected override IReadOnlySet<string> KnownNames => SimulatedMasterData.ErpSuppliers;

    protected override string SubjectPlural => "fornecedor(es)";
}

public sealed class CrmIntegrationAdapter(DocOpsDbContext dbContext) : MasterDataReconciliationAdapter(dbContext)
{
    public override IntegrationAdapterInfo Info { get; } = new(
        IntegrationKind.Crm,
        "CRM",
        "Confronta as contrapartes dos contratos com as contas registadas no CRM.",
        RequiresCredentials: false,
        IsSimulated: true,
        SimulationNotice: "A lista de contas do CRM é uma lista local fixa; não há ligação a nenhum "
            + "CRM. Os contratos e as contrapartes conferidas são os reais deste repositório.");

    protected override string FieldCode => "counterparty";

    protected override IReadOnlySet<string> KnownNames => SimulatedMasterData.CrmAccounts;

    protected override string SubjectPlural => "contraparte(s)";
}
