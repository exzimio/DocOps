import { expect, test, type Page } from '@playwright/test'
import { clickReliably, toggleReliably } from '../support/interactions'

const admin = { email: 'admin@asteria.example', password: 'Demo!Pass123' }
const manager = {
  email: 'sofia.martins@asteria.example',
  password: 'Demo!Pass123',
}

async function signIn(page: Page, account: { email: string; password: string }) {
  await page.context().clearCookies()
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(account.email)
  await page.getByLabel('Palavra-passe').fill(account.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}

async function openCatalogue(page: Page) {
  await page.goto('/app/admin')
  await clickReliably(page.getByRole('tab', { name: 'Catálogo' }))
  await expect(page.getByRole('heading', { name: 'Tipos de documento' })).toBeVisible()
}

/** Único por execução: estes tipos ficam na base como qualquer tipo criado pelo ecrã. */
function unique(prefix: string) {
  return `${prefix}${Date.now().toString(36).slice(-5)}`
}

const fieldForm = (page: Page) => page.getByRole('form', { name: 'Formulário do campo' })
const typeForm = (page: Page) => page.getByRole('form', { name: 'Formulário do tipo de documento' })

test.describe('Catálogo de tipos de documento', () => {
  test('um tipo novo aceita um campo opcional, e esse campo apaga-se enquanto ninguém o usa', async ({
    page,
  }) => {
    await signIn(page, admin)
    await openCatalogue(page)

    const code = unique('Ensaio')
    const prefix = unique('E').toUpperCase().slice(0, 4)

    await typeForm(page).getByLabel('Nome').fill('Tipo de ensaio')
    await typeForm(page).getByLabel('Código').fill(code)
    await typeForm(page).getByLabel('Prefixo').fill(prefix)
    await clickReliably(page.getByRole('button', { name: 'Criar tipo' }))

    const row = page.getByRole('row').filter({ hasText: code })
    await expect(row).toBeVisible()

    await clickReliably(row.getByRole('button', { name: 'Campos' }))
    await expect(page.getByRole('heading', { name: 'Campos de Tipo de ensaio' })).toBeVisible()

    await fieldForm(page).getByLabel('Rótulo').fill('Referência interna')
    await fieldForm(page).getByLabel('Código').fill('referencia')
    await clickReliably(page.getByRole('button', { name: 'Criar campo' }))

    const field = page.getByRole('row').filter({ hasText: 'referencia' })
    await expect(field).toBeVisible()

    // Ninguém o preencheu ainda, por isso apagá-lo não apaga o que documento nenhum diz.
    await clickReliably(field.getByRole('button', { name: 'Eliminar' }))
    await expect(page.getByRole('row').filter({ hasText: 'referencia' })).toHaveCount(0)

    // O tipo sai de circulação. Não se elimina, porque um tipo com documentos nunca poderia
    // desaparecer sem os deixar órfãos, e o produto por isso só desativa — mas um «Tipo de ensaio»
    // na lista de quem carrega documentos não é o catálogo como estava.
    await clickReliably(
      page.getByRole('row').filter({ hasText: code }).getByRole('button', { name: 'Desativar' }),
    )
    await expect(page.getByRole('row').filter({ hasText: code })).toHaveCount(0)
  })

  test('um campo que documentos já preenchem não se apaga, e o ecrã diz em quantos está', async ({
    page,
  }) => {
    await signIn(page, admin)
    await openCatalogue(page)

    await clickReliably(
      page.getByRole('row').filter({ hasText: 'SupplierInvoice' }).getByRole('button', { name: 'Campos' }),
    )

    const filled = page.getByRole('row').filter({ hasText: 'invoiceNumber' })
    await expect(filled).toBeVisible()

    // Sem botão de eliminar, e com o número no lugar dele: uma recusa sem o número é
    // indistinguível de uma avaria.
    await expect(filled.getByRole('button', { name: 'Eliminar' })).toHaveCount(0)
    await expect(filled).toContainText(/\d+ documento\(s\) usam-no/)
  })

  test('tornar um campo obrigatório é recusado quando havia documentos por preencher', async ({ page }) => {
    await signIn(page, admin)
    await openCatalogue(page)

    await clickReliably(
      page.getByRole('row').filter({ hasText: 'SupplierInvoice' }).getByRole('button', { name: 'Campos' }),
    )

    await fieldForm(page).getByLabel('Rótulo').fill('Campo de ensaio')
    await fieldForm(page).getByLabel('Código').fill(unique('ens'))
    await toggleReliably(fieldForm(page).getByLabel('Obrigatório'))
    await clickReliably(page.getByRole('button', { name: 'Criar campo' }))

    // A recusa vem do servidor, com a contagem que a explica — e aparece dentro do formulário que
    // a provocou, não no topo da página, longe de quem carregou no botão.
    const alert = fieldForm(page).getByRole('alert')
    await expect(alert).toBeVisible()
    await expect(alert).toContainText(/documento\(s\) deste tipo ficariam inválidos/)
  })

  test('o tipo de entrada não se desativa nem aceita campos obrigatórios', async ({ page }) => {
    await signIn(page, admin)
    await openCatalogue(page)

    const intake = page.getByRole('row').filter({ hasText: 'Unclassified' })
    await expect(intake).toContainText('Entrada')

    // Fechar esta porta fecharia a entrada de todos os ficheiros, incluindo os que chegam por
    // correio e pelo portal, onde ninguém está a olhar para reparar.
    await expect(intake.getByRole('button', { name: 'Desativar' })).toHaveCount(0)
    await expect(intake).toContainText('Sempre ativo')

    await clickReliably(intake.getByRole('button', { name: 'Campos' }))

    await fieldForm(page).getByLabel('Rótulo').fill('Obrigatório à entrada')
    await fieldForm(page).getByLabel('Código').fill(unique('obr'))
    await toggleReliably(fieldForm(page).getByLabel('Obrigatório'))
    await clickReliably(page.getByRole('button', { name: 'Criar campo' }))

    await expect(fieldForm(page).getByRole('alert')).toContainText('não aceita campos obrigatórios')
  })

  test('dois tipos não partilham prefixo de referência', async ({ page }) => {
    await signIn(page, admin)
    await openCatalogue(page)

    await typeForm(page).getByLabel('Nome').fill('Colide com faturas')
    await typeForm(page).getByLabel('Código').fill(unique('Colide'))
    await typeForm(page).getByLabel('Prefixo').fill('FAT')
    await clickReliably(page.getByRole('button', { name: 'Criar tipo' }))

    // Duas referências iguais para documentos diferentes, e a referência é o que as pessoas citam
    // umas às outras.
    await expect(typeForm(page).getByRole('alert')).toContainText('prefixo de referência')
  })

  test('quem administra pessoas não administra por isso o catálogo', async ({ page }) => {
    await signIn(page, manager)
    await page.goto('/app/admin')

    await expect(page.getByRole('tab', { name: 'Catálogo' })).toHaveCount(0)
    await expect(page.getByRole('heading', { name: 'Tipos de documento' })).toHaveCount(0)
  })
})
