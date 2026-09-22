import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'

/** A Manager: holds ai.use to review and documents.delete to clean up after the test. */
const manager = { email: 'sofia.martins@asteria.example', password: 'Demo!Pass123' }
const externalPartner = { email: 'contacto@northstar.example', password: 'Demo!Pass123' }

const invoiceSample = path.join(__dirname, '..', '..', '..', 'samples', 'fatura-atlas-tecnologia.txt')

async function signIn(page: Page, account: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(account.email)
  await page.getByLabel('Palavra-passe').fill(account.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}

/**
 * Uploads a sample as "Por classificar" — no type chosen, no field filled in — which is the state a
 * document arrives in before anyone has read it. Returns its URL.
 */
async function uploadIntakeDocument(page: Page, title: string): Promise<string> {
  await page.goto('/app/documents')
  await page.getByRole('button', { name: 'Carregar documento' }).click()

  const dialog = page.getByRole('dialog', { name: 'Novo documento' })
  await dialog.getByLabel('Ficheiro').setInputFiles(invoiceSample)
  await dialog.getByLabel('Título').fill(title)
  await dialog.getByLabel('Tipo').selectOption({ label: 'Por classificar' })
  await dialog.getByRole('button', { name: 'Criar documento' }).click()

  await page.waitForURL(/\/app\/documents\/[0-9a-f-]+$/)
  return page.url()
}

async function deleteDocument(page: Page, url: string) {
  page.once('dialog', (confirmation) => confirmation.accept())
  await page.goto(url)
  await page.getByRole('button', { name: 'Eliminar' }).click()
  await page.waitForURL(/\/app\/documents$/)
}

test.describe('Inteligência documental', () => {
  test('uma fatura por classificar é lida, revista, aplicada e fica registada na auditoria', async ({ page }) => {
    await signIn(page, manager)
    const documentUrl = await uploadIntakeDocument(page, `Fatura recebida E2E ${Date.now()}`)

    try {
      // A document with no type points the reviewer at the intelligence tab.
      await expect(page.getByText('Este documento ainda não tem tipo.')).toBeVisible()
      await page.getByRole('button', { name: 'Rever com IA' }).click()

      // Nothing has been read yet: the analysis is something a person asks for.
      await page.getByRole('button', { name: 'Analisar documento' }).click()

      // Whatever the provider is, the screen says which one produced this.
      await expect(page.getByRole('note').first()).toContainText('Modo de demonstração')

      // The document's own text is shown beside the suggestions, numbered as the citations number it.
      await expect(page.getByLabel('Texto do documento').getByText('Total a pagar: 1 906,50 €')).toBeVisible()

      // The classification is a suggestion with the line that supports it.
      const classification = page.getByRole('main').locator('section', { hasText: 'CLASSIFICAÇÃO' }).first()
      await expect(classification).toContainText('Fatura de fornecedor')
      await expect(classification.getByRole('button', { name: /linha 5/ })).toBeVisible()

      // Applying is refused while the classification is undecided: changing a type is never a default.
      await expect(page.getByRole('button', { name: 'Aplicar ao documento' })).toBeDisabled()

      await page.getByRole('button', { name: 'Passar a Fatura de fornecedor' }).click()
      await page.getByRole('button', { name: 'Aceitar tudo o que foi encontrado' }).click()

      const apply = page.getByRole('button', { name: 'Aplicar ao documento' })
      await expect(apply).toBeEnabled()
      await apply.click()
      await expect(page.getByText(/Aplicada por Sofia Martins/)).toBeVisible()

      // Only now does anything reach the document.
      await page.getByRole('tab', { name: 'Visão geral' }).click()
      await expect(page.getByRole('main')).toContainText('Fatura de fornecedor')
      await expect(page.getByRole('main')).toContainText('FT 2026/0815')

      // Values written from an accepted suggestion carry the confidence they were accepted with.
      await expect(page.getByText(/^IA \d+%$/).first()).toBeVisible()

      // Every decision is in the document's own audit trail, not only in some AI log.
      await page.getByRole('tab', { name: 'Auditoria' }).click()
      await expect(page.getByRole('main')).toContainText('ai.run.applied')
      await expect(page.getByRole('main')).toContainText('ai.suggestion.accepted')
      await expect(page.getByRole('main')).toContainText('document.reclassified')
    } finally {
      await deleteDocument(page, documentUrl)
    }
  })

  test('uma pergunta é respondida com a linha do documento que a sustenta', async ({ page }) => {
    await signIn(page, manager)
    const documentUrl = await uploadIntakeDocument(page, `Pergunta E2E ${Date.now()}`)

    try {
      await page.getByRole('button', { name: 'Rever com IA' }).click()
      await page.getByRole('button', { name: 'Qual é o total?' }).click()

      const answer = page.getByRole('main').locator('[aria-live="polite"]')
      await expect(answer).toContainText('906,50')

      // The citation names a line of this document, and clicking it shows that line.
      const citation = answer.getByRole('button', { name: /linha 21/ })
      await expect(citation).toContainText('Total a pagar')
      await citation.click()

      // What the document does not say is not answered.
      await page.getByLabel('Pergunta sobre o documento').fill('Qual é a cor do logótipo?')
      await page.getByRole('button', { name: 'Perguntar' }).click()
      await expect(answer).toContainText('Não encontrei esta informação no documento.')
      await expect(answer.getByRole('button', { name: /linha/ })).toHaveCount(0)
    } finally {
      await deleteDocument(page, documentUrl)
    }
  })

  test('sem a permissão de IA não há centro de inteligência nem separador', async ({ page }) => {
    await signIn(page, externalPartner)

    await expect(page.getByRole('link', { name: 'Centro de IA' })).toHaveCount(0)

    // The partner can open the document shared with them, but not its intelligence tab.
    await page.goto('/app/documents')
    await page.getByRole('main').locator('table tbody tr a').first().click()
    await page.waitForURL(/\/app\/documents\/[0-9a-f-]+$/)

    await expect(page.getByRole('tab', { name: 'Visão geral' })).toBeVisible()
    await expect(page.getByRole('tab', { name: 'Inteligência' })).toHaveCount(0)
  })
})
