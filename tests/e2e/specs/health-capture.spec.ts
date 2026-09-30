import { test } from '@playwright/test'
import { clickReliably } from '../support/interactions'
import { fullPageShot } from '../support/fullPageShot'

test.describe('Capturas da saúde do sistema', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('trabalho em segundo plano e trabalho à espera', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/app/admin')
    await clickReliably(page.getByRole('tab', { name: 'Sistema' }))
    await page.getByRole('heading', { name: 'Trabalho em segundo plano' }).waitFor()
    await page.getByRole('heading', { name: 'Trabalho à espera' }).waitFor()
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/admin-health.png')
  })
})
