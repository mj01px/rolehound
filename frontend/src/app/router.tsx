import { createBrowserRouter } from 'react-router'
import { SettingsPage } from '../features/account/SettingsPage'
import { UserPage } from '../features/admin/users/UserPage'
import { UsersPage } from '../features/admin/users/UsersPage'
import { AccessPage } from '../features/auth/AccessPage'
import { ConfirmEmailPage } from '../features/auth/ConfirmEmailPage'
import { LoginPage } from '../features/auth/LoginPage'
import { ForgotPasswordPage, ForgotPasswordSentPage, ResetPasswordPage } from '../features/auth/PasswordResetPages'
import { TwoFactorPage } from '../features/auth/TwoFactorPage'
import { AccuracyPage } from '../features/accuracy/AccuracyPage'
import { AdminAccuracyPage } from '../features/admin/AdminAccuracyPage'
import { AdminModelsPage } from '../features/admin/AdminModelsPage'
import { CollectionsPage } from '../features/admin/sources/CollectionsPage'
import { CompaniesPage } from '../features/companies/CompaniesPage'
import { SourcesPage } from '../features/admin/sources/SourcesPage'
import { ApplicationPage } from '../features/funnel/ApplicationPage'
import { FunnelPage } from '../features/funnel/FunnelPage'
import { JobDrawer } from '../features/jobs/JobDrawer'
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
          // The job detail opens over the list, so the list stays where it was.
          { path: '/', element: <JobsPage />, children: [{ path: 'vagas/:id', element: <JobDrawer /> }] },
          { path: '/funil', element: <FunnelPage /> },
          { path: '/funil/:id', element: <ApplicationPage /> },
          { path: '/empresas', element: <CompaniesPage /> },
          { path: '/qualidade', element: <AccuracyPage /> },
          { path: '/configuracoes', element: <SettingsPage /> },
          {
            element: <RequireAuth admin />,
            children: [
              { path: '/admin/usuarios', element: <UsersPage /> },
              { path: '/admin/usuarios/:id', element: <UserPage /> },
              { path: '/admin/fontes', element: <SourcesPage /> },
              { path: '/admin/fontes/leituras', element: <CollectionsPage /> },
              { path: '/admin/qualidade', element: <AdminAccuracyPage /> },
              { path: '/admin/modelos', element: <AdminModelsPage /> },
            ],
          },
        ],
      },
    ],
  },
])
