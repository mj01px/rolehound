import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { Link, Outlet, useLocation, useNavigate } from 'react-router'
import { ReauthProvider } from '../features/account/ReauthProvider'
import { useMe } from '../features/auth/api'
import { NotificationBell } from '../features/notifications/NotificationBell'
import { api } from '../lib/api'
import { Logo } from '../ui/Logo'
import { icons } from './navIcons'
import { Sidebar } from './Sidebar'
import styles from './AppLayout.module.css'

export function AppLayout() {
  const me = useMe().data
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const queryClient = useQueryClient()
  const [menuOpen, setMenuOpen] = useState(false)
  const logout = useMutation({
    mutationFn: () => api.post('/auth/logout'),
    onSuccess: () => {
      queryClient.clear()
      navigate('/entrar', { replace: true })
    },
  })

  // Phones: Escape closes the menu.
  useEffect(() => {
    if (!menuOpen) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setMenuOpen(false)
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [menuOpen])

  const inSettings = pathname.startsWith('/configuracoes')
  const inProfile = pathname.startsWith('/perfil')

  return (
    <ReauthProvider>
      <div className={styles.shell}>
        <Sidebar
          isAdmin={me?.role === 'admin'}
          onLogout={() => logout.mutate()}
          loggingOut={logout.isPending}
          mobileOpen={menuOpen}
          onCloseMobile={() => setMenuOpen(false)}
        />
        {menuOpen && <div className={styles.backdrop} onClick={() => setMenuOpen(false)} aria-hidden="true" />}
        <div className={styles.column}>
          <header className={styles.topbar}>
            <div className={styles.mobileOnly}>
              <button type="button" className={styles.iconButton} onClick={() => setMenuOpen(true)} aria-label="Abrir menu" aria-expanded={menuOpen}>
                {icons.menu}
              </button>
              <span className={styles.brand}>
                <Logo size={20} label="" />
                rolehound
              </span>
            </div>
            <div className={styles.account}>
              <NotificationBell />
              <Link
                to="/configuracoes"
                className={inSettings ? `${styles.iconButton} ${styles.current}` : styles.iconButton}
                aria-label="Configurações"
                aria-current={inSettings ? 'page' : undefined}
                title="Configurações"
              >
                {icons.settings}
              </Link>
              <Link
                to="/perfil"
                className={inProfile ? `${styles.profile} ${styles.profileCurrent}` : styles.profile}
                aria-label="Seu perfil"
                aria-current={inProfile ? 'page' : undefined}
                title={me ? `Seu perfil · ${me.username}` : 'Seu perfil'}
              >
                <span className={styles.avatar} aria-hidden="true">
                  {me?.username[0]?.toUpperCase() ?? ''}
                </span>
              </Link>
            </div>
          </header>
          <main className={styles.content}>
            <Outlet />
          </main>
        </div>
      </div>
    </ReauthProvider>
  )
}
