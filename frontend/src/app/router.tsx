import { createBrowserRouter } from 'react-router'
import { SettingsPage } from '../features/account/SettingsPage'
import { AdminUsersPage } from '../features/admin/AdminUsersPage'
import { AccessPage } from '../features/auth/AccessPage'
import { ConfirmEmailPage } from '../features/auth/ConfirmEmailPage'
import { LoginPage } from '../features/auth/LoginPage'
import { ForgotPasswordPage, ForgotPasswordSentPage, ResetPasswordPage } from '../features/auth/PasswordResetPages'
import { TwoFactorPage } from '../features/auth/TwoFactorPage'
import { AccuracyPage } from '../features/accuracy/AccuracyPage'
import { AdminAccuracyPage } from '../features/admin/AdminAccuracyPage'
import { AdminModelsPage } from '../features/admin/AdminModelsPage'
import { AdminSourcesPage } from '../features/admin/AdminSourcesPage'
import { ApplicationPage } from '../features/funnel/ApplicationPage'
import { FunnelPage } from '../features/funnel/FunnelPage'
import { JobDetailPage } from '../features/jobs/JobDetailPage'
import { JobsPage } from '../features/jobs/JobsPage'
import { OnboardingPage } from '../features/onboarding/OnboardingPage'
import { AppLayout } from './AppLayout'
import { RequireAuth, RequireGuest } from './RequireAuth'

// Paths are in Portuguese: people see them. Links in emails point to
// /acesso, /nova-senha and /confirmar-email.
export const router = createBrowserRouter([
  {
    element: <RequireGuest />,
    children: [
      { path: '/entrar', element: <LoginPage /> },
      { path: '/entrar/verificacao', element: <TwoFactorPage /> },
      { path: '/esqueci-senha', element: <ForgotPasswordPage /> },
      { path: '/esqueci-senha/enviado', element: <ForgotPasswordSentPage /> },
    ],
  },
  { path: '/acesso', element: <AccessPage /> },
  { path: '/nova-senha', element: <ResetPasswordPage /> },
  { path: '/confirmar-email', element: <ConfirmEmailPage /> },
  {
    element: <RequireAuth />,
    children: [
      { path: '/configuracao-inicial', element: <OnboardingPage /> },
      {
        element: <AppLayout />,
        children: [
          { path: '/', element: <JobsPage /> },
          { path: '/vagas/:id', element: <JobDetailPage /> },
          { path: '/funil', element: <FunnelPage /> },
          { path: '/funil/:id', element: <ApplicationPage /> },
          { path: '/qualidade', element: <AccuracyPage /> },
          { path: '/configuracoes', element: <SettingsPage /> },
          {
            element: <RequireAuth admin />,
            children: [
              { path: '/admin/usuarios', element: <AdminUsersPage /> },
              { path: '/admin/fontes', element: <AdminSourcesPage /> },
              { path: '/admin/qualidade', element: <AdminAccuracyPage /> },
              { path: '/admin/modelos', element: <AdminModelsPage /> },
            ],
          },
        ],
      },
    ],
  },
])
