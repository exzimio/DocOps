import { test } from '@playwright/test'

test.describe('Capturas de inteligência documental', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('centro de IA, revisão de sugestões e resposta com citação', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('ana.ferreira@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/app/ai')
    await page.getByRole('heading', { name: 'Inteligência documental' }).waitFor()
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/ai-center.png', fullPage: true })

    // Analysing takes the reviewer straight to the document, on the intelligence tab.
    await page.getByRole('button', { name: 'Analisar documento' }).first().click()
    await page.waitForURL(/tab=intelligence/)
    await page.getByRole('heading', { name: 'Sugestões da IA' }).waitFor()
    await page.getByRole('heading', { name: 'Classificação' }).waitFor()
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/ai-review.png' })

    // Clicking a piece of evidence highlights the line it was read from.
    await page.getByRole('button', { name: /linha 21/ }).first().click()
    await page.waitForTimeout(600)
    await page.screenshot({ path: 'screenshots/ai-evidence.png' })

    await page.getByRole('button', { name: 'Qual é o total?' }).click()
    await page.getByText(/906,50/).first().waitFor()
    await page.getByRole('heading', { name: 'Perguntar ao documento' }).scrollIntoViewIfNeeded()
    await page.waitForTimeout(400)
    await page.screenshot({ path: 'screenshots/ai-ask.png' })
  })
})
