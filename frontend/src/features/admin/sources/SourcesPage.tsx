import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { ApiError, errorMessage } from '../../../lib/api'
import { Alert, Notice } from '../../../ui/Alert'
import { Button } from '../../../ui/Button'
import { Modal, ModalHead } from '../../../ui/Modal'
import { MoreMenu } from '../../../ui/MoreMenu'
import { kindLabels, kinds, plural, useCollections, useConnectorRequests, useRemoveSource, useRunNow, useSourcesStatus, useTargets, useToggleSource, when, type SourceKind, type SourceTarget } from './api'
import { AddSourceDialog } from './AddSourceDialog'
import styles from './sources.module.css'

const nameColumn: Record<SourceKind, string> = { inhire: 'Empresa', gupy: 'Termo', github: 'Repositório', greenhouse: 'Empresa', lever: 'Empresa' }
const searchHint: Record<SourceKind, string> = {
  inhire: 'Buscar empresa',
  gupy: 'Buscar termo',
  github: 'Buscar repositório',
  greenhouse: 'Buscar empresa',
  lever: 'Buscar empresa',
}

const normalize = (text: string) => text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()
const clock = (value: string) => new Date(value).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })

/** Admin › Fontes: where collection reads jobs, one tab per kind of source. */
export function SourcesPage() {
  const targets = useTargets()
  const collections = useCollections(1)
  const status = useSourcesStatus()
  const run = useRunNow()
  const [kind, setKind] = useState<SourceKind>('inhire')
  const [query, setQuery] = useState('')
  const [adding, setAdding] = useState(false)
  const [added, setAdded] = useState<string | null>(null)

  const all = useMemo(() => targets.data ?? [], [targets.data])
  const counts = Object.fromEntries(kinds.map((value) => [value, all.filter((target) => target.source === value).length])) as Record<SourceKind, number>
  const openJobs = all.reduce((sum, target) => sum + target.activeJobs, 0)
  const last = collections.data?.[0]
  const waitUntil = status.data?.manualAvailableAt ?? null

  const shown = useMemo(() => {
    const words = normalize(query.trim())
    return all.filter((target) => target.source === kind && (!words || normalize(`${target.label ?? ''} ${target.key}`).includes(words)))
  }, [all, kind, query])

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <div className={styles.heading}>
          <h1 className={styles.title}>Fontes</h1>
          <p className={styles.lead}>
            De onde a coleta lê as vagas.
            {targets.data && ` ${plural(all.length, 'fonte', 'fontes')}, ${plural(openJobs, 'vaga aberta', 'vagas abertas')}.`}
          </p>
        </div>
        <div className={styles.headActions}>
          <Button variant="secondary" onClick={() => setAdding(true)}>
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
              <path d="M12 5v14M5 12h14" />
            </svg>
            Adicionar fonte
          </Button>
          <Button onClick={() => run.mutate()} disabled={run.isPending || waitUntil !== null} title={waitUntil ? `A coleta manual libera às ${clock(waitUntil)}` : undefined}>
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M21 12a9 9 0 1 1-3-6.7L21 8M21 3v5h-5" />
            </svg>
            {run.isPending ? 'Coletando…' : waitUntil ? `Coletar às ${clock(waitUntil)}` : 'Coletar agora'}
          </Button>
        </div>
      </header>

      {run.isPending && <Notice>Coletando todas as fontes. Pode levar alguns minutos.</Notice>}
      {run.data && (
        <Notice>
          Coleta feita: {plural(run.data.added, 'vaga nova', 'vagas novas')}, {run.data.reopened} reabertas, {run.data.closed} encerradas
          {run.data.failed > 0 ? `, ${plural(run.data.failed, 'fonte com erro', 'fontes com erro')}` : ''}.
        </Notice>
      )}
      {run.isError && <Alert>{errorMessage(run.error)}</Alert>}
      {added && <Notice>{added} adicionada. Entra na próxima coleta.</Notice>}
      {targets.isError && <Alert>{errorMessage(targets.error)}</Alert>}

      <section aria-label="Coleta" className={styles.strip}>
        <div className={styles.stripCell}>
          <span className={styles.stripLabel}>Última coleta</span>
          <span className={styles.stripValue}>{last ? when(last.startedAt) : 'Nenhuma ainda'}</span>
          {last && (
            <span className={styles.stripHint}>
              {plural(last.added, 'vaga nova', 'vagas novas')}, {plural(last.closed, 'encerrada', 'encerradas')}
            </span>
          )}
        </div>
        <div className={styles.stripCell}>
          <span className={styles.stripLabel}>Próxima automática</span>
          <span className={styles.stripValue}>{status.data ? when(status.data.nextScheduledAt) : '…'}</span>
          <span className={styles.stripHint}>Roda às 9h, 12h, 15h e 18h</span>
        </div>
        <div className={styles.stripCell}>
          <span className={styles.stripLabel}>Com erro na última coleta</span>
          <span className={last && last.failed > 0 ? `${styles.stripValue} ${styles.warnText}` : styles.stripValue}>
            {last ? (last.failed > 0 ? plural(last.failed, 'fonte', 'fontes') : 'Nenhuma') : '…'}
          </span>
          <Link to="/admin/fontes/leituras" className={styles.stripLink}>
            Ver últimas leituras
          </Link>
        </div>
      </section>

      <div className={styles.tabsRow}>
        <div role="tablist" aria-label="Tipo de fonte" className={styles.tabs}>
          {kinds.map((value) => (
            <button
              key={value}
              type="button"
              role="tab"
              id={`tab-${value}`}
              aria-selected={kind === value}
              aria-controls="sources-panel"
              className={kind === value ? `${styles.tab} ${styles.tabOn}` : styles.tab}
              onClick={() => {
                setKind(value)
                setQuery('')
              }}
            >
              {kindLabels[value]}
              <span className={styles.tabCount}>{counts[value]}</span>
            </button>
          ))}
        </div>
        <label className={styles.search}>
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
            <circle cx="11" cy="11" r="7" />
            <path d="m20 20-3.5-3.5" />
          </svg>
          <input type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder={searchHint[kind]} aria-label={searchHint[kind]} />
        </label>
      </div>

      <div role="tabpanel" id="sources-panel" aria-labelledby={`tab-${kind}`} className={styles.panel}>
        {kind === 'gupy' && (
          <p className={styles.panelNote}>Os termos vêm dos perfis de score de quem usa o app. Um termo que ninguém usa mais fica pausado e as vagas dele expiram com o tempo.</p>
        )}
        {targets.data && counts[kind] === 0 ? (
          <div className={styles.empty}>
            <p>Nenhuma fonte do {kindLabels[kind]} ainda.</p>
            {kind !== 'gupy' && (
              <Button variant="secondary" onClick={() => setAdding(true)}>
                Adicionar fonte
              </Button>
            )}
          </div>
        ) : (
          <div className={styles.tableWrap}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th>{nameColumn[kind]}</th>
                  <th>Vagas abertas</th>
                  <th>Última leitura</th>
                  <th>Coleta</th>
                  <th>
                    <span className={styles.srOnly}>Ações</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {shown.map((target) => (
                  <Row key={target.id} target={target} />
                ))}
              </tbody>
            </table>
            {targets.data && shown.length === 0 && <p className={styles.emptyRow}>Nada com essa busca.</p>}
          </div>
        )}
        {targets.data && counts[kind] > 0 && (
          <div className={styles.panelFoot}>
            {query.trim() ? `${shown.length} de ${counts[kind]}` : plural(counts[kind], kind === 'gupy' ? 'termo' : 'fonte', kind === 'gupy' ? 'termos' : 'fontes')}
          </div>
        )}
      </div>

      <ConnectorRequests />

      {adding && (
        <AddSourceDialog
          initial={kind}
          onClose={() => setAdding(false)}
          onAdded={(target) => {
            setAdding(false)
            setAdded(target.label ?? target.key)
            setKind(target.source as SourceKind)
          }}
        />
      )}
    </div>
  )
}

function Row({ target }: { target: SourceTarget }) {
  const toggle = useToggleSource(target)
  const remove = useRemoveSource(target)
  const [confirming, setConfirming] = useState(false)
  const managed = target.source === 'gupy'
  const name = target.label ?? target.key
  const failure = toggle.error ?? remove.error
  const failureText = failure instanceof ApiError ? failure.problem.detail : failure ? errorMessage(failure) : null

  return (
    <tr className={target.enabled ? undefined : styles.rowOff}>
      <td>
        <span className={styles.name}>
          <span className={styles.nameMain}>{name}</span>
          <span className={styles.nameKey}>
            {[target.label ? target.key : null, !target.enabled ? (managed ? 'ninguém usa agora' : 'pausada') : null, target.addedBy ? `adicionada por ${target.addedBy}` : null]
              .filter(Boolean)
              .join(' · ')}
          </span>
          {target.lastError && (
            <span className={styles.rowWarn}>
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M12 9v4M12 17h.01M10.3 3.9 2.4 18a2 2 0 0 0 1.7 3h15.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" />
              </svg>
              {target.lastError}
            </span>
          )}
          {failureText && <span className={styles.rowError}>{failureText}</span>}
        </span>
      </td>
      <td className={styles.number}>{target.activeJobs}</td>
      <td className={styles.muted}>{when(target.lastRunAt)}</td>
      <td>
        {managed ? (
          <span className={styles.muted}>{target.enabled ? 'Automática' : 'Pausada'}</span>
        ) : (
          <button
            type="button"
            role="switch"
            aria-checked={target.enabled}
            aria-label={`Coletar ${name}`}
            className={target.enabled ? `${styles.switch} ${styles.switchOn}` : styles.switch}
            onClick={() => toggle.mutate()}
            disabled={toggle.isPending}
          >
            <span className={styles.switchKnob} aria-hidden="true" />
          </button>
        )}
      </td>
      <td className={styles.actions}>
        {!managed && <MoreMenu label={`Mais ações para ${name}`} items={[{ key: 'remove', label: 'Remover fonte', danger: true, run: () => setConfirming(true) }]} />}
        {confirming && (
          <Modal titleId={`remove-${target.id}`} onClose={() => setConfirming(false)}>
            <div className={styles.dialogForm}>
              <ModalHead
                id={`remove-${target.id}`}
                title={`Remover ${name}?`}
                text={`As ${plural(target.activeJobs, 'vaga aberta', 'vagas abertas')} dela são encerradas agora. Para só parar de coletar, use o interruptor.`}
                onClose={() => setConfirming(false)}
              />
              <div className={styles.dialogActions}>
                <Button variant="secondary" onClick={() => setConfirming(false)}>
                  Cancelar
                </Button>
                <Button variant="danger" onClick={() => remove.mutate(undefined, { onSuccess: () => setConfirming(false) })} disabled={remove.isPending}>
                  Remover fonte
                </Button>
              </div>
            </div>
          </Modal>
        )}
      </td>
    </tr>
  )
}

/** Platforms people asked for when following companies Rolehound cannot read yet. */
function ConnectorRequests() {
  const requests = useConnectorRequests()
  if (!requests.data || requests.data.length === 0) return null
  return (
    <section aria-labelledby="requests-title" className={styles.panel}>
      <h2 id="requests-title" className={styles.requestsTitle}>
        Pedidos de conector
      </h2>
      <p className={styles.panelNote}>Quando alguém tenta seguir uma empresa num site que o Rolehound ainda não lê, o pedido fica aqui. Ajuda a decidir o próximo conector.</p>
      <ul className={styles.requests}>
        {requests.data.map((item) => (
          <li key={item.platform} className={styles.request}>
            <span className={styles.nameMain}>{item.platform}</span>
            <span className={styles.muted}>
              {plural(item.requests, 'pedido', 'pedidos')} de {plural(item.people, 'pessoa', 'pessoas')} · último {when(item.lastAt)}
            </span>
          </li>
        ))}
      </ul>
    </section>
  )
}
