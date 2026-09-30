import { test } from '@playwright/test'
import { fullPageShot } from '../support/fullPageShot'

test.describe('Capturas de pesquisa', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('texto integral, filtros e pergunta em linguagem natural', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('ana.ferreira@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/app/search')
    await page.getByRole('heading', { name: 'Pesquisa' }).waitFor()
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/search-empty.png')

    await page.getByRole('searchbox').fill('atlas tecnologia')
    await page
      .getByText(/de relevância/)
      .first()
      .waitFor()
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/search-results.png')

    await page.getByRole('button', { name: 'Mostrar filtros' }).click()
    await page.getByRole('button', { name: 'A aguardar', exact: true }).waitFor()
    await page.waitForTimeout(300)
    await fullPageShot(page, 'screenshots/search-filters.png')

    await page.getByRole('button', { name: 'Ocultar filtros' }).click()
    await page.getByRole('searchbox').fill('')
    await page.getByLabel('Pergunta em linguagem natural').fill('faturas que aguardam aprovação')
    await page.getByRole('button', { name: 'Perguntar' }).click()
    await page.getByText(/A pergunta foi lida como os filtros/).waitFor()
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/search-natural.png')
  })
})
