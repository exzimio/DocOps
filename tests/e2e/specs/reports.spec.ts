import { expect, test, type Page } from '@playwright/test'
import { clickReliably } from '../support/interactions'

const admin = { email: 'admin@asteria.example', password: 'Demo!Pass123' }
const employee = { email: 'ana.ferreira@asteria.example', password: 'Demo!Pass123' }

async function signIn(page: Page, account: { email: string; password: string }) {
  await page.context().clearCookies()
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(account.email)
  await page.getByLabel('Palavra-passe').fill(account.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}

test.describe('Análises', () => {
  test('o painel mostra indicadores, tendências e distribuições a partir dos dados reais', async ({
    page,
  }) => {
    await signIn(page, admin)
    await page.goto('/app/reports')

    await expect(page.getByRole('heading', { name: 'Análises' })).toBeVisible()

    // Indicadores: cada um é um cartão com o seu nome.
    const documents = page.getByRole('region', { name: 'Documentos criados', exact: true })
    await expect(documents).toBeVisible()
    await expect(documents).toContainText(/\d/)

    // Tendências: um gráfico por medida, cada um com uma série só — medidas de escalas diferentes
    // não partilham eixo.
    for (const series of [
      'Documentos criados por dia',
      'Processos iniciados por dia',
      'Processos concluídos por dia',
    ]) {
      await expect(page.getByRole('img', { name: series })).toBeVisible()
    }

    // Distribuições: os estados aparecem traduzidos, não com o vocabulário interno.
    const byStatus = page.getByRole('region', { name: 'Documentos por estado', exact: true })
    await expect(byStatus).toBeVisible()
    await expect(byStatus).not.toContainText('PendingApproval')
    await expect(byStatus).not.toContainText('SignatureRequired')

    // Estrangulamentos: passos com nome real e trabalho à espera.
    await expect(page.getByRole('heading', { name: 'Onde os processos param' })).toBeVisible()
  })

  test('mudar o período muda os números', async ({ page }) => {
    await signIn(page, admin)
    await page.goto('/app/reports')

    const documents = page.getByRole('region', { name: 'Documentos criados', exact: true })
    await expect(documents).toBeVisible()
    const over30 = await documents.innerText()

    await clickReliably(page.getByRole('button', { name: '7 dias' }))
    await expect(page.getByRole('button', { name: '7 dias' })).toHaveAttribute('aria-pressed', 'true')

    // Uma janela mais curta não pode devolver exatamente o mesmo painel: se devolvesse, o filtro
    // não estaria a chegar ao servidor.
    await expect(async () => {
      expect(await documents.innerText()).not.toBe(over30)
    }).toPass({ timeout: 10_000 })
  })

  test('uma medida sem base diz que não tem base, em vez de dizer zero', async ({ page }) => {
    await signIn(page, admin)
    await page.goto('/app/reports?')

    await clickReliably(page.getByRole('button', { name: '7 dias' }))

    // Numa janela de sete dias sobre um catálogo semeado ao longo de meses, há medidas sem nada
    // para medir. Zero diria que aconteceu e correu mal; o painel tem de dizer que não se pôs.
    await expect(page.getByText('Sem base de cálculo no período.').first()).toBeVisible({ timeout: 10_000 })
  })

  test('quem só pode ver não pode exportar, e vê o aviso de que o painel é a sua fatia', async ({ page }) => {
    await signIn(page, employee)
    await page.goto('/app/reports')

    await expect(page.getByRole('heading', { name: 'Análises' })).toBeVisible()

    // O botão de exportar exige reports.export, que este perfil não tem — e o servidor recusa na
    // mesma, como sempre.
    await expect(page.getByRole('button', { name: 'Exportar CSV' })).toHaveCount(0)

    // E o painel diz que os números são os que esta pessoa pode ver, não os da organização.
    await expect(page.getByText(/contam apenas o que tem permissão para ver/)).toBeVisible()
  })
})
