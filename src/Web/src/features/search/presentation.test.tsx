import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SnippetText } from './presentation'

/**
 * The excerpt marks what the server found in the document's own text. These tests hold the component
 * to that: it marks exactly the range it was given, and it never invents one.
 */
describe('SnippetText', () => {
  it('marks the range the server pointed at', () => {
    render(<SnippetText snippet={{ text: 'Fatura Atlas Tecnologia', start: 7, length: 5 }} />)

    expect(screen.getByText('Atlas').tagName).toBe('MARK')
  })

  it('keeps the whole excerpt readable around the mark', () => {
    const { container } = render(
      <SnippetText snippet={{ text: 'Total a pagar: 1 906,50 €', start: 15, length: 8 }} />,
    )

    expect(container.textContent).toBe('Total a pagar: 1 906,50 €')
  })

  it('marks nothing when there is nothing to mark', () => {
    const { container } = render(
      <SnippetText snippet={{ text: 'Sem correspondência', start: 0, length: 0 }} />,
    )

    expect(container.querySelector('mark')).toBeNull()
    expect(container.textContent).toBe('Sem correspondência')
  })

  /**
   * A range that does not fit the text would slice the excerpt into nonsense. The component shows
   * the text unmarked instead — wrong highlighting is worse than none.
   */
  it('refuses a range that falls outside the excerpt', () => {
    const { container } = render(<SnippetText snippet={{ text: 'Contrato', start: 4, length: 40 }} />)

    expect(container.querySelector('mark')).toBeNull()
    expect(container.textContent).toBe('Contrato')
  })
})
