import { describe, expect, it, vi, beforeEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LoginPage } from './LoginPage'
import { jsonResponse, mockFetch, renderWithProviders } from '@/test/renderWithProviders'
import type { CurrentUser } from '@/lib/types'

const anonymous = jsonResponse({ status: 401, title: 'Unauthorized' }, 401)

const financeUser: CurrentUser = {
  id: 'c0ffee00-0000-4000-8000-000000000001',
  email: 'ricardo.lopes@asteria.example',
  fullName: 'Ricardo Lopes',
  initials: 'RL',
  jobTitle: 'Responsável de Contas a Pagar',
  tenantId: 'c0ffee00-0000-4000-8000-0000000000ff',
  tenantName: 'Asteria Group',
  userType: 0,
  externalOrganisation: null,
  preferredCulture: 'pt-PT',
  isPlatformAdministrator: false,
  lastLoginAtUtc: null,
  roles: ['FinanceApprover'],
  permissions: ['documents.approve'],
  departments: [{ id: 'd1', name: 'Finance', code: 'FIN', isPrimary: true }],
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.unstubAllGlobals()
  })

  it('refuses to submit an empty form and reports both field errors', async () => {
    const fetchStub = mockFetch({ 'GET /api/auth/me': () => anonymous })
    vi.stubGlobal('fetch', fetchStub)

    renderWithProviders(<LoginPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Entrar' }))

    expect(await screen.findByText('Indique o seu endereço de e-mail.')).toBeInTheDocument()
    expect(screen.getByText('Indique a sua palavra-passe.')).toBeInTheDocument()

    // No login attempt reached the API.
    expect(fetchStub.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0)
  })

  it('sends the credentials to the API when the form is valid', async () => {
    const fetchStub = mockFetch({
      'GET /api/auth/me': () => anonymous,
      'POST /api/auth/login': () => jsonResponse(financeUser),
    })
    vi.stubGlobal('fetch', fetchStub)

    renderWithProviders(<LoginPage />)

    await userEvent.type(await screen.findByLabelText('Endereço de e-mail'), financeUser.email)
    await userEvent.type(screen.getByLabelText('Palavra-passe'), 'Demo!Pass123')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    await waitFor(() => {
      const loginCall = fetchStub.mock.calls.find(([, init]) => init?.method === 'POST')
      expect(loginCall).toBeDefined()
      expect(JSON.parse(loginCall![1]!.body as string)).toEqual({
        email: financeUser.email,
        password: 'Demo!Pass123',
        rememberMe: false,
      })
      // The session cookie must be carried by the browser, never handled by application code.
      expect(loginCall![1]!.credentials).toBe('include')
    })
  })

  it('translates the error code returned by the API', async () => {
    const fetchStub = mockFetch({
      'GET /api/auth/me': () => anonymous,
      'POST /api/auth/login': () =>
        jsonResponse({ status: 401, title: 'Autenticação falhou', code: 'invalid_credentials' }, 401),
    })
    vi.stubGlobal('fetch', fetchStub)

    renderWithProviders(<LoginPage />)

    await userEvent.type(await screen.findByLabelText('Endereço de e-mail'), 'errado@asteria.example')
    await userEvent.type(screen.getByLabelText('Palavra-passe'), 'PalavraErrada1!')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Credenciais inválidas. Verifique o e-mail e a palavra-passe.',
    )
  })

  it('fills the form from a demo account row', async () => {
    vi.stubGlobal('fetch', mockFetch({ 'GET /api/auth/me': () => anonymous }))

    renderWithProviders(<LoginPage />)

    const useButtons = await screen.findAllByRole('button', { name: 'Utilizar esta conta' })
    await userEvent.click(useButtons[0]!)

    expect(await screen.findByLabelText('Endereço de e-mail')).toHaveValue('admin@asteria.example')
    expect(screen.getByLabelText('Palavra-passe')).toHaveValue('Demo!Pass123')
  })
})
