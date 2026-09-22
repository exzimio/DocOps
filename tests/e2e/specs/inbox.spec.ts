import { expect, test, type Page } from '@playwright/test'

const manager = { email: 'sofia.martins@asteria.example', password: 'Demo!Pass123' }
const externalPartner = { email: 'contacto@northstar.example', password: 'Demo!Pass123' }

async function signIn(page: Page, account: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(account.email)
  await page.getByLabel('Palavra-passe').fill(account.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}

/** Picks one seeded message by who sent it — a stable identity, unlike its status badge. */
function messageFrom(page: Page, fromName: string) {
  return page.getByRole('list', { name: 'Mensagens' }).getByRole('button').filter({ hasText: fromName })
}

/**
 * Turns every status filter on. Tests below act on fixed seeded messages, and this suite may have
 * left one of them Processed or Archived on an earlier run against the same database — without
 * this, the default "Não lida"/"Lida" filter would hide it and the test would never find it.
 */
async function showEveryStatus(page: Page) {
  await page.getByRole('button', { name: 'Processada', exact: true }).click()
  await page.getByRole('button', { name: 'Arquivada', exact: true }).click()
}

test.describe('Caixa de entrada', () => {
  test('mostra a caixa de demonstração identificada como tal, e sincronizar duas vezes não duplica mensagens', async ({
    page,
  }) => {
    await signIn(page, manager)
    await page.goto('/app/inbox')

    await expect(page.getByText('Caixa de correio de demonstração')).toBeVisible()

    const list = page.getByRole('list', { name: 'Mensagens' })
    await expect(list.getByRole('button').first()).toBeVisible()
    const before = await list.getByRole('button').count()

    await page.getByRole('button', { name: 'Sincronizar' }).click()
    await expect(page.getByText(/nova\(s\) de \d+ mensagem\(ns\)/)).toBeVisible()

    await expect(list.getByRole('button')).toHaveCount(before)
  })

  test('abrir uma mensagem marca-a como lida', async ({ page }) => {
    await signIn(page, manager)
    await page.goto('/app/inbox')

    // A mensagem pode já estar "Lida" de uma corrida anterior desta suite (desktop e móvel
    // partilham a mesma base de dados); o que importa é que, depois de aberta, fica lida.
    const row = messageFrom(page, 'Carlos Mendes')
    await row.click()

    await expect(page.getByRole('heading', { name: /Ibérica Logística/ })).toBeVisible()
    await expect(row).toContainText('Lida')
  })

  test('o filtro de arquivadas mostra só o que está arquivado', async ({ page }) => {
    await signIn(page, manager)
    await page.goto('/app/inbox')

    await page.getByRole('button', { name: 'Não lida', exact: true }).click()
    await page.getByRole('button', { name: 'Lida', exact: true }).click()
    await page.getByRole('button', { name: 'Arquivada', exact: true }).click()

    const list = page.getByRole('list', { name: 'Mensagens' })
    await expect(list.getByRole('button').first()).toBeVisible()

    for (const row of await list.getByRole('button').all()) {
      await expect(row).toContainText('Arquivada')
    }
  })

  test('guardar um anexo cria um documento, liga a mensagem e o documento passa a existir', async ({ page }) => {
    await signIn(page, manager)
    await page.goto('/app/inbox')
    await showEveryStatus(page)

    // A minuta do contrato Nordhavn tem anexo. Desktop e móvel partilham a base de dados, por isso
    // uma corrida anterior desta mesma suite pode já a ter guardado como documento — nesse caso o
    // botão de guardar dá lugar à hiperligação, e é essa hiperligação que se verifica.
    await messageFrom(page, 'Henrique Bastos').click()
    await page.getByRole('heading', { name: /Nordhavn/ }).waitFor()

    const saveButton = page.getByRole('button', { name: 'Guardar como documento' })
    const createdNow = (await saveButton.count()) > 0

    if (createdNow) {
      await saveButton.click()

      const dialog = page.getByRole('dialog', { name: 'Guardar anexo como documento' })
      await dialog.getByLabel('Tipo').selectOption({ label: 'Por classificar' })
      await dialog.getByRole('button', { name: 'Guardar' }).click()
      await expect(dialog).toHaveCount(0)
    }

    // O diálogo fecha e a mensagem mostra a ligação ao novo documento. A seleção não salta para
    // outra mensagem só porque esta deixou de corresponder ao filtro "Não lida"/"Lida" ativo.
    const detail = page.getByRole('region', { name: /Nordhavn/ })
    await expect(detail.getByText('Associada ao documento')).toBeVisible()
    // "Processada" appears twice once someone acted on it: the status badge, and the "Processada
    // por…" note. The badge is what confirms the state; .first() is it, since the badge sits in
    // the header and the note only appears further down in the body.
    await expect(detail.getByText('Processada').first()).toBeVisible()

    // O documento é real: segue-se a ligação e existe do outro lado.
    await detail.getByRole('link', { name: /—/ }).click()
    await page.waitForURL(/\/app\/documents\/[0-9a-f-]+$/)
    await expect(page.getByRole('heading', { level: 1 })).not.toHaveText('')

    // Limpa o que este teste criou, para o catálogo de demonstração ficar como estava. Se a
    // mensagem já vinha processada de uma corrida anterior, o documento não é desta corrida e não
    // se apaga.
    if (createdNow) {
      page.once('dialog', (confirmation) => confirmation.accept())
      await page.getByRole('button', { name: 'Eliminar' }).click()
      await page.waitForURL(/\/app\/documents$/)
    }
  })

  test('associar a um documento existente liga a mensagem sem criar um novo documento', async ({ page }) => {
    await signIn(page, manager)
    await page.goto('/app/inbox')
    await showEveryStatus(page)

    // A renovação da parceria Nordvik não tem anexo. Associá-la de novo é seguro mesmo que uma
    // corrida anterior já a tenha associado: o servidor aceita substituir a ligação.
    await messageFrom(page, 'Anders Lindqvist').click()

    await page.getByRole('button', { name: 'Associar a documento existente' }).click()

    const dialog = page.getByRole('dialog', { name: 'Associar a um documento existente' })
    await dialog.getByRole('searchbox').fill('Northstar')

    const firstOption = dialog.getByRole('radio').first()
    await firstOption.waitFor()
    await firstOption.check()
    await dialog.getByRole('button', { name: 'Associar' }).click()

    await expect(dialog).toHaveCount(0)
    const detail = page.getByRole('region', { name: /Nordvik/ })
    await expect(detail.getByText('Associada ao documento')).toBeVisible()
    await expect(detail.getByText('Processada').first()).toBeVisible()
  })

  test('arquivar e reabrir devolvem a mensagem ao ecrã de ações', async ({ page }) => {
    await signIn(page, manager)
    await page.goto('/app/inbox')
    await showEveryStatus(page)

    // A fatura por regularizar da Northstar.
    await messageFrom(page, 'Patrícia Amorim').click()

    const detail = page.getByRole('region', { name: /regularizar/ })
    const archiveButton = detail.getByRole('button', { name: 'Arquivar' })
    const reopenButton = detail.getByRole('button', { name: 'Reabrir' })

    // Uma corrida anterior pode tê-la deixado arquivada; reabre-se primeiro para partir de um
    // estado conhecido, tal como aconteceria se alguém a tivesse arquivado por engano.
    if ((await reopenButton.count()) > 0) {
      await reopenButton.click()
      await expect(archiveButton).toBeVisible()
    }

    await archiveButton.click()
    await expect(detail.getByText('Arquivada')).toBeVisible()
    await expect(reopenButton).toBeVisible()

    await reopenButton.click()
    await expect(archiveButton).toBeVisible()
  })

  test('sem inbox.view a hiperligação da caixa de entrada não aparece', async ({ page }) => {
    await signIn(page, externalPartner)

    await expect(page.getByRole('link', { name: 'Caixa de entrada' })).toHaveCount(0)
  })
})
