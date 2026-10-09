# ADR-008: Portal externo como experiência separada, sem autorização própria

- **Estado:** aceite
- **Data:** 2026-09-24
- **Âmbito:** portal de fornecedores em `/portal`

## Contexto

O produto precisa de um portal externo separado em `/portal`, para um fornecedor, com início de
sessão, submissão de faturas, consulta do que submeteu, acompanhamento do estado do processo e
resposta a pedidos de informação. A restrição que importa é que um utilizador externo **nunca** vê
notas internas nem documentos que não lhe dizem respeito.

O problema não é construir os ecrãs. É que esta é a **única parte do produto que deixa entrar alguém
de fora**. Para um colaborador, a pergunta «e se esta pessoa vir isto?» tem uma resposta tranquila;
para um fornecedor não tem, porque o que ele vê sai da organização.

Há duas formas de falhar aqui, e são simétricas.

A primeira é **esconder em vez de isolar**: reaproveitar os ecrãs internos, reutilizar os DTO
internos e não mostrar os campos que não interessam. Funciona no dia em que é escrito e falha no dia
em que alguém acrescenta um campo a um DTO partilhado sem saber que ele também é servido a um
fornecedor.

A segunda é **duplicar as regras**: dar ao portal o seu próprio entendimento de quem pode ver o quê.
Dois conjuntos de regras sobre os mesmos dados não ficam de acordo por muito tempo, e quando
divergem é sempre o mais permissivo que ganha, porque o mais restritivo é o que alguém repara que
«está partido» e corrige.

## Decisão

O portal é uma **experiência separada dentro da mesma aplicação**, com o seu próprio shell, as suas
próprias rotas e os seus próprios contratos, mas **sem autorização própria**.

Concretamente:

- **Quem vê o quê é decidido pelo mesmo código de sempre.** `PortalService` filtra com
  `DocumentAccessResolver.BuildVisibilityPredicate`, o predicado que a listagem interna usa, e que já
  diz que um utilizador externo alcança um documento apenas por propriedade ou por concessão
  explícita, e nunca por um rótulo de sensibilidade.

- **Os contratos do portal são escritos à mão e estreitos.** `PortalSubmissionDto` não é o
  `DocumentListItemDto` com menos campos: é um tipo diferente, com os campos que um fornecedor tem
  razão para ver. A separação de tipos é o que garante que um campo acrescentado do lado interno não
  atravessa sozinho.

- **A separação corre nos dois sentidos.** Nenhum perfil interno detém `portal.access`, nem o de
  administrador da organização; nenhum perfil externo detém o que a aplicação interna exige. Um
  utilizador que escreva o endereço do outro lado é reencaminhado, e o servidor recusa na mesma.

- **O que o portal produz passa pelos serviços de domínio.** Uma submissão é criada pelo
  `IDocumentService`, com a mesma validação, referência, permissões e auditoria de um carregamento
  manual. É a mesma regra que o [ADR-007](ADR-007-external-connectors.md) impõe aos conetores.

## Regras não negociáveis

- **O portal nunca decide autorização por si.** Se uma pergunta de acesso não tiver resposta no
  avaliador existente, a resposta é estender o avaliador, não escrever uma regra no portal.

- **Nada de DTO partilhado entre dentro e fora.** Um tipo servido ao portal não é servido à aplicação
  interna, e vice-versa. O custo é alguma repetição; o que se compra é que uma fuga tenha de ser
  escrita de propósito em vez de acontecer por distração.

- **A cronologia do processo não sai.** O fornecedor vê as perguntas que lhe foram dirigidas e as
  respostas que deu: dois tipos de evento, selecionados pelo nome. Nunca se constrói esta lista por
  exclusão («tudo menos o que é interno»), porque uma lista construída por exclusão falha aberta: o
  tipo de evento que alguém acrescentar amanhã entra por omissão.

- **Uma resposta do fornecedor não move o processo.** Responder a uma pergunta acrescenta informação;
  quem decide é quem perguntou. Um fornecedor não pode aprovar a sua própria fatura respondendo a
  uma questão sobre ela.

- **Nada de mensagens não solicitadas.** Só se responde a uma pergunta em aberto. Sem isso, o portal
  seria um canal para um fornecedor escrever no processo quando lhe apetecesse.

## Alternativas consideradas

**Uma segunda aplicação, com o seu próprio deployment.** Rejeitada: duplicaria o cliente de API, a autenticação, o sistema de design, a
internacionalização e a configuração, para servir três ecrãs. A separação que importa é de
**experiência**, isto é, o que o utilizador vê e alcança, e essa consegue-se com um shell próprio e rotas
guardadas. Se um dia houver razão para separar o deployment (por exemplo, expor o portal numa rede
diferente), o corte já está feito nos sítios certos.

**Reutilizar os ecrãs e os DTO internos, escondendo campos.** Rejeitado, e é a alternativa mais
tentadora porque é a mais barata hoje. Esconder é uma propriedade da interface; isolar é uma
propriedade do servidor. A regra é sobre o que o utilizador externo **vê**, e a única
forma de a garantir é não enviar.

**Dar ao portal o seu próprio filtro de visibilidade.** Rejeitado: seriam dois conjuntos de regras
sobre os mesmos documentos. A tentação é real porque o filtro do portal pareceria mais simples («só
o que é dele»), mas «o que é dele» já é exatamente o que o avaliador responde para um utilizador
externo.

**Mostrar ao fornecedor a cronologia do processo, filtrada.** Rejeitado pela razão acima: filtrar
por exclusão falha aberto. Selecionar dois tipos de evento pelo nome falha fechado, que é o lado
certo para falhar.

**Deixar o fornecedor escrever quando quisesse.** Rejeitado. Um canal aberto acabaria a servir de
correio eletrónico paralelo, sem nada que ligasse a mensagem a uma decisão à espera.

## Consequências

**Positivas**

- Uma fuga do lado interno para o portal teria de ser escrita deliberadamente.
- As regras de acesso continuam num sítio só, exercitadas pelos dois lados, o que significa que os
  testes do portal também são testes do avaliador.
- O percurso completo («fornecedor submete → aparece do lado interno → IA classifica →
  processo arranca») funciona sem nenhum caminho especial para submissões externas.

**Negativas**

- Há repetição deliberada entre os contratos do portal e os internos, e essa repetição tem de ser
  mantida à mão quando o significado de um campo muda nos dois lados.
- O portal fica limitado ao que o avaliador de acesso sabe exprimir. Uma regra que ele não consiga
  exprimir obriga a estendê-lo, o que é mais trabalho do que escrever a regra no portal, e é
  exatamente esse atrito que se quis.

**Sinais de revisão:** necessidade de expor o portal numa rede separada da aplicação interna, ou um
segundo tipo de utilizador externo (um cliente, por exemplo) cujas regras de visibilidade não sejam
exprimíveis como propriedade ou concessão explícita.

## Na prática: o que acontece a uma submissão

Cinco pormenores que a decisão não antecipava. Os três primeiros respondem à mesma pergunta: **uma
submissão que chega serve para quê?**

### Entra como «por classificar», não como fatura

O tipo «fatura de fornecedor» exige fornecedor, número, data e total. Um portal que os exigisse antes
de aceitar o ficheiro seria uma porta de entrada pior do que o e-mail que veio substituir, e o
fornecedor não tem como saber que número interno a organização lhe vai dar. A submissão entra como
«por classificar», o mesmo tipo do correio que chega, e cai onde a inteligência documental a
apanha.

### Entra no departamento que a recebe

O tipo de receção é confidencial por omissão: ninguém leu o ficheiro ainda, ninguém sabe o que ele
contém. E um documento confidencial sem departamento é visível apenas ao seu dono. Sem nomear o
departamento recetor, o fornecedor estaria a submeter para dentro de uma caixa fechada: nenhum
interno o veria.

Dizer que a submissão pertence ao departamento financeiro abre-a a quem trata de papelada de
fornecedores, pela regra de classificação que já existia, em vez de por uma permissão inventada para
o efeito. O departamento recetor é o financeiro (`FIN`), fixo no serviço do portal, porque é quem
recebe faturas.

### E entra com concessão explícita de contribuidor

Ver não chega. Iniciar um processo sobre um documento exige `Edit`, e a regra de classificação nunca
concede mais do que leitura ao departamento dono. Sem a concessão, a submissão chegaria, ver-se-ia, e não
se lhe poderia fazer nada: o fornecedor teria posto o documento numa vitrina.

A concessão é escrita pelo serviço do portal, não pela API de partilha, e a distinção é
deliberada: isto é a política permanente da organização sobre a sua própria porta de entrada, não um
fornecedor a decidir quem pode mexer no que enviou.

### Quem envia um ficheiro tem direito a relê-lo

O teto de permissões do perfil corta o `Download` mesmo a quem é **dono** do documento, por isso o
perfil externo tem `documents.download` além de `documents.view` e `documents.create`. Sem isso, um
fornecedor submeteria um ficheiro e receberia um comprovativo que não abre.

A permissão não alarga o que ele alcança, porque o avaliador continua a limitá-lo ao que é seu:
apenas deixa de lhe negar uma coisa sobre os seus próprios documentos.

### «Aguarda a sua resposta» tem de significar o mesmo nos dois sítios

Há duas leituras possíveis: a tarefa com pedido de informação em aberto, ou a última mensagem.
Divergem, porque a tarefa continua aberta depois de o fornecedor responder, e só fecha quando um
interno decide. A primeira mostraria «aguarda a sua resposta» numa lista que leva a um ecrã sem onde
responder.

A definição é a do fornecedor: **a última coisa dita foi uma pergunta**. A outra deixaria a marca
acesa para sempre.
