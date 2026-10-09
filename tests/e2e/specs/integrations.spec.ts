import { expect, test, type Page } from '@playwright/test'
import { clickReliably, toggleReliably } from '../support/interactions'

const admin = { email: 'admin@asteria.example', password: 'Demo!Pass123' }
const manager = { email: 'sofia.martins@asteria.example', password: 'Demo!Pass123' }
const employee = { email: 'ana.ferreira@asteria.example', password: 'Demo!Pass123' }

async function signIn(page: Page, account: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Endereço de e-mail').fill(account.email)
  await page.getByLabel('Palavra-passe').fill(account.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL(/\/app\/dashboard$/)
}

test.describe('Centro de integrações', () => {
  test('mostra os seis adaptadores reais, cada um a dizer com precisão o que simula', async ({ page }) => {
    await signIn(page, admin)
    await page.goto('/app/integrations')
    await page.getByRole('heading', { name: 'Integrações' }).waitFor()

    for (const name of [
      'Microsoft Outlook',
      'ERP',
      'CRM',
      'Assinatura eletrónica',
      'Webhooks',
      'Importação de ficheiros',
    ]) {
      await expect(page.getByRole('region', { name })).toBeVisible()
    }

    // ADR-007: só se diz simulado o que realmente o é. A lista de fornecedores do ERP e a de contas
    // do CRM são listas locais fixas, e a caixa de correio corre em modo de demonstração — os três
    // têm de o dizer no próprio cartão. A assinatura, os webhooks e a importação de ficheiros fazem
    // trabalho real sobre dados reais (contam documentos, entregam HTTP, movem ficheiros do disco) e
    // não devem alegar ser uma simulação.
    for (const name of ['Microsoft Outlook', 'ERP', 'CRM']) {
      await expect(page.getByRole('region', { name }).getByRole('note')).toContainText('Simulada.')
    }
    for (const name of ['Assinatura eletrónica', 'Webhooks', 'Importação de ficheiros']) {
      await expect(page.getByRole('region', { name }).getByRole('note')).toHaveCount(0)
    }
  })

  test('executar a ERP reconcilia fornecedores reais e regista a divergência conhecida', async ({ page }) => {
    await signIn(page, admin)
    await page.goto('/app/integrations')
    await page.getByRole('heading', { name: 'Integrações' }).waitFor()

    const erpCard = page.getByRole('region', { name: 'ERP' })
    await clickReliably(erpCard.getByRole('button', { name: 'Executar agora' }))
    await expect(erpCard.getByText(/registo\(s\) reconciliado/)).toBeVisible({ timeout: 15_000 })

    // "Nordvik Systems" é fornecedor real de uma fatura seed mas está deliberadamente ausente da
    // lista fixa do ERP simulado — a reconciliação tem de encontrar exatamente essa divergência.
    // Corridas anteriores desta suite podem ter deixado várias entradas iguais no registo (a mesma
    // divergência repete-se sempre que a integração corre); só interessa que pelo menos uma exista.
    await clickReliably(erpCard.getByRole('button', { name: 'Ver registo' }))
    const logDialog = page.getByRole('dialog', { name: /Registo de ERP/ })
    await logDialog.waitFor()
    await expect(
      logDialog.getByText('«Nordvik Systems» não existe no sistema externo.').first(),
    ).toBeVisible()

    // O filtro de nível esconde a entrada informativa e deixa só o aviso.
    await clickReliably(logDialog.getByRole('button', { name: 'Aviso' }))
    await expect(
      logDialog.getByText('«Nordvik Systems» não existe no sistema externo.').first(),
    ).toBeVisible()
    await expect(logDialog.getByText(/valor\(es\) de/)).toHaveCount(0)

    await clickReliably(logDialog.getByRole('button', { name: 'Fechar' }))
    await expect(logDialog).toHaveCount(0)
  })

  test('desativar e reativar uma integração muda o estado real e sobrevive a recarregar a página', async ({
    page,
  }) => {
    await signIn(page, admin)
    await page.goto('/app/integrations')
    await page.getByRole('heading', { name: 'Integrações' }).waitFor()

    const fileImportCard = page.getByRole('region', { name: 'Importação de ficheiros' })
    const toggle = fileImportCard.getByRole('checkbox', { name: 'Ativa' })

    // O estado visível do interruptor só reflete a resposta do servidor depois de a mutação voltar
    // e a lista ser invalidada — por isso clica-se e espera-se pelo estado, em vez de usar
    // check()/uncheck(), que exigem que o clique por si só já tenha mudado o DOM.
    // Uma corrida anterior pode ter deixado isto desativado; garante-se um estado inicial conhecido.
    if (!(await toggle.isChecked())) {
      await toggleReliably(toggle)
      await expect(toggle).toBeChecked()
    }

    await toggleReliably(toggle)
    await expect(toggle).not.toBeChecked()
    await expect(fileImportCard.getByText('Desativada')).toBeVisible()
    // O botão continua visível — desativar não o esconde, só o desliga, e é essa diferença que uma
    // integração inativa tem de mostrar.
    await expect(fileImportCard.getByRole('button', { name: 'Executar agora' })).toBeDisabled()

    await page.reload()
    await page.getByRole('heading', { name: 'Integrações' }).waitFor()
    await expect(
      page.getByRole('region', { name: 'Importação de ficheiros' }).getByText('Desativada'),
    ).toBeVisible()

    // Repõe o catálogo de demonstração como estava.
    const restoredCard = page.getByRole('region', { name: 'Importação de ficheiros' })
    await toggleReliably(restoredCard.getByRole('checkbox', { name: 'Ativa' }))
    await expect(restoredCard.getByText('Desativada')).toHaveCount(0)
  })

  test('as definições do webhook recusam um destino inseguro e aceitam um HTTPS real', async ({ page }) => {
    await signIn(page, admin)
    await page.goto('/app/integrations')
    await page.getByRole('heading', { name: 'Integrações' }).waitFor()

    const webhookCard = page.getByRole('region', { name: 'Webhooks' })
    await clickReliably(webhookCard.getByRole('button', { name: 'Configurar' }))
    const dialog = page.getByRole('dialog', { name: 'Definições do webhook' })
    await dialog.waitFor()

    const endpointField = dialog.getByLabel('Destino (HTTPS)')
    await endpointField.fill('http://exemplo.com/hook')
    await clickReliably(dialog.getByRole('button', { name: 'Guardar' }))
    // A verificação que conta corre no servidor: um endereço sem HTTPS é recusado por SSRF, não só
    // por estética do formulário. E a recusa diz qual das regras falhou — sem a razão, quem escreveu
    // o endereço fica a adivinhar entre HTTPS, endereço interno e endereço mal formado.
    await expect(dialog.getByText('O destino do webhook não é aceitável.')).toBeVisible()
    await expect(dialog.getByText('O destino tem de usar HTTPS.')).toBeVisible()
    await expect(dialog).toBeVisible()

    // Outra regra, outra razão: o endereço da própria máquina é o caso clássico de SSRF.
    await endpointField.fill('https://127.0.0.1/hook')
    await clickReliably(dialog.getByRole('button', { name: 'Guardar' }))
    await expect(dialog.getByText('O destino aponta para a própria máquina.')).toBeVisible()

    await endpointField.fill('https://hooks.example.test/docops')
    await clickReliably(dialog.getByRole('button', { name: 'Guardar' }))
    await expect(dialog).toHaveCount(0)

    // O valor guardado é real: reabrir o diálogo mostra o mesmo endereço, lido da base de dados.
    await clickReliably(webhookCard.getByRole('button', { name: 'Configurar' }))
    const reopened = page.getByRole('dialog', { name: 'Definições do webhook' })
    await expect(reopened.getByLabel('Destino (HTTPS)')).toHaveValue('https://hooks.example.test/docops')

    // Limpa o destino para o catálogo de demonstração ficar como estava.
    await reopened.getByLabel('Destino (HTTPS)').fill('')
    await clickReliably(reopened.getByRole('button', { name: 'Guardar' }))
    await expect(reopened).toHaveCount(0)
  })

  test('quem só tem permissão de visualização vê os cartões sem os controlos de gestão', async ({ page }) => {
    await signIn(page, manager)
    await page.goto('/app/integrations')
    await page.getByRole('heading', { name: 'Integrações' }).waitFor()

    const erpCard = page.getByRole('region', { name: 'ERP' })
    await expect(erpCard).toBeVisible()
    await expect(erpCard.getByRole('button', { name: 'Executar agora' })).toHaveCount(0)
    await expect(erpCard.getByRole('button', { name: 'Configurar' })).toHaveCount(0)
    await expect(erpCard.getByRole('checkbox', { name: 'Ativa' })).toHaveCount(0)
    // Ver o registo continua disponível: essa parte só exige a permissão de visualização.
    await expect(erpCard.getByRole('button', { name: 'Ver registo' })).toBeVisible()
  })

  test('sem integrations.view a hiperligação das integrações não aparece', async ({ page }) => {
    await signIn(page, employee)

    await expect(page.getByRole('link', { name: 'Integrações' })).toHaveCount(0)
  })
})
