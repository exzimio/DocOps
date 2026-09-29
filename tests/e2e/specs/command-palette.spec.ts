import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

const admin = { email: 'admin@asteria.example', password: 'Demo!Pass123' }
const employee = { email: 'ana.ferreira@asteria.example', password: 'Demo!Pass123' }

async function signIn(page: Page, account: { email: string; password: string }) {
  await page.context().clearCookies()
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(account.email)
  await page.getByLabel('Palavra-passe').fill(account.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)

  // O atalho é do documento: sem um clique na página, a tecla pode não lá chegar depois de uma
  // navegação, porque o elemento que tinha o foco deixou de existir.
  await page.locator('body').click()
}

const palette = (page: Page) => page.getByRole('dialog', { name: 'Ir para' })

test.describe('Paleta de comandos', () => {
  test('abre com Ctrl+K e leva a um ecrã só com o teclado', async ({ page }) => {
    await signIn(page, admin)

    await page.keyboard.press('Control+k')
    await expect(palette(page)).toBeVisible()

    await page.keyboard.type('análises')
    await page.keyboard.press('Enter')

    // Do teclado ao ecrã, sem tocar no rato: é para isso que a interação existe.
    await expect(page).toHaveURL(/\/app\/reports$/)
    await expect(palette(page)).toHaveCount(0)
  })

  test('encontra documentos pelo título, e abre o que foi escolhido', async ({ page }) => {
    await signIn(page, admin)

    await page.keyboard.press('Control+k')
    await page.keyboard.type('contrato')

    const results = palette(page).getByRole('button')
    await expect(results.filter({ hasText: /CTR-/ }).first()).toBeVisible()

    await results.filter({ hasText: /CTR-/ }).first().click()

    await expect(page).toHaveURL(/\/app\/documents\/[0-9a-f-]+$/)
  })

  test('um termo sem correspondência não devolve o catálogo inteiro', async ({ page }) => {
    await signIn(page, admin)

    await page.keyboard.press('Control+k')
    await page.keyboard.type('zzzznaoexiste')

    // A versão anterior deste teste procurava um documento concreto na lista e encontrava-o — mas
    // encontrava-o porque a lista vinha **inteira**: o pedido usava o nome errado do parâmetro e o
    // servidor ignorava-o. Um teste que confirma a presença de algo passa com uma lista que não
    // filtrou nada. Este exige a ausência de tudo, que é o que não se consegue fingir.
    await expect(palette(page).getByText('Nada corresponde ao que escreveu.')).toBeVisible()
  })

  test('Escape fecha e devolve o foco a quem o abriu', async ({ page }) => {
    await signIn(page, admin)
    await page.goto('/app/documents')

    // Abrir a partir de um sítio concreto, porque é isso que torna a pergunta respondível: o foco
    // tem de voltar **ali**, e não para o princípio da página.
    // A caixa de pesquisa da listagem existe nos dois perfis; a barra lateral, no telemóvel, está
    // recolhida.
    const origem = page.getByPlaceholder(/Pesquisar por título/)
    await origem.focus()

    await page.keyboard.press('Control+k')
    await expect(palette(page)).toBeVisible()

    await page.keyboard.press('Escape')
    await expect(palette(page)).toHaveCount(0)

    await expect(origem).toBeFocused()
  })

  test('só oferece os ecrãs a que esta pessoa chega', async ({ page }) => {
    await signIn(page, employee)

    await page.keyboard.press('Control+k')
    await expect(palette(page)).toBeVisible()

    // Uma paleta que listasse a administração seria um atalho para um 403 — e, pior, diria a quem
    // não administra que a administração existe e onde fica.
    await expect(palette(page).getByRole('button', { name: 'Administração' })).toHaveCount(0)
    await expect(palette(page).getByRole('button', { name: 'Documentos' })).toBeVisible()
  })

  test('a paleta aberta não viola a norma de acessibilidade', async ({ page }) => {
    await signIn(page, admin)
    await page.keyboard.press('Control+k')
    await expect(palette(page)).toBeVisible()

    const result = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze()

    expect(result.violations.map((v) => v.id)).toEqual([])
  })
})
