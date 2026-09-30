import type { Page } from '@playwright/test'

/**
 * Uma captura de pagina inteira sem o cabecalho repetido a meio.
 *
 * `fullPage` nao tira uma fotografia: costura varias, uma por altura de janela. Um elemento
 * `sticky` ou `fixed` e repintado em cada faixa, pelo que o cabecalho e a barra lateral apareciam
 * outra vez no meio da imagem, por cima do conteudo. Nas capturas mais altas isso chegava a tapar
 * a mensagem que a captura existia para mostrar, e dava a impressao de um ecra partido.
 *
 * A correcao e na fotografia e nao no produto: o cabecalho fixo esta certo, e e o que se quer ao
 * usar a aplicacao.
 *
 * Neutraliza-se **so o que esta mesmo fixo**, lido do estilo calculado, e nao por uma regra que
 * apanhe tudo. Uma primeira versao punha `position: static` em todos os elementos e destapou o
 * link «Saltar para o conteudo principal», que so esta escondido porque esta posicionado fora do
 * ecra — a captura passou a mostrar uma coisa que ninguem ve.
 */
export async function fullPageShot(page: Page, path: string): Promise<void> {
  const marcados = await neutralizar(page)

  await page.screenshot({ path, fullPage: true })

  if (marcados > 0) {
    await restaurar(page)
  }
}

const MARCA = 'data-captura-sem-fixos'

function neutralizar(page: Page): Promise<number> {
  return page.evaluate((marca) => {
    const fixos = [...document.querySelectorAll<HTMLElement>('body *')].filter((elemento) => {
      const posicao = getComputedStyle(elemento).position

      return posicao === 'sticky' || posicao === 'fixed'
    })

    for (const elemento of fixos) {
      elemento.setAttribute(marca, elemento.style.position)
      elemento.style.position = 'static'
    }

    return fixos.length
  }, MARCA)
}

function restaurar(page: Page): Promise<void> {
  return page.evaluate((marca) => {
    for (const elemento of document.querySelectorAll<HTMLElement>(`[${marca}]`)) {
      elemento.style.position = elemento.getAttribute(marca) ?? ''
      elemento.removeAttribute(marca)
    }
  }, MARCA)
}
