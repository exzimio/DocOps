import { test } from '@playwright/test'

test.describe('Capturas de workflows', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('tarefas, workflows e cronologia', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('ricardo.lopes@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.getByRole('link', { name: 'Tarefas' }).click()
    await page.waitForURL(/\/app\/tasks$/)
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/tasks.png', fullPage: true })

    await page.getByRole('link', { name: 'Workflows' }).click()
    await page.waitForURL(/\/app\/workflows$/)
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/workflows.png', fullPage: true })

    await page.locator('table tbody tr a').first().click()
    await page.waitForURL(/\/app\/workflows\/instances\/[0-9a-f-]+$/)
    await page.getByText('Cronologia').waitFor()
    await page.screenshot({ path: 'screenshots/workflow-instance.png', fullPage: true })
  })
})
