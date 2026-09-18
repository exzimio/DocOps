import { test } from '@playwright/test'

test.describe('Capturas de documentos', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('lista e detalhe de documento', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('ricardo.lopes@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.getByRole('link', { name: 'Documentos' }).click()
    await page.waitForURL(/\/app\/documents$/)
    await page.waitForSelector('table tbody tr td')
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/documents-list.png', fullPage: true })

    await page.locator('table tbody tr a').first().click()
    await page.waitForURL(/\/app\/documents\/[0-9a-f-]+$/)
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/document-detail.png', fullPage: true })

    await page.getByRole('tab', { name: /Versões/ }).click()
    await page.screenshot({ path: 'screenshots/document-versions.png', fullPage: true })

    await page.getByRole('tab', { name: 'Auditoria' }).click()
    await page.waitForTimeout(500)
    await page.screenshot({ path: 'screenshots/document-audit.png', fullPage: true })
  })
})
