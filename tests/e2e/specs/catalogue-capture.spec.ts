import { test } from '@playwright/test'
import { clickReliably } from '../support/interactions'
import { fullPageShot } from '../support/fullPageShot'

test.describe('Capturas do catálogo de documentos', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('tipos, campos e uma recusa explicada', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/app/admin')
    await clickReliably(page.getByRole('tab', { name: 'Catálogo' }))
    await page.getByRole('heading', { name: 'Tipos de documento' }).waitFor()
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/admin-types.png')

    // Os campos de um tipo com documentos: é onde as recusas aparecem explicadas pelo número.
    const row = page.getByRole('row').filter({ hasText: 'SupplierInvoice' })
    await clickReliably(row.getByRole('button', { name: 'Campos' }))
    await page.getByRole('heading', { name: /Campos de/ }).waitFor()
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/admin-fields.png')

    // E a recusa do servidor, tal como o utilizador a lê.
    await page.getByLabel('Rótulo').fill('Campo de ensaio')
    await page.getByLabel('Código', { exact: true }).last().fill('ensaio')
    await page.getByLabel('Obrigatório').check()
    await clickReliably(page.getByRole('button', { name: 'Criar campo' }))
    await page.getByRole('alert').waitFor()
    await fullPageShot(page, 'screenshots/admin-fields-refusal.png')
  })
})
