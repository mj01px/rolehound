import { useMutation, useQueryClient } from '@tanstack/react-query'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { useMe } from '../features/auth/api'
import { api } from '../lib/api'
import { Logo } from '../ui/Logo'
import { ReauthProvider } from '../features/account/ReauthProvider'
import styles from './AppLayout.module.css'

const navClass = ({ isActive }: { isActive: boolean }) => (isActive ? `${styles.item} ${styles.active}` : styles.item)

export function AppLayout() {
  const me = useMe().data
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const logout = useMutation({
    mutationFn: () => api.post('/auth/logout'),
    onSuccess: () => {
      queryClient.clear()
      navigate('/entrar', { replace: true })
    },
  })

  return (
    <ReauthProvider>
      <div className={styles.shell}>
        <nav aria-label="Principal" className={styles.sidebar}>
          <div className={styles.brand}>
            <Logo size={20} label="" />
            <span>rolehound</span>
          </div>
          <div className={styles.group}>
            <NavLink to="/" className={() => navClass({ isActive: location.pathname === '/' || location.pathname.startsWith('/vagas/') })}>
              Vagas
            </NavLink>
            <NavLink to="/funil" className={navClass}>
              Funil
            </NavLink>
            <NavLink to="/qualidade" className={navClass}>
              Qualidade
            </NavLink>
          </div>
          {me?.role === 'admin' && (
            <div className={styles.group}>
              <span className={styles.groupLabel}>Admin</span>
              <NavLink to="/admin/usuarios" className={navClass}>
                Usuários
              </NavLink>
              <NavLink to="/admin/fontes" className={navClass}>
                Fontes
              </NavLink>
              <NavLink to="/admin/qualidade" className={navClass}>
                Qualidade
              </NavLink>
            </div>
          )}
          <div className={styles.spacer} />
          <div className={styles.group}>
            <NavLink to="/configuracoes" className={navClass}>
              Configurações
            </NavLink>
            <div className={styles.user}>
              <span className={styles.username}>{me?.username}</span>
              <button type="button" className={styles.logout} onClick={() => logout.mutate()} disabled={logout.isPending}>
                Sair
              </button>
            </div>
          </div>
        </nav>
        <main className={styles.content}>
          <Outlet />
        </main>
      </div>
    </ReauthProvider>
  )
}
