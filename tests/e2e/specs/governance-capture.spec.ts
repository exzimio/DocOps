import { test } from '@playwright/test'
import { clickReliably } from '../support/interactions'

test.describe('Capturas de etiquetas e retenção', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('etiquetas, remoção reversível e políticas de retenção', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/app/admin')
    await clickReliably(page.getByRole('tab', { name: 'Etiquetas' }))
    await page.getByRole('heading', { name: 'Etiquetas' }).waitFor()
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/admin-tags.png', fullPage: true })

    await clickReliably(page.getByRole('tab', { name: 'Retenção' }))
    await page.getByRole('heading', { name: 'Políticas de retenção' }).waitFor()
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/admin-retention.png', fullPage: true })

    // A recusa: retirar uma política que tipos ainda adotam.
    const adopted = page.getByRole('row').filter({ hasText: 'CTR-10A' })
    await clickReliably(adopted.getByRole('button', { name: 'Retirar' }))
    await page.getByRole('alert').waitFor()
    await page.screenshot({ path: 'screenshots/admin-retention-refusal.png', fullPage: true })
  })
})
