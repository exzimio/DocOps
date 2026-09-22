using DocOps.Domain.Inbox;

namespace DocOps.Infrastructure.Inbox;

/// <summary>One message of the demonstration mailbox, before it is given a date and an address.</summary>
/// <param name="Key">Stable part of the message identifier, so a second synchronisation recognises it.</param>
/// <param name="Folder">Which monitored mailbox it arrives at, as a local part.</param>
/// <param name="HoursAgo">How long before the synchronisation it arrived; keeps the inbox looking alive.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="FromName">The sender's name, as the mail client would show it.</param>
/// <param name="FromAddress">The sender's address. Always under <c>.example</c>, which cannot resolve.</param>
/// <param name="Importance">The flag the sender set.</param>
/// <param name="Body">The message body, written as business correspondence is written.</param>
/// <param name="Sample">Name of the file in <c>samples/</c> to attach, or <c>null</c> for a message without one.</param>
/// <param name="AttachmentName">What the attachment is called in the message, which is rarely the sample's own name.</param>
public sealed record DemoMail(
    string Key,
    string Folder,
    int HoursAgo,
    string Subject,
    string FromName,
    string FromAddress,
    EmailImportance Importance,
    string Body,
    string? Sample = null,
    string? AttachmentName = null);

/// <summary>
/// The messages the demonstration mailbox contains.
///
/// They are written as real business correspondence is written — a supplier chasing a payment, a law
/// firm sending a draft, a colleague forwarding something to the wrong address — because an inbox
/// full of "Test message 1" demonstrates nothing. They are fictional throughout: every company and
/// every person here is invented, and every address is under <c>.example</c>, which cannot resolve.
///
/// The table is deliberately flat data. Adding a message is adding a row; there is no other way, and
/// nothing here infers anything.
/// </summary>
public static class DemoMailbox
{
    /// <summary>The mailboxes a tenant monitors, as local parts. The domain comes from the tenant.</summary>
    public static readonly IReadOnlyList<string> Folders = ["faturas", "contratos", "geral"];

    public static readonly IReadOnlyList<DemoMail> Messages =
    [
        new(
            "atlas-ft-0815",
            "faturas",
            3,
            "Fatura FT 2026/0815 — Atlas Tecnologia",
            "Mariana Quadros",
            "faturacao@atlas-tecnologia.example",
            EmailImportance.High,
            """
            Exmos. Senhores,

            Junto envio a fatura FT 2026/0815, referente às licenças de software contratadas ao
            abrigo da encomenda ENC-2026-00045.

            O vencimento é a 30 dias da data de emissão. Agradeço que confirmem a receção.

            Com os melhores cumprimentos,
            Mariana Quadros
            Departamento Financeiro — Atlas Tecnologia, Lda.
            """,
            "fatura-atlas-tecnologia.txt",
            "FT-2026-0815.txt"),

        new(
            "nordhavn-minuta",
            "contratos",
            9,
            "Minuta do contrato de prestação de serviços — Nordhavn Logística",
            "Henrique Bastos",
            "juridico@nordhavn-logistica.example",
            EmailImportance.Normal,
            """
            Bom dia,

            Na sequência da nossa reunião de terça-feira, segue a minuta revista do contrato de
            prestação de serviços logísticos, já com a cláusula de vigência que acordámos.

            Fico a aguardar comentários do vosso departamento jurídico antes de avançarmos para
            assinatura.

            Cumprimentos,
            Henrique Bastos
            Nordhavn Logística, S.A.
            """,
            "contrato-nordhavn-logistica.txt",
            "minuta-contrato-nordhavn-v3.txt"),

        new(
            "brightline-invoice",
            "faturas",
            27,
            "Invoice BL-2026-0442 — Brightline Systems Ltd",
            "Eleanor Whitfield",
            "accounts@brightline-systems.example",
            EmailImportance.Normal,
            """
            Dear Sirs,

            Please find attached our invoice BL-2026-0442 for the consultancy days delivered in
            August. Payment terms are 30 days net, in GBP, to the account detailed on the invoice.

            Do let me know if you need a purchase order reference added.

            Kind regards,
            Eleanor Whitfield
            Accounts Receivable — Brightline Systems Ltd
            """,
            "invoice-brightline-uk.txt",
            "BL-2026-0442.txt"),

        new(
            "verdemar-encomenda",
            "faturas",
            50,
            "Confirmação da encomenda ENC-2026-00046",
            "Rui Salvador",
            "comercial@verdemar-servicos.example",
            EmailImportance.Normal,
            """
            Boa tarde,

            Confirmamos a receção da vossa encomenda ENC-2026-00046 e anexamos o documento de
            confirmação com as quantidades e os prazos de entrega acordados.

            Qualquer divergência, agradeço que nos digam até ao final da semana.

            Com os melhores cumprimentos,
            Rui Salvador
            Verdemar Serviços, Lda.
            """,
            "encomenda-verdemar.txt",
            "confirmacao-ENC-2026-00046.txt"),

        new(
            "northstar-pagamento",
            "faturas",
            74,
            "Reforço: fatura 2026/1184 por regularizar",
            "Patrícia Amorim",
            "cobrancas@northstar-supplies.example",
            EmailImportance.High,
            """
            Exmos. Senhores,

            A fatura 2026/1184, enviada a 12 de agosto, continua por regularizar. O prazo de
            pagamento terminou na passada sexta-feira.

            Agradecemos que nos indiquem a data prevista de pagamento ou, caso exista alguma
            divergência, qual o motivo da retenção.

            Com os melhores cumprimentos,
            Patrícia Amorim
            Departamento de Cobranças — Northstar Supplies
            """),

        new(
            "onboarding-colaborador",
            "geral",
            20,
            "Documentação de admissão — nova colaboradora (Operações)",
            "Teresa Albuquerque",
            "teresa.albuquerque@asteria.example",
            EmailImportance.Normal,
            """
            Olá,

            A admissão da nova colaboradora do departamento de Operações está confirmada para o
            início do próximo mês. Resumo do que ficou combinado, para ficar registado:

            - Início: primeiro dia útil do mês seguinte
            - Departamento: Operações
            - Equipamento: portátil e telemóvel, pedidos ao departamento de sistemas
            - Formação de acolhimento: primeira semana, com o responsável de equipa

            Falta apenas arquivar esta informação no processo individual.

            Obrigada,
            Teresa Albuquerque
            Recursos Humanos
            """),

        new(
            "iberica-divergencia",
            "faturas",
            96,
            "Divergência na fatura 2026/0442 — Ibérica Logística",
            "Carlos Mendes",
            "financeiro@iberica-logistica.example",
            EmailImportance.Normal,
            """
            Bom dia,

            Detetámos uma divergência entre a quantidade faturada e a guia de transporte da
            fatura 2026/0442. Estamos a apurar internamente e enviaremos nota de crédito se se
            confirmar.

            Peço desculpa pelo incómodo.

            Cumprimentos,
            Carlos Mendes
            Ibérica Logística
            """),

        new(
            "nordvik-renovacao",
            "contratos",
            120,
            "Renovação da parceria — Nordvik Systems",
            "Anders Lindqvist",
            "partnerships@nordvik-systems.example",
            EmailImportance.Normal,
            """
            Bom dia,

            O contrato de parceria aproxima-se do termo e gostaríamos de iniciar a conversa sobre a
            renovação. Podemos agendar uma chamada para a próxima semana?

            Envio a proposta comercial assim que tiverem disponibilidade.

            Cumprimentos,
            Anders Lindqvist
            Nordvik Systems
            """),

        new(
            "lusitania-catalogo",
            "geral",
            140,
            "Novo catálogo de material de escritório",
            "Sónia Peixoto",
            "clientes@lusitania-papelaria.example",
            EmailImportance.Low,
            """
            Caro cliente,

            Está disponível o novo catálogo de material de escritório, com condições especiais para
            clientes com conta aberta.

            Para consultar, responda a esta mensagem e enviaremos o acesso.

            Lusitânia Papelaria
            """),

        new(
            "reuniao-financeira",
            "geral",
            164,
            "Ata da reunião de fecho mensal",
            "Ricardo Lopes",
            "ricardo.lopes@asteria.example",
            EmailImportance.Normal,
            """
            Colegas,

            Segue o resumo do que ficou decidido na reunião de fecho:

            - As faturas acima de 5 000 € passam a exigir segunda aprovação.
            - As divergências de fornecedor passam a ser registadas no próprio documento.
            - O relatório mensal passa a ser fechado até ao dia 5.

            Quem não pôde estar presente, digam se querem acrescentar alguma coisa.

            Ricardo Lopes
            Financeiro
            """)
    ];
}
