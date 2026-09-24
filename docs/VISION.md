# DocOps Control Tower — o que é, e o que faz quando estiver concluído

> Documento de apresentação do projeto. Para a arquitetura, ver
> [`docs/ARCHITECTURE.md`](ARCHITECTURE.md).
>
> **Projeto de portefólio independente.** Demonstração técnica pessoal, sem afiliação a qualquer
> produto ou fornecedor comercial. Todas as organizações, pessoas e documentos são fictícios.

---

## 1. Em uma frase

Uma plataforma que trata um documento não como um ficheiro, mas como o **início de um processo de
negócio** — com responsáveis, prazos, decisões, prova e memória.

## 2. O problema

Nas organizações, os documentos que realmente importam — faturas de fornecedor, contratos,
encomendas, processos de colaboradores — chegam por e-mail, ficam em pastas partilhadas e circulam
por conversas informais. O resultado é sempre o mesmo:

| O que acontece | O que custa |
| --- | --- |
| A fatura chega ao e-mail de alguém | Ninguém sabe que existe até alguém se lembrar dela |
| A aprovação é dada numa conversa | Seis meses depois não há como provar quem aprovou |
| Os dados são copiados à mão para o ERP | Erros de digitação, tempo perdido, retrabalho |
| O prazo passa | Descobre-se com a chamada do fornecedor |
| Alguém pergunta «quem viu este contrato?» | Não há resposta |

Nenhum destes problemas é de tecnologia. São todos de **rasto**: ninguém sabe onde as coisas estão,
quem lhes tocou, nem o que falta fazer.

## 3. O que a plataforma faz

O percurso completo, tal como funciona hoje na demonstração:

1. **Uma fatura chega** — por correio eletrónico, submetida por um fornecedor no portal externo, ou
   carregada à mão. Seja qual for a porta, entra no mesmo sítio.
2. **É lida automaticamente** — a plataforma classifica o documento (é uma fatura? um contrato?) e
   extrai os campos (fornecedor, número, datas, valores), dizendo de que linha do documento tirou
   cada valor.
3. **Uma pessoa confirma** — nada é aplicado sem decisão humana. Quem valida vê a sugestão, a
   confiança associada e a citação exata; aceita, corrige ou rejeita, campo a campo.
4. **O processo arranca sozinho** — o circuito de aprovação certo para aquele tipo de documento:
   quem valida, quem aprova, com que prazo, o que acontece se ninguém responder.
5. **As pessoas certas recebem tarefas** — com prazos, prioridade, escalonamento automático quando o
   prazo passa, e possibilidade de reatribuir ou pedir mais informação.
6. **Tudo fica registado** — quem viu, quem descarregou, quem alterou, quem aprovou e quando. O
   registo é imutável: a aplicação não tem forma de o alterar ou apagar.
7. **Procura-se por linguagem natural** — «contratos do departamento jurídico aprovados este ano»
   devolve o que a pessoa que pergunta tem direito a ver, e nada mais.

## 4. O que estará concluído no fim

O projeto é construído em **dez fases**, cada uma entregue a funcionar de ponta a ponta. **Seis estão
concluídas; a sétima está em curso.**

### Já a funcionar

| Capacidade | O que significa na prática |
| --- | --- |
| **Repositório documental governado** | Documentos com versões, tipos, metadados configuráveis, etiquetas, políticas de retenção e classificação de sensibilidade |
| **Controlo de acesso explicável** | Cada pessoa vê exatamente o que lhe compete — e a aplicação mostra **porquê**, regra a regra |
| **Motor de circuitos de aprovação** | Circuitos desenhados visualmente, com condições, caminhos paralelos, prazos e escalonamento |
| **Desenhador visual de processos** | Quem percebe do negócio desenha o circuito; não é preciso programar |
| **Inteligência documental** | Classificação, extração de campos e perguntas sobre o conteúdo, sempre com validação humana |
| **Pesquisa por conteúdo e por pergunta** | Texto integral, filtros por qualquer dimensão, e perguntas em linguagem corrente |
| **Caixa de entrada documental** | O correio que chega transforma-se em documentos e processos, sem passos manuais |
| **Centro de integrações** | Ligações a ERP, CRM, correio, assinatura, webhooks e importação de ficheiros |

### A construir (fases 7 a 10)

| Capacidade | O que trará |
| --- | --- |
| **Portal externo** *(feito)* | Fornecedores submetem faturas e acompanham o estado, sem verem nada do que é interno |
| **Assinatura eletrónica** *(feita)* | Pedido de assinatura, estado, versão assinada e impressão digital do documento |
| **Relatórios e indicadores** | Tempo médio de aprovação, documentos em atraso, carga por departamento — sobre dados reais |
| **Administração** | Gestão de utilizadores, perfis, departamentos e tipos de documento pela interface |
| **Segurança e observabilidade** | Endurecimento, rastreio distribuído, testes de carga |
| **Acessibilidade e desempenho** | Auditoria de acessibilidade e afinação final |

## 5. Para quem serve

- **Departamentos financeiros** que recebem centenas de faturas por mês e precisam de as aprovar com
  rasto.
- **Departamentos jurídicos** que gerem contratos com datas de renovação que ninguém pode falhar.
- **Recursos humanos**, com documentação de colaboradores sujeita a prazos de conservação legal.
- **Qualquer organização auditada**, que tenha de responder a «mostre-me quem aprovou isto, quando, e
  com que documento à frente».

A plataforma é **multi-organização** desde a fundação: várias empresas partilham a mesma instalação
sem que nenhuma alcance os dados da outra — uma garantia verificada por testes automáticos que
falham a compilação se alguma entidade nova ficar desprotegida.

## 6. O que este projeto demonstra tecnicamente

Escrito de raiz, sem gerador de código nem *template*:

- **Backend** .NET 10 com C# 14, arquitetura em camadas explícitas, SQL Server real.
- **Frontend** React 19 com TypeScript, sistema de design próprio, tema claro/escuro, português e
  inglês.
- **Motor de workflow próprio** — não é um produto de terceiros embrulhado: as travessias do grafo
  são transacionais e falham de forma explícita em vez de saltarem passos.
- **Segurança levada a sério**: autenticação por cookie seguro, autorização por permissão validada no
  servidor em cada pedido, proteção contra falsificação de pedidos e contra pedidos forjados do lado
  do servidor, isolamento entre organizações em duas camadas independentes.
- **Cerca de 560 testes automáticos** — unitários, de integração contra SQL Server verdadeiro em
  contentor, e de ponta a ponta no browser, em ecrã de computador e de telemóvel.
- **Decisões documentadas**: sete registos de decisão arquitetural explicam não só o que foi feito,
  mas o que foi **rejeitado** e porquê.

## 7. Três princípios que moldam tudo

**Nada de fachada.** Não existe um único ecrã de demonstração estático, botão sem ação ou secção «em
breve». Tudo o que se vê está ligado à base de dados real. É uma restrição incómoda e é deliberada:
é o que separa um protótipo de software a funcionar.

**Honestidade sobre o que é simulado.** O projeto corre sem contas em serviços de terceiros. Onde
existe simulação — o fornecedor de IA de demonstração, a caixa de correio, as listas do ERP — isso é
dito no próprio ecrã, e diz-se **o quê**. E a regra corta nos dois sentidos: o que faz trabalho
verdadeiro não se rotula de demonstração só por estar atrás de uma abstração.

**A autorização é do servidor.** O que a interface esconde é conveniência; a decisão é sempre tomada
no servidor, e nunca a partir de dados enviados pelo cliente.

## 8. Demonstração em cinco minutos

1. Um fornecedor submete uma fatura no portal externo.
2. A fatura aparece do lado interno, por classificar.
3. A plataforma classifica-a e extrai os campos, com a citação de onde tirou cada valor.
4. Uma pessoa valida os campos — e cada decisão fica auditada.
5. O circuito de aprovação arranca automaticamente.
6. O responsável financeiro recebe a tarefa e aprova.
7. O documento fica aprovado e arquivado, com a cronologia completa visível.

Todo este percurso funciona contra a base de dados real. O guião detalhado, ecrã a ecrã, está em
[`docs/DEMO.md`](DEMO.md).

## 9. O que ainda não é

Por honestidade, e porque saber onde estão os limites faz parte de perceber o que se construiu:

- A **assinatura eletrónica** é uma demonstração de portefólio e **não tem validade legal**.
- O **fornecedor de IA** por omissão é determinista e local; a ligação a um modelo real existe, é
  opcional e está desligada.
- Os **conetores externos** (ERP, CRM, Microsoft Graph) têm contratos reais e implementações de
  demonstração — trocar por um sistema verdadeiro é escrever uma classe, não redesenhar.
- Não há **autenticação multifator** nem federação de identidade; a arquitetura suporta ambas.
