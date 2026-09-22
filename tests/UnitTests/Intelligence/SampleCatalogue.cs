using DocOps.Application.Intelligence;
using DocOps.Domain.Documents;
using DocOps.Domain.Intelligence;

namespace DocOps.UnitTests.Intelligence;

/// <summary>
/// The document types the demo tenant ships with, as the provider sees them, and the sample files
/// kept in <c>samples/</c> at the repository root.
/// </summary>
internal static class SampleCatalogue
{
    public static readonly DocumentTypeProfile Invoice = new(
        Guid.Parse("00000000-0000-0000-0000-000000000001"),
        "SupplierInvoice",
        "Fatura de fornecedor",
        [
            Field("supplier", "Fornecedor", MetadataFieldType.Text, required: true),
            Field("invoiceNumber", "Número da fatura", MetadataFieldType.Text, required: true),
            Field("invoiceDate", "Data da fatura", MetadataFieldType.Date, required: true),
            Field("dueDate", "Data de vencimento", MetadataFieldType.Date),
            Field("netAmount", "Valor sem IVA", MetadataFieldType.Currency),
            Field("vatAmount", "IVA", MetadataFieldType.Currency),
            Field("totalAmount", "Total", MetadataFieldType.Currency, required: true),
            Field("currency", "Moeda", MetadataFieldType.Select, options: ["EUR", "USD", "GBP"]),
            Field("purchaseOrder", "Encomenda associada", MetadataFieldType.Text)
        ]);

    public static readonly DocumentTypeProfile Contract = new(
        Guid.Parse("00000000-0000-0000-0000-000000000002"),
        "Contract",
        "Contrato",
        [
            Field("counterparty", "Contraparte", MetadataFieldType.Text, required: true),
            Field("contractType", "Tipo de contrato", MetadataFieldType.Select, required: true,
                options: ["Prestação de serviços", "Fornecimento", "Confidencialidade", "Arrendamento", "Parceria"]),
            Field("startDate", "Início de vigência", MetadataFieldType.Date, required: true),
            Field("endDate", "Fim de vigência", MetadataFieldType.Date),
            Field("contractValue", "Valor contratual", MetadataFieldType.Currency),
            Field("noticeDays", "Pré-aviso de renovação (dias)", MetadataFieldType.Number),
            Field("governingLaw", "Lei aplicável", MetadataFieldType.Text)
        ]);

    public static readonly DocumentTypeProfile PurchaseOrder = new(
        Guid.Parse("00000000-0000-0000-0000-000000000003"),
        "PurchaseOrder",
        "Encomenda",
        [
            Field("supplier", "Fornecedor", MetadataFieldType.Text, required: true),
            Field("orderNumber", "Número da encomenda", MetadataFieldType.Text, required: true),
            Field("orderDate", "Data da encomenda", MetadataFieldType.Date, required: true),
            Field("totalAmount", "Total", MetadataFieldType.Currency),
            Field("deliveryDate", "Data de entrega prevista", MetadataFieldType.Date)
        ]);

    public static readonly DocumentTypeProfile Memo = new(
        Guid.Parse("00000000-0000-0000-0000-000000000004"),
        "InternalMemo",
        "Comunicação interna",
        [Field("subject", "Assunto", MetadataFieldType.Text, required: true)]);

    public static IReadOnlyList<DocumentTypeProfile> All => [Invoice, Contract, PurchaseOrder, Memo];

    public static DocumentText Read(string fileName) =>
        new(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", fileName)));

    private static FieldProfile Field(
        string code, string label, MetadataFieldType type, bool required = false, string[]? options = null) =>
        new(code, label, type, required, options ?? []);
}
