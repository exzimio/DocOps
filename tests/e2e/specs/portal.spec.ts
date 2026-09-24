import { expect, test, type Page } from '@playwright/test'
import { clickReliably } from '../support/interactions'

const supplier = { email: 'contacto@northstar.example', password: 'Demo!Pass123' }
const admin = { email: 'admin@asteria.example', password: 'Demo!Pass123' }

async function signIn(page: Page, account: { email: string; password: string }) {
  // Sair primeiro: com sessão aberta, /login reencaminha para a área de quem está autenticado e o
  // formulário nunca chega a aparecer.
  await page.context().clearCookies()
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(account.email)
  await page.getByLabel('Palavra-passe').fill(account.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
}

test.describe('Portal externo', () => {
  test('um fornecedor entra no portal e um interno na aplicação, e nenhum atravessa', async ({ page }) => {
    await signIn(page, supplier)
    await page.waitForURL(/\/portal$/)
    await expect(page.getByRole('heading', { name: 'As suas submissões' })).toBeVisible()

    // Escrever o endereço à mão não abre a aplicação interna: o fornecedor volta ao portal.
    await page.goto('/app/documents')
    await page.waitForURL(/\/portal$/)

    // E o inverso: um interno não tem nada que fazer no portal.
    await signIn(page, admin)
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/portal')
    await page.waitForURL(/\/app\/dashboard$/)
  })

  test('o portal mostra o nome do fornecedor e nenhuma navegação interna', async ({ page }) => {
    await signIn(page, supplier)
    await page.waitForURL(/\/portal$/)

    // O cabeçalho identifica a organização que o visitante representa, não a que o recebe.
    await expect(page.getByText('Northstar Supplies').first()).toBeVisible()

    // Nada da navegação interna existe aqui — um convidado não deve sequer conseguir deduzir,
    // pela moldura, que ecrãs a organização tem.
    for (const internal of ['Documentos', 'Tarefas', 'Workflows', 'Caixa de entrada', 'Integrações']) {
      await expect(page.getByRole('link', { name: internal })).toHaveCount(0)
    }
  })

  test('submeter um documento cria uma submissão a sério, com ficheiro e estado', async ({ page }) => {
    await signIn(page, supplier)
    await page.waitForURL(/\/portal$/)

    const subject = `Fatura e2e ${Date.now()}`

    await page.getByLabel('Assunto').fill(subject)
    await page.getByLabel('Nota (opcional)').fill('Submetida por um teste automático.')
    await page.setInputFiles('#portal-file', {
      name: 'fatura-e2e.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('FATURA E2E\nNorthstar Supplies\nTotal: 500,00 EUR\n'),
    })
    await clickReliably(page.getByRole('button', { name: 'Submeter' }))

    const row = page.getByRole('list', { name: 'Submissões' }).getByRole('link').filter({ hasText: subject })
    await expect(row).toBeVisible({ timeout: 15_000 })
    await expect(row).toContainText('Recebido')

    // O detalhe mostra o que foi enviado e a nota que o acompanhou.
    await clickReliably(row)
    await expect(page.getByRole('heading', { name: subject })).toBeVisible()
    await expect(page.getByText('fatura-e2e.txt')).toBeVisible()
    await expect(page.getByText(/Submetida por um teste automático/)).toBeVisible()

    // Sem pergunta em aberto não há onde responder — o formulário só aparece quando faz sentido.
    await expect(page.getByLabel('A sua resposta')).toHaveCount(0)
    await expect(page.getByText('Ainda não há nada a perguntar sobre esta submissão.')).toBeVisible()

    // Limpa o que este teste criou, para o catálogo de demonstração ficar como estava. O fornecedor
    // não pode apagar o que submeteu — e não deve — por isso é um administrador que o faz.
    const submissionUrl = page.url()
    const documentId = submissionUrl.split('/').pop()!

    await signIn(page, admin)
    await page.waitForURL(/\/app\/dashboard$/)
    await page.goto(`/app/documents/${documentId}`)

    page.once('dialog', (confirmation) => confirmation.accept())
    await clickReliably(page.getByRole('button', { name: 'Eliminar' }))
    await page.waitForURL(/\/app\/documents$/)
  })

  test('o fornecedor descarrega o ficheiro que enviou', async ({ page }) => {
    await signIn(page, supplier)
    await page.waitForURL(/\/portal$/)

    await clickReliably(page.getByRole('list', { name: 'Submissões' }).getByRole('link').first())
    await expect(page.getByRole('link', { name: /Voltar às submissões/ })).toBeVisible()

    // Um fornecedor que não consegue reler o que enviou recebeu um comprovativo que não abre.
    const download = page.waitForEvent('download')
    await clickReliably(page.getByRole('button', { name: 'Descarregar' }))
    expect((await download).suggestedFilename()).not.toBe('')
  })

  test('sem pergunta em aberto, o detalhe não oferece caixa de resposta', async ({ page }) => {
    await signIn(page, supplier)
    await page.waitForURL(/\/portal$/)

    // A lista e o detalhe têm de concordar sobre o que é «aguarda a sua resposta»: onde a lista
    // não marca nada, o detalhe também não pede nada.
    const unmarked = page
      .getByRole('list', { name: 'Submissões' })
      .getByRole('link')
      .filter({ hasNotText: 'Aguarda a sua resposta' })
      .first()

    await clickReliably(unmarked)
    await expect(page.getByRole('heading', { name: 'Perguntas e respostas' })).toBeVisible()
    await expect(page.getByLabel('A sua resposta')).toHaveCount(0)
  })
})
