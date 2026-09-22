import { expect, test, type Page } from '@playwright/test'

const employee = { email: 'ana.ferreira@asteria.example', password: 'Demo!Pass123' }

async function signIn(page: Page, account: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(account.email)
  await page.getByLabel('Palavra-passe').fill(account.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}

test.describe('Pesquisa', () => {
  test('não procura nada enquanto não lhe pedirem nada', async ({ page }) => {
    await signIn(page, employee)
    await page.goto('/app/search')

    await expect(page.getByText('Escreva o que procura')).toBeVisible()
  })

  test('texto livre devolve resultados ordenados por relevância, com excerto e origem', async ({ page }) => {
    await signIn(page, employee)
    await page.goto('/app/search')

    await page.getByRole('searchbox').fill('atlas tecnologia')

    const results = page.getByRole('list', { name: 'Resultados da pesquisa' }).getByRole('listitem')
    await expect(results.first()).toBeVisible()

    // O excerto destaca a palavra encontrada, no texto do próprio documento.
    await expect(page.locator('mark').first()).toContainText(/atlas/i)

    // A ordenação por relevância põe em primeiro o que tem o termo no título.
    await expect(results.first()).toContainText('Atlas Tecnologia')

    // E a página diz sempre que motor respondeu.
    await expect(page.getByText(/índice de texto integral|correspondência de subcadeia/)).toBeVisible()
  })

  test('uma pesquisa sem correspondência diz que não há nada, sem erro', async ({ page }) => {
    await signIn(page, employee)
    await page.goto('/app/search')

    await page.getByRole('searchbox').fill('zzzznaoexiste')

    await expect(page.getByText('Sem resultados')).toBeVisible()
  })

  test('os filtros de processo e de aprovação estreitam a pesquisa', async ({ page }) => {
    await signIn(page, employee)
    await page.goto('/app/search')

    await page.getByRole('button', { name: 'Mostrar filtros' }).click()
    await page.getByRole('button', { name: 'A aguardar', exact: true }).click()

    const results = page.getByRole('list', { name: 'Resultados da pesquisa' }).getByRole('listitem')
    await expect(results.first()).toBeVisible()

    // Tudo o que aparece está mesmo à espera de aprovação.
    for (const row of await results.all()) {
      await expect(row).toContainText('Aguarda aprovação')
    }
  })

  test('uma pergunta em linguagem natural mostra como foi lida antes de mostrar o que encontrou', async ({
    page,
  }) => {
    await signIn(page, employee)
    await page.goto('/app/search')

    await page.getByLabel('Pergunta em linguagem natural').fill('faturas que aguardam aprovação')
    await page.getByRole('button', { name: 'Perguntar' }).click()

    // A leitura é mostrada, e é mostrada como leitura de demonstração — nunca como a de um modelo.
    await expect(page.getByText(/A pergunta foi lida como os filtros/)).toBeVisible()
    await expect(page.getByText('Demonstração')).toBeVisible()
    const reading = page.getByRole('list', { name: 'Filtros lidos na pergunta' })
    await expect(reading.getByRole('listitem').filter({ hasText: 'Aprovação:' })).toBeVisible()
    await expect(reading.getByRole('listitem').filter({ hasText: 'Tipo: Fatura de fornecedor' })).toBeVisible()
  })

  test('uma pergunta que ninguém ensinou é dita como tal, e não inventada', async ({ page }) => {
    await signIn(page, employee)
    await page.goto('/app/search')

    await page.getByLabel('Pergunta em linguagem natural').fill('qualquer coisa que ninguém ensinou')
    await page.getByRole('button', { name: 'Perguntar' }).click()

    await expect(page.getByText(/Nenhum filtro foi reconhecido/)).toBeVisible()
  })
})
