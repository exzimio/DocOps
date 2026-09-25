import { expect, test, type Page } from '@playwright/test'
import { clickReliably, toggleReliably } from '../support/interactions'

const admin = { email: 'admin@asteria.example', password: 'Demo!Pass123' }
const manager = { email: 'sofia.martins@asteria.example', password: 'Demo!Pass123' }

async function signIn(page: Page, account: { email: string; password: string }) {
  await page.context().clearCookies()
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(account.email)
  await page.getByLabel('Palavra-passe').fill(account.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}

/** Único por execução: estas contas ficam na base como qualquer conta real criada pelo ecrã. */
function unique(prefix: string) {
  return `${prefix}${Date.now().toString(36).slice(-6)}`
}

test.describe('Administração', () => {
  test('criar uma conta mostra a palavra-passe uma vez e a pessoa passa a constar da lista', async ({
    page,
  }) => {
    await signIn(page, admin)
    await page.goto('/app/admin')

    await expect(page.getByRole('heading', { name: 'Administração' })).toBeVisible()

    const email = `${unique('e2e.')}@asteria.example`

    await page.getByLabel('Endereço de e-mail').fill(email)
    await page.getByLabel('Nome próprio').fill('Conta')
    await page.getByLabel('Apelido').fill('De Ensaio')
    await page.getByLabel('Função').fill('Verificação ponta a ponta')
    await toggleReliably(page.getByRole('checkbox', { name: /^Employee/ }))

    await clickReliably(page.getByRole('button', { name: 'Criar conta' }))

    // A palavra-passe aparece aqui e só aqui: o servidor guarda o resumo criptográfico e não a sabe
    // devolver outra vez. Se o ecrã a pudesse mostrar de novo, estaria guardada em claro.
    await expect(page.getByText('Palavra-passe inicial')).toBeVisible()
    await expect(page.getByText(email, { exact: false }).first()).toBeVisible()

    // E a conta existe mesmo, não é só uma linha otimista no cliente: recarregar traz a lista do
    // servidor outra vez.
    await page.reload()
    await expect(page.getByText(email).first()).toBeVisible()
    await expect(page.getByText('Palavra-passe inicial')).toHaveCount(0)
  })

  test('a própria conta não tem botões e uma conta desativada só aparece a pedido', async ({ page }) => {
    await signIn(page, admin)
    await page.goto('/app/admin')

    // A linha de quem está a ver não oferece editar nem desativar. Retirar-se a si mesmo a
    // administração pode deixar a organização sem ninguém capaz de desfazer.
    const self = page.getByRole('row').filter({ hasText: admin.email })
    await expect(self).toContainText('A sua conta')
    await expect(self.getByRole('button')).toHaveCount(0)

    // A conta que administra a plataforma também não se edita daqui, mas por outro motivo. Dar a
    // ambas a mesma explicação seria dizer uma falsidade a uma delas.
    const platform = page.getByRole('row').filter({ hasText: 'admin@docops.example' })
    await expect(platform).toContainText('Conta de plataforma')
    await expect(platform).not.toContainText('A sua conta')

    const email = `${unique('e2e.off.')}@asteria.example`

    await page.getByLabel('Endereço de e-mail').fill(email)
    await page.getByLabel('Nome próprio').fill('Conta')
    await page.getByLabel('Apelido').fill('Desativada')
    await toggleReliably(page.getByRole('checkbox', { name: /^Employee/ }))
    await clickReliably(page.getByRole('button', { name: 'Criar conta' }))

    const row = page.getByRole('row').filter({ hasText: email })
    await expect(row).toBeVisible()

    await clickReliably(row.getByRole('button', { name: 'Desativar' }))

    // Desativada sai da lista de trabalho, mas não desaparece: a auditoria e os documentos ainda
    // lhe fazem referência.
    await expect(page.getByRole('row').filter({ hasText: email })).toHaveCount(0)

    await toggleReliably(page.getByLabel('Incluir desativadas'))

    const inactive = page.getByRole('row').filter({ hasText: email })
    await expect(inactive).toBeVisible()
    await expect(inactive).toContainText('Desativada')
  })

  test('um código de departamento repetido é recusado pelo servidor e o ecrã diz porquê', async ({
    page,
  }) => {
    await signIn(page, admin)
    await page.goto('/app/admin')

    await clickReliably(page.getByRole('tab', { name: 'Departamentos' }))

    const code = unique('E').toUpperCase().slice(0, 6)

    await page.getByLabel('Nome').fill('Ensaio ponta a ponta')
    await page.getByLabel('Código').fill(code)
    await clickReliably(page.getByRole('button', { name: 'Guardar' }))

    await expect(page.getByRole('row').filter({ hasText: code })).toBeVisible()

    await page.getByLabel('Nome').fill('Outro qualquer')
    await page.getByLabel('Código').fill(code)
    await clickReliably(page.getByRole('button', { name: 'Guardar' }))

    // A mensagem vem do servidor, não de uma validação do cliente: é lá que a unicidade é decidida.
    await expect(page.getByRole('alert')).toContainText('Já existe um departamento com este código.')
  })

  test('quem não administra não vê a entrada nem alcança o ecrã pelo endereço', async ({ page }) => {
    await signIn(page, manager)

    await expect(page.getByRole('link', { name: 'Administração' })).toHaveCount(0)

    // Esconder é conveniência; a recusa é do servidor. O ecrã pede os dados e não os recebe.
    await page.goto('/app/admin')
    await expect(page.getByRole('table')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Criar conta' })).toHaveCount(0)
  })
})
