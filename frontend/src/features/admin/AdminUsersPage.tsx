import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import uiStyles from '../../ui/ui.module.css'
import { useMe } from '../auth/api'
import { useSensitive } from '../account/sensitive'
import styles from './admin.module.css'

interface AdminUser {
  id: string
  email: string
  username: string
  role: 'user' | 'admin'
  status: 'active' | 'pending' | 'deactivated'
  createdAt: string
  lastLoginAt: string | null
  accessLinkExpiresAt: string | null
}

const usersKey = ['admin', 'users'] as const

const statusLabel = (user: AdminUser): string => {
  if (user.status === 'deactivated') return 'Desativado'
  if (user.status === 'active') return 'Ativo'
  if (!user.accessLinkExpiresAt) return 'Link vencido'
  const hours = Math.max(1, Math.round((new Date(user.accessLinkExpiresAt).getTime() - Date.now()) / 3_600_000))
  return `Link enviado, vence em ${hours} h`
}

const date = (value: string | null) => (value ? new Date(value).toLocaleDateString('pt-BR') : 'nunca')

export function AdminUsersPage() {
  const users = useQuery({ queryKey: usersKey, queryFn: ({ signal }) => api.get<AdminUser[]>('/admin/users', signal) })

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <h1 className={styles.title}>Usuários</h1>
        <p className={styles.muted}>Você vê as contas de cada pessoa, nunca as vagas delas.</p>
      </header>
      <GrantAccessForm />
      <section aria-labelledby="people-title">
        <h2 id="people-title" className={styles.sectionTitle}>
          Pessoas
        </h2>
        {users.isError && <Alert>{errorMessage(users.error)}</Alert>}
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Pessoa</th>
                <th>Papel</th>
                <th>Situação</th>
                <th>Último acesso</th>
                <th>
                  <span className={styles.srOnly}>Ações</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {users.data?.map((user) => <UserRow key={user.id} user={user} />)}
            </tbody>
          </table>
        </div>
        <p className={styles.muted} style={{ marginTop: 14 }}>
          Desativar bloqueia o login e encerra as sessões na hora, sem apagar dados.
        </p>
      </section>
    </div>
  )
}

function GrantAccessForm() {
  const sensitive = useSensitive()
  const queryClient = useQueryClient()
  const [email, setEmail] = useState('')
  const [username, setUsername] = useState('')
  const [role, setRole] = useState<'user' | 'admin'>('user')

  const create = useMutation({
    mutationFn: () => sensitive(() => api.post('/admin/users', { email, username, role })),
    onSuccess: async () => {
      setEmail('')
      setUsername('')
      setRole('user')
      await queryClient.invalidateQueries({ queryKey: usersKey })
    },
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    create.mutate()
  }

  const fieldError = (name: string) => (create.error instanceof ApiError ? create.error.field(name) : undefined)
  const cancelled = create.error instanceof Error && create.error.message === 'cancelled'

  return (
    <section aria-labelledby="grant-title">
      <h2 id="grant-title" className={styles.sectionTitle}>
        Liberar acesso
      </h2>
      <form className={styles.grant} onSubmit={submit} noValidate>
        <div className={styles.grow2}>
          <Field label="E-mail" type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="pessoa@exemplo.com" error={fieldError('email')} />
        </div>
        <div className={styles.grow1}>
          <Field label="Usuário" value={username} onChange={(e) => setUsername(e.target.value.toLowerCase())} placeholder="joao" error={fieldError('username')} />
        </div>
        <div className={styles.roleField}>
          <label htmlFor="role" className={uiStyles.label}>
            Papel
          </label>
          <select id="role" className={uiStyles.input} value={role} onChange={(e) => setRole(e.target.value as 'user' | 'admin')}>
            <option value="user">Usuário</option>
            <option value="admin">Admin</option>
          </select>
        </div>
        <Button type="submit" size="large" disabled={create.isPending || !email || !username}>
          Enviar link de acesso
        </Button>
      </form>
      {create.isSuccess && <Notice>Link de acesso enviado. Ele vale por 72 horas.</Notice>}
      {create.isError && !cancelled && !fieldError('email') && !fieldError('username') && <Alert>{errorMessage(create.error)}</Alert>}
    </section>
  )
}

function UserRow({ user }: { user: AdminUser }) {
  const me = useMe().data
  const sensitive = useSensitive()
  const queryClient = useQueryClient()
  const done = () => queryClient.invalidateQueries({ queryKey: usersKey })

  const action = useMutation({
    mutationFn: (kind: 'resend' | 'deactivate' | 'reactivate' | 'role') => {
      switch (kind) {
        case 'resend':
          return api.post(`/admin/users/${user.id}/access-link`)
        case 'deactivate':
          return api.post(`/admin/users/${user.id}/deactivate`)
        case 'reactivate':
          return api.post(`/admin/users/${user.id}/reactivate`)
        case 'role':
          return sensitive(() => api.put(`/admin/users/${user.id}/role`, { role: user.role === 'admin' ? 'user' : 'admin' }))
      }
    },
    onSettled: done,
  })

  const self = me?.id === user.id
  const error = action.error instanceof ApiError ? action.error.problem.detail : null

  return (
    <tr>
      <td>
        <span className={styles.person}>
          <span className={styles.name}>{user.username}</span>
          <span className={styles.muted}>{user.email}</span>
        </span>
      </td>
      <td className={styles.dim}>{user.role === 'admin' ? 'Admin' : 'Usuário'}</td>
      <td className={user.status === 'pending' ? styles.warn : user.status === 'deactivated' ? styles.dim : undefined}>{statusLabel(user)}</td>
      <td className={styles.mono}>{date(user.lastLoginAt)}</td>
      <td className={styles.rowActions}>
        {!self && user.status === 'pending' && (
          <button type="button" className={uiStyles.link} onClick={() => action.mutate('resend')} disabled={action.isPending}>
            Reenviar link
          </button>
        )}
        {!self && user.status !== 'pending' && (
          <button type="button" className={uiStyles.link} onClick={() => action.mutate('role')} disabled={action.isPending}>
            {user.role === 'admin' ? 'Tornar usuário' : 'Tornar admin'}
          </button>
        )}
        {!self && user.status === 'active' && (
          <button type="button" className={uiStyles.link} onClick={() => action.mutate('deactivate')} disabled={action.isPending}>
            Desativar
          </button>
        )}
        {user.status === 'deactivated' && (
          <button type="button" className={uiStyles.link} onClick={() => action.mutate('reactivate')} disabled={action.isPending}>
            Reativar
          </button>
        )}
        {error && <span className={styles.error}>{error}</span>}
      </td>
    </tr>
  )
}
