import type { Page } from '@playwright/test'

/**
 * Uma captura de página inteira sem o cabeçalho repetido a meio.
 *
 * `fullPage` não tira uma fotografia: costura várias, uma por altura de janela. Um elemento
 * `sticky` ou `fixed` é repintado em cada faixa, e o cabeçalho e a barra lateral apareceriam outra
 * vez no meio da imagem, por cima do conteúdo. Nas capturas mais altas isso chega a tapar a
 * mensagem que a captura existe para mostrar, e dá a impressão de um ecrã partido.
 *
 * A correção é na fotografia, e não no produto: o cabeçalho fixo está certo, e é o que se quer ao
 * usar a aplicação.
 *
 * Neutraliza-se **só o que está mesmo fixo**, lido do estilo calculado, e não por uma regra que
 * apanhe tudo. Pôr `position: static` em todos os elementos destaparia a ligação «Saltar para o
 * conteúdo principal», que só está escondida porque está posicionada fora do ecrã, e a captura
 * mostraria uma coisa que ninguém vê.
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
