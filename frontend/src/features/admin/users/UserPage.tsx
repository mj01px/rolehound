import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { errorMessage } from '../../../lib/api'
import { Alert, Notice } from '../../../ui/Alert'
import { Button } from '../../../ui/Button'
import { dateTime, initial, longDate, roleLabel, useUser, type AdminUser } from './api'
import { History } from './History'
import { useActionsFor } from './useActionsFor'
import { EditUserDialog } from './UserDialog'
import { StatusBadge } from './UsersPage'
import styles from './users.module.css'

const twoFactorLabel: Record<AdminUser['twoFactorMethod'], string> = {
  none: 'Desligada',
  totp: 'App autenticador',
  email: 'Código por e-mail',
}

/** One account: data, access, and its history underneath. */
export function UserPage() {
  const { id = '' } = useParams()
  const user = useUser(id)

  return (
    <div className={styles.page}>
      {user.isError && (
        <>
          <BackLink />
          <Alert>{errorMessage(user.error)}</Alert>
        </>
      )}
      {user.data && <Detail user={user.data} />}
    </div>
  )
}

function BackLink() {
  return (
    <Link to="/admin/usuarios" className={styles.backButton}>
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        <path d="M15 6l-6 6 6 6" />
      </svg>
      Voltar
    </Link>
  )
}

function Detail({ user }: { user: AdminUser }) {
  const actions = useActionsFor(user)
  const [editing, setEditing] = useState(false)
  const manage = actions.available.role || actions.available.reactivate || actions.available.deactivate

  return (
    <>
      <header className={styles.detailHead}>
        <div className={styles.heading}>
          <h1 className={styles.title}>{user.name || user.username}</h1>
          <p className={styles.lead}>
            {roleLabel(user.role)} · cadastrado em {longDate(user.createdAt)}
          </p>
        </div>
        <div className={styles.headActions}>
          <BackLink />
          {actions.available.resend && (
            <Button variant="secondary" onClick={() => actions.run('resend')} disabled={actions.pending}>
              Enviar link de acesso
            </Button>
          )}
          <Button onClick={() => setEditing(true)}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M4 20h4L19 9l-4-4L4 16z" />
            </svg>
            Editar
          </Button>
        </div>
      </header>

      {actions.sent && <Notice>Link de acesso enviado. Ele vale por 72 horas.</Notice>}
      {user.pendingEmail && (
        <Notice>
          Troca de e-mail esperando confirmação: o link foi para <strong>{user.pendingEmail}</strong>. Até a pessoa confirmar, ela entra com o e-mail atual.
        </Notice>
      )}
      {actions.error && <Alert>{actions.error}</Alert>}

      <div className={styles.columns}>
        <section aria-labelledby="data-title" className={styles.panel}>
          <div className={styles.panelHeadRow}>
            <h2 id="data-title" className={styles.panelTitle}>
              Dados
            </h2>
          </div>
          <dl className={styles.facts}>
            <Fact label="Nome">{user.name || 'Sem nome'}</Fact>
            <Fact label="Usuário">
              <span className={styles.monoValue}>{user.username}</span>
            </Fact>
            <Fact label="E-mail">{user.email}</Fact>
            <Fact label="Papel">{roleLabel(user.role)}</Fact>
            <Fact label="Configuração inicial">{user.onboardingCompleted ? 'Concluída' : 'Não concluída'}</Fact>
          </dl>
        </section>

        <section aria-labelledby="access-title" className={styles.panel}>
          <div className={styles.accessHead}>
            <span className={styles.squareAvatar} aria-hidden="true">
              {initial(user)}
            </span>
            <div className={styles.accessHeading}>
              <h2 id="access-title" className={styles.panelTitle}>
                Acesso
              </h2>
              <span className={styles.panelHint}>Entra como {user.username}</span>
            </div>
          </div>
          <dl className={styles.facts}>
            <Fact label="Situação">
              <StatusBadge user={user} />
            </Fact>
            <Fact label="Último acesso">{user.lastLoginAt ? dateTime(user.lastLoginAt) : 'Nunca entrou'}</Fact>
            {user.status === 'pending' && <Fact label="Convite vence">{user.accessLinkExpiresAt ? dateTime(user.accessLinkExpiresAt) : 'Já venceu'}</Fact>}
            <Fact label="Duas etapas">{twoFactorLabel[user.twoFactorMethod]}</Fact>
          </dl>
          {manage && (
            <div className={styles.panelFoot}>
              {actions.available.role && (
                <Button variant="secondary" onClick={() => actions.run('role')} disabled={actions.pending}>
                  {user.role === 'admin' ? 'Tornar usuário' : 'Tornar admin'}
                </Button>
              )}
              {actions.available.reactivate && (
                <Button variant="secondary" onClick={() => actions.run('reactivate')} disabled={actions.pending}>
                  Reativar acesso
                </Button>
              )}
              {actions.available.deactivate && (
                <Button variant="secondary" className={styles.dangerText} onClick={actions.askDeactivate} disabled={actions.pending}>
                  Desativar acesso
                </Button>
              )}
            </div>
          )}
        </section>
      </div>

      <History userId={user.id} />

      {editing && <EditUserDialog user={user} onClose={() => setEditing(false)} />}
      {actions.dialog}
    </>
  )
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className={styles.fact}>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  )
}
