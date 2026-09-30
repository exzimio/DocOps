import { test } from '@playwright/test'
import { clickReliably } from '../support/interactions'
import { fullPageShot } from '../support/fullPageShot'

test.describe('Capturas da administração', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('pessoas, perfis e departamentos', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/app/admin')
    await page.getByRole('heading', { name: 'Administração' }).waitFor()
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/admin-users.png')

    // O formulário preenchido, com os perfis oferecidos e a contagem de permissões de cada um.
    await page.getByLabel('Endereço de e-mail').fill('nova.pessoa@asteria.example')
    await page.getByLabel('Nome próprio').fill('Nova')
    await page.getByLabel('Apelido').fill('Pessoa')
    await page.getByLabel('Função').fill('Analista')
    await fullPageShot(page, 'screenshots/admin-user-form.png')

    await clickReliably(page.getByRole('tab', { name: 'Departamentos' }))
    await page.waitForLoadState('networkidle')
    await fullPageShot(page, 'screenshots/admin-departments.png')
  })
})
