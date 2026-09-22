import { test } from '@playwright/test'

test.describe('Capturas da caixa de entrada', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('lista, detalhe e ações sobre uma mensagem', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('sofia.martins@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/app/inbox')
    await page.getByRole('heading', { name: 'Caixa de entrada' }).waitFor()
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/inbox-list.png', fullPage: true })

    // A fatura com anexo já vem selecionada por omissão (é a mensagem mais recente), mas clicamos
    // de qualquer forma para exercitar a seleção pela lista.
    await page.getByRole('list', { name: 'Mensagens' }).getByText('Mariana Quadros').click()
    await page.getByRole('button', { name: 'Guardar como documento' }).waitFor()
    await page.waitForTimeout(300)
    await page.screenshot({ path: 'screenshots/inbox-detail.png', fullPage: true })

    await page.getByRole('button', { name: 'Guardar como documento' }).click()
    await page.getByRole('dialog', { name: 'Guardar anexo como documento' }).waitFor()
    await page.screenshot({ path: 'screenshots/inbox-save-dialog.png' })
  })
})
