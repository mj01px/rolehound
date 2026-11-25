import { Navigate, useNavigate } from 'react-router'
import { useMe } from '../auth/api'
import { AuthLayout } from '../auth/AuthLayout'
import { TermsAccept } from './TermsAccept'

/** For people already using the app when the texts change: accept the new version to go on. */
export function AcceptPage() {
  const me = useMe()
  const navigate = useNavigate()
  if (me.data?.termsAccepted) return <Navigate to="/" replace />
  return (
    <AuthLayout title="Termos atualizados">
      <TermsAccept changed onDone={() => navigate('/', { replace: true })} />
    </AuthLayout>
  )
}
