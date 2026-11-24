import { useState, type FormEvent } from 'react'
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
import styles from './application.module.css'

const priorityLabels: Record<number, string> = { 1: 'Altíssima', 2: 'Alta', 3: 'Média', 4: 'Baixa', 5: 'Baixíssima' }

const priorities = [1, 2, 3, 4, 5].map((value) => ({ value: String(value), label: `${priorityLabels[value]} (${value})` }))

// The stages a process moves through; rejected and withdrawn close it instead.
const stages = statuses.filter((status) => status.value !== 'rejected' && status.value !== 'withdrawn')
const closings = statuses.filter((status) => status.value === 'rejected' || status.value === 'withdrawn')

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
  const meta = [
    application.company,
    application.appliedOn ? `aplicada em ${shortDay(application.appliedOn)}` : null,
    detail.hasReferral ? 'com indicação' : null,
  ]
    .filter(Boolean)
    .join(' · ')

  return (
    <>
      <header className={styles.header}>
        <div className={styles.titles}>
          <h1 className={styles.title}>{application.title}</h1>
          {meta && <p className={styles.meta}>{meta}</p>}
        </div>
        <div className={styles.headerActions}>
          <Link to={`/vagas/${application.jobId}`} className={styles.quietLink}>
            Ver a vaga
          </Link>
          <a href={detail.jobUrl} target="_blank" rel="noopener noreferrer" className={styles.outlineLink}>
            Vaga original
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M7 17 17 7M8 7h9v9" />
            </svg>
          </a>
        </div>
      </header>

      <StageTrack status={application.status} closedAtSource={application.jobClosedAtSource} saving={save.isPending} onChange={(status) => saveWith({ status })} />
      {save.isError && <Alert>{errorMessage(save.error)}</Alert>}

      <NextStep detail={detail} saving={save.isPending} error={save.error} onSave={saveWith} />

      <div className={styles.columns}>
        <Timeline id={id} interactions={detail.interactions} />
        <Details id={id} detail={detail} saving={save.isPending} error={save.error} onSave={saveWith} />
      </div>
    </>
  )
}

/** The funnel as a colored ribbon: filled up to the current stage. Clicking a stage moves there. */
function StageTrack({ status, closedAtSource, saving, onChange }: { status: ApplicationStatus; closedAtSource: boolean; saving: boolean; onChange: (status: ApplicationStatus) => void }) {
  const current = stages.findIndex((stage) => stage.value === status)
  const closed = current === -1

  return (
    <section aria-label="Etapa" className={styles.stages}>
      <div className={styles.track} role="group" aria-label="Etapas do processo">
        {stages.map((stage, index) => {
          const reached = !closed && index <= current
          return (
            <button
              key={stage.value}
              type="button"
              className={styles.stage}
              aria-current={index === current ? 'step' : undefined}
              onClick={() => index !== current && onChange(stage.value)}
              disabled={saving}
            >
              <span className={styles.bar} style={reached ? { background: stage.color } : undefined} />
              <span className={index === current ? `${styles.stageLabel} ${styles.stageCurrent}` : styles.stageLabel}>
                {index === current && <span className={styles.dot} style={{ background: stage.color }} aria-hidden="true" />}
                {stage.label}
              </span>
            </button>
          )
        })}
      </div>
      {!closed && (
        <p className={styles.currentMobile}>
          <span className={styles.dot} style={{ background: stages[current]?.color }} aria-hidden="true" />
          {stages[current]?.label}
          <span className={styles.stepCount}>
            etapa {current + 1} de {stages.length}
          </span>
        </p>
      )}
      <p className={styles.hint}>
        {closed ? (
          <>
            Encerrada como <strong className={styles.closedAs}>{statusInfo(status).label}</strong>. Clique numa etapa para reabrir.
          </>
        ) : (
          <>
            Clique numa etapa para mudar. Acabou?{' '}
            {closings.map((closing, index) => (
              <span key={closing.value}>
                {index > 0 && ' · '}
                <button type="button" className={styles.inlineButton} onClick={() => onChange(closing.value)} disabled={saving}>
                  {closing.value === 'rejected' ? 'Rejeitada' : 'Desisti'}
                </button>
              </span>
            ))}
          </>
        )}
        {closedAtSource && <span className={styles.warn}> A fonte não lista mais essa vaga.</span>}
      </p>
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
      <div className={styles.nextText}>
        <span className={styles.label}>Próximo passo</span>
        <span className={application.nextStep ? styles.nextTitle : `${styles.nextTitle} ${styles.empty}`}>{application.nextStep || 'Nenhum definido'}</span>
      </div>
      {application.nextStepOn && (
        <div className={styles.nextWhen}>
          <span className={styles.data}>{shortDay(application.nextStepOn)}</span>
          {late > 0 && <span className={styles.late}>{late === 1 ? 'atrasado há 1 dia' : `atrasado há ${late} dias`}</span>}
        </div>
      )}
      <Button variant="secondary" onClick={() => setEditing(true)}>
        {application.nextStep ? 'Editar' : 'Definir'}
      </Button>
    </section>
  )
}

function Details({ id, detail, saving, error, onSave }: BlockProps & { id: string }) {
  const navigate = useNavigate()
  const leave = useLeaveFunnel(id)
  const { application } = detail
  const [editing, setEditing] = useState(false)
  const [confirmLeave, setConfirmLeave] = useState(false)
  const [form, setForm] = useState({
    priority: application.priority,
    contact: detail.contact,
    appliedOn: application.appliedOn ?? '',
    hasReferral: detail.hasReferral,
    notes: detail.notes,
  })
  const apiError = error instanceof ApiError ? error : null
  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) => setForm((current) => ({ ...current, [key]: value }))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    onSave({ ...form, appliedOn: form.appliedOn || null }, () => setEditing(false))
  }

  return (
    <aside aria-labelledby="details-title" className={styles.details}>
      <h2 id="details-title" className={styles.heading}>
        Detalhes
      </h2>

      {editing ? (
        <form className={styles.detailsForm} onSubmit={submit} noValidate>
          <div className={uiStyles.field}>
            <label htmlFor="application-priority" className={uiStyles.label}>
              Prioridade
            </label>
            <Select id="application-priority" value={String(form.priority)} options={priorities} onChange={(value) => set('priority', Number(value))} />
          </div>
          <Field label="Contato" value={form.contact} onChange={(event) => set('contact', event.target.value)} maxLength={200} error={apiError?.field('contact')} />
          <Field label="Aplicada em" type="date" value={form.appliedOn} onChange={(event) => set('appliedOn', event.target.value)} />
          <label className={styles.check}>
            <input type="checkbox" checked={form.hasReferral} onChange={(event) => set('hasReferral', event.target.checked)} />
            Tenho indicação
          </label>
          <label className={uiStyles.field}>
            <span className={uiStyles.label}>Notas</span>
            <textarea className={styles.textarea} value={form.notes} onChange={(event) => set('notes', event.target.value)} maxLength={5000} rows={5} />
          </label>
          <div className={styles.formActions}>
            <Button variant="ghost" onClick={() => setEditing(false)}>
              Cancelar
            </Button>
            <Button type="submit" disabled={saving}>
              Salvar
            </Button>
          </div>
        </form>
      ) : (
        <>
          <dl className={styles.facts}>
            <div className={styles.fact}>
              <dt>Prioridade</dt>
              <dd>
                {priorityLabels[application.priority]} <span className={styles.dim}>{application.priority} de 5</span>
              </dd>
            </div>
            <div className={styles.fact}>
              <dt>Contato</dt>
              <dd>{detail.contact || <span className={styles.dim}>Nenhum</span>}</dd>
            </div>
            <div className={styles.fact}>
              <dt>Aplicada em</dt>
              <dd className={styles.data}>{application.appliedOn ? formatDay(application.appliedOn) : <span className={styles.dim}>Não informado</span>}</dd>
            </div>
            <div className={styles.fact}>
              <dt>Indicação</dt>
              <dd>{detail.hasReferral ? 'Sim' : 'Não'}</dd>
            </div>
          </dl>
          <div className={styles.notes}>
            <span className={styles.label}>Notas</span>
            {detail.notes ? <p className={styles.notesText}>{detail.notes}</p> : <p className={styles.dim}>Nenhuma nota.</p>}
          </div>
          <button type="button" className={styles.textButton} onClick={() => setEditing(true)}>
            Editar detalhes
          </button>
        </>
      )}

      <div className={styles.leave}>
        {confirmLeave ? (
          <>
            <p className={styles.dim}>Tira a vaga do funil e apaga o histórico.</p>
            <div className={styles.formActions}>
              <Button variant="ghost" onClick={() => setConfirmLeave(false)}>
                Cancelar
              </Button>
              <Button variant="danger" onClick={() => leave.mutate(undefined, { onSuccess: () => navigate('/funil') })} disabled={leave.isPending}>
                Tirar do funil
              </Button>
            </div>
          </>
        ) : (
          <button type="button" className={`${styles.textButton} ${styles.dangerText}`} onClick={() => setConfirmLeave(true)}>
            Tirar do funil
          </button>
        )}
        {leave.isError && <Alert>{errorMessage(leave.error)}</Alert>}
      </div>
    </aside>
  )
}

function Timeline({ id, interactions }: { id: string; interactions: Interaction[] }) {
  const [editing, setEditing] = useState<Interaction | 'new' | null>(null)

  return (
    <section aria-labelledby="timeline-title" className={styles.history}>
      <div className={styles.headingRow}>
        <h2 id="timeline-title" className={styles.heading}>
          Histórico
        </h2>
        {editing === null && (
          <button type="button" className={styles.textButton} onClick={() => setEditing('new')}>
            Registrar
          </button>
        )}
      </div>
      {editing === 'new' && <InteractionEditor applicationId={id} onDone={() => setEditing(null)} />}
      <ol className={styles.timeline}>
        {interactions.map((interaction, index) =>
          editing !== 'new' && editing?.id === interaction.id ? (
            <li key={interaction.id}>
              <InteractionEditor applicationId={id} interaction={interaction} onDone={() => setEditing(null)} />
            </li>
          ) : (
            <li key={interaction.id} className={styles.entry}>
              <span className={styles.entryDate}>{shortDay(interaction.date)}</span>
              <span className={styles.rail} aria-hidden="true">
                <span className={interaction.isSystem ? styles.node : `${styles.node} ${styles.nodeOpen}`} style={interaction.isSystem ? { background: systemColor(interaction.title) } : undefined} />
                {index < interactions.length - 1 && <span className={styles.line} />}
              </span>
              <span className={styles.entryBody}>
                <span className={interaction.isSystem ? `${styles.entryTitle} ${styles.entrySystem}` : styles.entryTitle}>{interaction.title}</span>
                {interaction.detail && <span className={styles.entryDetail}>{interaction.detail}</span>}
                {!interaction.isSystem && editing === null && (
                  <button type="button" className={styles.entryEdit} onClick={() => setEditing(interaction)}>
                    Editar
                  </button>
                )}
              </span>
            </li>
          ),
        )}
      </ol>
    </section>
  )
}

/** Stage changes read "Aplicada → Entrevista"; their node takes the color of the stage reached. */
function systemColor(title: string): string {
  const reached = title.split('→').pop()?.trim() ?? ''
  return statuses.find((status) => status.label === reached)?.color ?? 'var(--faint)'
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
