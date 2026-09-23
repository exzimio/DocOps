import { test } from '@playwright/test'
import { clickReliably } from '../support/interactions'

test.describe('Capturas do centro de integrações', () => {
  test.skip(!process.env.E2E_CAPTURE, 'Defina E2E_CAPTURE=1 para gerar capturas de ecrã.')

  test('cartões, execução, registo e definições do webhook', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.goto('/app/integrations')
    await page.getByRole('heading', { name: 'Integrações' }).waitFor()
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: 'screenshots/integrations-list.png', fullPage: true })

    // O cartão do ERP: executar mostra uma reconciliação real sobre os documentos existentes.
    const erpCard = page.getByRole('region', { name: 'ERP' })
    await clickReliably(erpCard.getByRole('button', { name: 'Executar agora' }))
    await erpCard.getByText(/registo\(s\) reconciliado/).waitFor({ timeout: 15_000 })
    await page.waitForTimeout(300)
    await page.screenshot({ path: 'screenshots/integrations-erp-run.png', fullPage: true })

    await clickReliably(erpCard.getByRole('button', { name: 'Ver registo' }))
    const logDialog = page.getByRole('dialog', { name: /Registo/ })
    await logDialog.waitFor()
    await page.waitForTimeout(300)
    await page.screenshot({ path: 'screenshots/integrations-log.png' })
    await clickReliably(logDialog.getByRole('button', { name: 'Fechar' }))
    await logDialog.waitFor({ state: 'hidden' })

    // O cartão dos webhooks: configurar mostra o formulário real de destino.
    const webhookCard = page.getByRole('region', { name: 'Webhooks' })
    await clickReliably(webhookCard.getByRole('button', { name: 'Configurar' }))
    await page.getByRole('dialog', { name: 'Definições do webhook' }).waitFor()
    await page.screenshot({ path: 'screenshots/integrations-webhook-settings.png' })
  })
})
