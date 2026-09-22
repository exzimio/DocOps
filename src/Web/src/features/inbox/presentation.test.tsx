import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ConnectorNotice, ImportanceBadge } from './presentation'
import { EmailImportance, MailSource } from './types'

describe('ImportanceBadge', () => {
  it('says nothing about a normal message — importance is only worth a badge when it stands out', () => {
    const { container } = render(<ImportanceBadge importance={EmailImportance.Normal} />)

    expect(container.textContent).toBe('')
  })

  it('marks a high-importance message', () => {
    render(<ImportanceBadge importance={EmailImportance.High} />)

    expect(screen.getByText('Alta')).toBeInTheDocument()
  })
})

describe('ConnectorNotice', () => {
  it('names and explains whichever mail system answered, so a demo mailbox is never mistaken for a connected one', () => {
    render(<ConnectorNotice source={MailSource.Demo} name="Caixa de demonstração" description="Simulada." />)

    const notice = screen.getByRole('note')
    expect(notice).toHaveTextContent('Caixa de demonstração')
    expect(notice).toHaveTextContent('Simulada.')
  })

  it('renders compactly when asked to, keeping the description as a hint', () => {
    render(<ConnectorNotice source={MailSource.Demo} name="Demo" description="Explica." compact />)

    const badge = screen.getByTitle('Explica.')
    expect(badge).toHaveTextContent('Demo')
  })
})
