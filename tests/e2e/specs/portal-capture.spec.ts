import { test } from '@playwright/test'
import { clickReliably } from '../support/interactions'
import { askTheSupplier, undoTheQuestion } from '../support/portalQuestion'

test.describe('Capturas do portal externo', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('submeter, acompanhar e responder', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('contacto@northstar.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()

    await page.waitForURL(/\/portal$/)
    await page.getByRole('heading', { name: 'As suas submissões' }).waitFor()
    await page.waitForLoadState('networkidle')
    await page.screenshot({
      path: 'screenshots/portal-list.png',
      fullPage: true,
    })

    // Submeter um documento a sério.
    await page.getByLabel('Assunto').fill('Fatura NS-2026-0999')
    await page.getByLabel('Nota (opcional)').fill('Servicos de outubro.')
    await page.setInputFiles('#portal-file', {
      name: 'NS-2026-0999.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('FATURA NS-2026-0999\nNorthstar Supplies\nTotal: 980,00 EUR\n'),
    })
    await clickReliably(page.getByRole('button', { name: 'Submeter' }))
    await page.getByText('Fatura NS-2026-0999').first().waitFor({ timeout: 15_000 })
    await page.waitForTimeout(400)
    await page.screenshot({
      path: 'screenshots/portal-submitted.png',
      fullPage: true,
    })

    // Abrir o detalhe de uma submissão.
    await clickReliably(page.getByRole('link', { name: /Fatura NS-2026-0999/ }).first())
    await page.getByRole('heading', { name: /Perguntas e respostas/ }).waitFor()
    await page.waitForTimeout(300)
    await page.screenshot({
      path: 'screenshots/portal-detail.png',
      fullPage: true,
    })

    // Uma submissão com pergunta em aberto mostra o formulário de resposta — e a pergunta tem de
    // ser feita, porque a semente não traz nenhuma. Enquanto isto esperava por um estado que nunca
    // existia, esta captura falhava sem que ninguém desse por isso: os ficheiros de captura são
    // saltados na suíte normal, e nada mais os corria.
    const documentId = page.url().split('/').pop()!

    try {
      await askTheSupplier(documentId, 'Falta o número de encomenda. Pode indicar?')

      await page.goto('/portal')
      await clickReliably(page.getByRole('link').filter({ hasText: 'Aguarda a sua resposta' }).first())
      await page.getByRole('heading', { name: /Perguntas e respostas/ }).waitFor()
      await page.getByLabel('A sua resposta').waitFor()
      await page.screenshot({
        path: 'screenshots/portal-question.png',
        fullPage: true,
      })
    } finally {
      await undoTheQuestion(documentId)
    }
  })
})
