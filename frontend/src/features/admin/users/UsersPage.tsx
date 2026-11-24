import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { errorMessage } from '../../../lib/api'
import { Alert, Notice } from '../../../ui/Alert'
import { Button } from '../../../ui/Button'
import { initial, lastAccess, roleLabel, statusLabel, useUsers, type AdminUser } from './api'
import { RowActions } from './UserActions'
import { NewUserDialog } from './UserDialog'
import styles from './users.module.css'

const normalize = (text: string) =>
  text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()

/** Accounts with search and per-row actions; creating opens a modal. */
export function UsersPage() {
  const users = useUsers()
  const [query, setQuery] = useState('')
  const [creating, setCreating] = useState(false)
  const [invited, setInvited] = useState<string | null>(null)

  const shown = useMemo(() => {
    const words = normalize(query.trim())
    return (users.data ?? []).filter((user) => !words || normalize(`${user.name} ${user.username}`).includes(words))
  }, [users.data, query])

  const all = users.data ?? []
  const pending = all.filter((user) => user.status === 'pending').length

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <div className={styles.heading}>
          <h1 className={styles.title}>Usuários</h1>
          <p className={styles.lead}>
            {users.data
              ? `Quem tem acesso ao Rolehound. ${all.length} ${all.length === 1 ? 'pessoa' : 'pessoas'}${pending ? `, ${pending} ${pending === 1 ? 'convite pendente' : 'convites pendentes'}` : ''}.`
              : 'Quem tem acesso ao Rolehound.'}
          </p>
        </div>
        <Button onClick={() => setCreating(true)}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
            <path d="M12 5v14M5 12h14" />
          </svg>
          Novo usuário
        </Button>
      </header>

      {invited && <Notice>Convite enviado para {invited}. O link vale por 72 horas.</Notice>}
      {users.isError && <Alert>{errorMessage(users.error)}</Alert>}

      <label className={styles.search}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
          <circle cx="11" cy="11" r="7" />
          <path d="m20 20-3.5-3.5" />
        </svg>
        <input type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Buscar por nome ou usuário" aria-label="Buscar usuários" />
      </label>

      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Usuário</th>
              <th>Papel</th>
              <th>Situação</th>
              <th>Último acesso</th>
              <th>
                <span className={styles.srOnly}>Ações</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {shown.map((user) => (
              <Row key={user.id} user={user} />
            ))}
          </tbody>
        </table>
        {users.data && shown.length === 0 && <p className={styles.empty}>Ninguém com essa busca.</p>}
      </div>

      {creating && (
        <NewUserDialog
          onClose={() => setCreating(false)}
          onCreated={(user) => {
            setCreating(false)
            setInvited(user.email)
          }}
        />
      )}
    </div>
  )
}

function Row({ user }: { user: AdminUser }) {
  const off = user.status === 'deactivated'
  return (
    <tr className={off ? styles.rowOff : undefined}>
      <td>
        <Link to={`/admin/usuarios/${user.id}`} className={styles.person}>
          <span className={styles.avatar} aria-hidden="true">
            {initial(user)}
          </span>
          <span className={styles.personText}>
            <span className={styles.personName}>{user.name || user.username}</span>
            <span className={styles.personEmail}>{user.email}</span>
          </span>
        </Link>
      </td>
      <td>{roleLabel(user.role)}</td>
      <td>
        <StatusBadge user={user} />
      </td>
      <td className={styles.muted}>{lastAccess(user.lastLoginAt)}</td>
      <td>
        <RowActions user={user} />
      </td>
    </tr>
  )
}

export function StatusBadge({ user }: { user: AdminUser }) {
  const tone = user.status === 'active' ? styles.badgeOn : user.status === 'pending' ? styles.badgeWait : styles.badgeOff
  return (
    <span className={`${styles.badge} ${tone}`}>
      <span className={styles.badgeDot} aria-hidden="true" />
      {statusLabel(user)}
    </span>
  )
}
