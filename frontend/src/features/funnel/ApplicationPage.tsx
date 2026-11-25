import { useEffect, useRef, useState, type CSSProperties, type FormEvent, type KeyboardEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { ApiError, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { Select } from '../../ui/Select'
import uiStyles from '../../ui/ui.module.css'
import {
  formatDay,
  statuses,
  statusInfo,
  todayInBrazil,
  useApplication,
  useDeleteInteraction,
  useLeaveFunnel,
  useSaveApplication,
  useSaveInteraction,
  type ApplicationDetail,
  type ApplicationForm,
  type ApplicationStatus,
  type Interaction,
} from './api'
import { Pager } from '../../ui/Pager'
import styles from './application.module.css'

const priorityLabels: Record<number, string> = { 1: 'Altíssima', 2: 'Alta', 3: 'Média', 4: 'Baixa', 5: 'Baixíssima' }

const priorities = [1, 2, 3, 4, 5].map((value) => ({ value: String(value), label: `${priorityLabels[value]} (${value})` }))

// The stages a process moves through; rejected and withdrawn close it instead.
const stages = statuses.filter((status) => status.value !== 'rejected' && status.value !== 'withdrawn')

const PAGE_SIZE = 7

const byLabel = (label: string) => statuses.find((status) => status.label === label)

/** "18/11" for this year, "18/11/2025" otherwise. */
function shortDay(value: string | null): string {
  if (!value) return ''
  return value.slice(0, 4) === todayInBrazil().slice(0, 4) ? formatDay(value).slice(0, 5) : formatDay(value)
}

/** Whole days from a "yyyy-mm-dd" day to today in Brazil. */
function daysSince(value: string): number {
  return Math.round((Date.parse(todayInBrazil()) - Date.parse(value)) / 86_400_000)
}

/** The saved application as a form, the base of every partial save. */
function formOf(detail: ApplicationDetail): ApplicationForm {
  const { application } = detail
  return {
    status: application.status,
    priority: application.priority,
    appliedOn: application.appliedOn,
    nextStep: application.nextStep,
    nextStepOn: application.nextStepOn,
    contact: detail.contact,
    hasReferral: detail.hasReferral,
    notes: detail.notes,
  }
}

interface TimelineEntry {
  interaction: Interaction
  /** The stage the process was in from this entry on; colors its node. */
  stage: ApplicationStatus
  /** For stage changes and the first entry: the stage reached. */
  reached: ApplicationStatus | null
}

/**
 * Reads the history the API writes ("Entrou no funil", then "Aplicada → Entrevista" per
 * change) into the stage of each entry and the day each stage was last reached.
 */
function readHistory(detail: ApplicationDetail) {
  const oldestFirst = [...detail.interactions].sort((a, b) => a.date.localeCompare(b.date) || a.createdAt.localeCompare(b.createdAt))
  const firstChange = oldestFirst.find((item) => item.isSystem && item.title.includes('→'))
  const entered = (firstChange ? byLabel(firstChange.title.split('→')[0]?.trim() ?? '')?.value : undefined) ?? detail.application.status

  let stage: ApplicationStatus = entered
  const reachedOn: Partial<Record<ApplicationStatus, string>> = {}
  const entries: TimelineEntry[] = oldestFirst.map((interaction, index) => {
    let reached: ApplicationStatus | null = null
    if (interaction.isSystem && interaction.title.includes('→')) {
      const target = byLabel(interaction.title.split('→').pop()?.trim() ?? '')
      if (target) {
        stage = target.value
        reached = target.value
      }
    } else if (index === 0 && interaction.isSystem) {
      reached = entered
    }
    if (reached) reachedOn[reached] = interaction.date
    return { interaction, stage, reached }
  })

  return { entered, reachedOn, entries: entries.reverse() }
}

export function ApplicationPage() {
  const { id = '' } = useParams()
  const detail = useApplication(id)

  return (
    <div className={styles.page}>
      <Link to="/funil" className={styles.back}>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="m15 18-6-6 6-6" />
        </svg>
        Funil
      </Link>
      {detail.isError && <Alert>{errorMessage(detail.error)}</Alert>}
      {/* Keyed by update time so every block starts again from saved data after each save. */}
      {detail.data && <ApplicationView key={detail.data.application.updatedAt} id={id} detail={detail.data} />}
    </div>
  )
}

function ApplicationView({ id, detail }: { id: string; detail: ApplicationDetail }) {
  const { application } = detail
  const save = useSaveApplication(id)
  const saveWith = (changes: Partial<ApplicationForm>, onSuccess?: () => void) => save.mutate({ ...formOf(detail), ...changes }, { onSuccess })
  const history = readHistory(detail)

  return (
    <>
      <header className={styles.header}>
        <div className={styles.titles}>
          <h1 className={styles.title}>{application.title}</h1>
          <p className={styles.meta}>
            {application.company}
            {application.appliedOn && (
              <>
                {application.company && ' · '}aplicada em <span className={styles.mono}>{shortDay(application.appliedOn)}</span>
              </>
            )}
          </p>
        </div>
        <div className={styles.headerActions}>
          <a href={detail.jobUrl} target="_blank" rel="noopener noreferrer" className={styles.outlineLink}>
            Vaga original
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M7 17 17 7M8 7h9v9" />
            </svg>
          </a>
          <MoreMenu id={id} status={application.status} reopenAt={lastStageReached(history.reachedOn)} saving={save.isPending} onChange={(status) => saveWith({ status })} />
        </div>
      </header>

      <Stages status={application.status} reachedOn={history.reachedOn} closedAtSource={application.jobClosedAtSource} saving={save.isPending} onChange={(status) => saveWith({ status })} />
      {save.isError && <Alert>{errorMessage(save.error)}</Alert>}

      <NextStep detail={detail} saving={save.isPending} error={save.error} onSave={saveWith} />

      <Sections id={id} detail={detail} entries={history.entries} entered={history.entered} saving={save.isPending} error={save.error} onSave={saveWith} />

    </>
  )
}

/**
 * The funnel as a horizontal timeline: a dot per stage in its color, joined by a line that
 * blends into the next color up to the current stage, each with the day it was reached.
 */
function Stages({
  status,
  reachedOn,
  closedAtSource,
  saving,
  onChange,
}: {
  status: ApplicationStatus
  reachedOn: Partial<Record<ApplicationStatus, string>>
  closedAtSource: boolean
  saving: boolean
  onChange: (status: ApplicationStatus) => void
}) {
  const current = stages.findIndex((stage) => stage.value === status)
  const closed = current === -1
  // A closed process keeps the stages it went through.
  const last = closed ? Math.max(-1, ...stages.map((stage, index) => (reachedOn[stage.value] ? index : -1))) : current

  return (
    <section aria-label="Etapa" className={styles.stages}>
      <ol className={styles.track} aria-label="Etapas do processo">
        {stages.map((stage, index) => {
          const past = index < last
          const isCurrent = !closed && index === current
          const isNext = !closed && index === current + 1
          const reached = index <= last
          const day = reachedOn[stage.value]
          const note = reached ? (day ? shortDay(day) : index < last ? 'pulou' : '') : isNext ? 'próxima' : ''
          const nextColor = stages[index + 1]?.color
          return (
            <li key={stage.value} className={styles.step} style={{ '--stage': stage.color, '--next': nextColor } as CSSProperties}>
              <span className={styles.rail} aria-hidden="true">
                <span className={isCurrent ? `${styles.node} ${styles.nodeCurrent}` : reached ? styles.node : isNext ? `${styles.node} ${styles.nodeNext}` : `${styles.node} ${styles.nodeLater}`} />
                {index < stages.length - 1 && <span className={past ? `${styles.link} ${styles.linkDone}` : styles.link} />}
              </span>
              <button
                type="button"
                className={styles.stepButton}
                aria-current={isCurrent ? 'step' : undefined}
                onClick={() => !isCurrent && onChange(stage.value)}
                disabled={saving}
              >
                <span className={isCurrent ? `${styles.stepName} ${styles.stepNameCurrent}` : reached ? styles.stepName : `${styles.stepName} ${styles.stepNameLater}`}>{stage.label}</span>
                <span className={isCurrent ? `${styles.stepNote} ${styles.stepNoteCurrent}` : styles.stepNote}>{note || ' '}</span>
              </button>
            </li>
          )
        })}
      </ol>
      {(closed || closedAtSource) && (
        <p className={styles.stagesNote}>
          {closed && (
            <>
              Encerrada como <strong>{statusInfo(status).label}</strong>
              {reachedOn[status] && <> em {shortDay(reachedOn[status] ?? null)}</>}. Clique numa etapa para reabrir.
            </>
          )}
          {closedAtSource && <span className={styles.warn}>{closed ? ' ' : ''}A fonte não lista mais essa vaga.</span>}
        </p>
      )}
    </section>
  )
}

interface BlockProps {
  detail: ApplicationDetail
  saving: boolean
  error: Error | null
  onSave: (changes: Partial<ApplicationForm>, onSuccess?: () => void) => void
}

function NextStep({ detail, saving, error, onSave }: BlockProps) {
  const { application } = detail
  const [editing, setEditing] = useState(false)
  const [text, setText] = useState(application.nextStep)
  const [day, setDay] = useState(application.nextStepOn ?? '')
  const fieldError = error instanceof ApiError ? error.field('nextStep') : undefined
  const late = application.isOverdue && application.nextStepOn ? daysSince(application.nextStepOn) : 0

  const submit = (event: FormEvent) => {
    event.preventDefault()
    onSave({ nextStep: text, nextStepOn: day || null }, () => setEditing(false))
  }

  if (editing) {
    return (
      <form className={`${styles.next} ${styles.nextEditing}`} onSubmit={submit} noValidate>
        <div className={styles.fields}>
          <Field label="Próximo passo" value={text} onChange={(event) => setText(event.target.value)} maxLength={300} error={fieldError} autoFocus />
          <Field label="Data" type="date" value={day} onChange={(event) => setDay(event.target.value)} />
        </div>
        <div className={styles.formActions}>
          <Button variant="ghost" onClick={() => setEditing(false)}>
            Cancelar
          </Button>
          <Button type="submit" disabled={saving}>
            Salvar
          </Button>
        </div>
      </form>
    )
  }

  return (
    <section aria-label="Próximo passo" className={styles.next}>
      {late > 0 && <span className={styles.latePill}>{late === 1 ? 'Atrasado há 1 dia' : `Atrasado há ${late} dias`}</span>}
      <span className={styles.nextText}>
        <span className={styles.faint}>Próximo passo:</span> {application.nextStep ? <span className={styles.nextTitle}>{application.nextStep}</span> : <span className={styles.faint}>nenhum definido</span>}
      </span>
      {application.nextStepOn && <span className={styles.mono}>{shortDay(application.nextStepOn)}</span>}
      <button type="button" className={styles.textButton} onClick={() => setEditing(true)}>
        {application.nextStep ? 'Editar' : 'Definir'}
      </button>
    </section>
  )
}

type Tab = 'history' | 'details' | 'notes'

function Sections({ id, detail, entries, entered, saving, error, onSave }: BlockProps & { id: string; entries: TimelineEntry[]; entered: ApplicationStatus }) {
  const [tab, setTab] = useState<Tab>('history')
  // The tab whose add or edit form is open, opened by the icon beside the tabs.
  const [composing, setComposing] = useState<Tab | null>(null)
  const done = () => setComposing(null)
  const action = { history: { label: 'Registrar no histórico', icon: 'plus' }, details: { label: 'Editar detalhes', icon: 'pencil' }, notes: { label: 'Adicionar nota', icon: 'plus' } }[tab]
  const tabs: { value: Tab; label: string; count?: number }[] = [
    { value: 'history', label: 'Histórico', count: entries.length },
    { value: 'details', label: 'Detalhes' },
    { value: 'notes', label: 'Notas' },
  ]

  return (
    <section className={styles.sections}>
      <div className={styles.tabsRow}>
        <div role="tablist" aria-label="Seções" className={styles.tabs}>
          {tabs.map((item) => (
            <button
              key={item.value}
              type="button"
              role="tab"
              id={`tab-${item.value}`}
              aria-selected={tab === item.value}
              aria-controls="tab-panel"
              className={tab === item.value ? `${styles.tab} ${styles.tabActive}` : styles.tab}
              onClick={() => {
                setTab(item.value)
                setComposing(null)
              }}
            >
              {item.label}
              {item.count !== undefined && <span className={styles.tabCount}>{item.count}</span>}
            </button>
          ))}
        </div>
        {composing !== tab && (
          <button type="button" className={styles.tabAction} onClick={() => setComposing(tab)} aria-label={action.label} title={action.label}>
            {action.icon === 'plus' ? (
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true">
                <path d="M12 5v14M5 12h14" />
              </svg>
            ) : (
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M4 20h4L19 9a2.8 2.8 0 0 0-4-4L4 16v4Z" />
                <path d="m13.5 6.5 4 4" />
              </svg>
            )}
          </button>
        )}
      </div>
      <div role="tabpanel" id="tab-panel" aria-labelledby={`tab-${tab}`}>
        {tab === 'history' && <Timeline id={id} entries={entries} entered={entered} adding={composing === 'history'} onDoneAdding={done} />}
        {tab === 'details' && <Details detail={detail} saving={saving} error={error} onSave={onSave} editing={composing === 'details'} onDone={done} />}
        {tab === 'notes' && <Notes detail={detail} saving={saving} error={error} onSave={onSave} adding={composing === 'notes'} onDone={done} />}
      </div>
    </section>
  )
}

function Timeline({ id, entries, entered, adding, onDoneAdding }: { id: string; entries: TimelineEntry[]; entered: ApplicationStatus; adding: boolean; onDoneAdding: () => void }) {
  const [editing, setEditing] = useState<string | null>(null)
  const [page, setPage] = useState(0)
  const pages = Math.max(1, Math.ceil(entries.length / PAGE_SIZE))
  // A new entry lands on top, so adding one shows the first page.
  const shown = adding ? 0 : Math.min(page, pages - 1)
  const start = shown * PAGE_SIZE

  return (
    <>
      {adding && <InteractionEditor applicationId={id} onDone={onDoneAdding} />}
      {entries.length === 0 && !adding && <p className={styles.faint}>Nada registrado ainda.</p>}
      <ol className={styles.timeline} start={start + 1}>
        {entries.slice(start, start + PAGE_SIZE).map((entry) => {
          const { interaction } = entry
          if (editing === interaction.id) {
            return (
              <li key={interaction.id}>
                <InteractionEditor applicationId={id} interaction={interaction} onDone={() => setEditing(null)} />
              </li>
            )
          }
          const color = statusInfo(entry.stage).color
          return (
            <li key={interaction.id} className={styles.entry}>
              <span className={styles.entryDate}>{shortDay(interaction.date)}</span>
              <span className={styles.entryRail} aria-hidden="true">
                <span className={interaction.isSystem ? styles.entryNode : `${styles.entryNode} ${styles.entryNodeOpen}`} style={{ '--from': color } as CSSProperties} />
              </span>
              <span className={styles.entryBody}>
                {interaction.isSystem ? (
                  <span className={styles.entryText}>
                    {entry.reached && interaction.title.includes('→') ? (
                      <>
                        Passou para <strong style={{ color }}>{statusInfo(entry.reached).label}</strong>
                      </>
                    ) : entry.reached ? (
                      <>
                        Entrou no funil como <strong style={{ color: statusInfo(entered).color }}>{statusInfo(entered).label}</strong>
                      </>
                    ) : (
                      interaction.title
                    )}
                  </span>
                ) : (
                  <>
                    <span className={styles.entryTitle}>{interaction.title}</span>
                    {interaction.detail && <span className={styles.entryDetail}>{interaction.detail}</span>}
                    {editing === null && (
                      <button type="button" className={styles.entryEdit} onClick={() => setEditing(interaction.id)}>
                        Editar
                      </button>
                    )}
                  </>
                )}
              </span>
            </li>
          )
        })}
      </ol>
      <Pager page={shown} pages={pages} onChange={setPage} label="Páginas do histórico" />
    </>
  )
}

function Details({ detail, saving, error, onSave, editing, onDone }: BlockProps & { editing: boolean; onDone: () => void }) {
  const { application } = detail
  const [form, setForm] = useState({
    priority: application.priority,
    contact: detail.contact,
    appliedOn: application.appliedOn ?? '',
    hasReferral: detail.hasReferral,
  })
  const apiError = error instanceof ApiError ? error : null
  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) => setForm((current) => ({ ...current, [key]: value }))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    onSave({ ...form, appliedOn: form.appliedOn || null }, onDone)
  }

  if (editing) {
    return (
      <form className={styles.panelForm} onSubmit={submit} noValidate>
        <div className={styles.fields2}>
          <div className={uiStyles.field}>
            <label htmlFor="application-priority" className={uiStyles.label}>
              Prioridade
            </label>
            <Select id="application-priority" value={String(form.priority)} options={priorities} onChange={(value) => set('priority', Number(value))} />
          </div>
          <Field label="Aplicada em" type="date" value={form.appliedOn} onChange={(event) => set('appliedOn', event.target.value)} />
          <Field label="Contato" value={form.contact} onChange={(event) => set('contact', event.target.value)} maxLength={200} error={apiError?.field('contact')} />
          <label className={styles.check}>
            <input type="checkbox" checked={form.hasReferral} onChange={(event) => set('hasReferral', event.target.checked)} />
            Tenho indicação
          </label>
        </div>
        <div className={styles.formActions}>
          <Button variant="ghost" onClick={onDone}>
            Cancelar
          </Button>
          <Button type="submit" disabled={saving}>
            Salvar
          </Button>
        </div>
      </form>
    )
  }

  return (
    <div className={styles.panel}>
      <dl className={styles.facts}>
        <div className={styles.fact}>
          <dt>Prioridade</dt>
          <dd>
            {priorityLabels[application.priority]} <span className={styles.faint}>{application.priority} de 5</span>
          </dd>
        </div>
        <div className={styles.fact}>
          <dt>Aplicada em</dt>
          <dd className={styles.mono}>{application.appliedOn ? formatDay(application.appliedOn) : <span className={styles.faint}>Não informado</span>}</dd>
        </div>
        <div className={styles.fact}>
          <dt>Contato</dt>
          <dd>{detail.contact || <span className={styles.faint}>Nenhum</span>}</dd>
        </div>
        <div className={styles.fact}>
          <dt>Indicação</dt>
          <dd>{detail.hasReferral ? 'Sim' : 'Não'}</dd>
        </div>
      </dl>
    </div>
  )
}

interface Note {
  day: string | null
  text: string
}

const DATED = /^(\d{2}\/\d{2}\/\d{4})\n([\s\S]*)$/

/** Notes live in one text; each note is a paragraph, dated on its first line when added here. Newest first. */
function splitNotes(notes: string): Note[] {
  return notes
    .split(/\n{2,}/)
    .map((block) => block.trim())
    .filter(Boolean)
    .map((block) => {
      const dated = DATED.exec(block)
      return dated ? { day: dated[1] ?? null, text: dated[2]?.trim() ?? '' } : { day: null, text: block }
    })
}

// A blank line separates notes, so one inside a note would split it in two.
const tidy = (text: string) => text.trim().replace(/\n\s*\n+/g, '\n')

/** Back to the one notes text: dated notes keep their date line. */
const joinNotes = (notes: Note[]) => notes.map((note) => (note.day ? `${note.day}\n${note.text}` : note.text)).join('\n\n')

function Notes({ detail, saving, onSave, adding, onDone }: BlockProps & { adding: boolean; onDone: () => void }) {
  const notes = splitNotes(detail.notes)
  const [draft, setDraft] = useState('')
  const [editing, setEditing] = useState<number | null>(null)
  const [edit, setEdit] = useState('')

  const add = (event: FormEvent) => {
    event.preventDefault()
    const text = tidy(draft)
    if (!text) return
    onSave({ notes: joinNotes([{ day: formatDay(todayInBrazil()), text }, ...notes]) }, () => {
      setDraft('')
      onDone()
    })
  }

  // Saves one note, or removes it when its text is gone; the others stay as they were.
  const replace = (index: number, text: string | null) => {
    const next = text === null || !text.trim() ? notes.filter((_, i) => i !== index) : notes.map((note, i) => (i === index ? { ...note, text: tidy(text) } : note))
    onSave({ notes: joinNotes(next) }, () => setEditing(null))
  }

  return (
    <div className={styles.panel}>
      {adding && (
        <form className={styles.entryForm} onSubmit={add} noValidate>
          <textarea className={styles.textarea} aria-label="Nova nota" value={draft} onChange={(event) => setDraft(event.target.value)} maxLength={2000} rows={4} autoFocus placeholder="O que estudar, salário, impressões da conversa…" />
          <div className={styles.formActions}>
            <Button variant="ghost" onClick={onDone}>
              Cancelar
            </Button>
            <Button type="submit" disabled={saving || !draft.trim()}>
              Adicionar
            </Button>
          </div>
        </form>
      )}
      {notes.length === 0 && !adding && <p className={styles.faint}>Nenhuma nota. Use o + para guardar o que não cabe no histórico: o que estudar, salário, impressões.</p>}
      {notes.length > 0 && (
        <ul className={styles.notes}>
          {notes.map((note, index) => (
            <li key={`${note.day ?? 'undated'}-${index}`} className={styles.note}>
              {note.day && <span className={styles.noteDay}>{shortDay(note.day.split('/').reverse().join('-'))}</span>}
              {editing === index ? (
                <form
                  className={styles.noteForm}
                  onSubmit={(event) => {
                    event.preventDefault()
                    replace(index, edit)
                  }}
                  noValidate
                >
                  <textarea className={styles.textarea} aria-label="Editar nota" value={edit} onChange={(event) => setEdit(event.target.value)} maxLength={2000} rows={4} autoFocus />
                  <div className={styles.formActions}>
                    <Button variant="ghost" onClick={() => replace(index, null)} disabled={saving}>
                      Apagar
                    </Button>
                    <span className={styles.spacer} />
                    <Button variant="ghost" onClick={() => setEditing(null)}>
                      Cancelar
                    </Button>
                    <Button type="submit" disabled={saving}>
                      Salvar
                    </Button>
                  </div>
                </form>
              ) : (
                <>
                  <p className={styles.notesText}>{note.text}</p>
                  {editing === null && !adding && (
                    <button
                      type="button"
                      className={styles.entryEdit}
                      onClick={() => {
                        setEdit(note.text)
                        setEditing(index)
                      }}
                    >
                      Editar
                    </button>
                  )}
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/** The last open stage the process reached, where reopening takes it back to. */
function lastStageReached(reachedOn: Partial<Record<ApplicationStatus, string>>): ApplicationStatus {
  return [...stages].reverse().find((stage) => reachedOn[stage.value])?.value ?? 'applied'
}

interface MenuItem {
  key: string
  label: string
  hint?: string
  danger?: boolean
  run: () => void
}

/** "⋯" beside the original posting: close the process (or reopen it) and leave the funnel. */
function MoreMenu({
  id,
  status,
  reopenAt,
  saving,
  onChange,
}: {
  id: string
  status: ApplicationStatus
  reopenAt: ApplicationStatus
  saving: boolean
  onChange: (status: ApplicationStatus) => void
}) {
  const [open, setOpen] = useState(false)
  const [confirmLeave, setConfirmLeave] = useState(false)
  const wrap = useRef<HTMLDivElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)
  const list = useRef<HTMLDivElement>(null)
  const closed = status === 'rejected' || status === 'withdrawn'

  const items: MenuItem[] = [
    ...(closed
      ? [{ key: 'reopen', label: 'Reabrir', hint: `Volta para ${statusInfo(reopenAt).label}`, run: () => onChange(reopenAt) }]
      : [
          { key: 'rejected', label: 'Não passei nesta vaga', hint: 'Encerra e guarda o histórico', run: () => onChange('rejected') },
          { key: 'withdrawn', label: 'Desisti desta vaga', hint: 'Encerra e guarda o histórico', run: () => onChange('withdrawn') },
        ]),
    { key: 'leave', label: 'Tirar do funil', hint: 'Apaga a candidatura e o histórico', danger: true, run: () => setConfirmLeave(true) },
  ]

  useEffect(() => {
    if (!open) return
    list.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus()
    const onPointer = (event: PointerEvent) => {
      if (!wrap.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', onPointer)
    return () => document.removeEventListener('pointerdown', onPointer)
  }, [open])

  const close = () => {
    setOpen(false)
    trigger.current?.focus()
  }

  const onMenuKey = (event: KeyboardEvent<HTMLDivElement>) => {
    const buttons = [...(list.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]') ?? [])]
    const index = buttons.indexOf(document.activeElement as HTMLButtonElement)
    const move = (to: number) => buttons[(to + buttons.length) % buttons.length]?.focus()
    if (event.key === 'ArrowDown') move(index + 1)
    else if (event.key === 'ArrowUp') move(index - 1)
    else if (event.key === 'Home') move(0)
    else if (event.key === 'End') move(buttons.length - 1)
    else if (event.key === 'Escape') close()
    else if (event.key === 'Tab') setOpen(false)
    else return
    if (event.key !== 'Tab') event.preventDefault()
  }

  return (
    <div ref={wrap} className={styles.more}>
      <button
        ref={trigger}
        type="button"
        className={open ? `${styles.moreButton} ${styles.moreOpen}` : styles.moreButton}
        aria-label="Mais ações"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
        disabled={saving}
      >
        <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <circle cx="5" cy="12" r="1.7" />
          <circle cx="12" cy="12" r="1.7" />
          <circle cx="19" cy="12" r="1.7" />
        </svg>
      </button>
      {open && (
        <div ref={list} role="menu" aria-label="Mais ações" className={styles.menu} onKeyDown={onMenuKey}>
          {items.map((item) => (
            <div key={item.key}>
              {item.danger && <div className={styles.menuSeparator} role="separator" />}
              <button
                type="button"
                role="menuitem"
                className={item.danger ? `${styles.menuItem} ${styles.menuDanger}` : styles.menuItem}
                onClick={() => {
                  setOpen(false)
                  item.run()
                }}
              >
                <span>{item.label}</span>
                {item.hint && <span className={styles.menuHint}>{item.hint}</span>}
              </button>
            </div>
          ))}
        </div>
      )}
      {confirmLeave && <LeaveDialog id={id} onClose={() => setConfirmLeave(false)} />}
    </div>
  )
}

/** Leaving the funnel deletes the history, so it asks once more. */
function LeaveDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const navigate = useNavigate()
  const leave = useLeaveFunnel(id)

  useEffect(() => {
    dialog.current?.showModal()
  }, [])

  return (
    <dialog ref={dialog} className={styles.dialog} onCancel={onClose} onClose={onClose} aria-labelledby="leave-title">
      <div className={styles.dialogBody}>
        <h2 id="leave-title" className={styles.dialogTitle}>
          Tirar do funil?
        </h2>
        <p className={styles.dialogText}>A candidatura e todo o histórico são apagados. A vaga volta para a sua lista de vagas. Não dá para desfazer.</p>
        {leave.isError && <Alert>{errorMessage(leave.error)}</Alert>}
        <div className={styles.formActions}>
          <Button variant="ghost" onClick={onClose}>
            Cancelar
          </Button>
          <Button variant="danger" onClick={() => leave.mutate(undefined, { onSuccess: () => navigate('/funil') })} disabled={leave.isPending}>
            Tirar do funil
          </Button>
        </div>
      </div>
    </dialog>
  )
}

function InteractionEditor({ applicationId, interaction, onDone }: { applicationId: string; interaction?: Interaction; onDone: () => void }) {
  const save = useSaveInteraction(applicationId)
  const remove = useDeleteInteraction(applicationId)
  const [date, setDate] = useState(interaction?.date ?? todayInBrazil())
  const [title, setTitle] = useState(interaction?.title ?? '')
  const [detail, setDetail] = useState(interaction?.detail ?? '')
  const error = save.error instanceof ApiError ? save.error : null

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate({ ...(interaction ? { id: interaction.id } : {}), form: { date, title, detail } }, { onSuccess: onDone })
  }

  return (
    <form className={styles.entryForm} onSubmit={submit} noValidate>
      <div className={styles.fields}>
        <Field label="O que aconteceu" value={title} onChange={(event) => setTitle(event.target.value)} maxLength={200} error={error?.field('title')} autoFocus />
        <Field label="Data" type="date" value={date} onChange={(event) => setDate(event.target.value)} error={error?.field('date')} />
      </div>
      <label className={uiStyles.field}>
        <span className={uiStyles.label}>Detalhe (opcional)</span>
        <textarea className={styles.textarea} value={detail} onChange={(event) => setDetail(event.target.value)} maxLength={5000} rows={3} />
      </label>
      {(save.isError && !error?.problem.errors) || remove.isError ? <Alert>{errorMessage(save.error ?? remove.error)}</Alert> : null}
      <div className={styles.formActions}>
        {interaction && (
          <Button variant="ghost" onClick={() => remove.mutate(interaction.id, { onSuccess: onDone })} disabled={remove.isPending}>
            Apagar
          </Button>
        )}
        <span className={styles.spacer} />
        <Button variant="ghost" onClick={onDone}>
          Cancelar
        </Button>
        <Button type="submit" disabled={save.isPending || !title.trim()}>
          Salvar
        </Button>
      </div>
    </form>
  )
}
