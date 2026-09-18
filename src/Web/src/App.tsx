import { Suspense, lazy } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { AppShell } from '@/app/AppShell'
import { RequireAuth } from '@/features/auth/RequireAuth'
import { LandingPage } from '@/pages/LandingPage'
import { LoginPage } from '@/pages/LoginPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { DocumentsPage } from '@/pages/DocumentsPage'
import { DocumentDetailPage } from '@/pages/DocumentDetailPage'
import { TasksPage } from '@/pages/TasksPage'
import { WorkflowsPage } from '@/pages/WorkflowsPage'
import { WorkflowDefinitionPage } from '@/pages/WorkflowDefinitionPage'
import { WorkflowInstancePage } from '@/pages/WorkflowInstancePage'
import { NotFoundPage } from '@/pages/NotFoundPage'
import { LoadingState } from '@/components/ui/Surface'

// The designer pulls in the diagramming library; only the people who open it pay for it.
const WorkflowDesignerPage = lazy(() =>
  import('@/pages/WorkflowDesignerPage').then((module) => ({ default: module.WorkflowDesignerPage })),
)

export function App() {
  const { t } = useTranslation()

  return (
    <Routes>
      <Route path="/" element={<LandingPage />} />
      <Route path="/login" element={<LoginPage />} />

      <Route element={<RequireAuth />}>
        <Route path="/app" element={<AppShell />}>
          <Route index element={<Navigate to="/app/dashboard" replace />} />
          <Route path="dashboard" element={<DashboardPage />} />
          <Route path="documents" element={<DocumentsPage />} />
          <Route path="documents/:id" element={<DocumentDetailPage />} />
          <Route path="tasks" element={<TasksPage />} />
          <Route path="workflows" element={<WorkflowsPage />} />
          <Route path="workflows/instances/:id" element={<WorkflowInstancePage />} />
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

      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  )
}
