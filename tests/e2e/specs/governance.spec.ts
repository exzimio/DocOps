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

async function openTab(page: Page, name: string, heading: string) {
  await page.goto('/app/admin')
  await clickReliably(page.getByRole('tab', { name }))
  await expect(page.getByRole('heading', { name: heading })).toBeVisible()
}

function unique(prefix: string) {
  return `${prefix}${Date.now().toString(36).slice(-5)}`
}

const tagForm = (page: Page) => page.getByRole('form', { name: 'Formulário da etiqueta' })
const policyForm = (page: Page) => page.getByRole('form', { name: 'Formulário da política de retenção' })

test.describe('Etiquetas', () => {
  test('remover uma etiqueta é reversível: some da lista e volta com o que tinha', async ({ page }) => {
    await signIn(page, admin)
    await openTab(page, 'Etiquetas', 'Etiquetas')

    const name = unique('ensaio-')

    await tagForm(page).getByLabel('Nome').fill(name)
    await tagForm(page).getByLabel('Cor').fill('#2F6F62')
    await clickReliably(page.getByRole('button', { name: 'Criar etiqueta' }))

    const row = page.getByRole('row').filter({ hasText: name })
    await expect(row).toBeVisible()

    await clickReliably(row.getByRole('button', { name: 'Remover' }))
    await expect(page.getByRole('row').filter({ hasText: name })).toHaveCount(0)

    // Removida, não destruída: continua lá para quem a quiser de volta.
    await toggleReliably(page.getByLabel('Incluir removidas'))

    const removed = page.getByRole('row').filter({ hasText: name })
    await expect(removed).toContainText('Removida')

    await clickReliably(removed.getByRole('button', { name: 'Restaurar' }))
    await expect(page.getByRole('row').filter({ hasText: name })).toContainText('Editar')
  })

  test('um nome de etiqueta repetido é recusado, e a recusa aparece no formulário', async ({ page }) => {
    await signIn(page, admin)
    await openTab(page, 'Etiquetas', 'Etiquetas')

    const name = unique('repetida-')

    await tagForm(page).getByLabel('Nome').fill(name)
    await clickReliably(page.getByRole('button', { name: 'Criar etiqueta' }))

    // Esperar que a primeira exista antes de tentar a segunda: sem isto as duas viajam juntas e o
    // servidor recebe dois pedidos válidos, porque quando o segundo chega o primeiro ainda não
    // gravou. O teste estaria a medir a rede, não a regra.
    await expect(page.getByRole('row').filter({ hasText: name })).toBeVisible()

    await tagForm(page).getByLabel('Nome').fill(name)
    await clickReliably(page.getByRole('button', { name: 'Criar etiqueta' }))

    await expect(tagForm(page).getByRole('alert')).toContainText('Já existe uma etiqueta com este nome.')
  })

  test('uma cor inválida é recusada em vez de ser ignorada em silêncio', async ({ page }) => {
    await signIn(page, admin)
    await openTab(page, 'Etiquetas', 'Etiquetas')

    await tagForm(page).getByLabel('Nome').fill(unique('cor-'))
    await tagForm(page).getByLabel('Cor').fill('verde')
    await clickReliably(page.getByRole('button', { name: 'Criar etiqueta' }))

    // Uma cor que não chega é uma cor que alguém escolheu e nunca viu aplicada.
    await expect(tagForm(page).getByRole('alert')).toContainText('hexadecimal')
  })

  test('procurar reduz a lista ao que interessa', async ({ page }) => {
    await signIn(page, admin)
    await openTab(page, 'Etiquetas', 'Etiquetas')

    await page.getByLabel('Procurar etiqueta…').fill('fatura')

    const rows = page.getByRole('row').filter({ hasText: /Remover|Restaurar/ })
    await expect(rows.filter({ hasNotText: 'fatura' })).toHaveCount(0)

    await page.getByLabel('Procurar etiqueta…').fill('zzzznaoexiste')
    await expect(page.getByText('Nenhuma etiqueta')).toBeVisible()
  })
})

test.describe('Políticas de retenção', () => {
  test('retirar uma política que tipos adotam é recusado, e diz quais', async ({ page }) => {
    await signIn(page, admin)
    await openTab(page, 'Retenção', 'Políticas de retenção')

    const adopted = page.getByRole('row').filter({ hasText: 'CTR-10A' })
    await clickReliably(adopted.getByRole('button', { name: 'Retirar' }))

    // A recusa aparece junto à tabela, porque foi de uma linha da tabela que a ação partiu.
    const alert = page.getByRole('alert')
    await expect(alert).toContainText('tipo(s) de documento')
    await expect(alert).toContainText('Contrato')
  })

  test('uma duração impossível é recusada', async ({ page }) => {
    await signIn(page, admin)
    await openTab(page, 'Retenção', 'Políticas de retenção')

    await policyForm(page).getByLabel('Nome').fill('Política impossível')
    await policyForm(page).getByLabel('Código').fill(unique('X'))
    await policyForm(page).getByLabel('Duração em meses').fill('0')
    await clickReliably(page.getByRole('button', { name: 'Criar política' }))

    await expect(policyForm(page).getByRole('alert')).toContainText('entre 1 e 1200 meses')
  })

  test('mudar a duração é permitido, porque as obrigações mudam', async ({ page }) => {
    await signIn(page, admin)
    await openTab(page, 'Retenção', 'Políticas de retenção')

    const code = unique('T').toUpperCase().slice(0, 6)

    await policyForm(page).getByLabel('Nome').fill('Política de ensaio')
    await policyForm(page).getByLabel('Código').fill(code)
    await policyForm(page).getByLabel('Duração em meses').fill('24')
    await clickReliably(page.getByRole('button', { name: 'Criar política' }))

    const row = page.getByRole('row').filter({ hasText: code })
    await expect(row).toContainText('24 meses')

    await clickReliably(row.getByRole('button', { name: 'Editar' }))
    await policyForm(page).getByLabel('Duração em meses').fill('60')
    await clickReliably(page.getByRole('button', { name: 'Guardar' }))

    // Ao contrário do catálogo, aqui mudar não é falsificar: é seguir a obrigação que mudou.
    await expect(page.getByRole('row').filter({ hasText: code })).toContainText('60 meses')
  })

  test('quem administra pessoas não decide por isso quanto tempo se guardam documentos', async ({ page }) => {
    await signIn(page, manager)
    await page.goto('/app/admin')

    await expect(page.getByRole('tab', { name: 'Retenção' })).toHaveCount(0)
    await expect(page.getByRole('tab', { name: 'Etiquetas' })).toHaveCount(0)
  })
})
