import { expect, test, type Page } from '@playwright/test'
import { clickReliably } from '../support/interactions'

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

test.describe('Saúde do sistema', () => {
  test('lista os quatro trabalhos, mesmo os que nunca correram', async ({ page }) => {
    await signIn(page, admin)
    await page.goto('/app/admin')
    await clickReliably(page.getByRole('tab', { name: 'Sistema' }))

    await expect(page.getByRole('heading', { name: 'Trabalho em segundo plano' })).toBeVisible()

    // Um trabalho ausente do ecrã e um trabalho que nunca correu parecem o mesmo a quem lê, e não
    // são: um é uma lacuna da interface, o outro é um facto sobre o sistema.
    for (const job of [
      'Temporizadores de processos',
      'Escalonamento de prazos',
      'Fila de análises de IA',
      'Integrações',
    ]) {
      await expect(page.getByRole('row').filter({ hasText: job })).toBeVisible()
    }
  })

  test('o trabalho à espera aparece em números, nunca em identificadores', async ({ page }) => {
    await signIn(page, admin)
    await page.goto('/app/admin')
    await clickReliably(page.getByRole('tab', { name: 'Sistema' }))

    const queues = page.getByRole('heading', { name: 'Trabalho à espera' })
    await expect(queues).toBeVisible()

    for (const queue of ['Tarefas em atraso', 'Integrações em fila', 'Assinaturas pendentes']) {
      await expect(page.getByText(queue, { exact: true })).toBeVisible()
    }
  })

  test('quem administra pessoas não opera a plataforma', async ({ page }) => {
    await signIn(page, manager)
    await page.goto('/app/admin')

    await expect(page.getByRole('tab', { name: 'Sistema' })).toHaveCount(0)
    await expect(page.getByRole('heading', { name: 'Trabalho em segundo plano' })).toHaveCount(0)
  })
})
