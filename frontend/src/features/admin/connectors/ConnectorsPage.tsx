import { useState, type FormEvent } from 'react'
import { errorMessage } from '../../../lib/api'
import { Alert, Notice } from '../../../ui/Alert'
import { Button } from '../../../ui/Button'
import { Field } from '../../../ui/Field'
import { Modal, ModalHead } from '../../../ui/Modal'
import { MoreMenu } from '../../../ui/MoreMenu'
import overlay from '../../../ui/overlay.module.css'
import sources from '../sources/sources.module.css'
import { plural, when } from '../sources/api'
import { useConnectorRequests, useResolve, type ConnectorPlatform, type RequestStatus } from './api'
import styles from './connectors.module.css'

const tabs: { value: RequestStatus; label: string; empty: string }[] = [
  { value: 'open', label: 'Abertos', empty: 'Nenhum pedido aberto. Quando alguém tentar seguir uma empresa num site que o Rolehound ainda não lê, o pedido aparece aqui.' },
  { value: 'done', label: 'Atendidos', empty: 'Nenhum pedido atendido ainda.' },
  { value: 'declined', label: 'Recusados', empty: 'Nenhum pedido recusado.' },
]

/** Admin › Conectores: the platforms people asked for, to pick the next connector and close requests. */
export function ConnectorsPage() {
  const requests = useConnectorRequests()
  const [tab, setTab] = useState<RequestStatus>('open')
  const [message, setMessage] = useState<string | null>(null)
  const all = requests.data ?? []
  const shown = all.filter((item) => item.status === tab)
  const current = tabs.find((item) => item.value === tab)!

  return (
    <div className={sources.page}>
      <header className={sources.header}>
        <div className={sources.heading}>
          <h1 className={sources.title}>Conectores</h1>
          <p className={sources.lead}>Sites que as pessoas tentaram seguir e o Rolehound ainda não lê. Os mais pedidos ficam em cima: ajudam a escolher o próximo conector.</p>
        </div>
      </header>

      {requests.isError && <Alert>{errorMessage(requests.error)}</Alert>}
      {message && <Notice>{message}</Notice>}

      <div className={sources.tabsRow}>
        <div role="tablist" aria-label="Situação dos pedidos" className={sources.tabs}>
          {tabs.map((item) => (
            <button
              key={item.value}
              type="button"
              role="tab"
              id={`tab-${item.value}`}
              aria-selected={tab === item.value}
              aria-controls="requests-panel"
              className={tab === item.value ? `${sources.tab} ${sources.tabOn}` : sources.tab}
              onClick={() => {
                setTab(item.value)
                setMessage(null)
              }}
            >
              {item.label}
              <span className={sources.tabCount}>{all.filter((row) => row.status === item.value).length}</span>
            </button>
          ))}
        </div>
      </div>

      <div role="tabpanel" id="requests-panel" aria-labelledby={`tab-${tab}`} className={sources.panel}>
        {requests.data && shown.length === 0 ? (
          <p className={styles.empty}>{current.empty}</p>
        ) : (
          <ul className={styles.list}>
            {shown.map((item) => (
              <PlatformRow key={`${item.platform}-${item.status}`} item={item} onResolved={setMessage} />
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}

function PlatformRow({ item, onResolved }: { item: ConnectorPlatform; onResolved: (message: string) => void }) {
  const resolve = useResolve(item.platform)
  const [declining, setDeclining] = useState(false)

  const markDone = () =>
    resolve.mutate(
      { status: 'done' },
      {
        onSuccess: (result) =>
          onResolved(
            `${item.platform} atendido. ${result.notified === 0 ? 'Ninguém para avisar.' : `${plural(result.notified, 'pessoa avisada', 'pessoas avisadas')} no sino e no Telegram.`}`,
          ),
      },
    )

  const meta =
    item.status === 'open'
      ? `${plural(item.requests, 'pedido', 'pedidos')} de ${plural(item.people, 'pessoa', 'pessoas')} · último ${when(item.lastAt)}`
      : `${plural(item.requests, 'pedido', 'pedidos')} · ${item.status === 'done' ? 'atendido' : 'recusado'} ${when(item.resolvedAt)}`

  return (
    <li className={styles.row}>
      <div className={styles.text}>
        <span className={styles.name}>{item.platform}</span>
        <span className={styles.meta}>{meta}</span>
        {item.hosts.length > 0 && (
          <span className={styles.hosts}>
            {item.hosts.map((host) => (
              <span key={host} className={styles.host}>
                {host}
              </span>
            ))}
          </span>
        )}
        {item.reason && <span className={styles.meta}>Motivo: {item.reason}</span>}
        {resolve.isError && !declining && <span className={styles.error}>{errorMessage(resolve.error)}</span>}
      </div>
      {item.status === 'open' && (
        <div className={styles.actions}>
          <Button variant="secondary" onClick={markDone} disabled={resolve.isPending}>
            Marcar como atendido
          </Button>
          <MoreMenu label={`Mais ações para ${item.platform}`} items={[{ key: 'decline', label: 'Recusar pedidos', danger: true, run: () => setDeclining(true) }]} />
        </div>
      )}
      {declining && (
        <DeclineDialog
          item={item}
          pending={resolve.isPending}
          error={resolve.isError ? errorMessage(resolve.error) : null}
          onClose={() => setDeclining(false)}
          onDecline={(reason) =>
            resolve.mutate(
              { status: 'declined', reason },
              {
                onSuccess: () => {
                  setDeclining(false)
                  onResolved(`Pedidos do ${item.platform} recusados.`)
                },
              },
            )
          }
        />
      )}
    </li>
  )
}

function DeclineDialog({
  item,
  pending,
  error,
  onClose,
  onDecline,
}: {
  item: ConnectorPlatform
  pending: boolean
  error: string | null
  onClose: () => void
  onDecline: (reason: string) => void
}) {
  const [reason, setReason] = useState('')
  const submit = (event: FormEvent) => {
    event.preventDefault()
    onDecline(reason.trim())
  }

  return (
    <Modal titleId="decline-title" onClose={onClose}>
      <form className={overlay.form} onSubmit={submit} noValidate>
        <ModalHead
          id="decline-title"
          title={`Recusar pedidos do ${item.platform}`}
          text={`${plural(item.requests, 'pedido', 'pedidos')} saem dos abertos. Ninguém é avisado. Se alguém pedir de novo, um pedido novo abre.`}
          onClose={onClose}
        />
        <Field
          label="Motivo (opcional, só você vê)"
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          placeholder="Ex.: portal fechado, poucas empresas no Brasil"
          maxLength={200}
          autoFocus
        />
        {error && <Alert>{error}</Alert>}
        <div className={overlay.actions}>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" variant="danger" disabled={pending}>
            Recusar pedidos
          </Button>
        </div>
      </form>
    </Modal>
  )
}
