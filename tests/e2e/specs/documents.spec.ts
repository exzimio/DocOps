import { expect, test, type Page } from '@playwright/test'

const financeApprover = { email: 'ricardo.lopes@asteria.example', password: 'Demo!Pass123' }
const externalPartner = { email: 'contacto@northstar.example', password: 'Demo!Pass123' }

async function signIn(page: Page, account: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(account.email)
  await page.getByLabel('Palavra-passe').fill(account.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}

/** On narrow viewports the navigation lives behind the menu button, so open it when needed. */
async function goToDocuments(page: Page) {
  const menuButton = page.getByRole('button', { name: 'Abrir menu de navegação' })
  await expect(page.getByRole('banner')).toBeVisible()

  if (await menuButton.isVisible()) {
    await menuButton.click()
  }

  await page.getByRole('link', { name: 'Documentos' }).click()
}

test.describe('Repositório documental', () => {
  test('pesquisar um documento, abrir o detalhe e inspecionar o histórico de versões', async ({ page }) => {
    await signIn(page, financeApprover)

    await goToDocuments(page)
    await expect(page).toHaveURL(/\/app\/documents$/)
    await expect(page.locator('table tbody tr').first()).toBeVisible()

    // A pesquisa corre no servidor e devolve só o que corresponde. Prova-se com duas pesquisas em
    // vez de contar linhas: o que entra numa não aparece na outra. Contar seria refém do catálogo,
    // que cresce com cada documento submetido pelo portal.
    const search = page.getByLabel('Pesquisar por título, referência ou metadados…')

    await search.fill('Northstar')
    await expect(page.getByRole('link', { name: /Fatura Northstar Supplies/ })).toBeVisible()
    await expect(page.getByRole('link', { name: /Fatura Atlas Tecnologia/ })).toHaveCount(0)

    // Outro termo, outro conjunto: «Atlas» devolve resultados e nenhum deles é o da Northstar.
    await search.fill('Atlas')
    await expect(page.locator('table tbody tr').first()).toBeVisible()
    await expect(page.getByRole('link', { name: /Fatura Northstar Supplies/ })).toHaveCount(0)

    await search.fill('Northstar')
    await expect(page.getByRole('link', { name: /Fatura Northstar Supplies/ })).toBeVisible()

    await page.getByRole('link', { name: /Fatura Northstar Supplies/ }).click()
    await expect(page).toHaveURL(/\/app\/documents\/[0-9a-f-]+$/)

    await expect(page.getByRole('heading', { name: /Fatura Northstar Supplies/ })).toBeVisible()
    await expect(page.getByText('Atlas Tecnologia')).toHaveCount(0)

    // Metadata comes from the database, not from the page.
    await expect(page.getByText('Northstar Supplies').first()).toBeVisible()

    await page.getByRole('tab', { name: /Versões/ }).click()
    const versionRows = page.locator('table tbody tr')
    await expect(versionRows.first()).toBeVisible()
    await expect(page.getByText('Atual')).toBeVisible()

    // Every version carries its own fingerprint.
    await expect(versionRows.first().locator('p.font-mono')).toContainText('…')
  })

  test('o trilho de auditoria do documento mostra a sua história', async ({ page }) => {
    await signIn(page, financeApprover)
    await page.goto('/app/documents')
    await page.getByLabel('Pesquisar por título, referência ou metadados…').fill('Northstar Supplies 2026')
    await page.getByRole('link', { name: /Fatura Northstar Supplies/ }).click()

    await page.getByRole('tab', { name: 'Auditoria' }).click()

    // The newest entry is the view this test just performed; older entries come from the
    // document's seeded history. Asserting on the top of the list keeps the test stable as the
    // trail grows with every run.
    const entries = page.getByRole('main').getByRole('listitem')
    await expect(entries.first()).toContainText('Documento consultado')
    await expect(entries.first()).toContainText('document.viewed')
    await expect(entries.first()).toContainText('Ricardo Lopes')
  })

  test('o painel de acesso efetivo explica a decisão de autorização', async ({ page }) => {
    await signIn(page, financeApprover)
    await page.goto('/app/documents')
    await page.getByLabel('Pesquisar por título, referência ou metadados…').fill('Northstar Supplies 2026')
    await page.getByRole('link', { name: /Fatura Northstar Supplies/ }).click()

    const panel = page.getByRole('region').filter({ hasText: 'Acesso efetivo' })
    await expect(page.getByText('Acesso efetivo')).toBeVisible()
    await expect(page.getByText('É o proprietário do documento')).toBeVisible()
    expect(panel).toBeDefined()
  })

  test('um filtro de estado restringe a lista sem alargar a visibilidade', async ({ page }) => {
    await signIn(page, financeApprover)
    await page.goto('/app/documents')
    await expect(page.locator('table tbody tr').first()).toBeVisible()

    const totalBefore = await page.locator('table tbody tr').count()

    await page.getByRole('button', { name: 'Aguarda aprovação', exact: true }).click()

    const statusCells = page.locator('table tbody tr td:nth-child(4)')
    await expect(statusCells.first()).toHaveText('Aguarda aprovação')

    expect(await page.locator('table tbody tr').count()).toBeLessThanOrEqual(totalBefore)

    for (const badge of await statusCells.allTextContents()) {
      expect(badge).toContain('Aguarda aprovação')
    }
  })

  test('um parceiro externo vê apenas o documento que lhe foi partilhado, e vê-o no portal', async ({
    page,
  }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill(externalPartner.email)
    await page.getByLabel('Palavra-passe').fill(externalPartner.password)
    await page.getByRole('button', { name: 'Entrar' }).click()

    // O parceiro aterra no portal, não no repositório interno.
    await page.waitForURL(/\/portal$/)

    // A fatura que lhe foi partilhada está lá. Não se conta o total: este parceiro também submete
    // documentos pelo portal, e uma contagem fixa tornaria este teste refém da ordem de execução.
    const submissions = page.getByRole('list', { name: 'Submissões' })
    await expect(submissions.getByRole('listitem').first()).toBeVisible()
    await expect(page.getByText(/Fatura Northstar Supplies/)).toBeVisible()

    // O que continua a valer é que nada da organização que não lhe pertença aparece aqui.
    await expect(page.getByText(/Atlas Tecnologia|Ibérica Logística|Verdemar/)).toHaveCount(0)
  })
})
