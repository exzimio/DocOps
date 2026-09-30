import { expect, test, type Page } from '@playwright/test'
import { clickReliably } from '../support/interactions'

const admin = { email: 'admin@asteria.example', password: 'Demo!Pass123' }

async function signIn(page: Page) {
  await page.context().clearCookies()
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(admin.email)
  await page.getByLabel('Palavra-passe').fill(admin.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}

const rows = (page: Page) => page.getByRole('row').filter({ hasText: /-2026-/ })

/**
 * A referência do primeiro documento da lista — «FAT-2026-00003» e afins.
 *
 * Compara-se por referência e não pelo texto inteiro da linha: o texto traz tabulações e quebras
 * que dependem de como se leu o elemento, e duas leituras do mesmo documento podem diferir sem que
 * nada tenha mudado. A referência identifica.
 */
async function primeiraReferencia(page: Page): Promise<string> {
  const texto = await rows(page).first().innerText()
  const referencia = texto.match(/[A-Z]{3}-2026-\d+/)

  if (referencia === null) {
    throw new Error(`a primeira linha não traz referência: ${texto.slice(0, 80)}`)
  }

  return referencia[0]
}

/**
 * Uma vista filtrada tem de caber numa ligação.
 *
 * É o que separa uma ferramenta de um ecrã: «as faturas por aprovar» é uma coisa que se diz a um
 * colega, e dizê-la com um endereço é o que faz a frase chegar lá. Antes disto, os filtros viviam
 * dentro do componente — recarregar perdia-os, e não havia nada para enviar a ninguém.
 */
test.describe('Vistas que se podem enviar', () => {
  test('filtrar escreve o endereço, e recarregar não perde o que se filtrou', async ({ page }) => {
    await signIn(page)
    await page.goto('/app/documents')

    // Esperar pelo pedido que traz a lista filtrada, e não por um sinal no ecrã. O endereço muda
    // assim que se escreve, mas a lista só muda quando o servidor responde — ler entre as duas
    // coisas é ler a lista anterior, que foi exatamente o que a primeira versão deste teste fez.
    const resposta = page.waitForResponse(
      (r) => r.url().includes('/api/documents?') && r.url().includes('query=contrato'),
    )

    await page.getByPlaceholder(/Pesquisar por título/).fill('contrato')

    await expect(page).toHaveURL(/[?&]q=contrato/)
    await resposta

    const primeira = await primeiraReferencia(page)

    await page.reload()

    // Identidade, não contagem: a mesma vista é a que tem os mesmos documentos, e uma contagem
    // igual pode ser coincidência de duas listas diferentes.
    await expect(page.getByPlaceholder(/Pesquisar por título/)).toHaveValue('contrato')
    await expect(rows(page).first()).toContainText(primeira)
  })

  test('o endereço de uma vista filtrada abre a mesma vista noutro separador', async ({ page, context }) => {
    await signIn(page)
    await page.goto('/app/documents')

    const resposta = page.waitForResponse(
      (r) => r.url().includes('/api/documents?') && r.url().includes('statuses='),
    )

    await clickReliably(page.getByRole('button', { name: 'Aguarda aprovação' }))
    await expect(page).toHaveURL(/statuses=/)
    await resposta

    const endereco = page.url()
    const primeira = await primeiraReferencia(page)

    // O que interessa não é o estado deste separador: é que o endereço leve outra pessoa ao mesmo
    // sítio. Um separador novo não partilha memória nenhuma com este.
    const outra = await context.newPage()
    await outra.goto(endereco)

    await expect(outra.getByRole('button', { name: 'Aguarda aprovação' })).toBeVisible()
    await expect(rows(outra).first()).toContainText(primeira)

    await outra.close()
  })

  test('um endereço escrito à mão com disparates não parte o ecrã', async ({ page }) => {
    await signIn(page)

    // Ninguém escreve isto de propósito, mas um endereço copiado a meio, ou cortado por um cliente
    // de correio, chega assim. A listagem tem de continuar a ser uma listagem.
    await page.goto('/app/documents?page=abc&statuses=nao-e-numero&q=')

    await expect(page.getByRole('heading', { name: 'Documentos' })).toBeVisible()
    await expect(rows(page).first()).toBeVisible()
  })
})
