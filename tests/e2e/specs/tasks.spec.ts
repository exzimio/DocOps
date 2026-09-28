import { expect, test, type Page } from '@playwright/test'

const financeApprover = { email: 'ricardo.lopes@asteria.example', password: 'Demo!Pass123' }
const buyer = { email: 'ana.ferreira@asteria.example', password: 'Demo!Pass123' }

async function signIn(page: Page, account: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(account.email)
  await page.getByLabel('Palavra-passe').fill(account.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}

/** On narrow viewports the navigation lives behind the menu button. */
async function navigateTo(page: Page, label: string) {
  const menuButton = page.getByRole('button', { name: 'Abrir menu de navegação' })
  await expect(page.getByRole('banner')).toBeVisible()

  if (await menuButton.isVisible()) {
    await menuButton.click()
  }

  await page.getByRole('link', { name: label }).click()
}

test.describe('Centro de tarefas', () => {
  test('o centro de tarefas mostra o trabalho em aberto com indicadores de prazo', async ({ page }) => {
    await signIn(page, financeApprover)
    await navigateTo(page, 'Tarefas')

    await expect(page).toHaveURL(/\/app\/tasks$/)
    await expect(page.getByRole('heading', { name: 'Tarefas' })).toBeVisible()

    // The KPI row is computed by the server from real task data.
    await expect(page.getByText('Em atraso', { exact: true }).first()).toBeVisible()

    const rows = page.getByRole('main').locator('table tbody tr')
    await expect(rows.first()).toBeVisible()

    // Decision tasks offer approve and reject; the server decides which actions exist.
    await expect(rows.first().getByRole('button', { name: 'Aprovar' })).toBeVisible()
    await expect(rows.first().getByRole('button', { name: 'Rejeitar' })).toBeVisible()
  })

  test('aprovar uma tarefa faz avançar o processo e atualiza o documento', async ({ page }) => {
    await signIn(page, buyer)

    // An invoice this user owns: the workflow panel only offers definitions that match the
    // document's type, so a purchase order would (correctly) offer nothing.
    await page.goto('/app/documents')
    await page.getByLabel('Pesquisar por título, referência ou metadados…').fill('Atlas Tecnologia 2026/0771')
    await page
      .getByRole('link', { name: /Fatura Atlas Tecnologia 2026\/0771/ })
      .first()
      .click()
    await page.waitForURL(/\/app\/documents\/[0-9a-f-]+$/)

    // Start the invoice approval process from the document itself.
    const workflowSelect = page.getByLabel('Iniciar um processo sobre este documento')
    await workflowSelect.selectOption({ label: 'Aprovação de fatura de fornecedor' })
    await page.getByRole('button', { name: 'Iniciar processo' }).click()

    await expect(page.getByText('Em espera').first()).toBeVisible()

    // The validation task lands on Procurement, which this user belongs to.
    await navigateTo(page, 'Tarefas')
    const row = page
      .getByRole('main')
      .locator('table tbody tr', { hasText: 'Validar dados da fatura' })
      .first()
    await expect(row).toBeVisible()

    await row.getByRole('button', { name: 'Concluir' }).click()
    await page.getByLabel('Comentário').fill('Dados conferidos com a encomenda.')
    await page.getByRole('button', { name: 'Confirmar' }).click()

    // The step is gone from the open list: the process moved on to approval.
    await expect(
      page.getByRole('main').locator('table tbody tr', { hasText: 'Validar dados da fatura' }),
    ).toHaveCount(0)
  })

  test('a cronologia do processo conta a história completa', async ({ page }) => {
    await signIn(page, financeApprover)
    await navigateTo(page, 'Workflows')

    await expect(page.getByRole('heading', { name: 'Workflows' })).toBeVisible()
    await expect(page.getByText('Aprovação de fatura de fornecedor').first()).toBeVisible()

    await page.getByRole('main').locator('table tbody tr a').first().click()
    await page.waitForURL(/\/app\/workflows\/instances\/[0-9a-f-]+$/)

    await expect(page.getByText('Cronologia')).toBeVisible()
    await expect(page.getByText(/Processo .* iniciado/)).toBeVisible()
    await expect(page.getByText('Variáveis do processo')).toBeVisible()
  })

  test('a definição do workflow mostra os passos e o estado de validação', async ({ page }) => {
    await signIn(page, financeApprover)
    await page.goto('/app/workflows')

    await page.getByRole('button', { name: 'Passos do processo' }).first().click()
    await page.waitForURL(/\/app\/workflows\/[0-9a-f-]+$/)

    await expect(page.getByRole('heading', { name: 'Problemas de validação' })).toBeVisible()
    await expect(page.getByText('Sem problemas de validação.').first()).toBeVisible()
    await expect(page.getByText('Início').first()).toBeVisible()
  })
})
