import { test } from '@playwright/test'
import { clickReliably } from '../support/interactions'

test.describe('Capturas do editor de permissões', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('concessões, prazo e o acesso explicado de outra pessoa', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/app/documents')
    await clickReliably(page.getByRole('row').nth(1).getByRole('link').first())
    await page.waitForURL(/\/app\/documents\/[0-9a-f-]+$/)

    await clickReliably(page.getByRole('tab', { name: 'Permissões' }))
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/permissions-editor.png', fullPage: true })

    // A peça central: explicar o acesso de outra pessoa.
    await page.getByLabel('Acesso de').selectOption({ index: 1 })
    await page.getByText('Não alcança este documento').waitFor()
    await page.screenshot({ path: 'screenshots/permissions-why.png', fullPage: true })
  })
})
