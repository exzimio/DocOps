import { test } from '@playwright/test'
import { fullPageShot } from '../support/fullPageShot'

/**
 * Captures the current state of the interface. Not an assertion suite: it exists so the visual
 * result of a change can be reviewed, and is skipped unless E2E_CAPTURE is set.
 */
test.describe('Capturas de ecrã', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('landing, login e painel', async ({ page }) => {
    await page.goto('/')
    await fullPageShot(page, 'screenshots/landing.png')

    await page.goto('/login')
    await fullPageShot(page, 'screenshots/login.png')

    await page.getByLabel('Endereço de e-mail').fill('ricardo.lopes@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    // Esperar pelo conteúdo, e não só pelo endereço. `fullPage` fotografa a altura que a página tem
    // no instante do disparo: com os cartões ainda por chegar, essa altura é a da janela, e o
    // retrato sai cortado a meio de uma linha. Foi o que aconteceu — a captura clara media 853 px e
    // a escura, da mesma página momentos depois, media 1224.
    await page.getByRole('heading', { name: 'Departamentos' }).waitFor()
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/dashboard.png')

    await page.evaluate(() => {
      document.documentElement.dataset.theme = 'dark'
    })
    await fullPageShot(page, 'screenshots/dashboard-dark.png')
  })
})
