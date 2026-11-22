import { useState } from 'react'
import { Link } from 'react-router'
import { errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { formatDay, statusInfo, useBoard, type ApplicationCard } from './api'
import styles from './funnel.module.css'

export function FunnelPage() {
  const board = useBoard()
  const [showClosed, setShowClosed] = useState(false)
  const data = board.data
  const total = data ? data.columns.reduce((sum, column) => sum + column.items.length, 0) : 0

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <h1 className={styles.title}>Funil</h1>
        <p className={styles.muted}>Mova uma vaga para cá pelo detalhe dela. Qualquer status pode ir para qualquer outro.</p>
      </header>

      {board.isError && <Alert>{errorMessage(board.error)}</Alert>}

      {data && data.overdue.length > 0 && (
        <section aria-labelledby="overdue-title" className={styles.overdue}>
          <h2 id="overdue-title" className={styles.sectionTitle}>
            Atrasadas · {data.overdue.length}
          </h2>
          <ul className={styles.cards}>
            {data.overdue.map((card) => (
              <Card key={card.id} card={card} showStatus />
            ))}
          </ul>
        </section>
      )}

      {data && total === 0 && data.closed.length === 0 && <p className={styles.empty}>Seu funil está vazio. Abra uma vaga e use "Mover para o funil".</p>}

      {data && total > 0 && (
        <div className={styles.board}>
          {data.columns.map((column) => {
            const info = statusInfo(column.status)
            return (
              <section key={column.status} className={styles.column} aria-labelledby={`column-${column.status}`}>
                <h2 id={`column-${column.status}`} className={styles.columnTitle}>
                  <span className={styles.dot} style={{ background: info.color }} />
                  {info.label}
                  <span className={styles.count}>{column.items.length}</span>
                </h2>
                <ul className={styles.cards}>
                  {column.items.map((card) => (
                    <Card key={card.id} card={card} />
                  ))}
                </ul>
              </section>
            )
          })}
        </div>
      )}

      {data && data.closed.length > 0 && (
        <section aria-labelledby="closed-title">
          <button type="button" className={styles.toggle} onClick={() => setShowClosed((value) => !value)} aria-expanded={showClosed}>
            <span id="closed-title">Encerradas · {data.closed.length}</span>
            <span aria-hidden="true">{showClosed ? '▴' : '▾'}</span>
          </button>
          {showClosed && (
            <ul className={styles.cards}>
              {data.closed.map((card) => (
                <Card key={card.id} card={card} showStatus />
              ))}
            </ul>
          )}
        </section>
      )}
    </div>
  )
}

function Card({ card, showStatus = false }: { card: ApplicationCard; showStatus?: boolean }) {
  const info = statusInfo(card.status)
  return (
    <li>
      <Link to={`/funil/${card.id}`} className={styles.card}>
        <span className={styles.cardTitle}>{card.title}</span>
        {card.company && <span className={styles.cardCompany}>{card.company}</span>}
        <span className={styles.cardMeta}>
          <span className={styles.priority} title="Prioridade">
            P{card.priority}
          </span>
          {showStatus && (
            <span className={styles.statusTag}>
              <span className={styles.dot} style={{ background: info.color }} />
              {info.label}
            </span>
          )}
          {card.jobClosedAtSource && <span className={styles.closedTag}>Encerrada na fonte</span>}
        </span>
        {(card.nextStep || card.nextStepOn) && (
          <span className={card.isOverdue ? `${styles.next} ${styles.nextOverdue}` : styles.next}>
            {card.nextStepOn && <strong>{formatDay(card.nextStepOn)}</strong>} {card.nextStep}
          </span>
        )}
      </Link>
    </li>
  )
}
