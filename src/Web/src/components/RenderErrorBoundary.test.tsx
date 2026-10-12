import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Link, MemoryRouter, Outlet, Route, Routes, useLocation } from 'react-router-dom'
import { RenderErrorBoundary } from './RenderErrorBoundary'

function Broken(): never {
  throw new Error('render failure')
}

/** The shape the shells use: navigation outside the boundary, the routed screen inside it. */
function Shell() {
  const location = useLocation()

  return (
    <>
      <nav>
        <Link to="/healthy">Outro ecrã</Link>
      </nav>
      <RenderErrorBoundary resetKey={location.pathname}>
        <Outlet />
      </RenderErrorBoundary>
    </>
  )
}

describe('RenderErrorBoundary', () => {
  it('keeps the navigation and explains the failure when a screen throws, and recovers on the next screen', async () => {
    // React reports the caught error on the console; that is expected here.
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})

    render(
      <MemoryRouter initialEntries={['/broken']}>
        <Routes>
          <Route element={<Shell />}>
            <Route path="/broken" element={<Broken />} />
            <Route path="/healthy" element={<p>Ecrã que abre</p>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('Este ecrã não conseguiu abrir')
    expect(screen.getByRole('button', { name: 'Recarregar a página' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('link', { name: 'Outro ecrã' }))

    expect(screen.getByText('Ecrã que abre')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()

    consoleError.mockRestore()
  })
})
