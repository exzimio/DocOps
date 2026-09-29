import { Suspense, lazy } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { AppShell } from '@/app/AppShell'
import { PortalShell } from '@/app/PortalShell'
import { RequireAuth } from '@/features/auth/RequireAuth'
import { RequirePortal } from '@/features/auth/RequirePortal'
import { LandingPage } from '@/pages/LandingPage'
import { LoginPage } from '@/pages/LoginPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { NotFoundPage } from '@/pages/NotFoundPage'
import { LoadingState } from '@/components/ui/Surface'

// The designer pulls in the diagramming library; only the people who open it pay for it.
/**
 * Cada ecrã chega quando alguém lá vai.
 *
 * Antes, tudo era carregado de uma vez: quem abria o ecrã de entrada descarregava a administração,
 * o centro de IA, os gráficos das análises e o portal de fornecedores — código que a maioria das
 * pessoas nunca abre na sessão. Ficam imediatos apenas os ecrãs anteriores à sessão e o painel, que
 * é o primeiro a seguir ao início de sessão e onde uma espera se notaria.
 */
const DocumentsPage = lazy(() =>
  import('@/pages/DocumentsPage').then((module) => ({ default: module.DocumentsPage })),
)

const DocumentDetailPage = lazy(() =>
  import('@/pages/DocumentDetailPage').then((module) => ({ default: module.DocumentDetailPage })),
)

const TasksPage = lazy(() => import('@/pages/TasksPage').then((module) => ({ default: module.TasksPage })))

const WorkflowsPage = lazy(() =>
  import('@/pages/WorkflowsPage').then((module) => ({ default: module.WorkflowsPage })),
)

const WorkflowDefinitionPage = lazy(() =>
  import('@/pages/WorkflowDefinitionPage').then((module) => ({ default: module.WorkflowDefinitionPage })),
)

const WorkflowInstancePage = lazy(() =>
  import('@/pages/WorkflowInstancePage').then((module) => ({ default: module.WorkflowInstancePage })),
)

const AiCenterPage = lazy(() =>
  import('@/pages/AiCenterPage').then((module) => ({ default: module.AiCenterPage })),
)

const InboxPage = lazy(() => import('@/pages/InboxPage').then((module) => ({ default: module.InboxPage })))

const IntegrationsPage = lazy(() =>
  import('@/pages/IntegrationsPage').then((module) => ({ default: module.IntegrationsPage })),
)

const SearchPage = lazy(() => import('@/pages/SearchPage').then((module) => ({ default: module.SearchPage })))

const ReportsPage = lazy(() =>
  import('@/pages/ReportsPage').then((module) => ({ default: module.ReportsPage })),
)

const AdminPage = lazy(() => import('@/pages/AdminPage').then((module) => ({ default: module.AdminPage })))

const PortalPage = lazy(() => import('@/pages/PortalPage').then((module) => ({ default: module.PortalPage })))

const PortalSubmissionPage = lazy(() =>
  import('@/pages/PortalSubmissionPage').then((module) => ({ default: module.PortalSubmissionPage })),
)

const WorkflowDesignerPage = lazy(() =>
  import('@/pages/WorkflowDesignerPage').then((module) => ({ default: module.WorkflowDesignerPage })),
)

export function App() {
  const { t } = useTranslation()

  return (
    // Uma fronteira só, à volta de tudo o que é encaminhado. Cada rota adiada podia ter a sua, mas
    // catorze `Suspense` iguais dizem catorze vezes a mesma coisa — e a espera é a mesma: o ecrã
    // seguinte ainda não chegou.
    <Suspense fallback={<LoadingState label={t('common.loading')} />}>
      <Routes>
        <Route path="/" element={<LandingPage />} />
        <Route path="/login" element={<LoginPage />} />

        <Route element={<RequireAuth />}>
          {/* The external portal is a separate experience on purpose, with its own shell and nothing
            of the internal navigation in it. */}
          <Route element={<RequirePortal external />}>
            <Route path="/portal" element={<PortalShell />}>
              <Route index element={<PortalPage />} />
              <Route path="submissions/:id" element={<PortalSubmissionPage />} />
            </Route>
          </Route>

          <Route element={<RequirePortal external={false} />}>
            <Route path="/app" element={<AppShell />}>
              <Route index element={<Navigate to="/app/dashboard" replace />} />
              <Route path="dashboard" element={<DashboardPage />} />
              <Route path="documents" element={<DocumentsPage />} />
              <Route path="documents/:id" element={<DocumentDetailPage />} />
              <Route path="search" element={<SearchPage />} />
              <Route path="inbox" element={<InboxPage />} />
              <Route path="integrations" element={<IntegrationsPage />} />
              <Route path="reports" element={<ReportsPage />} />
              <Route path="admin" element={<AdminPage />} />
              <Route path="tasks" element={<TasksPage />} />
              <Route path="workflows" element={<WorkflowsPage />} />
              <Route path="workflows/instances/:id" element={<WorkflowInstancePage />} />
              <Route path="ai" element={<AiCenterPage />} />
              <Route path="workflows/:id" element={<WorkflowDefinitionPage />} />
              <Route
                path="workflows/:id/designer"
                element={
                  <Suspense fallback={<LoadingState label={t('common.loading')} />}>
                    <WorkflowDesignerPage />
                  </Suspense>
                }
              />
            </Route>
          </Route>
        </Route>

        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </Suspense>
  )
}
