import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { App } from './App'
import { ApiError } from './lib/api'
import { applyTheme, resolveInitialTheme } from './lib/theme'
import './i18n'
import './styles/index.css'

applyTheme(resolveInitialTheme())

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Authentication and authorisation failures are answers, not transient faults.
      retry: (failureCount, error) =>
        !(error instanceof ApiError && (error.isUnauthenticated || error.isForbidden)) && failureCount < 2,
      refetchOnWindowFocus: false,
      staleTime: 30_000,
    },
  },
})

const container = document.getElementById('root')

if (!container) {
  throw new Error('Root container #root is missing from index.html')
}

createRoot(container).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
