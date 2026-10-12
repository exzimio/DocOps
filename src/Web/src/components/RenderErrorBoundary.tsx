import { Component, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/Button'
import { ErrorState } from '@/components/ui/Surface'

/**
 * Catches a screen that fails while rendering, so the user gets an explanation and a way out instead
 * of a blank page. That includes a lazily loaded screen whose file is gone after a new deployment,
 * which a reload fixes.
 *
 * `resetKey` clears the error when it changes: around a shell's outlet it is the path, so moving to
 * another screen recovers without reloading the application.
 */
export class RenderErrorBoundary extends Component<
  { children: ReactNode; resetKey?: string },
  { hasError: boolean }
> {
  state = { hasError: false }

  static getDerivedStateFromError(): { hasError: boolean } {
    return { hasError: true }
  }

  componentDidUpdate(previous: { resetKey?: string }): void {
    if (this.state.hasError && previous.resetKey !== this.props.resetKey) {
      this.setState({ hasError: false })
    }
  }

  render(): ReactNode {
    return this.state.hasError ? <RenderErrorFallback /> : this.props.children
  }
}

function RenderErrorFallback() {
  const { t } = useTranslation()

  return (
    <ErrorState
      title={t('errors.genericTitle')}
      body={t('errors.renderBody')}
      action={
        <Button variant="secondary" onClick={() => window.location.reload()}>
          {t('errors.reload')}
        </Button>
      }
    />
  )
}
