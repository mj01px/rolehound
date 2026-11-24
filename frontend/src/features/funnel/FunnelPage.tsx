import { useState, type CSSProperties, type DragEvent } from 'react'
import { Link } from 'react-router'
import { errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { formatDay, statusInfo, todayInBrazil, useBoard, useMoveApplication, type ApplicationCard, type ApplicationStatus } from './api'
import styles from './funnel.module.css'

export function FunnelPage() {
  const board = useBoard()
  const move = useMoveApplication()
  const [showClosed, setShowClosed] = useState(false)
  // The card being dragged and the column under it.
  const [dragging, setDragging] = useState<ApplicationCard | null>(null)
  const [over, setOver] = useState<ApplicationStatus | null>(null)

  const dropZone = (status: ApplicationStatus) => ({
    onDragOver: (event: DragEvent) => {
      if (!dragging || dragging.status === status) return
      event.preventDefault()
      event.dataTransfer.dropEffect = 'move'
      if (over !== status) setOver(status)
    },
    onDragLeave: (event: DragEvent) => {
      if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setOver(null)
    },
    onDrop: (event: DragEvent) => {
      event.preventDefault()
      if (dragging && dragging.status !== status) move.mutate({ id: dragging.id, status })
      setDragging(null)
      setOver(null)
    },
  })
  const dragProps = (card: ApplicationCard) => ({
    dragging: dragging?.id === card.id,
    onDragStart: () => setDragging(card),
    onDragEnd: () => {
      setDragging(null)
      setOver(null)
    },
  })
  const data = board.data
  const total = data ? data.columns.reduce((sum, column) => sum + column.items.length, 0) : 0

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <h1 className={styles.title}>Funil</h1>
        <p className={styles.muted}>Arraste um card para mudar a etapa. Vagas entram aqui pelo detalhe delas.</p>
      </header>

      {board.isError && <Alert>{errorMessage(board.error)}</Alert>}
      {move.isError && <Alert>{errorMessage(move.error)}</Alert>}

      {data && data.overdue.length > 0 && (
        <section aria-labelledby="overdue-title" className={styles.overdue}>
          <h2 id="overdue-title" className={styles.sectionTitle}>
            Atrasadas · {data.overdue.length}
          </h2>
          <ul className={styles.cards}>
            {data.overdue.map((card) => (
              <Card key={card.id} card={card} showStatus {...dragProps(card)} />
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
              <section
                key={column.status}
                className={over === column.status ? `${styles.column} ${styles.columnOver}` : styles.column}
                style={{ '--stage': info.color } as CSSProperties}
                aria-labelledby={`column-${column.status}`}
                {...dropZone(column.status)}
              >
                <h2 id={`column-${column.status}`} className={styles.columnTitle}>
                  <span className={styles.columnBar} style={{ background: info.color }} aria-hidden="true" />
                  <span className={column.items.length ? styles.columnLabel : `${styles.columnLabel} ${styles.columnEmpty}`}>
                    {info.label}
                    <span className={styles.count}>{column.items.length}</span>
                  </span>
                </h2>
                <ul className={styles.cards}>
                  {column.items.map((card) => (
                    <Card key={card.id} card={card} {...dragProps(card)} />
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
                <Card key={card.id} card={card} showStatus {...dragProps(card)} />
              ))}
            </ul>
          )}
        </section>
      )}
    </div>
  )
}

/** "28/11" for this year, "28/11/2025" otherwise. */
const shortDay = (value: string) => (value.slice(0, 4) === todayInBrazil().slice(0, 4) ? formatDay(value).slice(0, 5) : formatDay(value))

interface CardProps {
  card: ApplicationCard
  showStatus?: boolean
  dragging: boolean
  onDragStart: () => void
  onDragEnd: () => void
}

function Card({ card, showStatus = false, dragging, onDragStart, onDragEnd }: CardProps) {
  const info = statusInfo(card.status)
  return (
    <li>
      <Link
        to={`/funil/${card.id}`}
        className={dragging ? `${styles.card} ${styles.cardDragging}` : styles.card}
        draggable
        onDragStart={(event) => {
          event.dataTransfer.effectAllowed = 'move'
          event.dataTransfer.setData('text/plain', card.id)
          onDragStart()
        }}
        onDragEnd={onDragEnd}
      >
        <span className={styles.cardTitle}>{card.title}</span>
        {card.company && <span className={styles.cardCompany}>{card.company}</span>}
        {(card.nextStep || card.nextStepOn) && (
          <span className={card.isOverdue ? `${styles.next} ${styles.nextOverdue}` : styles.next}>
            {card.nextStepOn && <span className={styles.nextDate}>{shortDay(card.nextStepOn)}</span>}
            {card.nextStep && <span className={styles.nextText}>{card.nextStep}</span>}
          </span>
        )}
        <span className={styles.cardFoot}>
          <span className={styles.priority} title={`Prioridade ${card.priority} de 5`}>
            P{card.priority}
          </span>
          {showStatus && (
            <span className={styles.statusTag}>
              <span className={styles.dot} style={{ background: info.color }} aria-hidden="true" />
              {info.label}
            </span>
          )}
          {card.jobClosedAtSource && <span className={styles.closedTag}>Encerrada na fonte</span>}
        </span>
      </Link>
    </li>
  )
}
