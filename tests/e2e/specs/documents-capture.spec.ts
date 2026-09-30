import { test } from '@playwright/test'
import { fullPageShot } from '../support/fullPageShot'

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
    await fullPageShot(page, 'screenshots/documents-list.png')

    await page.locator('table tbody tr a').first().click()
    await page.waitForURL(/\/app\/documents\/[0-9a-f-]+$/)
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/document-detail.png')

    await page.getByRole('tab', { name: /Versões/ }).click()
    await fullPageShot(page, 'screenshots/document-versions.png')

    await page.getByRole('tab', { name: 'Auditoria' }).click()
    await page.waitForTimeout(500)
    await fullPageShot(page, 'screenshots/document-audit.png')
  })
})
