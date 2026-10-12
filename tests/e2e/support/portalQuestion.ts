import { expect, request, type APIRequestContext } from '@playwright/test'

// O mesmo endereço que a configuração da suíte usa, para correr noutro porto sem editar nada aqui.
const ORIGEM = process.env.E2E_BASE_URL ?? 'http://localhost:5173'

/**
 * Põe uma submissão do portal à espera de resposta do fornecedor.
 *
 * O caminho é o real e passa por três pessoas: o fornecedor submete, alguém das finanças arranca o
 * processo, e quem fica com a primeira tarefa é que pergunta. Fazer isso pela interface seriam três
 * sessões e uma dúzia de ecrãs para chegar ao ecrã que se quer ver — por isso a preparação vai pela
 * API, com as contas verdadeiras e as permissões verdadeiras, e o browser fica para o que está sob
 * teste: o que o fornecedor vê e o que consegue responder.
 */
export async function askTheSupplier(documentId: string, question: string): Promise<void> {
  const financeiro = await sessao('ricardo.lopes@asteria.example')
  const comprador = await sessao('ana.ferreira@asteria.example')

  const definicoes = await json(financeiro, '/api/workflows')
  const definicao = definicoes.find((d: { isActive: boolean }) => d.isActive)
  expect(definicao, 'nenhuma definição de workflow ativa no catálogo').toBeTruthy()

  const arranque = await financeiro.post('/api/workflows/instances', {
    data: { definitionId: definicao.id, documentId },
  })
  expect(arranque.ok(), `arrancar o processo: ${arranque.status()}`).toBeTruthy()

  // A primeira tarefa do processo de faturas é de validação, e pertence a compras — é por isso que
  // quem pergunta ao fornecedor não é quem recebeu a submissão.
  const tarefas = await json(comprador, '/api/tasks?scope=Mine&page=1&pageSize=50&statuses=0&statuses=1')
  const tarefa = tarefas.items.find((t: { documentId: string }) => t.documentId === documentId)
  expect(tarefa, 'o processo não deixou nenhuma tarefa aberta em compras').toBeTruthy()

  const pergunta = await comprador.post(`/api/tasks/${tarefa.id}/request-information`, {
    data: { comment: question },
  })
  expect(pergunta.ok(), `pedir informação: ${pergunta.status()}`).toBeTruthy()

  await financeiro.dispose()
  await comprador.dispose()
}

/**
 * Desfaz o que a preparação criou: cancela o processo e elimina o documento.
 *
 * Um documento com processo a correr não se elimina — e ainda bem, senão um processo ficaria a
 * apontar para nada. Por isso a limpeza é por esta ordem, e existe para o catálogo de demonstração
 * ficar como estava.
 */
export async function undoTheQuestion(documentId: string): Promise<void> {
  const administrador = await sessao('admin@asteria.example')

  const instancias = await json(administrador, '/api/workflows/instances?page=1&pageSize=50')
  const instancia = instancias.items.find((i: { documentId: string }) => i.documentId === documentId)

  if (instancia) {
    await administrador.post(`/api/workflows/instances/${instancia.id}/cancel`, {
      data: { reason: 'Limpeza de um teste automático.' },
    })
  }

  await administrador.delete(`/api/documents/${documentId}`)
  await administrador.dispose()
}

async function sessao(email: string): Promise<APIRequestContext> {
  // `Origin` explícito: a API recusa um pedido sem origem permitida, e é isso que protege contra
  // CSRF. Um contexto de API que não o enviasse levava 403 e parecia um defeito de permissões.
  const contexto = await request.newContext({
    baseURL: ORIGEM,
    extraHTTPHeaders: { Origin: ORIGEM },
  })

  const entrada = await contexto.post('/api/auth/login', {
    data: { email, password: 'Demo!Pass123' },
  })
  expect(entrada.ok(), `entrar como ${email}: ${entrada.status()}`).toBeTruthy()

  return contexto
}

async function json(contexto: APIRequestContext, caminho: string) {
  const resposta = await contexto.get(caminho)
  expect(resposta.ok(), `${caminho}: ${resposta.status()}`).toBeTruthy()

  return resposta.json()
}
