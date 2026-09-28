import { expect, test } from '@playwright/test'

/**
 * A sessão vive num cookie `HttpOnly`, e em mais lado nenhum.
 *
 * É uma das regras não-negociáveis do projeto e, até agora, a única sem prova. O modo como se perde
 * é banal: alguém depara-se com um problema de sessão — uma janela nova que não herda o estado, um
 * pedido que falha depois de recarregar — e resolve-o guardando o token no `localStorage`. Passa a
 * funcionar, ninguém repara, e a partir daí qualquer script injetado na página lê a sessão.
 *
 * O teste não confia em procurar a palavra «token»: enumera tudo o que a página guardou e recusa
 * qualquer valor com ar de credencial.
 */
test.describe('Onde a sessão vive', () => {
  test('depois de entrar, nada de parecido com uma credencial fica guardado no browser', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    const stored = await page.evaluate(() => {
      const read = (storage: Storage) =>
        Object.fromEntries(
          Array.from({ length: storage.length }, (_, index) => storage.key(index))
            .filter((key): key is string => key !== null)
            .map((key) => [key, storage.getItem(key) ?? '']),
        )

      return { local: read(localStorage), session: read(sessionStorage), cookies: document.cookie }
    })

    const entries = [...Object.entries(stored.local), ...Object.entries(stored.session)]

    for (const [key, value] of entries) {
      expect(`${key} ${value}`.toLowerCase(), `«${key}» guarda algo com ar de credencial`).not.toMatch(
        /token|bearer|jwt|passw|secret|authorization/,
      )

      // Um JWT tem três partes separadas por pontos e começa por «eyJ». Não precisa de se chamar
      // token para ser um.
      expect(value, `«${key}» parece um JWT`).not.toMatch(/^eyJ[\w-]*\.[\w-]*\./)
    }

    // O cookie de sessão é `HttpOnly`, portanto o JavaScript da página não o vê. Se o vir, deixou
    // de ser HttpOnly.
    expect(stored.cookies.toLowerCase()).not.toContain('docops.session')
  })

  test('a sessão sobrevive a recarregar a página, que é o que o cookie está lá para fazer', async ({
    page,
  }) => {
    await page.goto('/login')
    await page.getByLabel('Endereço de e-mail').fill('admin@asteria.example')
    await page.getByLabel('Palavra-passe').fill('Demo!Pass123')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL(/\/app\/dashboard$/)

    await page.reload()

    // Sem isto, o teste anterior seria satisfeito por uma aplicação que não guarda a sessão em
    // sítio nenhum — e que, por isso, também não a mantém. O cabeçalho saúda pelo nome, que só se
    // sabe com a sessão de pé; a barra lateral não serve, porque no telemóvel está recolhida.
    await expect(page).toHaveURL(/\/app\/dashboard$/)
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Helena')
  })
})
