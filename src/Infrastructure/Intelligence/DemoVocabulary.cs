namespace DocOps.Infrastructure.Intelligence;

/// <summary>A label that introduces a value, with how much a match on it is worth.</summary>
internal sealed record LabelRule(string Text, decimal Confidence);

/// <summary>
/// How to find one field. Specific labels are tried first and trusted more; generic labels (a bare
/// "total", a bare "data") are tried last and skip lines that contain an exclusion, so "Total a
/// pagar" is not read from a "Total ilíquido" line.
/// </summary>
internal sealed record FieldRule(
    IReadOnlyList<LabelRule> Specific,
    IReadOnlyList<LabelRule> Generic,
    IReadOnlyList<string> Exclusions);

/// <summary>
/// What the demonstration provider knows. It is a table, not a model: every rule is visible here,
/// which is exactly why its output can be explained line by line and why it declares itself a demo.
/// Types and fields that are not listed still work through the labels of the tenant's own catalogue.
/// </summary>
internal static class DemoVocabulary
{
    /// <summary>Words that point to a document type, with a weight. Matched as whole words, accents ignored.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Term, int Weight)>> TypeSignals =
        new Dictionary<string, IReadOnlyList<(string, int)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["SupplierInvoice"] =
            [
                ("fatura", 3), ("fatura-recibo", 3), ("invoice", 3), ("total a pagar", 2), ("nif", 1),
                ("iva", 1), ("vencimento", 1), ("iban", 1), ("amount due", 2), ("vat", 1)
            ],
            ["Contract"] =
            [
                ("contrato", 3), ("outorgante", 3), ("clausula", 2), ("vigencia", 2), ("rege-se", 1),
                ("contract", 3), ("the parties", 2), ("governing law", 1)
            ],
            ["PurchaseOrder"] =
            [
                ("nota de encomenda", 4), ("encomenda", 2), ("purchase order", 4), ("prazo de entrega", 2),
                ("entrega prevista", 2), ("requisicao", 1)
            ],
            ["EmployeeDocument"] =
            [
                ("colaborador", 3), ("recursos humanos", 2), ("contrato de trabalho", 3), ("admissao", 2),
                ("funcionario", 2), ("employee", 3), ("onboarding", 2)
            ],
            ["InternalMemo"] =
            [
                ("comunicacao interna", 4), ("memorando", 3), ("a todos os colaboradores", 2), ("memo", 3)
            ],
            ["Email"] =
            [
                ("de:", 2), ("para:", 2), ("assunto:", 2), ("from:", 2), ("to:", 1), ("subject:", 2), ("enviado", 1)
            ],
            ["RequestForm"] =
            [
                ("pedido", 2), ("requerente", 3), ("formulario", 2), ("solicito", 1), ("request", 2)
            ],
            ["Report"] =
            [
                ("relatorio", 3), ("periodo", 1), ("resumo executivo", 2), ("indicadores", 1), ("report", 3)
            ]
        };

    /// <summary>Field rules by field code, shared by every type that uses the code.</summary>
    public static readonly IReadOnlyDictionary<string, FieldRule> Fields =
        new Dictionary<string, FieldRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["supplier"] = new(
                [L("fornecedor", 0.92m), L("emitente", 0.9m), L("supplier", 0.9m), L("vendor", 0.88m)],
                [],
                []),

            ["invoiceNumber"] = new(
                [
                    L("fatura n.º", 0.93m), L("fatura nº", 0.93m), L("fatura n°", 0.93m), L("fatura no.", 0.9m),
                    L("n.º da fatura", 0.93m), L("numero da fatura", 0.93m), L("invoice number", 0.93m),
                    L("invoice no.", 0.9m), L("invoice no", 0.88m), L("documento n.º", 0.8m)
                ],
                [],
                []),

            ["invoiceDate"] = new(
                [L("data de emissao", 0.93m), L("data da fatura", 0.93m), L("invoice date", 0.93m), L("issue date", 0.9m)],
                [L("data", 0.7m), L("date", 0.7m)],
                ["vencimento", "entrega", "validade", "due", "limite", "pagamento"]),

            ["dueDate"] = new(
                [L("data de vencimento", 0.93m), L("vencimento", 0.9m), L("data limite de pagamento", 0.92m), L("due date", 0.93m), L("payment due", 0.9m)],
                [],
                []),

            ["netAmount"] = new(
                [
                    L("total iliquido", 0.92m), L("valor sem iva", 0.93m), L("base tributavel", 0.92m),
                    L("subtotal", 0.88m), L("net amount", 0.92m), L("total s/ iva", 0.92m)
                ],
                [],
                []),

            ["vatAmount"] = new(
                [L("total iva", 0.92m), L("valor do iva", 0.93m), L("vat amount", 0.92m)],
                [L("iva", 0.85m), L("vat", 0.85m)],
                ["total", "sem iva", "s/ iva", "iliquido", "incluido", "included", "base", "nif", "taxa", "gb", "vat no", "vat number", "vat reg"]),

            ["totalAmount"] = new(
                [
                    L("total a pagar", 0.94m), L("total da fatura", 0.94m), L("valor total", 0.9m),
                    L("total geral", 0.92m), L("amount due", 0.93m), L("invoice total", 0.93m), L("total (eur)", 0.92m)
                ],
                [L("total", 0.75m)],
                ["iliquido", "sem iva", "s/ iva", "subtotal", "iva", "vat", "net"]),

            ["currency"] = new(
                [L("moeda", 0.93m), L("currency", 0.93m)],
                [],
                []),

            ["purchaseOrder"] = new(
                [
                    L("n.º de encomenda", 0.92m), L("nota de encomenda", 0.9m), L("encomenda", 0.88m),
                    L("requisicao", 0.85m), L("purchase order", 0.92m), L("po number", 0.9m)
                ],
                [],
                []),

            ["counterparty"] = new(
                [
                    L("segundo outorgante", 0.92m), L("contraparte", 0.93m), L("prestador de servicos", 0.88m),
                    L("second party", 0.9m), L("counterparty", 0.93m)
                ],
                [],
                []),

            ["startDate"] = new(
                [
                    L("produz efeitos a", 0.9m), L("inicio de vigencia", 0.93m), L("com inicio em", 0.9m),
                    L("data de inicio", 0.92m), L("start date", 0.92m), L("effective date", 0.9m)
                ],
                [],
                []),

            ["endDate"] = new(
                [
                    L("vigora ate", 0.9m), L("fim de vigencia", 0.93m), L("termina em", 0.9m),
                    L("data de termo", 0.92m), L("end date", 0.92m), L("expires on", 0.9m), L("expiry date", 0.92m)
                ],
                [],
                []),

            ["contractValue"] = new(
                [
                    L("valor total do contrato", 0.93m), L("valor do contrato", 0.92m), L("valor contratual", 0.93m),
                    L("contract value", 0.92m)
                ],
                [L("preco", 0.75m)],
                []),

            ["noticeDays"] = new(
                [L("pre-aviso de", 0.9m), L("pre-aviso", 0.85m), L("aviso previo de", 0.9m), L("notice period", 0.9m)],
                [],
                []),

            ["governingLaw"] = new(
                [L("rege-se pela", 0.88m), L("lei aplicavel", 0.9m), L("governing law", 0.9m), L("governed by", 0.88m)],
                [],
                []),

            ["orderNumber"] = new(
                [L("encomenda n.º", 0.93m), L("n.º da encomenda", 0.93m), L("numero da encomenda", 0.93m), L("order number", 0.93m), L("po number", 0.9m)],
                [],
                []),

            ["orderDate"] = new(
                [L("data da encomenda", 0.93m), L("order date", 0.93m)],
                [L("data", 0.7m)],
                ["entrega", "vencimento", "delivery"]),

            ["deliveryDate"] = new(
                [L("data de entrega", 0.92m), L("entrega prevista", 0.92m), L("prazo de entrega", 0.85m), L("delivery date", 0.92m)],
                [],
                []),

            ["subject"] = new(
                [L("assunto", 0.93m), L("subject", 0.93m)],
                [],
                []),

            ["fromAddress"] = new(
                [L("de", 0.8m), L("from", 0.85m), L("remetente", 0.92m)],
                [],
                []),

            ["requester"] = new(
                [L("requerente", 0.93m), L("pedido por", 0.9m), L("requested by", 0.9m)],
                [],
                []),

            ["employeeName"] = new(
                [L("colaborador", 0.9m), L("nome do colaborador", 0.93m), L("employee", 0.9m), L("trabalhador", 0.88m)],
                [],
                []),

            ["period"] = new(
                [L("periodo", 0.9m), L("period", 0.9m)],
                [],
                []),

            ["author"] = new(
                [L("autor", 0.9m), L("elaborado por", 0.9m), L("author", 0.9m), L("prepared by", 0.9m)],
                [],
                [])
        };

    /// <summary>Words that mark the recipient's block, so the recipient is not mistaken for the issuer.</summary>
    public static readonly IReadOnlyList<string> RecipientMarkers =
        ["cliente", "exmo", "exma", "bill to", "customer", "destinatario", "para:"];

    /// <summary>Legal-form suffixes that identify a company name at the top of a document.</summary>
    public static readonly IReadOnlyList<string> CompanySuffixes =
        ["lda", "lda.", "s.a.", "sa", "unipessoal", "ltd", "ltd.", "gmbh", "inc", "inc.", "s.l.", "llc"];

    /// <summary>Question words mapped to the fields that answer them, in order of preference.</summary>
    public static readonly IReadOnlyList<(string[] Terms, string[] FieldCodes)> QuestionIntents =
    [
        (["iva", "vat", "imposto"], ["vatAmount"]),
        (["vencimento", "vence", "prazo de pagamento", "due", "pagar ate"], ["dueDate"]),
        (["expira", "termina", "fim", "caduca", "expire", "expires", "end", "ends", "valido ate", "vigora"], ["endDate"]),
        (["comeca", "inicio", "inicia", "start", "starts", "effective"], ["startDate"]),
        (["total", "valor", "montante", "quanto", "amount", "how much", "price", "preco"], ["totalAmount", "contractValue", "netAmount"]),
        (["encomenda", "purchase order", "requisicao"], ["purchaseOrder", "orderNumber"]),
        (["emitiu", "emitente", "fornecedor", "issued", "supplier", "vendor", "quem", "who", "contraparte", "outorgante"],
            ["supplier", "counterparty", "fromAddress", "requester", "author", "employeeName"]),
        (["numero", "number", "n.º", "referencia"], ["invoiceNumber", "orderNumber"]),
        (["lei", "law", "jurisdicao", "governed"], ["governingLaw"]),
        (["aviso", "notice", "renovacao", "renewal"], ["noticeDays"]),
        (["moeda", "currency"], ["currency"]),
        (["data", "quando", "when", "date"], ["invoiceDate", "startDate", "orderDate", "receivedAt", "effectiveDate"]),
        (["assunto", "subject", "sobre"], ["subject"])
    ];

    /// <summary>Words that mean the question is about who approved the document — answered from the audit trail, not the text.</summary>
    public static readonly IReadOnlyList<string> ApprovalTerms =
        ["aprovou", "aprovado por", "aprovacao", "quem aprovou", "approved", "approver", "approval"];

    /// <summary>Words that suggest the question was asked in English, so the answer is given in English too.</summary>
    public static readonly IReadOnlyList<string> EnglishMarkers =
        ["what", "who", "when", "which", "how", "is the", "does", "the"];

    public static readonly IReadOnlySet<string> StopWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "qual", "quais", "quem", "quando", "onde", "como", "este", "esta", "deste", "desta", "documento",
        "para", "pelo", "pela", "sobre", "what", "which", "when", "where", "does", "this", "that", "document",
        "with", "from", "have", "tem", "sao", "foi", "sera"
    };

    private static LabelRule L(string text, decimal confidence) => new(text, confidence);
}
