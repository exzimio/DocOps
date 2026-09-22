using DocOps.Domain.Documents;
using DocOps.Domain.Search;

namespace DocOps.Infrastructure.Search;

/// <summary>A phrase the demo reader knows, and what it means.</summary>
/// <typeparam name="T">The filter value the phrase stands for.</typeparam>
public sealed record SearchPhrase<T>(string Phrase, T Value);

/// <summary>
/// The words the demo question reader knows, in the two languages the application speaks.
///
/// It is a table rather than a model, and it is kept apart from the rules that use it so that what
/// the reader understands can be read at a glance — and so that nobody mistakes it for something
/// that infers meaning. Adding a phrase here is how the reader learns; there is no other way.
///
/// Phrases are written without accents and in lower case, because that is how the question arrives
/// after folding. Longer phrases are tried first, so "aguarda aprovacao" is not swallowed by
/// "aprovado".
/// </summary>
public static class SearchVocabulary
{
    public static readonly IReadOnlyList<SearchPhrase<DocumentStatus>> Statuses =
    [
        new("pendentes de aprovacao", DocumentStatus.PendingApproval),
        new("pendente de aprovacao", DocumentStatus.PendingApproval),
        new("pending approval", DocumentStatus.PendingApproval),
        new("para assinatura", DocumentStatus.SignatureRequired),
        new("signature required", DocumentStatus.SignatureRequired),
        new("awaiting signature", DocumentStatus.SignatureRequired),
        new("em revisao", DocumentStatus.InReview),
        new("in review", DocumentStatus.InReview),
        new("under review", DocumentStatus.InReview),
        new("rascunhos", DocumentStatus.Draft),
        new("rascunho", DocumentStatus.Draft),
        new("drafts", DocumentStatus.Draft),
        new("draft", DocumentStatus.Draft),
        new("arquivados", DocumentStatus.Archived),
        new("arquivado", DocumentStatus.Archived),
        new("archived", DocumentStatus.Archived),
        new("assinados", DocumentStatus.Signed),
        new("assinado", DocumentStatus.Signed),
        new("signed", DocumentStatus.Signed),
        new("rejeitados", DocumentStatus.Rejected),
        new("rejeitado", DocumentStatus.Rejected),
        new("rejected", DocumentStatus.Rejected),
        new("aprovados", DocumentStatus.Approved),
        new("aprovado", DocumentStatus.Approved),
        new("approved", DocumentStatus.Approved)
    ];

    public static readonly IReadOnlyList<SearchPhrase<DocumentApprovalState>> ApprovalStates =
    [
        new("a aguardar aprovacao", DocumentApprovalState.Awaiting),
        new("aguardam aprovacao", DocumentApprovalState.Awaiting),
        new("aguarda aprovacao", DocumentApprovalState.Awaiting),
        new("awaiting approval", DocumentApprovalState.Awaiting),
        new("waiting for approval", DocumentApprovalState.Awaiting),
        new("por aprovar", DocumentApprovalState.Awaiting),
        new("com pedido de informacao", DocumentApprovalState.InformationRequested),
        new("information requested", DocumentApprovalState.InformationRequested),
        new("nunca enviados para aprovacao", DocumentApprovalState.NotRequested),
        new("sem aprovacao", DocumentApprovalState.NotRequested),
        new("never sent for approval", DocumentApprovalState.NotRequested),
        new("aprovacao rejeitada", DocumentApprovalState.Rejected),
        new("recusados na aprovacao", DocumentApprovalState.Rejected),
        new("ja aprovados", DocumentApprovalState.Approved),
        new("aprovados por", DocumentApprovalState.Approved),
        new("already approved", DocumentApprovalState.Approved)
    ];

    public static readonly IReadOnlyList<SearchPhrase<DocumentWorkflowState>> WorkflowStates =
    [
        new("com processo em curso", DocumentWorkflowState.InProgress),
        new("em circuito", DocumentWorkflowState.InProgress),
        new("em curso", DocumentWorkflowState.InProgress),
        new("in progress", DocumentWorkflowState.InProgress),
        new("processo concluido", DocumentWorkflowState.Completed),
        new("processos concluidos", DocumentWorkflowState.Completed),
        new("concluidos", DocumentWorkflowState.Completed),
        new("completed workflow", DocumentWorkflowState.Completed),
        new("processo cancelado", DocumentWorkflowState.Cancelled),
        new("cancelados", DocumentWorkflowState.Cancelled),
        new("processo com erro", DocumentWorkflowState.Failed),
        new("com erro", DocumentWorkflowState.Failed),
        new("sem processo", DocumentWorkflowState.None),
        new("sem circuito", DocumentWorkflowState.None),
        new("no workflow", DocumentWorkflowState.None)
    ];

    public static readonly IReadOnlyList<SearchPhrase<SecurityClassification>> Classifications =
    [
        new("confidenciais", SecurityClassification.Confidential),
        new("confidencial", SecurityClassification.Confidential),
        new("confidential", SecurityClassification.Confidential),
        new("restritos", SecurityClassification.Restricted),
        new("restrito", SecurityClassification.Restricted),
        new("restricted", SecurityClassification.Restricted),
        new("publicos", SecurityClassification.Public),
        new("publico", SecurityClassification.Public),
        new("public", SecurityClassification.Public)
    ];

    /// <summary>Phrases that mean "only what belongs to me".</summary>
    public static readonly IReadOnlyList<string> Mine =
    [
        "os meus documentos", "meus documentos", "minhas faturas", "atribuidos a mim",
        "my documents", "mine", "assigned to me"
    ];

    /// <summary>
    /// Words carrying no filter and no content: verbs of asking, articles, prepositions. They are
    /// dropped from the leftover text so a question does not become a search for "mostrar".
    /// </summary>
    public static readonly IReadOnlySet<string> Noise = new HashSet<string>(StringComparer.Ordinal)
    {
        "mostrar", "mostra", "mostre", "listar", "lista", "liste", "encontrar", "encontra",
        "procurar", "procura", "ver", "quero", "quais", "quais sao", "sao", "todos", "todas",
        "os", "as", "o", "a", "um", "uma", "uns", "umas", "de", "do", "da", "dos", "das",
        "em", "no", "na", "nos", "nas", "com", "para", "por", "que", "e", "ou", "documento",
        "documentos", "ficheiro", "ficheiros",
        "show", "list", "find", "get", "all", "the", "of", "in", "on", "with", "for", "from",
        "and", "or", "that", "document", "documents", "file", "files", "me",
        "departamento", "departamentos", "seccao", "tipo", "tipos", "estado", "estados",
        "etiqueta", "etiquetas", "department", "departments", "type", "types", "state",
        "status", "tag", "tags"
    };
}
