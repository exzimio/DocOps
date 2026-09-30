import { test } from '@playwright/test'
import { fullPageShot } from '../support/fullPageShot'

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
    await fullPageShot(page, 'screenshots/tasks.png')

    await page.getByRole('link', { name: 'Workflows' }).click()
    await page.waitForURL(/\/app\/workflows$/)
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/workflows.png')

    await page.locator('table tbody tr a').first().click()
    await page.waitForURL(/\/app\/workflows\/instances\/[0-9a-f-]+$/)
    await page.getByText('Cronologia').waitFor()
    await fullPageShot(page, 'screenshots/workflow-instance.png')
  })

  test('desenhador de processos', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/app/workflows')
    await page.getByRole('link', { name: 'Desenhar' }).first().click()
    await page.waitForURL(/\/app\/workflows\/[0-9a-f-]+\/designer$/)
    await page.getByRole('heading', { name: 'Desenhador de processos' }).waitFor()
    await page.locator('.react-flow__node').first().waitFor()
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/workflow-designer.png')
  })
})
