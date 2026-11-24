import { useState } from 'react'
import { useNavigate } from 'react-router'
import { MoreMenu, type MenuItem } from '../../../ui/MoreMenu'
import type { AdminUser } from './api'
import { EditUserDialog } from './UserDialog'
import { useActionsFor } from './useActionsFor'
import styles from './users.module.css'

/** The pencil and the ⋯ menu at the end of a table row. */
export function RowActions({ user }: { user: AdminUser }) {
  const navigate = useNavigate()
  const actions = useActionsFor(user)
  const [editing, setEditing] = useState(false)
  const label = user.name || user.username

  const items: MenuItem[] = [
    {
      key: 'open',
      label: 'Ver detalhes',
      run: () => navigate(`/admin/usuarios/${user.id}`),
    },
    ...(actions.available.resend
      ? [
          {
            key: 'resend',
            label: 'Enviar link de acesso',
            run: () => actions.run('resend'),
          },
        ]
      : []),
    ...(actions.available.role
      ? [
          {
            key: 'role',
            label: user.role === 'admin' ? 'Tornar usuário' : 'Tornar admin',
            run: () => actions.run('role'),
          },
        ]
      : []),
    ...(actions.available.reactivate
      ? [
          {
            key: 'reactivate',
            label: 'Reativar acesso',
            run: () => actions.run('reactivate'),
          },
        ]
      : []),
    ...(actions.available.deactivate
      ? [
          {
            key: 'deactivate',
            label: 'Desativar acesso',
            danger: true,
            run: actions.askDeactivate,
          },
        ]
      : []),
  ]

  return (
    <div className={styles.rowActions}>
      <button type="button" className={styles.iconButton} aria-label={`Editar ${label}`} onClick={() => setEditing(true)}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M4 20h4L19 9l-4-4L4 16z" />
        </svg>
      </button>
      <MoreMenu label={`Mais ações para ${label}`} items={items} disabled={actions.pending} />
      {actions.error && <span className={styles.rowError}>{actions.error}</span>}
      {editing && <EditUserDialog user={user} onClose={() => setEditing(false)} />}
      {actions.dialog}
    </div>
  )
}
