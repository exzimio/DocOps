import { test } from '@playwright/test'

test.describe('Capturas para a revisão de UX', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('ecrãs principais e estados vazios', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/app/documents')
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/ux-documents.png', fullPage: true })

    // O estado vazio que se vê mais vezes: uma pesquisa que não encontra nada.
    await page.goto('/app/documents?q=zzzznaoexiste')
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/ux-documents-empty.png', fullPage: true })

    await page.goto('/app/search')
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/ux-search-initial.png', fullPage: true })

    // O ecrã de abertura das tarefas já é um estado vazio para esta conta: tem tarefas, mas nenhuma
    // em aberto. É onde a mensagem tinha de deixar de afirmar uma causa que não conhece.
    await page.goto('/app/tasks')
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/ux-tasks.png', fullPage: true })

    await page.getByRole('button', { name: 'Alargar os filtros' }).click()

    // Esperar pelas linhas, e não por `networkidle`: entre o clique e a resposta o ecrã mostra os
    // dados anteriores, e uma captura tirada aí retrata um estado que ninguém chega a ler.
    await page.getByRole('main').locator('table tbody tr').first().waitFor()
    await page.screenshot({ path: 'screenshots/ux-tasks-widened.png', fullPage: true })
  })
})

test.describe('Captura da paleta de comandos', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('aberta, com um termo escrito', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)
    await page.locator('body').click()

    await page.keyboard.press('Control+k')
    await page.keyboard.type('contrato')
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/ux-command-palette.png' })
  })
})
