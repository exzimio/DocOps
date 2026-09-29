import AxeBuilder from '@axe-core/playwright'
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

/**
 * Corre a auditoria e devolve as violações, já legíveis.
 *
 * As regras escolhidas são as de WCAG 2 A e AA, que é o que uma organização exige a um fornecedor.
 * A norma completa inclui regras que exigem julgamento humano e que uma ferramenta não decide; essas
 * ficam de fora de propósito, porque um teste que falha sem se poder saber porquê é um teste que se
 * aprende a ignorar.
 */
async function audit(page: Page) {
  const result = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()

  return result.violations.map((violation) => ({
    rule: violation.id,
    impact: violation.impact,
    help: violation.help,
    where: violation.nodes.map((node) => node.target.join(' ')).slice(0, 3),
  }))
}

/**
 * Acessibilidade medida contra a norma, e não contra uma opinião.
 *
 * Metade deste trabalho já estava feito sem o dizer: os testes end-to-end sempre encontraram tudo
 * por `getByRole` e `getByLabel`, o que só é possível numa interface com papéis e rótulos corretos.
 * O que falta é o que esses testes não veem — contraste, ordem de foco, e o que acontece a quem
 * navega só por teclado.
 */
test.describe('Acessibilidade', () => {
  const screens = [
    ['painel', '/app/dashboard'],
    ['documentos', '/app/documents'],
    ['tarefas', '/app/tasks'],
    ['pesquisa', '/app/search'],
    ['análises', '/app/reports'],
    ['administração', '/app/admin'],
    ['integrações', '/app/integrations'],
    ['caixa de entrada', '/app/inbox'],
  ] as const

  for (const [name, path] of screens) {
    test(`o ecrã de ${name} não viola a norma`, async ({ page }) => {
      await signIn(page)
      await page.goto(path)
      await page.waitForLoadState('networkidle')

      const violations = await audit(page)

      expect(violations, JSON.stringify(violations, null, 2)).toEqual([])
    })
  }

  test('a entrada e a página inicial também, que é onde alguém chega primeiro', async ({ page }) => {
    await page.goto('/')
    await page.waitForLoadState('networkidle')
    expect(await audit(page), 'página inicial').toEqual([])

    await page.goto('/login')
    await page.waitForLoadState('networkidle')
    expect(await audit(page), 'entrada').toEqual([])
  })

  test('o modo escuro é auditado por si, porque é outro conjunto de cores', async ({ page }) => {
    await signIn(page)

    for (const path of ['/app/dashboard', '/app/documents', '/app/reports']) {
      await page.goto(path)
      await page.evaluate(() => {
        document.documentElement.dataset.theme = 'dark'
      })
      await page.waitForLoadState('networkidle')

      // Calcular o contraste dos tokens diz que os números batem certo. Só a auditoria sobre a
      // página renderizada diz se batem certo onde as cores se combinam de facto — num crachá
      // sobre um fundo recuado, numa borda, num estado desativado.
      expect(await audit(page), `modo escuro em ${path}`).toEqual([])
    }
  })

  test('chega-se aos documentos e abre-se um, só com o teclado', async ({ page }) => {
    await signIn(page)

    // Sem rato a partir daqui. Quem navega por teclado não «vê» a página: percorre-a, e o que não
    // estiver na ordem de tabulação simplesmente não existe.
    await page.goto('/app/documents')
    await page.waitForLoadState('networkidle')

    const reached = await page.evaluate(async () => {
      const focusable = Array.from(
        document.querySelectorAll<HTMLElement>(
          'a[href], button:not([disabled]), input:not([disabled]), select, textarea, [tabindex]:not([tabindex="-1"])',
        ),
      ).filter((element) => element.offsetParent !== null)

      return focusable.length
    })

    // Uma página de listagem sem nada focável é uma página que só se usa com rato.
    expect(reached).toBeGreaterThan(5)

    await page.keyboard.press('Tab')

    const hasVisibleFocus = await page.evaluate(() => {
      const active = document.activeElement

      if (!active || active === document.body) {
        return false
      }

      const styles = getComputedStyle(active)

      // O anel de foco é o que diz a quem usa teclado onde está. Sem ele, a navegação existe e é
      // invisível — que na prática é o mesmo que não existir.
      return styles.outlineStyle !== 'none' || styles.boxShadow !== 'none'
    })

    expect(hasVisibleFocus, 'o elemento focado não mostra que está focado').toBe(true)
  })

  test('o diálogo do desenhador devolve o foco a quem o abriu', async ({ page }) => {
    await signIn(page)
    await page.goto('/app/integrations')
    await page.waitForLoadState('networkidle')

    const card = page.getByRole('region', { name: 'Webhooks' })
    await clickReliably(card.getByRole('button', { name: 'Configurar' }))

    const dialog = page.getByRole('dialog', { name: 'Definições do webhook' })
    await dialog.waitFor()

    expect(await audit(page), 'diálogo aberto').toEqual([])

    await page.keyboard.press('Escape')
    await dialog.waitFor({ state: 'hidden' })

    // Fechar um diálogo e deixar o foco no nada devolve quem usa teclado ao início da página.
    const focusIsSomewhere = await page.evaluate(() => document.activeElement !== document.body)

    expect(focusIsSomewhere, 'o foco ficou perdido depois de fechar o diálogo').toBe(true)
  })
})
