import { expect, test, type Page } from '@playwright/test'

const admin = { email: 'admin@asteria.example', password: 'Demo!Pass123' }
const employee = { email: 'ana.ferreira@asteria.example', password: 'Demo!Pass123' }

async function signIn(page: Page, user: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(user.email)
  await page.getByLabel('Palavra-passe').fill(user.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}

async function openWorkflows(page: Page) {
  await page.goto('/app/workflows')
  await page.getByRole('heading', { name: 'Workflows', exact: true }).waitFor()
}

/** Creates a process through the UI and returns the designer URL it landed on. */
async function createWorkflow(page: Page, name: string): Promise<string> {
  await openWorkflows(page)
  await page.getByRole('button', { name: 'Novo workflow' }).click()

  const dialog = page.getByRole('dialog', { name: 'Criar workflow' })
  await dialog.getByLabel('Nome do processo').fill(name)
  await dialog.getByRole('button', { name: 'Criar' }).click()

  await page.waitForURL(/\/app\/workflows\/[0-9a-f-]+\/designer$/)
  await page.getByRole('heading', { name: 'Desenhador de processos' }).waitFor()

  return page.url()
}

test.describe('Desenhador de workflows', () => {
  test('cria um processo e desenha-o até ficar sem erros de validação', async ({ page }) => {
    await signIn(page, admin)

    const designerUrl = await createWorkflow(page, `Circuito E2E ${Date.now()}`)

    // A newly created process starts with a start and an end node already connected.
    await expect(page.locator('.react-flow__node')).toHaveCount(2)
    await expect(page.locator('.react-flow__edge')).toHaveCount(1)

    await page.getByRole('button', { name: 'Aprovação', exact: true }).click()

    // The palette selects the node it just added, so its properties are ready to edit.
    await expect(page.getByLabel('Nome do passo')).toHaveValue('Aprovação')
    await page.getByLabel('Título da tarefa').fill('Aprovar o pedido')
    await page.getByLabel('Prazo (h)').fill('24')

    await expect(page.locator('.react-flow__node')).toHaveCount(3)
    await expect(page.getByText('Alterações por guardar')).toBeVisible()

    await page.getByRole('button', { name: 'Guardar', exact: true }).click()
    await expect(page.getByText('Alterações por guardar')).toBeHidden()

    // The unconnected node is a real design problem, and the server is the one that says so.
    await page.getByRole('button', { name: 'Validar' }).click()
    await expect(page.getByText(/não é alcançável|não tem liga/i).first()).toBeVisible()

    await page.reload()
    await page.waitForURL(designerUrl)
    await expect(page.locator('.react-flow__node')).toHaveCount(3)
    await expect(page.getByLabel('Nome do processo')).toHaveValue(/Circuito E2E/)

    // Leave the demo catalogue as it was found.
    await page.getByRole('button', { name: 'Eliminar', exact: true }).click()
    await page.getByRole('button', { name: 'Confirmar eliminação' }).click()
    await page.waitForURL(/\/app\/workflows$/)
  })

  test('recusa ativar um processo com um passo por configurar e elimina o rascunho', async ({ page }) => {
    await signIn(page, admin)

    await createWorkflow(page, `Rascunho E2E ${Date.now()}`)

    // An approval with nobody to assign it to is an error the server refuses to activate, even
    // though the designer happily lets it be saved while the process is being drawn.
    await page.getByRole('button', { name: 'Aprovação', exact: true }).click()
    await page.getByRole('button', { name: 'Guardar', exact: true }).click()
    await expect(page.getByText('Alterações por guardar')).toBeHidden()

    await page.getByRole('button', { name: 'Ativar' }).click()
    await expect(page.getByRole('alert')).toContainText(/valida/i)
    await expect(page.getByRole('main').getByText('Inativo')).toBeVisible()

    await page.getByRole('button', { name: 'Eliminar', exact: true }).click()
    await page.getByRole('button', { name: 'Confirmar eliminação' }).click()

    await page.waitForURL(/\/app\/workflows$/)
  })

  test('sem permissão de desenho não há botões de desenho', async ({ page }) => {
    await signIn(page, employee)
    await openWorkflows(page)

    await expect(page.getByRole('button', { name: 'Novo workflow' })).toHaveCount(0)
    await expect(page.getByRole('main').getByRole('link', { name: 'Desenhar' })).toHaveCount(0)
  })
})
