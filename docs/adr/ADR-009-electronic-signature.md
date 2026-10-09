# ADR-009: Assinatura como registo verificável, nunca como alegação de validade legal

- **Estado:** aceite
- **Data:** 2026-09-24
- **Âmbito:** pedidos de assinatura sobre documentos

## Contexto

O produto precisa de pedidos de assinatura que mostrem o signatário, a data do pedido, o estado, a
data de assinatura, a versão do documento, o histórico de auditoria e o *hash* do documento. E é uma
demonstração de portefólio, que **não pode reclamar validade legal**: é essa condição que decide tudo
o resto.

A tensão está toda aí. Uma assinatura eletrónica **a sério**, qualificada ou avançada na aceção do
eIDAS, exige uma chave privada sob controlo exclusivo do signatário, um certificado emitido por uma
entidade de confiança, e carimbo temporal de uma fonte independente. Nada disso existe aqui, nem
podia existir num projeto de portefólio que corre sem contas em serviços de terceiros.

O risco não é técnico, é de honestidade. Um ecrã com o título «Assinatura eletrónica», um botão
«Assinar» e um resumo SHA-256 em tipo monoespaçado **parece-se exatamente** com a coisa real. Quem o
vir numa demonstração assume o que o ecrã aparenta, não o que o código faz.

## Decisão

Construir a funcionalidade como aquilo que ela verdadeiramente é, **um registo**, e dizê-lo onde
quer que apareça.

O que fica registado: *esta pessoa concordou, neste momento, com um documento cujo conteúdo tinha
este resumo*. Nada mais é alegado.

O que torna o registo digno de ser guardado, em vez de decorativo, é a ligação ao conteúdo:

- O pedido **fixa a versão** (`DocumentVersionId`) e **copia o resumo** (`RequestedContentHash`) no
  momento em que é feito.
- Assinar é **recusado** se o conteúdo mudou entretanto: consentir um documento é consentir o seu
  conteúdo, e o conteúdo mudou.
- Uma versão nova depois de assinado **não reescreve** o registo antigo. Faz com que ele deixe de
  corresponder, e `ContentStillMatches` diz isso por extenso.

A pergunta «isto ainda é o documento que foi assinado?» tem assim uma resposta que **não depende de
confiar na aplicação**: recalcula-se o resumo do ficheiro e compara-se.

## Regras não negociáveis

- **Nunca reclamar validade legal**, em nenhum ecrã, texto de API, mensagem de erro ou documento. O
  aviso aparece no topo do painel, antes de tudo o resto, e não em rodapé.

- **Assinar é pessoal.** Só a pessoa nomeada no pedido assina. Nenhuma permissão, antiguidade ou
  papel administrativo permite assinar em nome de outra: uma assinatura é a afirmação de alguém
  sobre um documento, e não há sentido útil em que seja feita por procuração. O administrador da
  organização, que detém todas as permissões, é recusado como qualquer outro.

- **Assinar não é editar.** Quem é chamado a assinar precisa de ver o documento, não de o poder
  alterar. Exigir `Edit` para assinar obrigaria a distribuir direitos de edição com o único fim de
  recolher uma assinatura.

- **Um pedido em aberto por documento.** Dois tornariam ambígua a pergunta «isto está assinado?», e
  nenhum dos signatários saberia se a sua assinatura era a que contava.

- **Nunca criar um pedido que ninguém possa cumprir.** Se o signatário não pode ver o documento, ou
  não tem sequer permissão para assinar, o pedido é recusado no momento em que se pede.

## Alternativas consideradas

**Assinatura criptográfica a sério, com par de chaves gerado pela aplicação.** Rejeitado, e é a
alternativa mais perigosa das quatro. Seria tecnicamente possível e produziria um artefacto
criptograficamente verificável, mas uma chave que a aplicação gera e guarda não está sob controlo
exclusivo do signatário, que é precisamente o que distingue uma assinatura avançada de um registo.
O resultado seria **mais** convincente e **igualmente** sem valor legal: a pior combinação possível.

**Integrar um fornecedor real (DocuSign, Adobe Sign) atrás de uma abstração**, como o
[ADR-007](ADR-007-external-connectors.md) faz para o ERP e o CRM. Rejeitado por uma
razão simples: os outros conetores simulam sistemas cujo lado de lá não faz afirmações jurídicas
sobre o utilizador. Um fornecedor de assinatura simulado produziria um objeto que **se diz**
juridicamente válido, e a demonstração passaria a exibir uma alegação falsa em vez de uma abstração
honesta. A abstração continua possível mais tarde; o que não é aceitável é simulá-la.

**Deixar a assinatura seguir a versão mais recente do documento.** Rejeitado: seria uma assinatura
sobre o que quer que alguém carregasse depois. É exatamente o abuso que fixar a versão previne.

**Invalidar a assinatura quando chega uma versão nova.** Rejeitado, e é uma distinção subtil que
vale a pena ter escrita. A assinatura continua a registar o que registou: uma pessoa concordou com
um conteúdo, num momento, e isso aconteceu. O que muda é que deixa de descrever o conteúdo atual.
Apagar o registo seria perder prova; re-apontá-lo em silêncio para o conteúdo novo seria falsificá-la.
Dizer «já não corresponde» é a única leitura verdadeira das três.

## Consequências

**Positivas**

- O que aparece no ecrã é verificável por quem o lê, sem confiar na aplicação: o resumo está lá.
- A funcionalidade demonstra o desenho (versão fixada, resumo comparado, quem pode o quê) de que uma
  implementação a sério também precisaria.
- A honestidade é defensável numa entrevista: a pergunta «isto é mesmo uma assinatura?» tem resposta
  pronta, e a resposta é interessante.

**Negativas**

- O aviso ocupa espaço permanente em todos os ecrãs de assinatura, e é deliberado que incomode.
- Recusar assinar quando o conteúdo mudou pode parecer excessivo a quem só queria corrigir uma
  gralha. É o lado certo para errar.

**Sinais de revisão:** integração com um fornecedor real de assinatura, ou um requisito de validade
jurídica, que obrigaria a rever esta ADR inteira e não apenas a estendê-la.

## Na prática

### O ciclo de vida do documento já protegia o conteúdo, e a verificação fica na mesma

Um documento em «aguarda assinatura» **não aceita versões novas**:
`DocumentLifecycle.AllowsContentChange` só o permite em rascunho, revisão ou rejeitado. A comparação
de resumos é, portanto, defesa em profundidade e não a primeira linha.

Mantém-se por duas razões: custa uma comparação de cadeias, e é o que mantém a garantia
verdadeira se aquela regra alguma vez mudar. O `ContentStillMatches` continua a ser necessário de
qualquer forma: é o que responde à pergunta meses depois, quando o documento já seguiu o seu
caminho.

### A transição de estado não usa os direitos de quem assina

Se a mudança de estado do documento passasse pelo `IDocumentService`, que aplica os direitos de quem
chama, um jurista a quem se pede para assinar um documento financeiro levaria `403`, porque não pode
editar documentos de outro departamento, e não tem de poder.

A transição é aplicada como o motor de workflow a aplica: validada pelo `DocumentLifecycle`, e não
pelos direitos de quem age. O estado decorre de um facto que aconteceu, não
de quem o comunica.

### Pedidos impossíveis de cumprir, em duas dimensões

Um pedido a quem não vê o documento, ou a quem não tem sequer permissão para assinar, é o mesmo
erro: um pedido que fica na lista de alguém e devolve uma recusa em cada tentativa. Ambos são
recusados no momento em que se pede, com indicação do que falta conceder.

Para o segundo, o `DocumentAccessResolver` expõe as permissões efetivas de outra pessoa.
Perguntar «esta pessoa conseguiria fazer isto?» antes de lho pedir é uma pergunta legítima, e a
alternativa era o serviço de assinaturas recalcular permissões por sua conta.

### O registo vive no histórico do documento

As entradas de auditoria são escritas contra o documento, com a identidade do pedido nos
metadados. Ancorá-las ao próprio pedido de assinatura seria tecnicamente correto e praticamente
invisível: o sítio onde as pessoas procuram a história de um documento é o documento, e o trilho
mostraria apenas a mudança de estado, sem dizer quem assinou nem sobre que conteúdo.

### O nó de workflow espera por uma pessoa, não por um sistema

O passo de assinatura segue a forma de uma tarefa humana e não a da fila das integrações, porque não
há nada a chamar: é uma pessoa que tem de agir, e pessoas são exatamente o que o motor já sabe
esperar. Um grupo, departamento ou perfil, é resolvido para uma pessoa concreta quando o passo
corre, porque «o departamento financeiro assinou isto» não é uma afirmação que alguém possa
sustentar. Se não resolver, o processo falha de forma explícita.
