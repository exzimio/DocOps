import { expect, test } from '@playwright/test'

const financeApprover = { email: 'ricardo.lopes@asteria.example', password: 'Demo!Pass123' }

test.describe('Autenticação e navegação protegida', () => {
  test('a página pública apresenta o produto e encaminha para a autenticação', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByRole('heading', { name: 'DocOps Control Tower', level: 1 })).toBeVisible()
    await expect(page.getByText('Projeto de portefólio independente', { exact: false }).first()).toBeVisible()

    await page.getByRole('link', { name: 'Abrir demonstração interativa' }).click()

    await expect(page).toHaveURL(/\/login$/)
    await expect(page.getByRole('heading', { name: 'Iniciar sessão', level: 2 })).toBeVisible()
  })

  test('uma rota protegida redireciona um visitante anónimo para o início de sessão', async ({ page }) => {
    await page.goto('/app/dashboard')

    await expect(page).toHaveURL(/\/login$/)
  })

  test('credenciais inválidas são recusadas com uma mensagem clara', async ({ page }) => {
    await page.goto('/login')

    await page.getByLabel('Endereço de e-mail').fill(financeApprover.email)
    await page.getByLabel('Palavra-passe').fill('PalavraErrada123!')
    await page.getByRole('button', { name: 'Entrar' }).click()

    await expect(page.getByRole('alert')).toContainText('Credenciais inválidas')
    await expect(page).toHaveURL(/\/login$/)
  })

  test('início de sessão válido abre o painel com dados reais do servidor', async ({ page }) => {
    await page.goto('/login')

    await page.getByLabel('Endereço de e-mail').fill(financeApprover.email)
    await page.getByLabel('Palavra-passe').fill(financeApprover.password)
    await page.getByRole('button', { name: 'Entrar' }).click()

    await expect(page).toHaveURL(/\/app\/dashboard$/)
    await expect(page.getByRole('heading', { name: /Ricardo Lopes/ })).toBeVisible()

    // Data that can only come from the database: the tenant, its departments and the granted
    // permissions. Scoped to <main> because the header hides the tenant name on narrow viewports.
    const main = page.getByRole('main')
    await expect(main.getByText('Asteria Group').first()).toBeVisible()
    await expect(main.getByRole('cell', { name: 'Human Resources' })).toBeVisible()
    await expect(main.getByText('documents.approve')).toBeVisible()

    // The session cookie must not be readable by scripts.
    const cookies = await page.context().cookies()
    const session = cookies.find((cookie) => cookie.name === 'docops.session')
    expect(session?.httpOnly).toBe(true)
    expect(await page.evaluate(() => document.cookie)).not.toContain('docops.session')
  })

  test('terminar sessão devolve o utilizador ao início de sessão e revoga o acesso', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill(financeApprover.email)
    await page.getByLabel('Palavra-passe').fill(financeApprover.password)
    await page.getByRole('button', { name: 'Entrar' }).click()
    await expect(page).toHaveURL(/\/app\/dashboard$/)

    await page.getByRole('button', { name: 'Terminar sessão' }).click()
    await expect(page).toHaveURL(/\/login$/)

    await page.goto('/app/dashboard')
    await expect(page).toHaveURL(/\/login$/)
  })

  test('a interface alterna para inglês sem recarregar a aplicação', async ({ page }) => {
    await page.goto('/login')

    await page.getByRole('combobox').selectOption('en')

    await expect(page.getByRole('heading', { name: 'Sign in', level: 2 })).toBeVisible()
    await expect(page.getByLabel('E-mail address')).toBeVisible()
  })
})
