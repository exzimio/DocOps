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

const linhas = (page: Page) => page.getByRole('main').locator('table tbody tr')

/**
 * Um ecrã vazio não pode inventar a razão de estar vazio.
 *
 * Uma frase única para todos os casos de lista vazia, como «quando um processo lhe atribuir
 * trabalho, aparece aqui», apareceria também quando há trabalho de sobra e são os filtros a
 * escondê-lo. Quem a lesse acreditaria e sairia do ecrã, quando bastava alargar. É o mesmo defeito
 * de um botão sem ação, só que ao contrário: em vez de prometer o que não faz, afirma o que não sabe.
 */
test.describe('O ecrã vazio das tarefas', () => {
  test('no ecrã de abertura do administrador, a frase nomeia os filtros que escondem o trabalho', async ({
    page,
  }) => {
    // Sem tocar em filtro nenhum: o administrador do catálogo tem tarefas, mas nenhuma em aberto,
    // enquanto a organização tem oito à espera de alguém. Os dois filtros que vêm postos por
    // omissão, «as minhas» e «apenas em aberto», bastam para esconder esse trabalho.
    await signIn(page, admin)
    await page.goto('/app/tasks')

    await expect(page.getByText('Sem tarefas para os filtros escolhidos')).toBeVisible()

    // A frase nomeia os filtros que estão postos, e não «filtros» em abstrato.
    await expect(page.getByText(/só as suas tarefas e só as que continuam por fechar/)).toBeVisible()
    await expect(page.getByText(/podem ser os filtros a escondê-lo/)).toBeVisible()

    await clickReliably(page.getByRole('button', { name: 'Alargar os filtros' }))

    // O botão faz o que a frase prometeu: aparece trabalho, e os filtros nomeados ficaram desfeitos.
    await expect(linhas(page).first()).toBeVisible()
    await expect(page.getByRole('button', { name: 'Toda a organização' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    await expect(page.getByRole('checkbox', { name: 'Apenas em aberto' })).not.toBeChecked()
  })

  test('enquanto a lista alargada não chega, espera-se em vez de se afirmar o contrário', async ({
    page,
  }) => {
    await signIn(page, admin)
    await page.goto('/app/tasks')
    await expect(page.getByRole('button', { name: 'Alargar os filtros' })).toBeVisible()

    // A consulta em curso é atrasada de propósito. Sem atraso, o instante existe na mesma e ninguém
    // o consegue apanhar — foi preciso uma captura de ecrã para o ver.
    await page.route(/\/api\/tasks\?.*scope=All/, async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 1500))
      await route.continue()
    })

    await clickReliably(page.getByRole('button', { name: 'Alargar os filtros' }))

    // O `placeholderData` mantém o resultado anterior enquanto o novo não chega. Com linhas isso é
    // o comportamento certo; sem linhas, o ecrã vazio descrevia os filtros novos por cima de um
    // resultado velho e chegava a afirmar que nenhum processo tinha criado tarefas.
    await expect(page.getByText(/Nenhum processo criou tarefas/)).toHaveCount(0)
    await expect(page.getByText(/A carregar/)).toBeVisible()

    await expect(linhas(page).first()).toBeVisible({ timeout: 10_000 })
  })

  test('a quem já está a ver tudo o que lhe é permitido não se oferece alargar', async ({ page }) => {
    // Ana Ferreira não tem `tasks.view.all`. Em «delegadas por mim», sem mais nenhum filtro, não há
    // nada que alargar lhe mostrasse — e um botão que não muda nada seria um botão sem ação.
    await signIn(page, employee)
    await page.goto('/app/tasks')

    await expect(page.getByRole('button', { name: 'Toda a organização' })).toHaveCount(0)

    await clickReliably(page.getByRole('button', { name: 'Delegadas por mim' }))
    await clickReliably(page.getByRole('checkbox', { name: 'Apenas em aberto' }))

    await expect(page.getByText('Sem tarefas para os filtros escolhidos')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Alargar os filtros' })).toHaveCount(0)

    // E a frase fala do que este ecrã é, em vez de falar de trabalho que lhe seja atribuído.
    await expect(page.getByText(/Ainda não delegou nenhuma tarefa/)).toBeVisible()
  })
})
