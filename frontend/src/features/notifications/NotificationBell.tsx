import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useId, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { Link, useLocation } from 'react-router'
import { icons } from '../../app/navIcons'
import { api } from '../../lib/api'
import { isReading, readingKey } from './reading'
import styles from './NotificationBell.module.css'

const PANEL_WIDTH = 340
const EDGE = 24

interface Notification {
  /** Names what it tells; a read one comes back only when that changes. */
  id: string
  kind: 'model' | 'overdue' | 'sample' | 'highlights' | 'company' | 'collection' | 'source' | 'connector'
  title: string
  detail: string
  link: string
}

/**
 * The bell in the top bar. The API works the list out from the current state, so a
 * notification goes away once the person does the thing, or when they open it or mark
 * all as read (FR-NOT-08).
 */
export function NotificationBell() {
  const { pathname, search } = useLocation()
  const [open, setOpen] = useState(false)
  const panelId = useId()
  const wrap = useRef<HTMLDivElement>(null)
  const bell = useRef<HTMLButtonElement>(null)
  const [place, setPlace] = useState<CSSProperties>({})
  const queryClient = useQueryClient()
  const notifications = useQuery({
    queryKey: ['notifications'],
    queryFn: ({ signal }) => api.get<Notification[]>('/notifications', signal),
    // Every few seconds while a collection someone asked for may be running, every few minutes otherwise.
    refetchInterval: () => (isReading(queryClient) ? 5 * 1000 : 5 * 60 * 1000),
    staleTime: 30 * 1000,
  })
  const { refetch } = notifications

  // A collection ended (a new "collection" notice): the job lists and companies change, so they reload now.
  const seen = useRef<Set<string> | null>(null)
  useEffect(() => {
    const ids = (notifications.data ?? []).filter((item) => item.kind === 'collection').map((item) => item.id)
    if (seen.current === null) {
      seen.current = new Set(ids)
      return
    }

    const fresh = ids.filter((id) => !seen.current!.has(id))
    ids.forEach((id) => seen.current!.add(id))
    if (fresh.length > 0) {
      queryClient.setQueryData(readingKey, 0)
      void queryClient.invalidateQueries({ queryKey: ['jobs'] })
      void queryClient.invalidateQueries({ queryKey: ['companies'] })
    }
  }, [notifications.data, queryClient])

  // The item leaves the bell at once; the API remembers it was read.
  const markRead = useMutation({
    mutationFn: (ids: string[]) => api.post('/notifications/read', { ids }),
    onMutate: (ids) => {
      queryClient.setQueryData<Notification[]>(['notifications'], (current) => current?.filter((item) => !ids.includes(item.id)))
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  })

  // Moving around the app is when things get done, so check again.
  useEffect(() => {
    void refetch()
  }, [pathname, search, refetch])

  useEffect(() => {
    if (!open) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false)
    }
    const onPointer = (event: PointerEvent) => {
      if (!wrap.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('keydown', onKey)
    document.addEventListener('pointerdown', onPointer)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('pointerdown', onPointer)
    }
  }, [open])

  // A speech bubble: centered under the bell when it fits, kept clear of the window's edge, its tip on the bell.
  useLayoutEffect(() => {
    if (!open) return
    const measure = () => {
      const rect = bell.current?.getBoundingClientRect()
      if (!rect) return
      const edge = window.innerWidth < 480 ? 12 : EDGE
      const width = Math.min(PANEL_WIDTH, window.innerWidth - 2 * edge)
      const center = rect.left + rect.width / 2
      const left = Math.min(Math.max(center - width / 2, edge), window.innerWidth - width - edge)
      setPlace({ top: rect.bottom + 12, left, width, ['--tip' as string]: `${center - left}px` })
    }
    measure()
    window.addEventListener('resize', measure)
    return () => window.removeEventListener('resize', measure)
  }, [open])

  const items = notifications.data ?? []
  const label = items.length === 0 ? 'Notificações' : `Notificações: ${items.length}`

  return (
    <div ref={wrap} className={styles.wrap}>
      <button
        ref={bell}
        type="button"
        className={open ? `${styles.bell} ${styles.bellOpen}` : styles.bell}
        onClick={() => setOpen((value) => !value)}
        aria-label={label}
        aria-expanded={open}
        aria-controls={panelId}
        title="Notificações"
      >
        {icons.bell}
        {items.length > 0 && (
          <span className={styles.count} aria-hidden="true">
            {items.length > 9 ? '9+' : items.length}
          </span>
        )}
      </button>
      {open && (
        <div id={panelId} className={styles.panel} style={place} role="region" aria-label="Notificações">
          <div className={styles.headingRow}>
            <p className={styles.heading}>Notificações</p>
            {items.length > 0 && (
              <button type="button" className={styles.markAll} onClick={() => markRead.mutate(items.map((item) => item.id).slice(0, 50))} disabled={markRead.isPending}>
                Marcar todas como lidas
              </button>
            )}
          </div>
          {items.length === 0 ? (
            notifications.isError ? (
              <p className={styles.empty}>Não deu para carregar as notificações agora. Tente de novo em instantes.</p>
            ) : (
              <div className={styles.emptyState}>
                <p className={styles.emptyTitle}>Nenhuma notificação</p>
                <p className={styles.emptyText}>Aqui aparecem destaques novos, follow-ups atrasados e avisos das suas fontes e da IA.</p>
              </div>
            )
          ) : (
            <ul className={styles.list}>
              {items.map((item) => (
                <li key={item.id}>
                  <Link
                    to={item.link}
                    className={styles.item}
                    onClick={() => {
                      markRead.mutate([item.id])
                      setOpen(false)
                    }}
                  >
                    <span className={`${styles.dot} ${styles[item.kind]}`} aria-hidden="true" />
                    <span className={styles.text}>
                      <span className={styles.title}>{item.title}</span>
                      <span className={styles.detail}>{item.detail}</span>
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}
