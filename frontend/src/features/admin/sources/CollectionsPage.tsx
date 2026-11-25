import { useState } from 'react'
import { Link } from 'react-router'
import { errorMessage } from '../../../lib/api'
import { Alert } from '../../../ui/Alert'
import { kindLabels, plural, useCollections, when, type CollectionBatch, type SourceKind } from './api'
import styles from './sources.module.css'

/** The latest collections, each with only the sources that changed something or failed. */
export function CollectionsPage() {
  const collections = useCollections(10)

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <div className={styles.heading}>
          <h1 className={styles.title}>Últimas leituras</h1>
          <p className={styles.lead}>O que cada fonte trouxe nas coletas recentes.</p>
        </div>
        <Link to="/admin/fontes" className={styles.backButton}>
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M15 6l-6 6 6 6" />
          </svg>
          Fontes
        </Link>
      </header>

      {collections.isError && <Alert>{errorMessage(collections.error)}</Alert>}
      {collections.data?.length === 0 && <p className={styles.lead}>Nenhuma coleta rodou ainda.</p>}
      {collections.data?.map((batch, index) => (
        <Batch key={batch.id} batch={batch} startOpen={index === 0} />
      ))}
    </div>
  )
}

function Summary({ batch }: { batch: CollectionBatch }) {
  return (
    <span className={styles.batchSummary}>
      <span className={batch.added > 0 ? styles.goodText : undefined}>+{plural(batch.added, 'nova', 'novas')}</span>
      {' · '}
      {plural(batch.reopened, 'reaberta', 'reabertas')} · {plural(batch.closed, 'encerrada', 'encerradas')}
      {batch.failed > 0 && (
        <>
          {' · '}
          <span className={styles.warnText}>{batch.failed} com erro</span>
        </>
      )}
    </span>
  )
}

function Batch({ batch, startOpen }: { batch: CollectionBatch; startOpen: boolean }) {
  const [open, setOpen] = useState(startOpen)
  const panelId = `batch-${batch.id}`

  return (
    <section className={styles.panel} aria-labelledby={`${panelId}-title`}>
      <h2 id={`${panelId}-title`} className={styles.batchHeading}>
        <button type="button" className={styles.batchToggle} aria-expanded={open} aria-controls={panelId} onClick={() => setOpen((value) => !value)}>
          <span className={styles.batchTitle}>
            {batch.partial ? 'Leitura de fonte nova' : 'Coleta'} de {when(batch.startedAt)}
          </span>
          <Summary batch={batch} />
          <svg className={open ? styles.chevronOpen : styles.chevron} width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M6 9l6 6 6-6" />
          </svg>
        </button>
      </h2>
      {open && (
        <div id={panelId}>
          {batch.changes.length > 0 && (
            <div className={styles.tableWrap}>
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th>Fonte</th>
                    <th className={styles.right}>Listadas</th>
                    <th className={styles.right}>Novas</th>
                    <th className={styles.right}>Reabertas</th>
                    <th className={styles.right}>Encerradas</th>
                  </tr>
                </thead>
                <tbody>
                  {batch.changes.map((run) => (
                    <tr key={run.id}>
                      <td>
                        <span className={styles.name}>
                          <span>
                            <span className={styles.nameMain}>{run.key}</span> <span className={styles.nameKey}>{kindLabels[run.source as SourceKind] ?? run.source}</span>
                          </span>
                          {run.error && <span className={styles.rowWarn}>{run.error} Nada mudou nesta fonte.</span>}
                          {!run.error && run.warning && <span className={styles.rowNote}>{run.warning}</span>}
                        </span>
                      </td>
                      {run.error ? (
                        <>
                          <td className={`${styles.number} ${styles.right} ${styles.muted}`}>-</td>
                          <td className={`${styles.number} ${styles.right} ${styles.muted}`}>-</td>
                          <td className={`${styles.number} ${styles.right} ${styles.muted}`}>-</td>
                          <td className={`${styles.number} ${styles.right} ${styles.muted}`}>-</td>
                        </>
                      ) : (
                        <>
                          <td className={`${styles.number} ${styles.right}`}>{run.listed}</td>
                          <td className={run.added > 0 ? `${styles.number} ${styles.right} ${styles.goodText}` : `${styles.number} ${styles.right} ${styles.muted}`}>{run.added}</td>
                          <td className={run.reopened > 0 ? `${styles.number} ${styles.right}` : `${styles.number} ${styles.right} ${styles.muted}`}>{run.reopened}</td>
                          <td className={run.closed > 0 ? `${styles.number} ${styles.right}` : `${styles.number} ${styles.right} ${styles.muted}`}>{run.closed}</td>
                        </>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          <div className={styles.panelFoot}>
            {batch.changes.length === 0
              ? `Nenhuma das ${plural(batch.targets, 'fonte', 'fontes')} teve novidade.`
              : `Só aparecem as fontes que mudaram algo ou deram erro. ${plural(batch.unchanged, 'leu', 'leram')} sem novidade.`}
          </div>
        </div>
      )}
    </section>
  )
}
