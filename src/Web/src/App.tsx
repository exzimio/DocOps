import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from '@/app/AppShell'
import { RequireAuth } from '@/features/auth/RequireAuth'
import { LandingPage } from '@/pages/LandingPage'
import { LoginPage } from '@/pages/LoginPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { DocumentsPage } from '@/pages/DocumentsPage'
import { DocumentDetailPage } from '@/pages/DocumentDetailPage'
import { NotFoundPage } from '@/pages/NotFoundPage'

export function App() {
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
        </Route>
      </Route>

      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  )
}
