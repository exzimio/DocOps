import { expect, test, type Page } from '@playwright/test'

/**
 * Nenhum ecrã pode ser mais largo do que a janela.
 *
 * A auditoria de acessibilidade não apanha isto: o `axe` verifica contraste, nomes acessíveis e
 * estrutura, e uma página que rola de lado passa em todos esses critérios. Mas num telemóvel é dos
 * defeitos que mais se sente, e nenhum outro teste o apanha.
 *
 * A causa costuma ser a armadilha clássica das grelhas: um item de grelha tem `min-width: auto` e
 * recusa-se a encolher abaixo do conteúdo, portanto uma tabela larga empurra o cartão, o cartão
 * empurra a página, e um `overflow-x-auto` lá dentro nunca chega a entrar em ação. Quando um ecrã
 * novo falhar aqui, procurar por `minmax(0, 1fr)` e por `min-w-0`.
 */
const rotas = [
  '/app/dashboard',
  '/app/documents',
  '/app/inbox',
  '/app/search',
  '/app/tasks',
  '/app/workflows',
  '/app/ai',
  '/app/reports',
  '/app/admin',
  '/app/integrations',
]

/**
 * A administração vive toda em `/app/admin` e muda de conteúdo por separador, sem mudar de
 * endereço. Medir só a rota é medir um sexto do ecrã — e foi assim que o separador do catálogo
 * continuou a transbordar depois de o das pessoas já estar corrigido.
 */
const separadoresDaAdministracao = [
  'Pessoas',
  'Departamentos',
  'Catálogo',
  'Etiquetas',
  'Retenção',
  'Sistema',
]

test.describe('Largura dos ecrãs', () => {
  test('nenhuma página é mais larga do que a janela', async ({ page }) => {
    await entrar(page)

    for (const rota of rotas) {
      await page.goto(rota)
      await page.waitForLoadState('networkidle')
      await medir(page, rota)
    }
  })

  test('nenhum separador da administração é mais largo do que a janela', async ({ page }) => {
    await entrar(page)
    await page.goto('/app/admin')
    await page.waitForLoadState('networkidle')

    for (const separador of separadoresDaAdministracao) {
      await page.getByRole('tab', { name: separador, exact: true }).click()
      await page.waitForLoadState('networkidle')
      await medir(page, `/app/admin (${separador})`)
    }
  })
})

async function medir(page: Page, onde: string) {
  const medida = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    janela: document.documentElement.clientWidth,
  }))

  const excesso = medida.scroll - medida.janela

  // Nomear os culpados na própria mensagem: sem isso, uma falha aqui é um número sem pista, e o
  // elemento que causa o transbordo não é o que salta à vista — é o antepassado mais exterior que
  // já era largo de mais, e tudo o que está lá dentro apenas o herda.
  expect(excesso, `${onde} transborda ${excesso}px:\n${await culpados(page)}`).toBe(0)
}

async function culpados(page: Page): Promise<string> {
  return page.evaluate(() => {
    const janela = document.documentElement.clientWidth

    return [...document.querySelectorAll<HTMLElement>('body *')]
      .filter((elemento) => elemento.getBoundingClientRect().right > janela + 1)
      .map((elemento) => {
        let profundidade = 0

        for (let no: HTMLElement | null = elemento; no?.parentElement; no = no.parentElement) {
          profundidade += 1
        }

        return { elemento, profundidade }
      })
      .sort((a, b) => a.profundidade - b.profundidade)
      .slice(0, 5)
      .map(
        ({ elemento, profundidade }) =>
          `  [${profundidade}] ${elemento.tagName.toLowerCase()} ` +
          `largura=${Math.round(elemento.getBoundingClientRect().width)} ` +
          `| ${(elemento.getAttribute('class') ?? '').slice(0, 90)}`,
      )
      .join('\n')
  })
}

async function entrar(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
  await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}
