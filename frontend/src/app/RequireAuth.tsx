import { Navigate, Outlet, useLocation } from 'react-router'
import { useMe } from '../features/auth/api'

/** Pages behind a session. The API enforces the same; this only avoids flashing private screens. */
export function RequireAuth({ admin = false }: { admin?: boolean }) {
  const me = useMe()
  const { pathname } = useLocation()

  if (me.isPending) {
    return null
  }

  if (!me.data) {
    return <Navigate to="/entrar" replace />
  }

  // Until onboarding ends, the API refuses the rest of the app (FR-ONB-01); go straight there.
  if (!me.data.onboarded && pathname !== '/configuracao-inicial') {
    return <Navigate to="/configuracao-inicial" replace />
  }

  if (admin && me.data.role !== 'admin') {
    return <Navigate to="/" replace />
  }

  return <Outlet />
}

/** Sign-in pages for people without a session; signed-in people go home. */
export function RequireGuest() {
  const me = useMe()

  if (me.isPending) {
    return null
  }

  return me.data ? <Navigate to="/" replace /> : <Outlet />
}
