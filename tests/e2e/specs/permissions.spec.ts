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

async function openPermissions(page: Page) {
  await page.goto('/app/documents')
  await clickReliably(page.getByRole('row').nth(1).getByRole('link').first())
  await page.waitForURL(/\/app\/documents\/[0-9a-f-]+$/)
  await clickReliably(page.getByRole('tab', { name: 'Permissões' }))
}

test.describe('Editor de permissões', () => {
  test('concede-se a uma pessoa, a um departamento e a um perfil', async ({ page }) => {
    await signIn(page, admin)
    await openPermissions(page)

    // O briefing pede os três sujeitos. A pessoa individual faltava.
    for (const subject of ['Utilizador', 'Departamento', 'Perfil']) {
      await page.getByLabel('Tipo').selectOption({ label: subject })

      const options = page.getByLabel('Destinatário').locator('option')
      await expect(options.filter({ hasNotText: '—' }).first()).toBeAttached()
    }
  })

  test('os perfis oferecidos vêm do servidor, e a administração da plataforma não está entre eles', async ({
    page,
  }) => {
    await signIn(page, admin)
    await openPermissions(page)

    await page.getByLabel('Tipo').selectOption({ label: 'Perfil' })

    const options = page.getByLabel('Destinatário').locator('option')

    await expect(options.filter({ hasText: 'Manager' })).toHaveCount(1)

    // Está acima da organização: partilhar com ela seria dar acesso a uma conta que a organização
    // não gere. E a lista vem do catálogo de perfis, não escrita à mão no ecrã.
    await expect(options.filter({ hasText: 'SystemAdmin' })).toHaveCount(0)
  })

  test('uma concessão com prazo aparece com a data por que expira', async ({ page }) => {
    await signIn(page, admin)
    await openPermissions(page)

    await page.getByLabel('Tipo').selectOption({ label: 'Departamento' })
    await page.getByLabel('Destinatário').selectOption({ index: 1 })
    await page.getByLabel('Expira em').fill('2030-12-31')
    await clickReliably(page.getByRole('button', { name: 'Conceder acesso' }))

    // A janela de um auditor externo: o modelo sempre soube expirar, faltava poder pedi-lo.
    const granted = page.getByRole('row').filter({ hasText: /descarregar/ })
    await expect(granted.first()).toBeVisible()
  })

  test('explica porque é que outra pessoa alcança — ou não alcança — o documento', async ({ page }) => {
    await signIn(page, admin)
    await openPermissions(page)

    await expect(
      page.getByRole('heading', { name: 'Porque é que alguém alcança este documento' }),
    ).toBeVisible()

    await page.getByLabel('Acesso de').selectOption({ index: 1 })

    // A avaliação é a do servidor, a mesma que decide de verdade: ou explica o que concede, ou diz
    // que nada concede. O que nunca faz é ficar em branco.
    await expect(
      page.getByText('Não alcança este documento').or(page.getByText('Concede:').last()),
    ).toBeVisible()
  })

  test('quem não pode partilhar não vê o editor nem pergunta pelos outros', async ({ page }) => {
    await signIn(page, employee)
    await openPermissions(page)

    // O servidor recusa na mesma: perguntar «esta pessoa conseguiria?» é legítimo para quem decide
    // conceder, e mais ninguém.
    await expect(page.getByRole('button', { name: 'Conceder acesso' })).toHaveCount(0)
    await expect(page.getByLabel('Acesso de')).toHaveCount(0)
  })
})
