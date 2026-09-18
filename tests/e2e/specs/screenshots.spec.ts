import { test } from '@playwright/test'

/**
 * Captures the current state of the interface. Not an assertion suite: it exists so the visual
 * result of a change can be reviewed, and is skipped unless E2E_CAPTURE is set.
 */
test.describe('Capturas de ecrã', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('landing, login e painel', async ({ page }) => {
    await page.goto('/')
    await page.screenshot({ path: 'screenshots/landing.png', fullPage: true })

    await page.goto('/login')
    await page.screenshot({ path: 'screenshots/login.png', fullPage: true })

    await page.getByLabel('Endereço de e-mail').fill('ricardo.lopes@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)
    await page.screenshot({ path: 'screenshots/dashboard.png', fullPage: true })

    await page.evaluate(() => {
      document.documentElement.dataset.theme = 'dark'
    })
    await page.screenshot({ path: 'screenshots/dashboard-dark.png', fullPage: true })
  })
})
