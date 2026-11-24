import { useInfiniteQuery } from '@tanstack/react-query'
import { useEffect, useRef, type ReactNode } from 'react'
import { Link } from 'react-router'
import { api, errorMessage } from '../../../lib/api'
import { Alert } from '../../../ui/Alert'
import { dateTime } from './api'
import styles from './users.module.css'

/** Four at a time; the next four load when the list is scrolled to its end. */
const PAGE_SIZE = 4

interface Person {
  id: string
  name: string
}

interface Entry {
  id: string
  type: string
  occurredAt: string
  /** Who did it; null when the person acted on their own account. */
  actor: Person | null
  /** The account it happened to. */
  subject: Person | null
}

interface HistoryPage {
  items: Entry[]
  total: number
  offset: number
}

type Icon = 'enter' | 'warn' | 'check' | 'key' | 'lock' | 'mail' | 'edit' | 'shield' | 'user' | 'power'

/** Title of an event on the account (as the person did it, or as an admin did it) and its icon. */
const events: Record<string, { own: string; byAdmin: string; icon: Icon }> = {
  LoginSucceeded: { own: 'Entrou no app', byAdmin: 'Entrou no app', icon: 'enter' },
  LoginFailed: { own: 'Tentativa de entrar com a senha errada', byAdmin: 'Tentativa de entrar com a senha errada', icon: 'warn' },
  LockedOut: { own: 'Conta bloqueada por tentativas de entrar', byAdmin: 'Conta bloqueada por tentativas de entrar', icon: 'warn' },
  AccessGranted: { own: 'Criou a senha e liberou o acesso', byAdmin: 'Criou a senha e liberou o acesso', icon: 'check' },
  PasswordResetRequested: { own: 'Pediu para redefinir a senha', byAdmin: 'Pediu para redefinir a senha', icon: 'lock' },
  PasswordChanged: { own: 'Trocou a senha', byAdmin: 'Trocou a senha', icon: 'lock' },
  TwoFactorEnabled: { own: 'Ligou a verificação em duas etapas', byAdmin: 'Ligou a verificação em duas etapas', icon: 'lock' },
  TwoFactorDisabled: { own: 'Desligou a verificação em duas etapas', byAdmin: 'Desligou a verificação em duas etapas', icon: 'lock' },
  RecoveryCodesRegenerated: { own: 'Gerou novos códigos de recuperação', byAdmin: 'Gerou novos códigos de recuperação', icon: 'lock' },
  SessionsRevoked: { own: 'Encerrou as sessões nos outros aparelhos', byAdmin: 'Encerrou as sessões nos outros aparelhos', icon: 'lock' },
  AiKeyChanged: { own: 'Trocou a chave de IA', byAdmin: 'Trocou a chave de IA', icon: 'key' },
  EmailChangeRequested: { own: 'Pediu a troca de e-mail', byAdmin: 'Troca de e-mail pedida', icon: 'mail' },
  EmailChanged: { own: 'Confirmou o e-mail novo', byAdmin: 'Confirmou o e-mail novo', icon: 'mail' },
  AccessLinkSent: { own: 'Link de acesso enviado', byAdmin: 'Link de acesso enviado', icon: 'mail' },
  UserCreated: { own: 'Conta criada', byAdmin: 'Conta criada', icon: 'user' },
  UserEdited: { own: 'Dados editados', byAdmin: 'Dados editados', icon: 'edit' },
  RoleChanged: { own: 'Papel alterado', byAdmin: 'Papel alterado', icon: 'shield' },
  UserDeactivated: { own: 'Acesso desativado', byAdmin: 'Acesso desativado', icon: 'power' },
  UserReactivated: { own: 'Acesso reativado', byAdmin: 'Acesso reativado', icon: 'power' },
}

/** What the person did to someone else's account as an admin. */
const didTo: Record<string, string> = {
  UserCreated: 'Criou a conta de',
  AccessLinkSent: 'Enviou o link de acesso para',
  UserEdited: 'Editou os dados de',
  EmailChangeRequested: 'Pediu a troca de e-mail de',
  RoleChanged: 'Mudou o papel de',
  UserDeactivated: 'Desativou o acesso de',
  UserReactivated: 'Reativou o acesso de',
}

const paths: Record<Icon, ReactNode> = {
  enter: <path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4M10 17l5-5-5-5M15 12H3" />,
  warn: <path d="M12 9v4M12 17h.01M10.3 3.9 2.4 18a2 2 0 0 0 1.7 3h15.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" />,
  check: <path d="M20 6 9 17l-5-5" />,
  key: (
    <>
      <circle cx="8" cy="15" r="4" />
      <path d="M10.8 12.2 20 3M17 6l3 3" />
    </>
  ),
  lock: (
    <>
      <rect x="5" y="11" width="14" height="10" rx="2" />
      <path d="M8 11V7a4 4 0 0 1 8 0v4" />
    </>
  ),
  mail: <path d="M4 5h16v14H4zM4 7l8 6 8-6" />,
  edit: <path d="M4 20h4L19 9l-4-4L4 16z" />,
  shield: <path d="M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z" />,
  user: (
    <>
      <circle cx="12" cy="8" r="4" />
      <path d="M4 21a8 8 0 0 1 16 0" />
    </>
  ),
  power: <path d="M12 3v9M6.3 6.3a8 8 0 1 0 11.4 0" />,
}

function PersonLink({ person }: { person: Person }) {
  return (
    <Link to={`/admin/usuarios/${person.id}`} className={styles.historyLink}>
      {person.name}
    </Link>
  )
}

function Item({ entry, userId }: { entry: Entry; userId: string }) {
  const known = events[entry.type]
  const icon: Icon = known?.icon ?? 'shield'
  const onOther = entry.subject !== null && entry.subject.id !== userId
  const tone = icon === 'warn' ? styles.eventIconWarn : icon === 'check' ? styles.eventIconGood : ''

  let title: ReactNode = entry.actor ? (known?.byAdmin ?? 'Evento de segurança') : (known?.own ?? 'Evento de segurança')
  if (onOther && entry.subject) {
    title = (
      <>
        {didTo[entry.type] ?? 'Mexeu na conta de'} <PersonLink person={entry.subject} />
      </>
    )
  }

  return (
    <li className={styles.event}>
      <span className={`${styles.eventIcon} ${tone}`} aria-hidden="true">
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          {paths[icon]}
        </svg>
      </span>
      <div className={styles.eventBody}>
        <span className={styles.eventTitle}>{title}</span>
        <span className={styles.eventMeta}>
          <time dateTime={entry.occurredAt}>{dateTime(entry.occurredAt)}</time>
          {!onOther && entry.actor && (
            <>
              {' · por '}
              <PersonLink person={entry.actor} />
            </>
          )}
        </span>
      </div>
    </li>
  )
}

/** The account's history from the security log: the last 90 days, newest first, no IPs. */
export function History({ userId }: { userId: string }) {
  const history = useInfiniteQuery({
    queryKey: ['admin', 'users', userId, 'history'],
    initialPageParam: 0,
    queryFn: ({ pageParam, signal }) => api.get<HistoryPage>(`/admin/users/${userId}/history?limit=${PAGE_SIZE}&offset=${pageParam}`, signal),
    getNextPageParam: (last) => (last.offset + last.items.length < last.total ? last.offset + last.items.length : undefined),
  })
  const box = useRef<HTMLOListElement>(null)
  const end = useRef<HTMLLIElement>(null)
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = history

  useEffect(() => {
    if (!box.current || !end.current || !hasNextPage) return
    const observer = new IntersectionObserver(
      ([seen]) => {
        if (seen?.isIntersecting && !isFetchingNextPage) void fetchNextPage()
      },
      { root: box.current, rootMargin: '0px 0px 40px 0px' },
    )
    observer.observe(end.current)
    return () => observer.disconnect()
  }, [hasNextPage, isFetchingNextPage, fetchNextPage])

  const entries = history.data?.pages.flatMap((page) => page.items) ?? []
  const total = history.data?.pages[0]?.total ?? 0

  return (
    <section aria-labelledby="history-title" className={styles.panel}>
      <div className={styles.panelHeadRow}>
        <h2 id="history-title" className={styles.panelTitle}>
          Histórico
        </h2>
        {history.data && (
          <span className={styles.panelHint}>
            {total} {total === 1 ? 'evento' : 'eventos'} nos últimos 90 dias
          </span>
        )}
      </div>
      {history.isError && (
        <div className={styles.panelBody}>
          <Alert>{errorMessage(history.error)}</Alert>
        </div>
      )}
      {history.data && entries.length === 0 && <p className={styles.historyEmpty}>Nada nos últimos 90 dias.</p>}
      {entries.length > 0 && (
        <ol ref={box} className={styles.events} tabIndex={0} aria-label="Eventos do histórico">
          {entries.map((entry) => (
            <Item key={entry.id} entry={entry} userId={userId} />
          ))}
          <li ref={end} className={styles.eventsEnd}>
            {isFetchingNextPage ? 'Carregando mais…' : !hasNextPage && entries.length > PAGE_SIZE ? 'Fim do histórico' : ''}
          </li>
        </ol>
      )}
    </section>
  )
}
